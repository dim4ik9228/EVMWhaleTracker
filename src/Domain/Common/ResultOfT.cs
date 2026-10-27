namespace Domain.Common;

/// <summary>
/// Represents a successful value or a structured failure.
/// </summary>
/// <typeparam name="T">The value returned by a successful operation.</typeparam>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException(ResultOfTDiagnostics.FailedValueAccess);

    private Result(T value) : base(true, Error.None)
    {
        _value = value;
    }

    private Result(Error error) : base(false, error)
    {
    }

    public static Result<T> Success(T value) => new(value);

    public new static Result<T> Failure(Error error) => new(error);
}
