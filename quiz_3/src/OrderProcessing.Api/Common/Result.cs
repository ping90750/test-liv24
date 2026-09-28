namespace OrderProcessing.Common;

public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
}

public sealed record Error(string Code, string Message, ErrorKind Kind);

/// <summary>
/// Success value or a typed error. Replaces the old string return values
/// ("Invalid user", "Success", ...) so callers can branch on error codes.
/// </summary>
public sealed class Result<T>
{
    private readonly T? _value;
    private readonly Error? _error;

    private Result(T value) => _value = value;

    private Result(Error error) => _error = error;

    public bool IsSuccess => _error is null;

    public bool IsFailure => !IsSuccess;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Result has no value; it failed with '{_error!.Code}'.");

    public Error Error => _error ?? throw new InvalidOperationException("Result succeeded; it has no error.");

    public static implicit operator Result<T>(T value) => new(value);

    public static implicit operator Result<T>(Error error) => new(error);
}
