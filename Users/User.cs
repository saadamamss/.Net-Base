using Microsoft.AspNetCore.Identity;

namespace DataForge.Users;

public class User : IdentityUser
{
    public string Name { get; set; } = string.Empty;
    public string? Avatar { get; set; }
    public bool IsActive { get; set; } = true;
    public bool EmailVerified { get; set; } = false;
    public string? VerifyToken { get; set; }
    public DateTime? VerifyTokenExpiry { get; set; }
    public string? ResetToken { get; set; }
    public DateTime? ResetTokenExpiry { get; set; }
    public int TokenVersion { get; set; } = 0;
    public int FailedLoginAttempts { get; set; } = 0;
    public DateTime? LockedUntil { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}