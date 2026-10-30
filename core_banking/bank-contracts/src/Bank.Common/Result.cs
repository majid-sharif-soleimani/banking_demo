namespace Bank.Common;

/// <summary>A business rule violation; the API returns it as 422 problem details with this code.</summary>
public sealed record Error(string Code, string Message);

/// <summary>
/// The outcome of a use case that can fail for a business reason. Unexpected failures stay exceptions.
/// Implicit conversions let a feature return either a value or an <see cref="Error"/>.
/// </summary>
public readonly struct Result<T>
{
    private readonly T? value;
    private readonly Error? error;

    private Result(T? value, Error? error)
    {
        this.value = value;
        this.error = error;
    }

    public bool IsSuccess => error is null;

    public T Value => IsSuccess ? value! : throw new InvalidOperationException($"The result failed with '{error!.Code}'; it has no value.");

    public Error Error => error ?? throw new InvalidOperationException("The result succeeded; it has no error.");

    public static Result<T> Success(T value) => new(value, null);

    public static Result<T> Failure(Error error) => new(default, error ?? throw new ArgumentNullException(nameof(error)));

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(Error error) => Failure(error);
}
