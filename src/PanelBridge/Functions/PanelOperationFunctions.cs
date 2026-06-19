using System.Net;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;
using PanelBridge.Models.Requests;
using PanelBridge.Models.Responses;
using PanelBridge.Panels;

namespace PanelBridge.Functions;

/// <summary>
/// Panel-scoped operations that aren't tied to an individual case in caselookup.
/// All require a "panel" field in the request body (or query string for GET routes).
/// </summary>
public sealed class PanelOperationFunctions(PanelClientRegistry panels, DocumentsFeature documents)
{
    private const string NotesTag = "Notes";
    private const string DocsTag = "Documents";
    private const string RefTag = "Reference data";

    [Function("RemoveNote")]
    [OpenApiOperation(operationId: "removeNote", tags: new[] { NotesTag }, Summary = "Remove a note by guid", Description = "Econ-only operation (SortRefer has no equivalent).")]
    [OpenApiSecurity("api_key", SecuritySchemeType.ApiKey, Name = "X-API-Key", In = OpenApiSecurityLocationType.Header)]
    [OpenApiSecurity("oauth2", SecuritySchemeType.OAuth2, Flows = typeof(PanelBridge.Security.OpenApiSecurityFlows))]
    [OpenApiRequestBody("application/json", typeof(PanelGuidRequest), Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, "application/json", typeof(ApiResponse), Description = "not_supported_on_panel for SortRefer")]
    public async Task<IActionResult> RemoveNote(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "case/notes/remove")] HttpRequest req,
        CancellationToken ct) =>
        await PanelGuidDispatch(req, ct, "notes-remove", (c, guid) => c.RemoveNoteAsync(guid, ct));

    [Function("ListAllDocuments")]
    [OpenApiOperation(operationId: "listAllDocuments", tags: new[] { DocsTag }, Summary = "List all new documents across cases", Description = "Returns signed download links and per-document guids. Panel XML is normalised to JSON.")]
    [OpenApiSecurity("api_key", SecuritySchemeType.ApiKey, Name = "X-API-Key", In = OpenApiSecurityLocationType.Header)]
    [OpenApiSecurity("oauth2", SecuritySchemeType.OAuth2, Flows = typeof(PanelBridge.Security.OpenApiSecurityFlows))]
    [OpenApiRequestBody("application/json", typeof(PanelOnlyRequest), Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, "application/json", typeof(ApiResponse))]
    public async Task<IActionResult> ListAllDocuments(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "documents/list")] HttpRequest req,
        CancellationToken ct)
    {
        const string action = "documents-list-all";
        if (!documents.Enabled)
            return new NotFoundObjectResult(ApiResponse.Disabled(action,
                "The /documents/* endpoints are disabled in this environment."));
        return await PanelOnlyDispatchWithJsonData(req, ct, action, c => c.ListAllDocumentsAsync(ct));
    }

    [Function("MarkDocumentRead")]
    [OpenApiOperation(operationId: "markDocumentRead", tags: new[] { DocsTag }, Summary = "Mark a document as read by its guid")]
    [OpenApiSecurity("api_key", SecuritySchemeType.ApiKey, Name = "X-API-Key", In = OpenApiSecurityLocationType.Header)]
    [OpenApiSecurity("oauth2", SecuritySchemeType.OAuth2, Flows = typeof(PanelBridge.Security.OpenApiSecurityFlows))]
    [OpenApiParameter("id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "The document guid from list-all-documents")]
    [OpenApiRequestBody("application/json", typeof(PanelOnlyRequest), Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, "application/json", typeof(ApiResponse))]
    public async Task<IActionResult> MarkDocumentRead(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "documents/{id}/mark-read")] HttpRequest req,
        string id,
        CancellationToken ct)
    {
        const string action = "documents-mark-read";
        if (!documents.Enabled)
            return new NotFoundObjectResult(ApiResponse.Disabled(action,
                "The /documents/* endpoints are disabled in this environment."));

        var (body, error) = await RequestValidator.ReadJsonAsync<PanelOnlyRequest>(req, ct);
        if (error is not null) return HttpResults.InvalidRequest(action, error);
        if (!panels.TryGet(body!.Panel, out var client))
            return HttpResults.InvalidRequest(action, $"Unknown panel '{body.Panel}'.");
        var result = await client.MarkDocumentReadAsync(id, ct);
        return HttpResults.ToActionResult(result, body.Panel, action);
    }

    [Function("ListLenders")]
    [OpenApiOperation(operationId: "listLenders", tags: new[] { RefTag }, Summary = "List lenders configured on the panel", Description = "Returns lenders[] as { id, name } - panel XML is parsed server-side.")]
    [OpenApiSecurity("api_key", SecuritySchemeType.ApiKey, Name = "X-API-Key", In = OpenApiSecurityLocationType.Header)]
    [OpenApiSecurity("oauth2", SecuritySchemeType.OAuth2, Flows = typeof(PanelBridge.Security.OpenApiSecurityFlows))]
    [OpenApiParameter("panel", In = ParameterLocation.Query, Required = true, Type = typeof(string), Description = "Panel name (e.g. sortrefer, econ)")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(LenderListResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, "application/json", typeof(ApiResponse))]
    public async Task<IActionResult> ListLenders(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "lenders/list")] HttpRequest req,
        CancellationToken ct)
    {
        const string action = "lenders-list";
        var panel = req.Query["panel"].ToString();
        if (string.IsNullOrWhiteSpace(panel))
            return HttpResults.InvalidRequest(action, "Query string 'panel' is required.");
        if (!panels.TryGet(panel, out var client))
            return HttpResults.InvalidRequest(action, $"Unknown panel '{panel}'.");

        var result = await client.ListLendersAsync(ct);
        if (result.Status != PanelOperationStatus.Success)
            return HttpResults.ToActionResult(result, panel, action);

        var lenders = ParseLenders(result.Data as string);
        return new OkObjectResult(ApiResponse.Success(
            panel: panel, action: action,
            message: $"{lenders.Length} lender(s).",
            data: new LenderListData { Lenders = lenders }));
    }

    [Function("ListMilestones")]
    [OpenApiOperation(operationId: "listMilestones", tags: new[] { RefTag }, Summary = "List milestones configured on the panel", Description = "Returns the panel-side milestone catalogue. Panel XML is normalised to JSON.")]
    [OpenApiSecurity("api_key", SecuritySchemeType.ApiKey, Name = "X-API-Key", In = OpenApiSecurityLocationType.Header)]
    [OpenApiSecurity("oauth2", SecuritySchemeType.OAuth2, Flows = typeof(PanelBridge.Security.OpenApiSecurityFlows))]
    [OpenApiParameter("panel", In = ParameterLocation.Query, Required = true, Type = typeof(string), Description = "Panel name (e.g. sortrefer, econ)")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, "application/json", typeof(ApiResponse))]
    public async Task<IActionResult> ListMilestones(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "milestones/list")] HttpRequest req,
        CancellationToken ct)
    {
        const string action = "milestones-list";
        var panel = req.Query["panel"].ToString();
        if (string.IsNullOrWhiteSpace(panel))
            return HttpResults.InvalidRequest(action, "Query string 'panel' is required.");
        if (!panels.TryGet(panel, out var client))
            return HttpResults.InvalidRequest(action, $"Unknown panel '{panel}'.");

        var result = await client.ListMilestonesAsync(ct);
        if (result.Status != PanelOperationStatus.Success)
            return HttpResults.ToActionResult(result, panel, action);

        return new OkObjectResult(ApiResponse.Success(
            panel: panel, action: action,
            message: result.Message,
            data: XmlJson.FromXml(result.Data as string)));
    }

    private async Task<IActionResult> PanelOnlyDispatchWithJsonData(
        HttpRequest req,
        CancellationToken ct,
        string action,
        Func<IPanelClient, Task<PanelOperationResult>> call)
    {
        var (body, error) = await RequestValidator.ReadJsonAsync<PanelOnlyRequest>(req, ct);
        if (error is not null) return HttpResults.InvalidRequest(action, error);
        if (!panels.TryGet(body!.Panel, out var client))
            return HttpResults.InvalidRequest(action, $"Unknown panel '{body.Panel}'.");
        var result = await call(client);
        if (result.Status != PanelOperationStatus.Success)
            return HttpResults.ToActionResult(result, body.Panel, action);
        return new OkObjectResult(ApiResponse.Success(
            panel: body.Panel, action: action,
            message: result.Message,
            data: XmlJson.FromXml(result.Data as string)));
    }

    private async Task<IActionResult> PanelGuidDispatch(
        HttpRequest req,
        CancellationToken ct,
        string action,
        Func<IPanelClient, string, Task<PanelOperationResult>> call)
    {
        var (body, error) = await RequestValidator.ReadJsonAsync<PanelGuidRequest>(req, ct);
        if (error is not null) return HttpResults.InvalidRequest(action, error);
        if (!panels.TryGet(body!.Panel, out var client))
            return HttpResults.InvalidRequest(action, $"Unknown panel '{body.Panel}'.");
        var result = await call(client, body.Guid);
        return HttpResults.ToActionResult(result, body.Panel, action);
    }

    private static LenderItem[] ParseLenders(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return Array.Empty<LenderItem>();
        try
        {
            var doc = XDocument.Parse(xml);
            var root = doc.Root;
            if (root is null) return Array.Empty<LenderItem>();

            // SortRefer wraps lenders in <response><lenders><lender><key/><value/></lender>...</lenders></response>.
            var container = root.Name.LocalName.Equals("response", StringComparison.OrdinalIgnoreCase)
                ? root.Element(XName.Get("lenders")) ?? root.Element(XName.Get("Lenders"))
                : root.Name.LocalName.Equals("lenders", StringComparison.OrdinalIgnoreCase) ? root : null;
            if (container is null) return Array.Empty<LenderItem>();

            return container.Elements()
                .Where(e => e.Name.LocalName.Equals("lender", StringComparison.OrdinalIgnoreCase))
                .Select(e => new LenderItem
                {
                    Id = e.Element(XName.Get("key"))?.Value?.Trim()
                         ?? e.Element(XName.Get("id"))?.Value?.Trim()
                         ?? "",
                    Name = e.Element(XName.Get("value"))?.Value?.Trim()
                           ?? e.Element(XName.Get("name"))?.Value?.Trim()
                           ?? "",
                })
                .ToArray();
        }
        catch
        {
            return Array.Empty<LenderItem>();
        }
    }
}
