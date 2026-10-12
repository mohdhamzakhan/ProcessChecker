using System.DirectoryServices.Protocols;
using System.Net;
using System.Text;

namespace ApiGateway;

public class AdOptions
{
    public bool Enabled { get; set; }
    public string Server { get; set; } = "";
    public int Port { get; set; } = 636;
    public bool UseSsl { get; set; } = true;
    public string AuthType { get; set; } = "Basic";      // Basic (with SSL) or Negotiate (Windows)
    public string Domain { get; set; } = "";             // NetBIOS name, e.g. CORP
    public string UpnSuffix { get; set; } = "";          // e.g. company.com
    public string BaseDn { get; set; } = "";             // where users are searched
    public string AllowedGroupDn { get; set; } = "";     // optional: only members may sign in
    public string AdminGroupDn { get; set; } = "";       // optional: members become Admin
    public int TimeoutSeconds { get; set; } = 10;
}

public record AdUser(string Email, string DisplayName, bool IsAdmin);

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
        if (_o.Enabled && (string.IsNullOrWhiteSpace(_o.Server) || string.IsNullOrWhiteSpace(_o.BaseDn)))
            throw new InvalidOperationException("Ad:Server and Ad:BaseDn are required when Ad:Enabled is true.");
        if (_o.Enabled && !_o.UseSsl && _o.AuthType.Equals("Basic", StringComparison.OrdinalIgnoreCase))
            _log.LogWarning("AD is using Basic bind WITHOUT SSL: passwords travel unencrypted. Use LDAPS (port 636).");
    }

    public bool Enabled => _o.Enabled;

    /// null = wrong credentials or not allowed. Throws AdUnavailableException when AD cannot be reached.
    public AdUser? Authenticate(string username, string password)
    {
        if (!_o.Enabled) return null;
        // An empty password makes many LDAP servers treat the bind as anonymous and succeed
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)) return null;

        var (bindName, attr, value) = Normalize(username);

        using var conn = Connect();
        try
        {
            conn.Bind(new NetworkCredential(bindName, password));
        }
        catch (LdapException ex) when (ex.ErrorCode == 49)      // invalid credentials / locked / disabled / expired
        {
            return null;
        }
        catch (Exception ex) when (ex is LdapException or DirectoryOperationException)
        {
            throw new AdUnavailableException("The directory server could not be reached.", ex);
        }

        try
        {
            var filter = $"(&(objectCategory=person)(objectClass=user)({attr}={Escape(value)}))";
            var res = (SearchResponse)conn.SendRequest(new SearchRequest(
                _o.BaseDn, filter, SearchScope.Subtree, "mail", "displayName", "userPrincipalName"));

            if (res.Entries.Count != 1)
            {
                _log.LogInformation("AD user {User} authenticated but was not found under {Base}.", username, _o.BaseDn);
                return null;
            }

            var entry = res.Entries[0];
            if (!string.IsNullOrWhiteSpace(_o.AllowedGroupDn) && !InGroup(conn, entry.DistinguishedName, _o.AllowedGroupDn))
            {
                _log.LogInformation("AD user {User} is not in the allowed group.", username);
                return null;
            }

            var email = Read(entry, "mail") ?? Read(entry, "userPrincipalName");
            if (string.IsNullOrWhiteSpace(email))
            {
                _log.LogWarning("AD user {User} has neither mail nor userPrincipalName.", username);
                return null;
            }

            var name = Read(entry, "displayName") ?? email;
            var isAdmin = !string.IsNullOrWhiteSpace(_o.AdminGroupDn) && InGroup(conn, entry.DistinguishedName, _o.AdminGroupDn);
            return new AdUser(email.Trim().ToLowerInvariant(), name, isAdmin);
        }
        catch (Exception ex) when (ex is LdapException or DirectoryOperationException)
        {
            throw new AdUnavailableException("The directory search failed. Check Ad:BaseDn and the group names.", ex);
        }
    }

    private LdapConnection Connect()
    {
        var conn = new LdapConnection(new LdapDirectoryIdentifier(_o.Server, _o.Port))
        {
            AuthType = _o.AuthType.Equals("Negotiate", StringComparison.OrdinalIgnoreCase)
                ? System.DirectoryServices.Protocols.AuthType.Negotiate
                : System.DirectoryServices.Protocols.AuthType.Basic,
            Timeout = TimeSpan.FromSeconds(_o.TimeoutSeconds)
        };
        conn.SessionOptions.ProtocolVersion = 3;
        conn.SessionOptions.SecureSocketLayer = _o.UseSsl;
        conn.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
        return conn;
    }

    // Nested groups are included (LDAP_MATCHING_RULE_IN_CHAIN)
    private bool InGroup(LdapConnection conn, string userDn, string groupDn)
    {
        var filter = $"(&(distinguishedName={Escape(userDn)})(memberOf:1.2.840.113556.1.4.1941:={Escape(groupDn)}))";
        var res = (SearchResponse)conn.SendRequest(
            new SearchRequest(_o.BaseDn, filter, SearchScope.Subtree, "distinguishedName"));
        return res.Entries.Count > 0;
    }

    // jsmith -> CORP\jsmith; CORP\jsmith as typed; jsmith@company.com as typed
    private (string Bind, string Attr, string Value) Normalize(string input)
    {
        var u = input.Trim();
        if (u.Contains('\\')) return (u, "sAMAccountName", u[(u.IndexOf('\\') + 1)..]);
        if (u.Contains('@')) return (u, "userPrincipalName", u);
        if (!string.IsNullOrWhiteSpace(_o.Domain)) return ($"{_o.Domain}\\{u}", "sAMAccountName", u);
        var upn = $"{u}@{_o.UpnSuffix}";
        return (upn, "userPrincipalName", upn);
    }

    private static string? Read(SearchResultEntry e, string attribute) =>
        e.Attributes[attribute] is { Count: > 0 } a ? a[0]?.ToString() : null;

    // RFC 4515 escaping so a typed username cannot change the filter
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