namespace DataForge.Common.Middleware;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyMiddleware> _logger;
    private readonly string _apiKey;

    public ApiKeyMiddleware(RequestDelegate next, ILogger<ApiKeyMiddleware> logger, IConfiguration configuration)
    {
        _next = next;
        _logger = logger;
        _apiKey = configuration["ApiKey"] ?? throw new InvalidOperationException("ApiKey is not configured in appsettings.json");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (!path.StartsWith("/api/v1/items", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue("X-Api-Key", out var providedKey))
        {
            _logger.LogWarning("API Key missing for path: {Path}", path);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                success = false,
                message = "Missing X-Api-Key header.",
                code = "UNAUTHORIZED",
                data = (object?)null
            });
            return;
        }

        if (providedKey.ToString() != _apiKey)
        {
            _logger.LogWarning("Invalid API Key for path: {Path}", path);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                success = false,
                message = "Invalid API Key.",
                code = "UNAUTHORIZED",
                data = (object?)null
            });
            return;
        }

        _logger.LogInformation("API Key validated for path: {Path}", path);
        await _next(context);
    }
}
