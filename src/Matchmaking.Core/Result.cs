namespace Matchmaking.Core;

public sealed record DomainError(string Code, string Message);

public abstract record Result<T>
{
    private Result() { }
    public sealed record Success(T Value) : Result<T>;
    public sealed record Failure(DomainError Error) : Result<T>;

    public static Result<T> Ok(T value) => value is null
        ? throw new ArgumentNullException(nameof(value))
        : new Success(value);

    public static Result<T> Fail(string code, string message) =>
        new Failure(new DomainError(code, message));
}

public static class ResultExtensions
{
    public static Result<U> Map<T, U>(this Result<T> source, Func<T, U> transform) => source switch
    {
        Result<T>.Success success => Result<U>.Ok(transform(success.Value)),
        Result<T>.Failure failure => new Result<U>.Failure(failure.Error),
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };

    public static Result<U> Bind<T, U>(this Result<T> source, Func<T, Result<U>> next) => source switch
    {
        Result<T>.Success success => next(success.Value),
        Result<T>.Failure failure => new Result<U>.Failure(failure.Error),
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };
}

public abstract record Option<T>
{
    private Option() { }
    public sealed record Some(T Value) : Option<T>;
    public sealed record None : Option<T>;
}
