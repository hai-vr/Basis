public readonly struct Result<T>
{
    public bool IsSuccess { get; }
    public T Value { get; }
    public string Error { get; }
    public long ResponseCode { get; }

    private Result(bool ok, T value, string error, long code)
    {
        IsSuccess = ok; Value = value; Error = error; ResponseCode = code;
    }

    public static Result<T> Ok(T value) => new(true, value, null, -1);
    public static Result<T> Fail(string error, long responseCode = -1) => new(false, default, error, responseCode);

    public void Deconstruct(out bool ok, out T value, out string error)
    { ok = IsSuccess; value = Value; error = Error; }

    public override string ToString()
        => IsSuccess ? $"OK: {Value}" : $"FAIL[{ResponseCode.ToString() ?? "-"}]: {Error}";
}
