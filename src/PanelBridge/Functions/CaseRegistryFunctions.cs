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

public sealed class CaseRegistryFunctions(
    PanelBridgeDbContext db,
    ILogger<CaseRegistryFunctions> logger)
{
    private const string Tag = "Case registry";

    [Function("QueryCases")]
    [OpenApiOperation(operationId: "queryCases", tags: new[] { Tag }, Summary = "Query cases",
        Description = "Search the local caselookup with optional filters: universalId, panel, panelRef, internalRef, status. Supports pagination via skip/take. Items carry the panel name (not id).")]
    [OpenApiSecurity("api_key", SecuritySchemeType.ApiKey, Name = "X-API-Key", In = OpenApiSecurityLocationType.Header)]
    [OpenApiSecurity("oauth2", SecuritySchemeType.OAuth2, Flows = typeof(PanelBridge.Security.OpenApiSecurityFlows))]
    [OpenApiRequestBody("application/json", typeof(QueryCasesRequest), Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(CaseQueryResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, "application/json", typeof(ApiResponse))]
    public async Task<IActionResult> Query(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "case/query")] HttpRequest req,
        CancellationToken ct)
    {
        const string action = "query";
        var (body, error) = await RequestValidator.ReadJsonAsync<QueryCasesRequest>(req, ct);
        if (error is not null) return HttpResults.InvalidRequest(action, error);

        var query = db.CaseLookups.AsNoTracking().AsQueryable();

        if (body!.UniversalId.HasValue)
            query = query.Where(c => c.UniversalId == body.UniversalId.Value);

        if (!string.IsNullOrWhiteSpace(body.PanelRef))
            query = query.Where(c => c.PanelRef == body.PanelRef);

        if (!string.IsNullOrWhiteSpace(body.InternalRef))
            query = query.Where(c => c.InternalRef == body.InternalRef);

        if (!string.IsNullOrWhiteSpace(body.Status)
            && Enum.TryParse<CaseStatus>(body.Status, ignoreCase: true, out var status))
            query = query.Where(c => c.Status == status);

        if (!string.IsNullOrWhiteSpace(body.Panel))
        {
            var panel = await db.Panels.FirstOrDefaultAsync(p => p.Name == body.Panel, ct);
            if (panel is null)
                return HttpResults.InvalidRequest(action, $"Unknown panel '{body.Panel}'.");
            query = query.Where(c => c.PanelId == panel.Id);
        }

        var panelNamesById = await db.Panels.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(c => c.UpdatedAt)
            .Skip(body.Skip)
            .Take(body.Take)
            .ToListAsync(ct);

        var items = rows.Select(c => new CaseSummary
        {
            UniversalId = c.UniversalId,
            Panel = panelNamesById.TryGetValue(c.PanelId, out var n) ? n : "",
            PanelRef = c.PanelRef,
            InternalRef = c.InternalRef,
            RegionId = c.RegionId,
            CaseTypeId = c.CaseTypeId,
            CaseType = c.CaseType.ToString().ToLowerInvariant(),
            Status = c.Status.ToString().ToLowerInvariant(),
            AssignedCaseHandlerId = c.AssignedCaseHandlerId,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt,
        }).ToArray();

        return new OkObjectResult(ApiResponse.Success(
            panel: null, action: action,
            message: $"{items.Length} case(s) returned.",
            data: new CaseQueryData
            {
                Total = total,
                Skip = body.Skip,
                Take = body.Take,
                Items = items,
            }));
    }

    [Function("SetUniversalId")]
    [OpenApiOperation(operationId: "setUniversalId", tags: new[] { Tag }, Summary = "Register a new caselookup row and generate its universalId",
        Description = "Creates a new caselookup record for the given panel + panelReference + internalReference combination, generates a universalId, and returns it. If a caselookup already exists for the (panel, panelReference) pair the existing universalId is returned (idempotent).")]
    [OpenApiSecurity("api_key", SecuritySchemeType.ApiKey, Name = "X-API-Key", In = OpenApiSecurityLocationType.Header)]
    [OpenApiSecurity("oauth2", SecuritySchemeType.OAuth2, Flows = typeof(PanelBridge.Security.OpenApiSecurityFlows))]
    [OpenApiRequestBody("application/json", typeof(SetUniversalIdRequest), Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(SetUniversalIdResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, "application/json", typeof(ApiResponse))]
    public async Task<IActionResult> SetUniversalId(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "case/set-universal-id")] HttpRequest req,
        CancellationToken ct)
    {
        const string action = "set-universal-id";
        var (body, error) = await RequestValidator.ReadJsonAsync<SetUniversalIdRequest>(req, ct);
        if (error is not null) return HttpResults.InvalidRequest(action, error);

        var panel = await db.Panels.FirstOrDefaultAsync(p => p.Name == body!.PanelName, ct);
        if (panel is null)
            return HttpResults.InvalidRequest(action, $"Unknown panel '{body!.PanelName}'.");

        var existing = await db.CaseLookups.FirstOrDefaultAsync(
            c => c.PanelId == panel.Id && c.PanelRef == body!.PanelReference, ct);

        if (existing is not null)
        {
            if (string.IsNullOrWhiteSpace(existing.InternalRef)
                && !string.IsNullOrWhiteSpace(body!.InternalReference))
            {
                existing.InternalRef = body.InternalReference;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
            }
            logger.LogInformation("Returning existing universalId {Id} for {Panel}/{PanelRef}",
                existing.UniversalId, panel.Name, existing.PanelRef);

            return new OkObjectResult(ApiResponse.Success(
                panel: panel.Name, action: action,
                message: "Case lookup already exists; returning existing universalId.",
                data: new SetUniversalIdData { UniversalId = existing.UniversalId }));
        }

        var record = new CaseLookup
        {
            UniversalId = Guid.NewGuid(),
            PanelRef = body!.PanelReference,
            PanelId = panel.Id,
            InternalRef = body.InternalReference,
            CaseType = PanelRefClassifier.Classify(body.PanelReference),
            Status = CaseStatus.Pending,
        };
        db.CaseLookups.Add(record);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Created caselookup {Id} for {Panel}/{PanelRef}",
            record.UniversalId, panel.Name, record.PanelRef);

        return new OkObjectResult(ApiResponse.Success(
            panel: panel.Name, action: action,
            message: "Case lookup created.",
            data: new SetUniversalIdData { UniversalId = record.UniversalId }));
    }

    [Function("ListAllCases")]
    [OpenApiOperation(operationId: "listAllCases", tags: new[] { Tag }, Summary = "List every case in the caselookup table",
        Description = "Returns every row in caselookup with pagination. Does not call the panels. Same item shape as case/query.")]
    [OpenApiSecurity("api_key", SecuritySchemeType.ApiKey, Name = "X-API-Key", In = OpenApiSecurityLocationType.Header)]
    [OpenApiSecurity("oauth2", SecuritySchemeType.OAuth2, Flows = typeof(PanelBridge.Security.OpenApiSecurityFlows))]
    [OpenApiRequestBody("application/json", typeof(QueryCasesRequest), Required = false, Description = "Optional pagination only - filter fields are ignored.")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(CaseQueryResponse))]
    public async Task<IActionResult> ListAll(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "case/list")] HttpRequest req,
        CancellationToken ct)
    {
        const string action = "list";
        var (body, _) = await RequestValidator.ReadJsonAsync<QueryCasesRequest>(req, ct);
        var skip = body?.Skip ?? 0;
        var take = body?.Take ?? 50;
        if (take < 1 || take > 200) take = 50;
        if (skip < 0) skip = 0;
        return await ProjectAndReturn(db.CaseLookups.AsNoTracking(), skip, take, action, ct);
    }

    [Function("ListAwaitingAccept")]
    [OpenApiOperation(operationId: "listAwaitingAccept", tags: new[] { Tag }, Summary = "List caselookup rows with status=pending",
        Description = "Returns every caselookup row whose status is Pending. Does not call the panels. Same item shape as case/query.")]
    [OpenApiSecurity("api_key", SecuritySchemeType.ApiKey, Name = "X-API-Key", In = OpenApiSecurityLocationType.Header)]
    [OpenApiSecurity("oauth2", SecuritySchemeType.OAuth2, Flows = typeof(PanelBridge.Security.OpenApiSecurityFlows))]
    [OpenApiRequestBody("application/json", typeof(QueryCasesRequest), Required = false)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(CaseQueryResponse))]
    public async Task<IActionResult> ListAwaitingAccept(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "case/list/awaiting-accept")] HttpRequest req,
        CancellationToken ct)
    {
        const string action = "list-awaiting-accept";
        var (body, _) = await RequestValidator.ReadJsonAsync<QueryCasesRequest>(req, ct);
        var skip = body?.Skip ?? 0;
        var take = body?.Take ?? 50;
        if (take < 1 || take > 200) take = 50;
        if (skip < 0) skip = 0;

        var query = db.CaseLookups.AsNoTracking()
            .Where(c => c.Status == CaseStatus.Pending);

        if (!string.IsNullOrWhiteSpace(body?.Panel))
        {
            var panel = await db.Panels.FirstOrDefaultAsync(p => p.Name == body.Panel, ct);
            if (panel is null)
                return HttpResults.InvalidRequest(action, $"Unknown panel '{body.Panel}'.");
            query = query.Where(c => c.PanelId == panel.Id);
        }

        return await ProjectAndReturn(query, skip, take, action, ct);
    }

    private async Task<IActionResult> ProjectAndReturn(
        IQueryable<CaseLookup> query, int skip, int take, string action, CancellationToken ct)
    {
        var panelNamesById = await db.Panels.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(c => c.UpdatedAt)
            .Skip(skip).Take(take)
            .ToListAsync(ct);

        var items = rows.Select(c => new CaseSummary
        {
            UniversalId = c.UniversalId,
            Panel = panelNamesById.TryGetValue(c.PanelId, out var n) ? n : "",
            PanelRef = c.PanelRef,
            InternalRef = c.InternalRef,
            RegionId = c.RegionId,
            CaseTypeId = c.CaseTypeId,
            CaseType = c.CaseType.ToString().ToLowerInvariant(),
            Status = c.Status.ToString().ToLowerInvariant(),
            AssignedCaseHandlerId = c.AssignedCaseHandlerId,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt,
        }).ToArray();

        return new OkObjectResult(ApiResponse.Success(
            panel: null, action: action,
            message: $"{items.Length} case(s) returned.",
            data: new CaseQueryData { Total = total, Skip = skip, Take = take, Items = items }));
    }
}

