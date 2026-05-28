using DataForge.Common.Models;
using DataForge.Users.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataForge.Users;

[ApiController]
[Route("api/v1/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly UsersService _usersService;

    public UsersController(UsersService usersService)
    {
        _usersService = usersService;
    }

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int limit = 10)
    {
        var result = await _usersService.GetAllAsync(page, limit);
        return Ok(ApiResponse<PaginatedResult<UserResponseDto>>.Ok(result));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        // Non-admin can only get their own profile
        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var isAdmin = User.IsInRole("Admin");

        if (!isAdmin && currentUserId != id)
            return StatusCode(403, ApiResponse<object>.Fail(
                "You can only access your own profile.",
                AppCodes.Forbidden));

        var user = await _usersService.GetByIdAsync(id);
        if (user is null)
            return NotFound(ApiResponse<object>.Fail("User not found.", AppCodes.NotFound));

        return Ok(ApiResponse<UserResponseDto>.Ok(user));
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(string id, UpdateUserDto dto)
    {
        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var isAdmin = User.IsInRole("Admin");

        if (!isAdmin && currentUserId != id)
            return StatusCode(403, ApiResponse<object>.Fail(
                "You can only update your own profile.",
                AppCodes.Forbidden));

        var (success, message, code, data) = await _usersService.UpdateAsync(id, dto);
        if (!success) return NotFound(ApiResponse<object>.Fail(message, code));

        return Ok(ApiResponse<UserResponseDto>.Ok(data!, message, code));
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(string id)
    {
        var (success, message, code) = await _usersService.DeleteAsync(id);
        if (!success) return NotFound(ApiResponse<object>.Fail(message, code));

        return Ok(ApiResponse<object>.Ok(null!, message, code));
    }
}