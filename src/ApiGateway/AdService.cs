using Microsoft.AspNetCore.Authentication;
using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.Runtime.InteropServices;
using System.Text;

namespace ApiGateway;

public class AdOptions
{
    public bool Enabled { get; set; }
    public string Domain { get; set; } = "";                              // same value as ADSettings:Domain in your other app
    public string[] AdminGroups { get; set; } = Array.Empty<string>();    // group names (sAMAccountName) that make someone Admin
    public string[] AllowedGroups { get; set; } = Array.Empty<string>();  // empty = every domain user may sign in
    public string? Container { get; set; }                                // optional, limits the people search, e.g. OU=Staff,DC=corp,DC=com
    public string? ServiceUser { get; set; }                              // optional: account used for the people search only
    public string? ServicePassword { get; set; }
}

public record AdUser(string Email, string DisplayName, bool IsAdmin, string Username);
public record DirectoryPerson(string Email, string Name, string Username);

public class AdUnavailableException : Exception
{
    public AdUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}

public class AdService
{
    private readonly AdOptions _o;
    private readonly ILogger<AdService> _log;

    public AdService(AdOptions options, ILogger<AdService> log)
    {
        _o = options; _log = log;
        if (_o.Enabled && string.IsNullOrWhiteSpace(_o.Domain))
            throw new InvalidOperationException("Ad:Domain is required when Ad:Enabled is true.");
        if (_o.Enabled && !OperatingSystem.IsWindows())
            throw new InvalidOperationException("AD sign-in with PrincipalContext works only when the gateway runs on Windows.");
    }

    public bool Enabled => _o.Enabled;

    /// null = wrong credentials or not allowed. Throws AdUnavailableException when AD cannot be reached.
    public AdUser? Authenticate(string username, string password)
    {
        if (!_o.Enabled) return null;
        // A blank password can turn into an anonymous bind that "succeeds", so refuse it up front
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)) return null;

        var typed = username.Trim();
        var sam = typed.Contains('\\') ? typed[(typed.IndexOf('\\') + 1)..] : typed;

        try
        {
            using var ctx = new PrincipalContext(ContextType.Domain, _o.Domain);
            if (!ctx.ValidateCredentials(typed, password)) return null;

            using var user = UserPrincipal.FindByIdentity(ctx, sam);
            if (user == null)
            {
                _log.LogInformation("{User} authenticated but was not found in the directory.", typed);
                return null;
            }

            HashSet<string>? groups = null;
            if (_o.AdminGroups.Length > 0 || _o.AllowedGroups.Length > 0) groups = GroupNames(user);

            if (_o.AllowedGroups.Length > 0 && !_o.AllowedGroups.Any(g => groups!.Contains(g)))
            {
                _log.LogInformation("{User} is not in an allowed group.", typed);
                return null;
            }

            var email = (user.EmailAddress ?? user.UserPrincipalName ?? "").Trim().ToLowerInvariant();
            if (email.Length == 0)
            {
                email = $"{user.SamAccountName}@{_o.Domain}".ToLowerInvariant();
                _log.LogWarning("{User} has no mail attribute; using {Email}. Emails to this address may not arrive.", typed, email);
            }

            var name = user.DisplayName ?? user.Name ?? email;
            var isAdmin = groups != null && _o.AdminGroups.Any(g => groups.Contains(g));
            return new AdUser(email, name, isAdmin, user.SamAccountName ?? sam);
        }
        catch (PrincipalException ex)
        {
            throw new AdUnavailableException("The company directory could not be reached.", ex);
        }
        catch (COMException ex)
        {
            throw new AdUnavailableException("The company directory could not be reached.", ex);
        }
    }

    /// People search for the "assign to" picker. Skips disabled accounts and accounts without an email.
    public List<DirectoryPerson> Search(string term, int max = 15)
    {
        if (!_o.Enabled) return new();
        term = term.Trim();
        if (term.Length < 2) return new();

        var t = Escape(term);
        var filter =
            "(&(objectCategory=person)(objectClass=user)(!(userAccountControl:1.2.840.113556.1.4.803:=2))" +
            $"(|(displayName=*{t}*)(sAMAccountName={t}*)(mail={t}*)(givenName={t}*)(sn={t}*)))";

        var path = string.IsNullOrWhiteSpace(_o.Container)
            ? $"LDAP://{_o.Domain}"
            : $"LDAP://{_o.Domain}/{_o.Container}";

        try
        {
            using var root = string.IsNullOrWhiteSpace(_o.ServiceUser)
                ? new DirectoryEntry(path)
                : new DirectoryEntry(path, _o.ServiceUser, _o.ServicePassword);

            using var searcher = new DirectorySearcher(root, filter,
                new[] { "displayName", "sAMAccountName", "mail", "userPrincipalName" }, SearchScope.Subtree)
            {
                SizeLimit = max,
                ServerTimeLimit = TimeSpan.FromSeconds(10),
                ClientTimeout = TimeSpan.FromSeconds(15)
            };

            var list = new List<DirectoryPerson>();
            using var results = searcher.FindAll();
            foreach (SearchResult r in results)
            {
                var sam = Prop(r, "samaccountname");
                var email = (Prop(r, "mail") ?? Prop(r, "userprincipalname") ?? "").Trim().ToLowerInvariant();
                if (email.Length == 0 || sam == null) continue;
                list.Add(new DirectoryPerson(email, Prop(r, "displayname") ?? sam, sam));
            }
            return list.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            throw new AdUnavailableException("The directory search failed: " + ex.Message, ex);
        }
    }

    private HashSet<string> GroupNames(UserPrincipal user)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            // includes nested groups
            foreach (var g in user.GetAuthorizationGroups())
                if (!string.IsNullOrEmpty(g.SamAccountName)) set.Add(g.SamAccountName);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Reading all groups failed; falling back to direct groups.");
            try
            {
                foreach (var g in user.GetGroups())
                    if (!string.IsNullOrEmpty(g.SamAccountName)) set.Add(g.SamAccountName);
            }
            catch (Exception ex2) { _log.LogWarning(ex2, "Reading direct groups failed too."); }
        }
        return set;
    }

    private static string? Prop(SearchResult r, string name) =>
        r.Properties[name].Count > 0 ? r.Properties[name][0]?.ToString() : null;

    // RFC 4515 escaping, so typed text cannot change the LDAP filter
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