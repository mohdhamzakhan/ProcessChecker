namespace ApiGateway;

public static class Roles
{
    public const string Admin = "Admin";
    public const string User = "User";
    public static readonly string[] All = { Admin, User };
}

public class UserEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";          // always lower-case
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = Roles.User;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public static class AuthSources
    {
        public const string Local = "Local";
        public const string Ad = "AD";
    }

    // inside UserEntity:
    public string AuthSource { get; set; } = AuthSources.Local;
}

public record UserInfo(string Email, string Role, bool IsActive);

public record LoginRequest(string? Email, string? Password);
public record CreateUserRequest(string? Email, string? DisplayName, string? Password, string? Role);
public record UpdateUserRequest(bool? IsActive, string? Role, string? NewPassword);
public record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);