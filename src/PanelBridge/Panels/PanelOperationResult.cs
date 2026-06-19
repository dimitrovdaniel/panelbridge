namespace PanelBridge.Panels;

public enum PanelOperationStatus
{
    Success,
    Failure,
    NotSupported,
    Unavailable,
}

public sealed record PanelOperationResult(
    PanelOperationStatus Status,
    string Message,
    object? Data = null)
{
    public static PanelOperationResult Success(string message, object? data = null)
        => new(PanelOperationStatus.Success, message, data);

    public static PanelOperationResult Failure(string message)
        => new(PanelOperationStatus.Failure, message);

    public static PanelOperationResult NotSupported(string action)
        => new(PanelOperationStatus.NotSupported, $"Action '{action}' is not supported on this panel.");

    public static PanelOperationResult Unavailable(string message)
        => new(PanelOperationStatus.Unavailable, message);
}
