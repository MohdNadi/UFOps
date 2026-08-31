namespace UFOps.Core;

public enum ErrorCategory
{
    Validation,
    Input,
    Io,
    Permission,
    Conflict,
    Extraction,
    Cancelled,
    Internal
}

public sealed record UFOpsError
{
    public ErrorCode Code { get; }
    public ErrorCategory Category { get; }
    public string Message { get; }
    public bool Retryable { get; }

    public UFOpsError(ErrorCode code, ErrorCategory category, string message, bool retryable = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Code = code;
        Category = category;
        Message = message;
        Retryable = retryable;
    }
}

public sealed class Result<T>
{
    private readonly T? _value;

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public UFOpsError? Error { get; }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("A failed result has no value.");

    internal Result(bool success, T? value, UFOpsError? error)
    {
        IsSuccess = success;
        _value = value;
        Error = error;
    }
}

public static class Result
{
    public static Result<T> Success<T>(T value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return new Result<T>(true, value, null);
    }

    public static Result<T> Failure<T>(UFOpsError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<T>(false, default, error);
    }
}
