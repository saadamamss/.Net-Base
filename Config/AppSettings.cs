namespace DotnetStarterKit.Config;

public class AppSettings
{
    public string FrontendUrl { get; set; } = string.Empty;
}

public class JwtSettings
{
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int AccessTokenExpiryMinutes { get; set; } = 15;
    public int RefreshTokenExpiryDays { get; set; } = 7;
}

public class CorsSettings
{
    public string AllowedOrigins { get; set; } = string.Empty;

    public string[] GetOrigins() =>
        AllowedOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries);
}

public class MailSettings
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
}

public class RateLimitingSettings
{
    public int PermitLimit { get; set; } = 100;
    public int WindowMinutes { get; set; } = 1;
}