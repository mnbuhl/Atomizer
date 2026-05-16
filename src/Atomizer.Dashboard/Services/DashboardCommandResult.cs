namespace Atomizer.Dashboard.Services;

internal sealed class DashboardCommandResult<T>
{
    private DashboardCommandResult(int statusCode, T? value, string? message)
    {
        StatusCode = statusCode;
        Value = value;
        Message = message;
    }

    public int StatusCode { get; }
    public T? Value { get; }
    public string? Message { get; }

    public static DashboardCommandResult<T> Ok(T value) => new(200, value, null);

    public static DashboardCommandResult<T> BadRequest(string message) => new(400, default, message);

    public static DashboardCommandResult<T> NotFound(string message) => new(404, default, message);

    public static DashboardCommandResult<T> Conflict(string message) => new(409, default, message);
}
