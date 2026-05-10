namespace DotnetStarterKit.Common.Models;

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public T? Data { get; set; }

    public static ApiResponse<T> Ok(T data, string message = "Operation successful", string code = "SUCCESS") => new()
    {
        Success = true,
        Data = data,
        Message = message,
        Code = code
    };

    public static ApiResponse<object> Fail(string message, string code = "ERROR") => new()
    {
        Success = false,
        Message = message,
        Code = code
    };
}