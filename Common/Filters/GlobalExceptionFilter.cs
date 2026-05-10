using DotnetStarterKit.Common.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Net;

namespace DotnetStarterKit.Common.Filters;

public class GlobalExceptionFilter : IExceptionFilter
{
    private readonly ILogger<GlobalExceptionFilter> _logger;

    public GlobalExceptionFilter(ILogger<GlobalExceptionFilter> logger)
    {
        _logger = logger;
    }

    public void OnException(ExceptionContext context)
    {
        var requestId = context.HttpContext.Items["RequestId"]?.ToString() ?? "N/A";

        _logger.LogError(context.Exception, "[{RequestId}] Unhandled exception", requestId);

        var response = ApiResponse<object>.Fail(
            "An unexpected error occurred.",
            AppCodes.InternalError
        );

        context.Result = new ObjectResult(response)
        {
            StatusCode = (int)HttpStatusCode.InternalServerError
        };

        context.ExceptionHandled = true;
    }
}