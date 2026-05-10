using System.ComponentModel.DataAnnotations;

namespace DotnetStarterKit.Users.DTOs;

public class UpdateUserDto
{
    public string? Name { get; set; }
    public string? Avatar { get; set; }
    public bool? IsActive { get; set; }
}

public class UserResponseDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Avatar { get; set; }
    public bool IsActive { get; set; }
    public bool EmailVerified { get; set; }
    public IList<string> Roles { get; set; } = [];
    public DateTime CreatedAt { get; set; }
}