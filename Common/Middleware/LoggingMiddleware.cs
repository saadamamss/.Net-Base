namespace DotnetStarterKit.Common.Middleware;

public class LoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<LoggingMiddleware> _logger;

    public LoggingMiddleware(RequestDelegate next, ILogger<LoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var requestId = Guid.NewGuid().ToString("N")[..8].ToUpper();
        context.Items["RequestId"] = requestId;
        context.Response.Headers["X-Request-Id"] = requestId;

        var method = context.Request.Method;
        var path = context.Request.Path;
        var start = DateTime.UtcNow;

        await _next(context);

        var ms = (DateTime.UtcNow - start).TotalMilliseconds;
        var status = context.Response.StatusCode;

        _logger.LogInformation("[{RequestId}] {Method} {Path} {Status} — {Ms}ms",
            requestId, method, path, status, (int)ms);
    }
}