using DataForge.Common.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Net;

namespace DataForge.Common.Filters;

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

        var (statusCode, message, code) = context.Exception switch
        {
            KeyNotFoundException ex => (404, ex.Message, AppCodes.NotFound),
            InvalidOperationException ex => (409, ex.Message, AppCodes.Conflict),
            ArgumentException ex => (400, ex.Message, AppCodes.ValidationError),
            UnauthorizedAccessException ex => (403, ex.Message, AppCodes.Forbidden),
            _ => (500, context.Exception.Message ?? "An unexpected error occurred.", AppCodes.InternalError)
        };

        var response = ApiResponse<object>.Fail(message, code);

        context.Result = new ObjectResult(response) { StatusCode = statusCode };
        context.ExceptionHandled = true;
    }
}