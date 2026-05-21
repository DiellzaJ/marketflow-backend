namespace MarketFlow.Application.Common.Models;

public enum ServiceResultFailureType
{
    None,
    Validation,
    NotFound,
    Conflict
}

public class ServiceResult<T>
{
    public bool Succeeded { get; init; }

    public string Message { get; init; } = string.Empty;

    public T? Data { get; init; }

    public ServiceResultFailureType FailureType { get; init; }

    public static ServiceResult<T> Success(T? data, string message = "Operation completed.")
    {
        return new ServiceResult<T>
        {
            Succeeded = true,
            Message = message,
            Data = data,
            FailureType = ServiceResultFailureType.None
        };
    }

    public static ServiceResult<T> Failure(
        string message,
        ServiceResultFailureType failureType = ServiceResultFailureType.Validation)
    {
        return new ServiceResult<T>
        {
            Succeeded = false,
            Message = message,
            FailureType = failureType
        };
    }
}
