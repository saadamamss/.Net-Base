using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DotnetStarterKit.Auth.DTOs;
using DotnetStarterKit.Common.Models;
using DotnetStarterKit.Config;
using DotnetStarterKit.Mail;
using DotnetStarterKit.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace DotnetStarterKit.Auth;

public class AuthService
{
    private readonly UserManager<User> _userManager;
    private readonly JwtSettings _jwt;
    private readonly AppSettings _app;
    private readonly MailService _mailService;

    public AuthService(
        UserManager<User> userManager,
        IOptions<JwtSettings> jwt,
        IOptions<AppSettings> app, MailService mailService)

    {
        _userManager = userManager;
        _jwt = jwt.Value;
        _app = app.Value;
        _mailService = mailService;
    }

    // ── Register ───────────────────────────────────────────────
    public async Task<(bool Success, string Message, string Code)> RegisterAsync(RegisterDto dto)
    {
        var existing = await _userManager.FindByEmailAsync(dto.Email);
        if (existing is not null)
            return (false, "Email already in use.", AppCodes.Conflict);

        var user = new User
        {
            Name = dto.Name,
            Email = dto.Email,
            UserName = dto.Email,
            VerifyToken = GenerateSecureToken(),
            VerifyTokenExpiry = DateTime.UtcNow.AddHours(24)
        };

        var result = await _userManager.CreateAsync(user, dto.Password);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return (false, errors, AppCodes.ValidationError);
        }

        await _userManager.AddToRoleAsync(user, "User");

        // Send emails
        await _mailService.SendWelcomeAsync(user.Email!, user.Name);
        await _mailService.SendVerificationEmailAsync(
            user.Email!, user.Name, user.VerifyToken!, _app.FrontendUrl);

        return (true, "Registration successful. Please verify your email.", AppCodes.Created);
    }

    // ── Verify Email ───────────────────────────────────────────
    public async Task<(bool Success, string Message, string Code)> VerifyEmailAsync(VerifyEmailDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Email);
        if (user is null)
            return (false, "User not found.", AppCodes.NotFound);

        if (user.EmailVerified)
            return (false, "Email already verified.", AppCodes.Conflict);

        if (user.VerifyToken != dto.Token || user.VerifyTokenExpiry < DateTime.UtcNow)
            return (false, "Invalid or expired token.", AppCodes.InvalidToken);

        user.EmailVerified = true;
        user.VerifyToken = null;
        user.VerifyTokenExpiry = null;
        await _userManager.UpdateAsync(user);

        return (true, "Email verified successfully.", AppCodes.Success);
    }

    // ── Login ──────────────────────────────────────────────────
    public async Task<(bool Success, string Message, string Code, AuthResponseDto? Data)> LoginAsync(LoginDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Email);
        if (user is null)
            return (false, "Invalid credentials.", AppCodes.InvalidCredentials, null);

        // Account locked?
        if (user.LockedUntil.HasValue && user.LockedUntil > DateTime.UtcNow)
        {
            var remaining = (int)(user.LockedUntil.Value - DateTime.UtcNow).TotalMinutes;
            return (false, $"Account locked. Try again in {remaining} minutes.", AppCodes.AccountLocked, null);
        }

        // Email verified?
        if (!user.EmailVerified)
            return (false, "Please verify your email first.", AppCodes.EmailNotVerified, null);

        // Password check
        var validPassword = await _userManager.CheckPasswordAsync(user, dto.Password);
        if (!validPassword)
        {
            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= 5)
            {
                user.LockedUntil = DateTime.UtcNow.AddMinutes(30);
                user.FailedLoginAttempts = 0;
            }
            await _userManager.UpdateAsync(user);
            return (false, "Invalid credentials.", AppCodes.InvalidCredentials, null);
        }

        // Reset failed attempts
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        await _userManager.UpdateAsync(user);

        var roles = await _userManager.GetRolesAsync(user);
        var accessToken = GenerateAccessToken(user, roles);
        var refreshToken = GenerateSecureToken();

        // Store refresh token hash
        await _userManager.SetAuthenticationTokenAsync(user, "RefreshToken", "RefreshToken", refreshToken);

        var response = new AuthResponseDto
        {
            AccessToken = accessToken,
            User = new UserDto
            {
                Id = user.Id,
                Name = user.Name,
                Email = user.Email!,
                Avatar = user.Avatar,
                EmailVerified = user.EmailVerified,
                Roles = roles
            }
        };

        return (true, "Login successful.", AppCodes.Success, response);
    }

    // ── Refresh Token ──────────────────────────────────────────
    public async Task<(bool Success, string Message, string Code, string? AccessToken)> RefreshAsync(string refreshToken)
    {
        // Find user by refresh token
        var users = _userManager.Users.ToList();
        User? matchedUser = null;

        foreach (var u in users)
        {
            var stored = await _userManager.GetAuthenticationTokenAsync(u, "RefreshToken", "RefreshToken");
            if (stored == refreshToken)
            {
                matchedUser = u;
                break;
            }
        }

        if (matchedUser is null)
            return (false, "Invalid refresh token.", AppCodes.InvalidToken, null);

        var roles = await _userManager.GetRolesAsync(matchedUser);
        var newAccessToken = GenerateAccessToken(matchedUser, roles);

        return (true, "Token refreshed.", AppCodes.Success, newAccessToken);
    }

    // ── Logout ─────────────────────────────────────────────────
    public async Task<(bool Success, string Message)> LogoutAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return (false, "User not found.");

        await _userManager.RemoveAuthenticationTokenAsync(user, "RefreshToken", "RefreshToken");
        user.TokenVersion++;
        await _userManager.UpdateAsync(user);

        return (true, "Logged out successfully.");
    }

    // ── Forgot Password ────────────────────────────────────────
    public async Task<(bool Success, string Message)> ForgotPasswordAsync(ForgotPasswordDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Email);

        // Always return success (security: don't reveal if email exists)
        if (user is null) return (true, "If this email exists, a reset link has been sent.");

        user.ResetToken = GenerateSecureToken();
        user.ResetTokenExpiry = DateTime.UtcNow.AddHours(1);
        await _userManager.UpdateAsync(user);

        await _mailService.SendPasswordResetAsync(
    user.Email!, user.Name, user.ResetToken!, _app.FrontendUrl);

        return (true, "If this email exists, a reset link has been sent.");
    }

    // ── Reset Password ─────────────────────────────────────────
    public async Task<(bool Success, string Message, string Code)> ResetPasswordAsync(ResetPasswordDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Email);
        if (user is null)
            return (false, "Invalid request.", AppCodes.NotFound);

        if (user.ResetToken != dto.Token || user.ResetTokenExpiry < DateTime.UtcNow)
            return (false, "Invalid or expired token.", AppCodes.InvalidToken);

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, dto.NewPassword);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return (false, errors, AppCodes.ValidationError);
        }

        user.ResetToken = null;
        user.ResetTokenExpiry = null;
        user.TokenVersion++;
        await _userManager.RemoveAuthenticationTokenAsync(user, "RefreshToken", "RefreshToken");
        await _userManager.UpdateAsync(user);

        return (true, "Password reset successfully.", AppCodes.Success);
    }

    // ── Helpers ────────────────────────────────────────────────
    public string GenerateAccessToken(User user, IList<string> roles)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Email, user.Email!),
            new(ClaimTypes.Name, user.Name),
            new("tokenVersion", user.TokenVersion.ToString())
        };

        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_jwt.AccessTokenExpiryMinutes),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GenerateSecureToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}