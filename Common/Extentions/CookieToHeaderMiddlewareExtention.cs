using DataForge.Common.Middleware;

namespace Microsoft.AspNetCore.Builder
{
    public static class CookieToHeaderMiddlewareExtention
    {
        public static IApplicationBuilder UseCookieToHeaderMiddleware(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<CookieToHeaderMiddleware>();
        }
    }
}