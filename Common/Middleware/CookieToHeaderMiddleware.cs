namespace DataForge.Common.Middleware;

public class CookieToHeaderMiddleware
{
    private readonly RequestDelegate _next;

    public CookieToHeaderMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var accessToken = context.Request.Cookies["accessToken"];

        if (!string.IsNullOrEmpty(accessToken) &&
            !context.Request.Headers.ContainsKey("Authorization"))
        {
            context.Request.Headers.Append("Authorization", $"Bearer {accessToken}");
        }

        await _next(context);
    }
}