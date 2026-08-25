namespace Siri.SharedKernel;

/// <summary>
/// Outcome of an operation that has no return value. Use for expected failures instead of
/// throwing exceptions (see .claude/rules/backend.md — "Result &lt;T&gt; ...ไม่ throw exception เป็น control flow").
/// Exceptions remain reserved for truly unexpected failures (DB down, bad config), handled by the
/// global exception handler.
/// </summary>
public class Result
{
    protected Result(bool isSuccess, DomainError error)
    {
        if (isSuccess && error != DomainError.None)
        {
            throw new InvalidOperationException("A successful result cannot carry an error.");
        }

        if (!isSuccess && error == DomainError.None)
        {
            throw new InvalidOperationException("A failed result must carry an error.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public DomainError Error { get; }

    public static Result Success() => new(true, DomainError.None);

    public static Result Failure(DomainError error) => new(false, error);

    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, DomainError.None);

    public static Result<TValue> Failure<TValue>(DomainError error) => new(default, false, error);
}

/// <summary>
/// Outcome of an operation that produces <typeparamref name="TValue"/> on success or a
/// <see cref="DomainError"/> on failure. <see cref="Value"/> throws if accessed on a failed result —
/// callers must check <see cref="Result.IsSuccess"/> first.
/// </summary>
public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    internal Result(TValue? value, bool isSuccess, DomainError error)
        : base(isSuccess, error)
    {
        _value = value;
    }

    /// <summary>The success value. Throws <see cref="InvalidOperationException"/> if the result failed.</summary>
    public TValue Value =>
        IsSuccess
            ? _value!
            : throw new InvalidOperationException("Cannot access the value of a failed result.");

    public static implicit operator Result<TValue>(TValue value) => Success(value);
}
