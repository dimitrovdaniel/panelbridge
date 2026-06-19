using System.Text.Json.Serialization;

namespace PanelBridge.Models.Responses;

// These mirror ApiResponse's shape but with typed `data` for OpenAPI generation.
// Functions still return `ApiResponse` via OkObjectResult - serialisation is identical.

public sealed class HandlerSummary
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("firstName")] public string FirstName { get; set; } = "";
    [JsonPropertyName("lastName")] public string LastName { get; set; } = "";
    [JsonPropertyName("email")] public string Email { get; set; } = "";
    [JsonPropertyName("telephone")] public string Telephone { get; set; } = "";
    [JsonPropertyName("panels")] public string[] Panels { get; set; } = Array.Empty<string>();
}

public sealed class HandlerListData
{
    [JsonPropertyName("handlers")] public HandlerSummary[] Handlers { get; set; } = Array.Empty<HandlerSummary>();
}

public sealed class HandlerListResponse
{
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("panel")] public string? Panel { get; set; }
    [JsonPropertyName("action")] public string Action { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("data")] public HandlerListData? Data { get; set; }
}

public sealed class LenderItem
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
}

public sealed class LenderListData
{
    [JsonPropertyName("lenders")] public LenderItem[] Lenders { get; set; } = Array.Empty<LenderItem>();
}

public sealed class LenderListResponse
{
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("panel")] public string? Panel { get; set; }
    [JsonPropertyName("action")] public string Action { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("data")] public LenderListData? Data { get; set; }
}

public sealed class CaseSummary
{
    [JsonPropertyName("universalId")] public Guid UniversalId { get; set; }
    [JsonPropertyName("panel")] public string Panel { get; set; } = "";
    [JsonPropertyName("panelRef")] public string PanelRef { get; set; } = "";
    [JsonPropertyName("internalRef")] public string? InternalRef { get; set; }
    [JsonPropertyName("regionId")] public int? RegionId { get; set; }
    [JsonPropertyName("caseTypeId")] public int CaseTypeId { get; set; }
    [JsonPropertyName("caseType")] public string CaseType { get; set; } = "unknown";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("assignedCaseHandlerId")] public int? AssignedCaseHandlerId { get; set; }
    [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; set; }
    [JsonPropertyName("updatedAt")] public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CaseQueryData
{
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("skip")] public int Skip { get; set; }
    [JsonPropertyName("take")] public int Take { get; set; }
    [JsonPropertyName("items")] public CaseSummary[] Items { get; set; } = Array.Empty<CaseSummary>();
}

public sealed class CaseQueryResponse
{
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("panel")] public string? Panel { get; set; }
    [JsonPropertyName("action")] public string Action { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("data")] public CaseQueryData? Data { get; set; }
}

public sealed class SetUniversalIdData
{
    [JsonPropertyName("universalId")] public Guid UniversalId { get; set; }
}

public sealed class SetUniversalIdResponse
{
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("panel")] public string? Panel { get; set; }
    [JsonPropertyName("action")] public string Action { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("data")] public SetUniversalIdData? Data { get; set; }
}

public sealed class AddNoteData
{
    /// <summary>
    /// Panel-side guid of the created note where the panel exposes one. SortRefer's
    /// add_note does not return a guid, so this field is null for SortRefer-routed cases.
    /// Econ's CreateCaseNote populates it.
    /// </summary>
    [JsonPropertyName("guid")] public string? Guid { get; set; }
}

public sealed class AddNoteResponse
{
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("panel")] public string? Panel { get; set; }
    [JsonPropertyName("action")] public string Action { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("data")] public AddNoteData? Data { get; set; }
}
