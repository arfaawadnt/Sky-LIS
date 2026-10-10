namespace Platform.SharedKernel.Results;

/// <summary>
/// The outcome of an operation that can be refused: either success or exactly one structured <see cref="Error"/>.
/// Used for domain-internal composition (factories, validations) where throwing would be premature.
/// </summary>
public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (isSuccess && error != Error.None)
        {
            throw new ArgumentException("A successful result cannot carry an error.", nameof(error));
        }

        if (!isSuccess && error == Error.None)
        {
            throw new ArgumentException("A failed result must carry an error.", nameof(error));
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public static Result Success() => new(true, Error.None);

    public static Result Failure(Error error) => new(false, error);

    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);

    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);

    /// <summary>Returns the first failure in <paramref name="results"/>, or success when every one succeeded.</summary>
    public static Result Combine(params Result[] results)
    {
        ArgumentNullException.ThrowIfNull(results);
        foreach (var result in results)
        {
            if (result.IsFailure)
            {
                return result;
            }
        }

        return Success();
    }

    public static implicit operator Result(Error error) => Failure(error);

    /// <summary>Throws the error as a <see cref="Primitives.DomainException"/> when this result is a failure.</summary>
    public void ThrowIfFailure()
    {
        if (IsFailure)
        {
            throw Error.ToException();
        }
    }
}

/// <summary>A <see cref="Result"/> that carries a value on success.</summary>
/// <typeparam name="TValue">The value type.</typeparam>
public class Result<TValue> : Result
{
    private readonly TValue? _value;

    protected internal Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error) => _value = value;

    /// <summary>The value; reading it on a failed result is a programming error.</summary>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"The result failed with {Error.Code}; read Error instead of Value.");

    public static implicit operator Result<TValue>(TValue value) => Success(value);

    public static implicit operator Result<TValue>(Error error) => Failure<TValue>(error);

    public TOut Match<TOut>(Func<TValue, TOut> onSuccess, Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return IsSuccess ? onSuccess(Value) : onFailure(Error);
    }
}
