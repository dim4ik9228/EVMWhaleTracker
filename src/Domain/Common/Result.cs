namespace Domain.Common;

/// <summary>
/// Represents success or failure for expected business-flow outcomes.
/// </summary>
public class Result
{
    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
        {
            throw new ArgumentException(ResultDiagnostics.SuccessWithError, nameof(error));
        }

        if (!isSuccess && error == Error.None)
        {
            throw new ArgumentException(ResultDiagnostics.FailureWithoutError, nameof(error));
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public static Result Success() => new(true, Error.None);

    public static Result Failure(Error error) => new(false, error);
}
