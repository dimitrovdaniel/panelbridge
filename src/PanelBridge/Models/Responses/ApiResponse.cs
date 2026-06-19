using System.Text.Json.Serialization;

namespace PanelBridge.Models.Responses;

public sealed record ApiResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("panel")] string? Panel,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("data"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? Data = null)
{
    public const string StatusSuccess = "success";
    public const string StatusFailure = "failure";
    public const string StatusInvalidRequest = "invalid_request";
    public const string StatusNotFound = "not_found";
    public const string StatusNotSupportedOnPanel = "not_supported_on_panel";
    public const string StatusPanelUnavailable = "panel_unavailable";

    public static ApiResponse Success(string? panel, string action, string message, object? data = null)
        => new(StatusSuccess, panel, action, message, data);

    public static ApiResponse Failure(string? panel, string action, string message)
        => new(StatusFailure, panel, action, message);

    public static ApiResponse InvalidRequest(string action, string message)
        => new(StatusInvalidRequest, null, action, message);

    public static ApiResponse NotFound(string action, string message)
        => new(StatusNotFound, null, action, message);

    public static ApiResponse Disabled(string action, string message)
        => new("disabled", null, action, message);

    public static ApiResponse NotSupportedOnPanel(string panel, string action)
        => new(StatusNotSupportedOnPanel, panel, action,
            $"Action '{action}' is not supported on panel '{panel}'.");

    public static ApiResponse PanelUnavailable(string panel, string action, string message)
        => new(StatusPanelUnavailable, panel, action, message);
}
