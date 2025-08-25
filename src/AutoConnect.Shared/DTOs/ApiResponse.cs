namespace AutoConnect.Shared.DTOs;

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }

    public static ApiResponse<T> SuccessResult(T data, string? message = null)
    {
        return new ApiResponse<T>
        {
            Success = true,
            Data = data,
            Message = message
        };
    }

    public static ApiResponse<T> ErrorResult(string error, string? message = null)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Error = error,
            Message = message
        };
    }
}

public class ApiResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }

    // Changed method names to avoid conflict with properties
    public static ApiResponse CreateSuccess(string? message = null)
    {
        return new ApiResponse
        {
            Success = true,
            Message = message
        };
    }

    public static ApiResponse CreateError(string error, string? message = null)
    {
        return new ApiResponse
        {
            Success = false,
            Error = error,
            Message = message
        };
    }
}