using DataForge.Common.Middleware;

namespace Microsoft.AspNetCore.Builder
{
    public static class LoggingMiddlewareExtention
    {
        public static IApplicationBuilder UseLoggingMiddleware(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<LoggingMiddleware>();
        }
    }
}