using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace ApiGateway;

public static class Passwords
{
    private const int Iterations = 210_000;
    private static readonly string Dummy = Hash("dummy-password-used-for-timing");

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var h = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"v1.{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(h)}";
    }

    /// Does the same work whether or not the user exists, so response time does not reveal valid emails.
    public static bool Verify(string password, string? stored)
    {
        var real = stored != null;
        var parts = (stored ?? Dummy).Split('.');
        if (parts.Length != 4 || parts[0] != "v1" || !int.TryParse(parts[1], out var iter)) return false;

        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iter, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected) && real;
    }

    public static string? Validate(string? password) =>
        string.IsNullOrEmpty(password) || password.Length < 10
            ? "Password must be at least 10 characters."
            : null;
}

public class TokenService
{
    private readonly SymmetricSecurityKey _key;
    private readonly int _hours;

    public TokenService(IConfiguration cfg)
    {
        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(cfg["Jwt:Key"]!));
        _hours = cfg.GetValue("Jwt:ExpiryHours", 8);
    }

    public (string Token, DateTime Expires) Create(UserEntity u, string? username = null)
    {
        var expires = DateTime.UtcNow.AddHours(_hours);
        var claims = new[]
        {
            new Claim("sub", u.Id.ToString()),
            new Claim("email", u.Email),
            new Claim("name", u.DisplayName),
            new Claim("role", u.Role),
            new Claim("uname", (username ?? u.Email).ToLowerInvariant())
        };
        var jwt = new JwtSecurityToken("ProcessChecker", "ProcessChecker", claims,
            DateTime.UtcNow, expires, new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(jwt), expires);
    }
}