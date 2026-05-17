namespace MarketFlow.Application.Common.Models;

public class ServiceResult<T>
{
    public bool Succeeded { get; init; }

    public string Message { get; init; } = string.Empty;

    public T? Data { get; init; }

    public static ServiceResult<T> Success(T? data, string message = "Operation completed.")
    {
        return new ServiceResult<T>
        {
            Succeeded = true,
            Message = message,
            Data = data
        };
    }

    public static ServiceResult<T> Failure(string message)
    {
        return new ServiceResult<T>
        {
            Succeeded = false,
            Message = message
        };
    }
}
