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

public sealed class HandlerFunctions(
    PanelBridgeDbContext db,
    PanelClientRegistry panels,
    ILogger<HandlerFunctions> logger)
{
    private const string Tag = "Case handlers";

    [Function("AddHandler")]
    [OpenApiOperation(operationId: "addHandler", tags: new[] { Tag }, Summary = "Create a case handler in PanelBridge and register them on the named panels", Description = "Inserts into PanelBridge's casehandlers + casehandlers_panels tables, then dispatches add_case_handler to each panel in memberPanels. Aggregated panel results are returned alongside the persisted handler.")]
    [OpenApiSecurity("api_key", SecuritySchemeType.ApiKey, Name = "X-API-Key", In = OpenApiSecurityLocationType.Header)]
    [OpenApiSecurity("oauth2", SecuritySchemeType.OAuth2, Flows = typeof(PanelBridge.Security.OpenApiSecurityFlows))]
    [OpenApiRequestBody("application/json", typeof(AddHandlerRequest), Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, "application/json", typeof(ApiResponse))]
    public async Task<IActionResult> Add(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "handler/add")] HttpRequest req,
        CancellationToken ct)
    {
        const string action = "handler-add";
        var (body, error) = await RequestValidator.ReadJsonAsync<AddHandlerRequest>(req, ct);
        if (error is not null) return HttpResults.InvalidRequest(action, error);

        var requestedPanels = (body!.MemberPanels ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var p in requestedPanels)
            if (!panels.TryGet(p, out _))
                return HttpResults.InvalidRequest(action, $"Unknown panel '{p}'.");

        var existing = await db.CaseHandlers
            .FirstOrDefaultAsync(h => h.Email == body.Email, ct);
        if (existing is not null)
            return HttpResults.InvalidRequest(action, $"A case handler with email '{body.Email}' already exists.");

        var handler = new CaseHandler
        {
            FirstName = body.FirstName,
            LastName = body.LastName,
            Email = body.Email,
            Telephone = body.Telephone,
        };
        db.CaseHandlers.Add(handler);
        await db.SaveChangesAsync(ct);

        var panelEntities = await db.Panels.ToListAsync(ct);
        var panelResults = new List<object>();
        foreach (var panelName in requestedPanels)
        {
            var panel = panelEntities.First(p =>
                string.Equals(p.Name, panelName, StringComparison.OrdinalIgnoreCase));
            db.CaseHandlerPanels.Add(new CaseHandlerPanel
            {
                CaseHandlerId = handler.Id,
                PanelId = panel.Id,
            });

            var client = panels.Get(panel.Name);
            var result = await client.AddHandlerAsync(
                new HandlerDetails(handler.FullName, handler.Email, handler.Telephone), ct);
            panelResults.Add(new
            {
                panel = panel.Name,
                status = ToStatusString(result.Status),
                message = result.Message,
            });
        }
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Added handler {Email} ({HandlerId}) on panels {Panels}",
            handler.Email, handler.Id, string.Join(",", requestedPanels));

        return new OkObjectResult(ApiResponse.Success(
            panel: null, action: action,
            message: $"Handler '{handler.Email}' added.",
            data: new
            {
                handler = ToDto(handler, requestedPanels),
                panelResults,
            }));
    }

    [Function("EditHandler")]
    [OpenApiOperation(operationId: "editHandler", tags: new[] { Tag }, Summary = "Update an PanelBridge case handler and propagate changes to their panels", Description = "Updates the casehandlers row, propagates editable fields (email, telephone, password) to every panel the handler is on, and optionally registers them on additional panels via addMemberPanel.")]
    [OpenApiSecurity("api_key", SecuritySchemeType.ApiKey, Name = "X-API-Key", In = OpenApiSecurityLocationType.Header)]
    [OpenApiSecurity("oauth2", SecuritySchemeType.OAuth2, Flows = typeof(PanelBridge.Security.OpenApiSecurityFlows))]
    [OpenApiRequestBody("application/json", typeof(EditHandlerRequest), Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, "application/json", typeof(ApiResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, "application/json", typeof(ApiResponse))]
    public async Task<IActionResult> Edit(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "handler/edit")] HttpRequest req,
        CancellationToken ct)
    {
        const string action = "handler-edit";
        var (body, error) = await RequestValidator.ReadJsonAsync<EditHandlerRequest>(req, ct);
        if (error is not null) return HttpResults.InvalidRequest(action, error);

        var addPanels = (body!.AddMemberPanel ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var p in addPanels)
            if (!panels.TryGet(p, out _))
                return HttpResults.InvalidRequest(action, $"Unknown panel '{p}'.");

        var handler = await db.CaseHandlers.FirstOrDefaultAsync(h => h.Email == body.Email, ct);
        if (handler is null)
            return HttpResults.NotFound(action, $"No case handler with email '{body.Email}'.");

        var panelEntities = await db.Panels.ToListAsync(ct);
        var memberPanelIds = await db.CaseHandlerPanels
            .Where(chp => chp.CaseHandlerId == handler.Id)
            .Select(chp => chp.PanelId)
            .ToListAsync(ct);
        var memberPanels = panelEntities.Where(p => memberPanelIds.Contains(p.Id)).ToList();

        var update = new HandlerUpdate(handler.Email,
            NewEmail: body.NewEmail,
            NewTelephone: body.NewTelephone,
            NewMobile: body.NewMobile,
            NewPassword: body.NewPassword);

        var panelResults = new List<object>();
        foreach (var panel in memberPanels)
        {
            var client = panels.Get(panel.Name);
            var result = await client.EditHandlerAsync(update, ct);
            panelResults.Add(new
            {
                panel = panel.Name,
                status = ToStatusString(result.Status),
                message = result.Message,
            });
        }

        if (!string.IsNullOrWhiteSpace(body.NewEmail)) handler.Email = body.NewEmail;
        if (!string.IsNullOrWhiteSpace(body.NewTelephone)) handler.Telephone = body.NewTelephone;

        foreach (var panelName in addPanels)
        {
            var panel = panelEntities.First(p =>
                string.Equals(p.Name, panelName, StringComparison.OrdinalIgnoreCase));
            var alreadyMember = memberPanelIds.Contains(panel.Id);
            if (!alreadyMember)
            {
                db.CaseHandlerPanels.Add(new CaseHandlerPanel
                {
                    CaseHandlerId = handler.Id,
                    PanelId = panel.Id,
                });
                memberPanelIds.Add(panel.Id);
            }

            var client = panels.Get(panel.Name);
            var addResult = await client.AddHandlerAsync(
                new HandlerDetails(handler.FullName, handler.Email, handler.Telephone), ct);
            panelResults.Add(new
            {
                panel = panel.Name,
                status = ToStatusString(addResult.Status),
                message = alreadyMember
                    ? $"Already a member; re-sent add. {addResult.Message}"
                    : addResult.Message,
            });
        }

        await db.SaveChangesAsync(ct);

        var finalPanelNames = panelEntities
            .Where(p => memberPanelIds.Contains(p.Id))
            .Select(p => p.Name)
            .ToArray();

        logger.LogInformation("Edited handler {HandlerId}; panels now {Panels}",
            handler.Id, string.Join(",", finalPanelNames));

        return new OkObjectResult(ApiResponse.Success(
            panel: null, action: action,
            message: $"Handler '{handler.Email}' updated.",
            data: new
            {
                handler = ToDto(handler, finalPanelNames),
                panelResults,
            }));
    }

    [Function("ListHandlers")]
    [OpenApiOperation(operationId: "listHandlers", tags: new[] { Tag }, Summary = "List PanelBridge case handlers with their panel memberships")]
    [OpenApiSecurity("api_key", SecuritySchemeType.ApiKey, Name = "X-API-Key", In = OpenApiSecurityLocationType.Header)]
    [OpenApiSecurity("oauth2", SecuritySchemeType.OAuth2, Flows = typeof(PanelBridge.Security.OpenApiSecurityFlows))]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(HandlerListResponse))]
    public async Task<IActionResult> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "handler/list")] HttpRequest req,
        CancellationToken ct)
    {
        const string action = "handler-list";

        var panelEntities = await db.Panels.ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var memberships = await db.CaseHandlerPanels
            .AsNoTracking()
            .ToListAsync(ct);
        var membershipsByHandler = memberships
            .GroupBy(m => m.CaseHandlerId)
            .ToDictionary(g => g.Key, g => g.Select(m => panelEntities[m.PanelId]).ToArray());

        var handlers = await db.CaseHandlers
            .AsNoTracking()
            .OrderBy(h => h.LastName).ThenBy(h => h.FirstName)
            .ToListAsync(ct);

        var dto = handlers.Select(h => ToDto(h,
            membershipsByHandler.TryGetValue(h.Id, out var p) ? p : Array.Empty<string>()));

        return new OkObjectResult(ApiResponse.Success(
            panel: null, action: action,
            message: $"{handlers.Count} case handler(s).",
            data: new { handlers = dto }));
    }

    private static object ToDto(CaseHandler h, IReadOnlyCollection<string> panels) => new
    {
        h.Id,
        h.FirstName,
        h.LastName,
        h.Email,
        h.Telephone,
        panels,
    };

    private static string ToStatusString(PanelOperationStatus s) => s switch
    {
        PanelOperationStatus.Success => "success",
        PanelOperationStatus.Failure => "failure",
        PanelOperationStatus.NotSupported => "not_supported_on_panel",
        PanelOperationStatus.Unavailable => "panel_unavailable",
        _ => "unknown",
    };
}
