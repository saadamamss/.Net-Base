using DotnetStarterKit.Common.Models;
using DotnetStarterKit.Users.DTOs;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DotnetStarterKit.Users;

public class UsersService
{
    private readonly UserManager<User> _userManager;

    public UsersService(UserManager<User> userManager)
    {
        _userManager = userManager;
    }

    public async Task<PaginatedResult<UserResponseDto>> GetAllAsync(int page, int limit)
    {
        var query = _userManager.Users.OrderByDescending(u => u.CreatedAt);
        var total = await query.CountAsync();

        var users = await query
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync();

        var items = new List<UserResponseDto>();
        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            items.Add(MapToDto(user, roles));
        }

        return new PaginatedResult<UserResponseDto>
        {
            Items = items,
            Total = total,
            Page = page,
            Limit = limit
        };
    }

    public async Task<UserResponseDto?> GetByIdAsync(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null) return null;

        var roles = await _userManager.GetRolesAsync(user);
        return MapToDto(user, roles);
    }

    public async Task<(bool Success, string Message, string Code, UserResponseDto? Data)> UpdateAsync(string id, UpdateUserDto dto)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
            return (false, "User not found.", AppCodes.NotFound, null);

        if (dto.Name is not null) user.Name = dto.Name;
        if (dto.Avatar is not null) user.Avatar = dto.Avatar;
        if (dto.IsActive.HasValue) user.IsActive = dto.IsActive.Value;
        user.UpdatedAt = DateTime.UtcNow;

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return (false, errors, AppCodes.ValidationError, null);
        }

        var roles = await _userManager.GetRolesAsync(user);
        return (true, "User updated successfully.", AppCodes.Success, MapToDto(user, roles));
    }

    public async Task<(bool Success, string Message, string Code)> DeleteAsync(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
            return (false, "User not found.", AppCodes.NotFound);

        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return (false, errors, AppCodes.ValidationError);
        }

        return (true, "User deleted successfully.", AppCodes.Success);
    }

    private static UserResponseDto MapToDto(User user, IList<string> roles) => new()
    {
        Id = user.Id,
        Name = user.Name,
        Email = user.Email!,
        Avatar = user.Avatar,
        IsActive = user.IsActive,
        EmailVerified = user.EmailVerified,
        Roles = roles,
        CreatedAt = user.CreatedAt
    };
}