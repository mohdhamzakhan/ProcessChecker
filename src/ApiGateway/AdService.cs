using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace ApiGateway;

public class AdOptions
{
    public bool Enabled { get; set; }
    public string Domain { get; set; } = "";
    public string Label { get; set; } = "";
    public string[] AdminGroups { get; set; } = Array.Empty<string>();
    public string[] AllowedGroups { get; set; } = Array.Empty<string>();
    public string[] GroupPrefixes { get; set; } = Array.Empty<string>();   // only these groups go into the token (empty = all, up to 150)
    public string? Container { get; set; }
    public string? ServiceUser { get; set; }
    public string? ServicePassword { get; set; }
}

public record AdUser(string Email, string DisplayName, bool IsAdmin, string Username, List<string> Groups);
public record DirectoryPerson(string Email, string Name, string Username);
public record DirectoryGroup(string Token, string Name, string Sam);

public class AdUnavailableException : Exception
{
    public AdUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}

public class AdService
{
    private static readonly Regex GroupName = new(@"^[A-Za-z0-9_.\- $]{1,64}$", RegexOptions.Compiled);
    private readonly AdOptions _o;
    private readonly ILogger<AdService> _log;

    public AdService(AdOptions options, ILogger<AdService> log)
    {
        _o = options; _log = log;
        if (_o.Enabled && string.IsNullOrWhiteSpace(_o.Domain))
            throw new InvalidOperationException("Ad:Domain is required when Ad:Enabled is true.");
        if (_o.Enabled && !OperatingSystem.IsWindows())
            throw new InvalidOperationException("AD sign-in works only when the gateway runs on Windows.");
        if (_o.Enabled) _log.LogInformation("Active Directory sign-in is ON for domain {Domain}.", _o.Domain);
        else _log.LogWarning("Active Directory sign-in is OFF (Ad:Enabled is false). Only local accounts can sign in.");
    }

    public bool Enabled => _o.Enabled;

    private PrincipalContext Context() =>
        string.IsNullOrWhiteSpace(_o.ServiceUser)
            ? new PrincipalContext(ContextType.Domain, _o.Domain)
            : new PrincipalContext(ContextType.Domain, _o.Domain, _o.ServiceUser, _o.ServicePassword);

    /// null = wrong credentials or not allowed. Throws AdUnavailableException when AD cannot be reached.
    public AdUser? Authenticate(string username, string password)
    {
        if (!_o.Enabled) return null;
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)) return null;   // blank = anonymous bind

        var typed = username.Trim();
        var sam = typed.Contains('\\') ? typed[(typed.IndexOf('\\') + 1)..] : typed;

        try
        {
            using var ctx = new PrincipalContext(ContextType.Domain, _o.Domain);
            if (!ctx.ValidateCredentials(typed, password))
            {
                _log.LogInformation("AD: wrong user name or password for {User}.", typed);
                return null;
            }

            using var user = UserPrincipal.FindByIdentity(ctx, sam);
            if (user == null) { _log.LogInformation("{User} authenticated but was not found in the directory.", typed); return null; }

            var groups = GroupNames(user);

            if (_o.AllowedGroups.Length > 0 && !_o.AllowedGroups.Any(g => groups.Contains(g)))
            {
                _log.LogInformation("{User} is not in an allowed group.", typed);
                return null;
            }

            var email = (user.EmailAddress ?? user.UserPrincipalName ?? "").Trim().ToLowerInvariant();
            if (email.Length == 0)
            {
                email = $"{user.SamAccountName}@{_o.Domain}".ToLowerInvariant();
                _log.LogWarning("{User} has no mail attribute; using {Email}.", typed, email);
            }

            var name = user.DisplayName ?? user.Name ?? email;
            var isAdmin = _o.AdminGroups.Any(g => groups.Contains(g));

            // only these groups travel in the token and the headers; set Ad:GroupPrefixes to keep it small
            var tokenGroups = groups
                .Where(g => GroupName.IsMatch(g) && (_o.GroupPrefixes.Length == 0
                    || _o.GroupPrefixes.Any(p => g.StartsWith(p, StringComparison.OrdinalIgnoreCase))))
                .Select(g => g.ToLowerInvariant()).Distinct().Take(150).ToList();

            return new AdUser(email, name, isAdmin, user.SamAccountName ?? sam, tokenGroups);
        }
        catch (PrincipalException ex) { throw new AdUnavailableException("The company directory could not be reached.", ex); }
        catch (COMException ex) { throw new AdUnavailableException("The company directory could not be reached.", ex); }
    }

    /// People search for the picker. Skips disabled accounts and accounts without an email.
    public List<DirectoryPerson> Search(string term, int max = 15)
    {
        if (!_o.Enabled || term.Trim().Length < 2) return new();
        var t = Escape(term.Trim());
        var filter =
            "(&(objectCategory=person)(objectClass=user)(!(userAccountControl:1.2.840.113556.1.4.803:=2))" +
            $"(|(displayName=*{t}*)(sAMAccountName={t}*)(mail={t}*)(givenName={t}*)(sn={t}*)))";

        try
        {
            var list = new List<DirectoryPerson>();
            foreach (SearchResult r in Find(filter, new[] { "displayName", "sAMAccountName", "mail", "userPrincipalName" }, max))
            {
                var sam = Prop(r, "samaccountname");
                var email = (Prop(r, "mail") ?? Prop(r, "userprincipalname") ?? "").Trim().ToLowerInvariant();
                if (email.Length == 0 || sam == null) continue;
                list.Add(new DirectoryPerson(email, Prop(r, "displayname") ?? sam, sam));
            }
            return list.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        { throw new AdUnavailableException("The directory search failed: " + ex.Message, ex); }
    }

    public List<DirectoryGroup> SearchGroups(string term, int max = 10)
    {
        if (!_o.Enabled || term.Trim().Length < 2) return new();
        var t = Escape(term.Trim());
        var filter = $"(&(objectCategory=group)(|(cn=*{t}*)(sAMAccountName=*{t}*)(displayName=*{t}*)))";

        try
        {
            var list = new List<DirectoryGroup>();
            foreach (SearchResult r in Find(filter, new[] { "sAMAccountName", "displayName", "cn" }, max))
            {
                var sam = Prop(r, "samaccountname");
                if (sam == null || !GroupName.IsMatch(sam)) continue;      // other names cannot travel in a header
                list.Add(new DirectoryGroup("group:" + sam.ToLowerInvariant(), Prop(r, "displayname") ?? Prop(r, "cn") ?? sam, sam));
            }
            return list.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        { throw new AdUnavailableException("The directory search failed: " + ex.Message, ex); }
    }

    /// The email of a user name, or null.
    public string? FindEmail(string username)
    {
        if (!_o.Enabled) return null;
        var sam = username.Contains('\\') ? username[(username.IndexOf('\\') + 1)..] : username;
        try
        {
            using var ctx = Context();
            using var u = UserPrincipal.FindByIdentity(ctx, sam);
            var mail = (u?.EmailAddress ?? "").Trim().ToLowerInvariant();
            return mail.Length > 0 ? mail : null;
        }
        catch (Exception ex) when (ex is PrincipalException or COMException)
        { _log.LogWarning("Looking up {User} failed: {Msg}", username, ex.Message); return null; }
    }

    /// The emails of the (enabled) members of a group, nested groups included.
    public List<string> GroupMemberEmails(string sam, int max = 200)
    {
        var list = new List<string>();
        if (!_o.Enabled || !GroupName.IsMatch(sam)) return list;
        try
        {
            using var ctx = Context();
            using var g = GroupPrincipal.FindByIdentity(ctx, IdentityType.SamAccountName, sam);
            if (g == null) return list;
            foreach (var m in g.GetMembers(true))
                using (m)
                {
                    if (m is not UserPrincipal u || u.Enabled == false) continue;
                    var mail = (u.EmailAddress ?? "").Trim().ToLowerInvariant();
                    if (mail.Length > 0 && !list.Contains(mail)) list.Add(mail);
                    if (list.Count >= max) break;
                }
        }
        catch (Exception ex) when (ex is PrincipalException or COMException)
        { _log.LogWarning("Reading the members of {Group} failed: {Msg}", sam, ex.Message); }
        return list;
    }

    private SearchResultCollection Find(string filter, string[] props, int max)
    {
        var path = string.IsNullOrWhiteSpace(_o.Container) ? $"LDAP://{_o.Domain}" : $"LDAP://{_o.Domain}/{_o.Container}";
        using var root = string.IsNullOrWhiteSpace(_o.ServiceUser)
            ? new DirectoryEntry(path)
            : new DirectoryEntry(path, _o.ServiceUser, _o.ServicePassword);
        using var searcher = new DirectorySearcher(root, filter, props, SearchScope.Subtree)
        {
            SizeLimit = max,
            ServerTimeLimit = TimeSpan.FromSeconds(10),
            ClientTimeout = TimeSpan.FromSeconds(15)
        };
        return searcher.FindAll();
    }

    private HashSet<string> GroupNames(UserPrincipal user)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var g in user.GetAuthorizationGroups())
                if (!string.IsNullOrEmpty(g.SamAccountName)) set.Add(g.SamAccountName);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Reading all groups failed; falling back to direct groups.");
            try { foreach (var g in user.GetGroups()) if (!string.IsNullOrEmpty(g.SamAccountName)) set.Add(g.SamAccountName); }
            catch (Exception ex2) { _log.LogWarning(ex2, "Reading direct groups failed too."); }
        }
        return set;
    }

    private static string? Prop(SearchResult r, string name) =>
        r.Properties[name].Count > 0 ? r.Properties[name][0]?.ToString() : null;

    private static string Escape(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s)
            switch (c)
            {
                case '\\': sb.Append(@"\5c"); break;
                case '*': sb.Append(@"\2a"); break;
                case '(': sb.Append(@"\28"); break;
                case ')': sb.Append(@"\29"); break;
                case '\0': sb.Append(@"\00"); break;
                default: sb.Append(c); break;
            }
        return sb.ToString();
    }
}