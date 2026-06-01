namespace PoRedoImage.Domain;

/// <summary>
/// Discriminated-union result type for repository / service operations that can fail
/// in expected ways (storage unavailable, not configured, circuit open). Replaces the
/// silent no-op pattern (return null / return []) that hid Po2Logic Failure #9.
/// <para>
/// Pattern-match: <c>result.Match(ok => ..., err => ...)</c> or use <see cref="IsSuccess"/>
/// + <see cref="Value"/> / <see cref="Error"/> in a single branch.
/// </para>
/// </summary>
public readonly struct Result<T, E>
{
    public T? Value { get; }
    public E? Error { get; }
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    private Result(T value)
    {
        Value = value;
        Error = default;
        IsSuccess = true;
    }

    private Result(E error)
    {
        Value = default;
        Error = error;
        IsSuccess = false;
    }

    public static Result<T, E> Ok(T value) => new(value);
    public static Result<T, E> Fail(E error) => new(error);

    public TResult Match<TResult>(Func<T, TResult> onOk, Func<E, TResult> onErr) =>
        IsSuccess ? onOk(Value!) : onErr(Error!);
}

/// <summary>Typed storage error categories — distinguishes "not configured" from "transient".</summary>
public enum StorageError
{
    NotConfigured = 0,
    NotFound = 1,
    TransientFailure = 2,
    Conflict = 3,
    CircuitOpen = 4,
    Unknown = 99
}
