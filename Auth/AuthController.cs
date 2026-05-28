using DataForge.Auth.DTOs;
using DataForge.Common.Models;
using DataForge.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace DataForge.Auth;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly UserManager<User> _userManager;
    private const string RefreshTokenCookie = "refreshToken";
    private const int RefreshTokenExpiryDays = 7;

    public AuthController(AuthService authService, UserManager<User> userManager)
    {
        _authService = authService;
        _userManager = userManager;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterDto dto)
    {
        var (success, message, code) = await _authService.RegisterAsync(dto);
        if (!success) return Conflict(ApiResponse<object>.Fail(message, code));
        return StatusCode(201, ApiResponse<object>.Ok(null!, message, code));
    }

    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail(VerifyEmailDto dto)
    {
        var (success, message, code) = await _authService.VerifyEmailAsync(dto);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message, code));
        return Ok(ApiResponse<object>.Ok(null!, message, code));
    }

    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginDto dto)
    {
        var (success, message, code, data) = await _authService.LoginAsync(dto);

        if (!success)
        {
            var status = code == AppCodes.AccountLocked ? 423 : 401;
            return StatusCode(status, ApiResponse<object>.Fail(message, code));
        }

        var user = await _userManager.FindByEmailAsync(dto.Email) ?? throw new Exception();
        var refreshToken = await _userManager.GetAuthenticationTokenAsync(user, "RefreshToken", "RefreshToken");

        // Access token → HttpOnly cookie
        Response.Cookies.Append("accessToken", data!.AccessToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = false, // true in production
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddMinutes(15)
        });

        // Refresh token → HttpOnly cookie
        Response.Cookies.Append("refreshToken", refreshToken!, new CookieOptions
        {
            HttpOnly = true,
            Secure = false,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(RefreshTokenExpiryDays)
        });

        // Response بدون أي tokens
        return Ok(ApiResponse<UserDto>.Ok(data.User, message, code));
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh()
    {
        var refreshToken = Request.Cookies["refreshToken"];
        if (string.IsNullOrEmpty(refreshToken))
            return Unauthorized(ApiResponse<object>.Fail("No refresh token.", AppCodes.InvalidToken));

        var (success, message, code, accessToken) = await _authService.RefreshAsync(refreshToken);
        if (!success)
        {
            Response.Cookies.Delete("refreshToken");
            return Unauthorized(ApiResponse<object>.Fail(message, code));
        }

        // Access token الجديد → cookie
        Response.Cookies.Append("accessToken", accessToken!, new CookieOptions
        {
            HttpOnly = true,
            Secure = false,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddMinutes(15)
        });

        return Ok(ApiResponse<object>.Ok(null!, message, code));
    }

[HttpPost("logout")]
[Authorize]
public async Task<IActionResult> Logout()
{
    var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
    await _authService.LogoutAsync(userId);

    Response.Cookies.Delete("accessToken");
    Response.Cookies.Delete("refreshToken");

    return Ok(ApiResponse<object>.Ok(null!, "Logged out successfully."));
}

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordDto dto)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        var (success, message, code) = await _authService.ChangePasswordAsync(userId, dto);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message, code));
        return Ok(ApiResponse<object>.Ok(null!, message, code));
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordDto dto)
    {
        var (_, message) = await _authService.ForgotPasswordAsync(dto);
        return Ok(ApiResponse<object>.Ok(null!, message));
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordDto dto)
    {
        var (success, message, code) = await _authService.ResetPasswordAsync(dto);
        if (!success) return BadRequest(ApiResponse<object>.Fail(message, code));
        return Ok(ApiResponse<object>.Ok(null!, message, code));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!;
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return NotFound(ApiResponse<object>.Fail("User not found.", AppCodes.NotFound));

        var roles = await _userManager.GetRolesAsync(user);
        var dto = new UserDto
        {
            Id = user.Id,
            Name = user.Name,
            Email = user.Email!,
            Avatar = user.Avatar,
            EmailVerified = user.EmailVerified,
            Roles = roles
        };

        return Ok(ApiResponse<UserDto>.Ok(dto));
    }
}