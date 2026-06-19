using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using PanelBridge.Domain;
using PanelBridge.Models.Requests;
using PanelBridge.Models.Responses;
using PanelBridge.Panels;
using PanelBridge.Persistence;

namespace PanelBridge.Functions;

public sealed class CaseLifecycleFunctions(
    PanelBridgeDbContext db,
    PanelClientRegistry panels,
    DocumentsFeature documents,
    ILogger<CaseLifecycleFunctions> logger)
{
    private const string Tag = "Case lifecycle";

    [Function("AcceptCase")]
    [OpenApiOperation(operationId: "acceptCase", tags: new[] { Tag }, Summary = "Accept a case",
        Description = "Assigns a case handler on the case's panel. Sets case status to Accepted on success.")]
    [OpenApiSecurity("api_key", SecuritySchemeType.ApiKey, Name = "X-API-Key", In = OpenApiSecurityLocationType.Header)]
    [OpenApiSecurity("oauth2", SecuritySchemeType.OAuth2, Flows = typeof(PanelBridge.Security.OpenApiSecurityFlows))]
    [OpenApiParameter("id", In = ParameterLocation.Path, Required = true, Type = typeof(Guid), Description = "PanelBridge universal id of the case")]
    [OpenApiRequestBody("application/json", typeof(AcceptCaseRequest), Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse), Description = "Panel success or business failure")]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, "application/json", typeof(ApiResponse), Description = "Invalid request or not supported on this panel")]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, "application/json", typeof(ApiResponse), Description = "Case not found")]
    [OpenApiResponseWithBody(HttpStatusCode.BadGateway, "application/json", typeof(ApiResponse), Description = "Panel unavailable / transport error")]
    public async Task<IActionResult> Accept(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "case/{id:guid}/accept")] HttpRequest req,
        Guid id,
        CancellationToken ct)
    {
        const string action = "accept";
        var (body, error) = await RequestValidator.ReadJsonAsync<AcceptCaseRequest>(req, ct);
        if (error is not null) return HttpResults.InvalidRequest(action, error);

        var ctx = await ResolveAsync(id, action, ct);
        if (ctx.Error is not null) return ctx.Error;

        var handler = await db.CaseHandlers.FirstOrDefaultAsync(h => h.Email == body!.CaseHandlerEmail, ct);
        if (handler is null)
            return HttpResults.NotFound(action, $"No case handler with email '{body!.CaseHandlerEmail}' in PanelBridge. Register them via handler/add first.");

        var details = new AcceptCaseDetails(
            handler.FullName, handler.Email, handler.Telephone, body!.InternalRef);

        var result = await ctx.Client!.AcceptAsync(ctx.Case!.PanelRef, details, ct);

        if (result.Status == PanelOperationStatus.Success)
        {
            ctx.Case!.InternalRef = body.InternalRef;
            ctx.Case!.Status = CaseStatus.Accepted;
            ctx.Case!.AssignedCaseHandlerId = handler.Id;
            await TouchAndSaveAsync(ctx.Case!, ct);
        }

        return HttpResults.ToActionResult(result, ctx.PanelKey!, action);
    }

    [Function("CancelCase")]
    [OpenApiOperation(operationId: "cancelCase", tags: new[] { Tag }, Summary = "Cancel a case", Description = "Requires a reason. Panel may return failure if already completed.")]
    [OpenApiSecurity("api_key", SecuritySchemeType.ApiKey, Name = "X-API-Key", In = OpenApiSecurityLocationType.Header)]
    [OpenApiSecurity("oauth2", SecuritySchemeType.OAuth2, Flows = typeof(PanelBridge.Security.OpenApiSecurityFlows))]
    [OpenApiParameter("id", In = ParameterLocation.Path, Required = true, Type = typeof(Guid))]
    [OpenApiRequestBody("application/json", typeof(ReasonOnlyRequest), Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, "application/json", typeof(ApiResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, "application/json", typeof(ApiResponse))]
    public async Task<IActionResult> Cancel(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "case/{id:guid}/cancel")] HttpRequest req,
        Guid id,
        CancellationToken ct)
        => await ReasonedAsync(req, id, "cancel", CaseStatus.Cancelled,
            (client, panelRef, reason) => client.CancelAsync(panelRef, reason, ct), ct);

    [Function("CompleteCase")]
    [OpenApiOperation(operationId: "completeCase", tags: new[] { Tag }, Summary = "Complete a case", Description = "Marks the case complete. One-way: panel will not accept re-opening once complete.")]
    [OpenApiSecurity("api_key", SecuritySchemeType.ApiKey, Name = "X-API-Key", In = OpenApiSecurityLocationType.Header)]
    [OpenApiSecurity("oauth2", SecuritySchemeType.OAuth2, Flows = typeof(PanelBridge.Security.OpenApiSecurityFlows))]
    [OpenApiParameter("id", In = ParameterLocation.Path, Required = true, Type = typeof(Guid))]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, "application/json", typeof(ApiResponse))]
    public async Task<IActionResult> Complete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "case/{id:guid}/complete")] HttpRequest req,
        Guid id,
        CancellationToken ct)
    {
        const string action = "complete";
        var ctx = await ResolveAsync(id, action, ct);
        if (ctx.Error is not null) return ctx.Error;

        var result = await ctx.Client!.CompleteAsync(ctx.Case!.PanelRef, ct);
        if (result.Status == PanelOperationStatus.Success)
        {
            ctx.Case!.Status = CaseStatus.Completed;
            await TouchAndSaveAsync(ctx.Case!, ct);
        }
        return HttpResults.ToActionResult(result, ctx.PanelKey!, action);
    }

    [Function("DeclineCase")]
    [OpenApiOperation(operationId: "declineCase", tags: new[] { Tag }, Summary = "Decline a case", Description = "Requires a reason. Can be reactivated later on supporting panels.")]
    [OpenApiSecurity("api_key", SecuritySchemeType.ApiKey, Name = "X-API-Key", In = OpenApiSecurityLocationType.Header)]
    [OpenApiSecurity("oauth2", SecuritySchemeType.OAuth2, Flows = typeof(PanelBridge.Security.OpenApiSecurityFlows))]
    [OpenApiParameter("id", In = ParameterLocation.Path, Required = true, Type = typeof(Guid))]
    [OpenApiRequestBody("application/json", typeof(ReasonOnlyRequest), Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, "application/json", typeof(ApiResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, "application/json", typeof(ApiResponse))]
    public async Task<IActionResult> Decline(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "case/{id:guid}/decline")] HttpRequest req,
        Guid id,
        CancellationToken ct)
        => await ReasonedAsync(req, id, "decline", CaseStatus.Declined,
            (client, panelRef, reason) => client.DeclineAsync(panelRef, reason, ct), ct);

    [Function("ReactivateCase")]
    [OpenApiOperation(operationId: "reactivateCase", tags: new[] { Tag }, Summary = "Reactivate a case", Description = "Reactivates a suspended or cancelled case. Returns 400 not_supported_on_panel for Econ cases.")]
    [OpenApiSecurity("api_key", SecuritySchemeType.ApiKey, Name = "X-API-Key", In = OpenApiSecurityLocationType.Header)]
    [OpenApiSecurity("oauth2", SecuritySchemeType.OAuth2, Flows = typeof(PanelBridge.Security.OpenApiSecurityFlows))]
    [OpenApiParameter("id", In = ParameterLocation.Path, Required = true, Type = typeof(Guid))]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, "application/json", typeof(ApiResponse), Description = "not_supported_on_panel for Econ-routed cases")]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, "application/json", typeof(ApiResponse))]
    public async Task<IActionResult> Reactivate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "case/{id:guid}/reactivate")] HttpRequest req,
        Guid id,
        CancellationToken ct)
    {
        const string action = "reactivate";
        var ctx = await ResolveAsync(id, action, ct);
        if (ctx.Error is not null) return ctx.Error;

        var result = await ctx.Client!.ReactivateAsync(ctx.Case!.PanelRef, ct);
        if (result.Status == PanelOperationStatus.Success)
        {
            ctx.Case!.Status = CaseStatus.Accepted;
            await TouchAndSaveAsync(ctx.Case!, ct);
        }
        return HttpResults.ToActionResult(result, ctx.PanelKey!, action);
    }

    [Function("SuspendCase")]
    [OpenApiOperation(operationId: "suspendCase", tags: new[] { Tag }, Summary = "Suspend a case", Description = "Requires a reason. Returns 400 not_supported_on_panel for Econ cases.")]
    [OpenApiSecurity("api_key", SecuritySchemeType.ApiKey, Name = "X-API-Key", In = OpenApiSecurityLocationType.Header)]
    [OpenApiSecurity("oauth2", SecuritySchemeType.OAuth2, Flows = typeof(PanelBridge.Security.OpenApiSecurityFlows))]
    [OpenApiParameter("id", In = ParameterLocation.Path, Required = true, Type = typeof(Guid))]
    [OpenApiRequestBody("application/json", typeof(ReasonOnlyRequest), Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, "application/json", typeof(ApiResponse), Description = "not_supported_on_panel for Econ-routed cases")]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, "application/json", typeof(ApiResponse))]
    public async Task<IActionResult> Suspend(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "case/{id:guid}/suspend")] HttpRequest req,
        Guid id,
        CancellationToken ct)
        => await ReasonedAsync(req, id, "suspend", CaseStatus.Suspended,
            (client, panelRef, reason) => client.SuspendAsync(panelRef, reason, ct), ct);

    [Function("ChangeHandler")]
    [OpenApiOperation(operationId: "changeHandler", tags: new[] { Tag }, Summary = "Change the case handler", Description = "Reassigns the case to a different handler on the panel. The handler must already exist in PanelBridge's casehandlers table - only the email is required; the rest is looked up.")]
    [OpenApiSecurity("api_key", SecuritySchemeType.ApiKey, Name = "X-API-Key", In = OpenApiSecurityLocationType.Header)]
    [OpenApiSecurity("oauth2", SecuritySchemeType.OAuth2, Flows = typeof(PanelBridge.Security.OpenApiSecurityFlows))]
    [OpenApiParameter("id", In = ParameterLocation.Path, Required = true, Type = typeof(Guid))]
    [OpenApiRequestBody("application/json", typeof(ChangeHandlerRequest), Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, "application/json", typeof(ApiResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, "application/json", typeof(ApiResponse))]
    public async Task<IActionResult> ChangeHandler(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "case/{id:guid}/change-handler")] HttpRequest req,
        Guid id,
        CancellationToken ct)
    {
        const string action = "change-handler";
        var (body, error) = await RequestValidator.ReadJsonAsync<ChangeHandlerRequest>(req, ct);
        if (error is not null) return HttpResults.InvalidRequest(action, error);

        var ctx = await ResolveAsync(id, action, ct);
        if (ctx.Error is not null) return ctx.Error;

        var handler = await db.CaseHandlers.FirstOrDefaultAsync(h => h.Email == body!.CaseHandlerEmail, ct);
        if (handler is null)
            return HttpResults.NotFound(action, $"No case handler with email '{body!.CaseHandlerEmail}' in PanelBridge. Register them via handler/add first.");

        var details = new AcceptCaseDetails(
            handler.FullName, handler.Email, handler.Telephone, InternalRef: null);
        var result = await ctx.Client!.ChangeHandlerAsync(ctx.Case!.PanelRef, details, ct);
        if (result.Status == PanelOperationStatus.Success)
        {
            ctx.Case!.AssignedCaseHandlerId = handler.Id;
            await TouchAndSaveAsync(ctx.Case!, ct);
        }
        return HttpResults.ToActionResult(result, ctx.PanelKey!, action);
    }

    [Function("FetchInstruction")]
    [OpenApiOperation(operationId: "fetchInstruction", tags: new[] { Tag }, Summary = "Fetch the panel-side instruction for a case", Description = "Returns the panel-side instruction document as JSON (panel XML is normalised).")]
    [OpenApiSecurity("api_key", SecuritySchemeType.ApiKey, Name = "X-API-Key", In = OpenApiSecurityLocationType.Header)]
    [OpenApiSecurity("oauth2", SecuritySchemeType.OAuth2, Flows = typeof(PanelBridge.Security.OpenApiSecurityFlows))]
    [OpenApiParameter("id", In = ParameterLocation.Path, Required = true, Type = typeof(Guid))]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, "application/json", typeof(ApiResponse))]
    public async Task<IActionResult> FetchInstruction(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "case/{id:guid}/fetch")] HttpRequest req,
        Guid id,
        CancellationToken ct)
    {
        const string action = "fetch";
        var ctx = await ResolveAsync(id, action, ct);
        if (ctx.Error is not null) return ctx.Error;

        var result = await ctx.Client!.FetchInstructionAsync(ctx.Case!.PanelRef, ct);
        if (result.Status == PanelOperationStatus.Success)
        {
            var parsed = XmlJson.FromXml(result.Data as string);
            object data = parsed is Dictionary<string, object?> dict && dict.TryGetValue("Instruction", out var instruction)
                ? new Dictionary<string, object?> { ["Instruction"] = instruction }
                : new Dictionary<string, object?> { ["Instruction"] = null };

            return new OkObjectResult(ApiResponse.Success(
                panel: ctx.PanelKey, action: action,
                message: result.Message,
                data: data));
        }
        return HttpResults.ToActionResult(result, ctx.PanelKey!, action);
    }

    [Function("MilestoneUpdate")]
    [OpenApiOperation(operationId: "milestoneUpdate", tags: new[] { Tag }, Summary = "Mark a panel milestone complete or incomplete", Description = "milestoneCode is the panel-native event id (e.g. '26' on SortRefer).")]
    [OpenApiSecurity("api_key", SecuritySchemeType.ApiKey, Name = "X-API-Key", In = OpenApiSecurityLocationType.Header)]
    [OpenApiSecurity("oauth2", SecuritySchemeType.OAuth2, Flows = typeof(PanelBridge.Security.OpenApiSecurityFlows))]
    [OpenApiParameter("id", In = ParameterLocation.Path, Required = true, Type = typeof(Guid))]
    [OpenApiRequestBody("application/json", typeof(MilestoneUpdateRequest), Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, "application/json", typeof(ApiResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, "application/json", typeof(ApiResponse))]
    public async Task<IActionResult> MilestoneUpdate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "case/{id:guid}/milestone-update")] HttpRequest req,
        Guid id,
        CancellationToken ct)
    {
        const string action = "milestone-update";
        var (body, error) = await RequestValidator.ReadJsonAsync<MilestoneUpdateRequest>(req, ct);
        if (error is not null) return HttpResults.InvalidRequest(action, error);

        var ctx = await ResolveAsync(id, action, ct);
        if (ctx.Error is not null) return ctx.Error;

        var result = await ctx.Client!.SetMilestoneAsync(ctx.Case!.PanelRef, body!.MilestoneCode, body.Completed, ct);
        return HttpResults.ToActionResult(result, ctx.PanelKey!, action);
    }

    [Function("SetInternalReference")]
    [OpenApiOperation(operationId: "setInternalReference", tags: new[] { Tag }, Summary = "Update the internal reference on the panel side", Description = "Calls update_supplier_reference on SortRefer. Also updates PanelBridge's local internalRef on success.")]
    [OpenApiSecurity("api_key", SecuritySchemeType.ApiKey, Name = "X-API-Key", In = OpenApiSecurityLocationType.Header)]
    [OpenApiSecurity("oauth2", SecuritySchemeType.OAuth2, Flows = typeof(PanelBridge.Security.OpenApiSecurityFlows))]
    [OpenApiParameter("id", In = ParameterLocation.Path, Required = true, Type = typeof(Guid))]
    [OpenApiRequestBody("application/json", typeof(SetSupplierReferenceRequest), Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, "application/json", typeof(ApiResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, "application/json", typeof(ApiResponse))]
    public async Task<IActionResult> SetInternalReference(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "case/{id:guid}/set-internal-reference")] HttpRequest req,
        Guid id,
        CancellationToken ct)
    {
        const string action = "set-internal-reference";
        var (body, error) = await RequestValidator.ReadJsonAsync<SetSupplierReferenceRequest>(req, ct);
        if (error is not null) return HttpResults.InvalidRequest(action, error);

        var ctx = await ResolveAsync(id, action, ct);
        if (ctx.Error is not null) return ctx.Error;

        var result = await ctx.Client!.SetSupplierReferenceAsync(ctx.Case!.PanelRef, body!.SupplierReference, ct);
        if (result.Status == PanelOperationStatus.Success)
        {
            ctx.Case!.InternalRef = body.SupplierReference;
            await TouchAndSaveAsync(ctx.Case!, ct);
        }
        return HttpResults.ToActionResult(result, ctx.PanelKey!, action);
    }

    [Function("FetchQuote")]
    [OpenApiOperation(operationId: "fetchQuote", tags: new[] { Tag }, Summary = "Fetch the quotation PDF for a case", Description = "Calls SortRefer's get_quote_pdf. Panel XML is normalised to JSON; the base64-encoded PDF stream sits inside data.response.pdf (or similar leaf node) per the panel's XML.")]
    [OpenApiSecurity("api_key", SecuritySchemeType.ApiKey, Name = "X-API-Key", In = OpenApiSecurityLocationType.Header)]
    [OpenApiSecurity("oauth2", SecuritySchemeType.OAuth2, Flows = typeof(PanelBridge.Security.OpenApiSecurityFlows))]
    [OpenApiParameter("id", In = ParameterLocation.Path, Required = true, Type = typeof(Guid))]
    [OpenApiRequestBody("application/json", typeof(FetchQuoteRequest), Required = false)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, "application/json", typeof(ApiResponse))]
    public async Task<IActionResult> FetchQuote(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "case/{id:guid}/fetch-quote")] HttpRequest req,
        Guid id,
        CancellationToken ct)
    {
        const string action = "fetch-quote";
        var (body, _) = await RequestValidator.ReadJsonAsync<FetchQuoteRequest>(req, ct);
        var zip = body?.Zip ?? false;

        var ctx = await ResolveAsync(id, action, ct);
        if (ctx.Error is not null) return ctx.Error;

        var result = await ctx.Client!.FetchQuotePdfAsync(ctx.Case!.PanelRef, zip, ct);
        if (result.Status == PanelOperationStatus.Success)
        {
            return new OkObjectResult(ApiResponse.Success(
                panel: ctx.PanelKey, action: action,
                message: result.Message,
                data: XmlJson.FromXml(result.Data as string)));
        }
        return HttpResults.ToActionResult(result, ctx.PanelKey!, action);
    }

    [Function("SetEstimatedCompletion")]
    [OpenApiOperation(operationId: "setEstimatedCompletion", tags: new[] { Tag }, Summary = "Set the estimated completion date for a case", Description = "Date must be yyyy-MM-dd and in the future per SortRefer.")]
    [OpenApiSecurity("api_key", SecuritySchemeType.ApiKey, Name = "X-API-Key", In = OpenApiSecurityLocationType.Header)]
    [OpenApiSecurity("oauth2", SecuritySchemeType.OAuth2, Flows = typeof(PanelBridge.Security.OpenApiSecurityFlows))]
    [OpenApiParameter("id", In = ParameterLocation.Path, Required = true, Type = typeof(Guid))]
    [OpenApiRequestBody("application/json", typeof(SetEstimatedCompletionRequest), Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, "application/json", typeof(ApiResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, "application/json", typeof(ApiResponse))]
    public async Task<IActionResult> SetEstimatedCompletion(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "case/{id:guid}/set-est-completion")] HttpRequest req,
        Guid id,
        CancellationToken ct)
    {
        const string action = "set-est-completion";
        var (body, error) = await RequestValidator.ReadJsonAsync<SetEstimatedCompletionRequest>(req, ct);
        if (error is not null) return HttpResults.InvalidRequest(action, error);

        var ctx = await ResolveAsync(id, action, ct);
        if (ctx.Error is not null) return ctx.Error;

        var result = await ctx.Client!.SetEstimatedCompletionAsync(ctx.Case!.PanelRef, body!.Date, ct);
        return HttpResults.ToActionResult(result, ctx.PanelKey!, action);
    }

    [Function("AddNote")]
    [OpenApiOperation(operationId: "addNote", tags: new[] { "Notes" }, Summary = "Add a note to a case", Description = "Note is wrapped in CDATA. isPrivate=true means visible only to the panel + case handlers, not the introducer. Response data.guid is the note guid where the panel exposes one (Econ does, SortRefer's add_note does not).")]
    [OpenApiSecurity("api_key", SecuritySchemeType.ApiKey, Name = "X-API-Key", In = OpenApiSecurityLocationType.Header)]
    [OpenApiSecurity("oauth2", SecuritySchemeType.OAuth2, Flows = typeof(PanelBridge.Security.OpenApiSecurityFlows))]
    [OpenApiRequestBody("application/json", typeof(AddNoteRequest), Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(AddNoteResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, "application/json", typeof(ApiResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, "application/json", typeof(ApiResponse))]
    public async Task<IActionResult> AddNote(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "case/notes/add")] HttpRequest req,
        CancellationToken ct)
    {
        const string action = "notes-add";
        var (body, error) = await RequestValidator.ReadJsonAsync<AddNoteRequest>(req, ct);
        if (error is not null) return HttpResults.InvalidRequest(action, error);

        var ctx = await ResolveAsync(body!.UniversalId, action, ct);
        if (ctx.Error is not null) return ctx.Error;

        var result = await ctx.Client!.AddNoteAsync(ctx.Case!.PanelRef, body.Text, body.IsPrivate, ct);

        if (result.Status == PanelOperationStatus.Success)
        {
            return new OkObjectResult(ApiResponse.Success(
                panel: ctx.PanelKey, action: action,
                message: result.Message,
                data: new { guid = result.Data as string }));
        }
        return HttpResults.ToActionResult(result, ctx.PanelKey!, action);
    }

    [Function("ListDocumentsForCase")]
    [OpenApiOperation(operationId: "listDocumentsForCase", tags: new[] { "Documents" }, Summary = "List documents on a case", Description = "Returns signed download links valid for 120 hours per SortRefer. Panel XML is normalised to JSON.")]
    [OpenApiSecurity("api_key", SecuritySchemeType.ApiKey, Name = "X-API-Key", In = OpenApiSecurityLocationType.Header)]
    [OpenApiSecurity("oauth2", SecuritySchemeType.OAuth2, Flows = typeof(PanelBridge.Security.OpenApiSecurityFlows))]
    [OpenApiParameter("id", In = ParameterLocation.Path, Required = true, Type = typeof(Guid), Description = "PanelBridge universal id of the case")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, "application/json", typeof(ApiResponse))]
    public async Task<IActionResult> ListDocumentsForCase(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "documents/{id:guid}/list")] HttpRequest req,
        Guid id,
        CancellationToken ct)
    {
        const string action = "documents-list";
        if (!documents.Enabled)
            return new NotFoundObjectResult(ApiResponse.Disabled(action,
                "The /documents/* endpoints are disabled in this environment."));

        var ctx = await ResolveAsync(id, action, ct);
        if (ctx.Error is not null) return ctx.Error;

        var result = await ctx.Client!.ListDocumentsForCaseAsync(ctx.Case!.PanelRef, ct);
        if (result.Status == PanelOperationStatus.Success)
        {
            return new OkObjectResult(ApiResponse.Success(
                panel: ctx.PanelKey, action: action,
                message: result.Message,
                data: XmlJson.FromXml(result.Data as string)));
        }
        return HttpResults.ToActionResult(result, ctx.PanelKey!, action);
    }

    private async Task<IActionResult> ReasonedAsync(
        HttpRequest req,
        Guid id,
        string action,
        CaseStatus statusOnSuccess,
        Func<IPanelClient, string, string, Task<PanelOperationResult>> invoke,
        CancellationToken ct)
    {
        var (body, error) = await RequestValidator.ReadJsonAsync<ReasonOnlyRequest>(req, ct);
        if (error is not null) return HttpResults.InvalidRequest(action, error);

        var ctx = await ResolveAsync(id, action, ct);
        if (ctx.Error is not null) return ctx.Error;

        var result = await invoke(ctx.Client!, ctx.Case!.PanelRef, body!.Reason);
        if (result.Status == PanelOperationStatus.Success)
        {
            ctx.Case!.Status = statusOnSuccess;
            await TouchAndSaveAsync(ctx.Case!, ct);
        }
        return HttpResults.ToActionResult(result, ctx.PanelKey!, action);
    }

    private async Task<ResolveContext> ResolveAsync(Guid id, string action, CancellationToken ct)
    {
        var caseRecord = await db.CaseLookups.FirstOrDefaultAsync(c => c.UniversalId == id, ct);
        if (caseRecord is null)
        {
            logger.LogInformation("Case {Id} not found for action {Action}", id, action);
            return new ResolveContext { Error = HttpResults.NotFound(action, $"Case '{id}' not found.") };
        }

        var panel = await db.Panels.FirstOrDefaultAsync(p => p.Id == caseRecord.PanelId, ct);
        if (panel is null)
            return new ResolveContext
            {
                Error = HttpResults.NotFound(action, $"Panel for case '{id}' is not configured.")
            };

        if (!panels.TryGet(panel.Name, out var client))
            return new ResolveContext
            {
                Error = HttpResults.InvalidRequest(action, $"No client configured for panel '{panel.Name}'.")
            };

        return new ResolveContext { Case = caseRecord, Client = client, PanelKey = panel.Name };
    }

    private async Task TouchAndSaveAsync(CaseLookup c, CancellationToken ct)
    {
        c.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private sealed class ResolveContext
    {
        public CaseLookup? Case { get; set; }
        public IPanelClient? Client { get; set; }
        public string? PanelKey { get; set; }
        public IActionResult? Error { get; set; }
    }
}
