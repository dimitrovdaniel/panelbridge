using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PanelBridge.Domain;

namespace PanelBridge.Panels.SortRefer;

public sealed class SortReferClient(
    HttpClient http,
    IOptions<SortReferOptions> options,
    ILogger<SortReferClient> logger) : IPanelClient
{
    public const string HttpClientName = "SortRefer";

    private readonly SortReferOptions _options = options.Value;

    public string Key => PanelKey.SortRefer;

    // v0.1 lifecycle

    public Task<PanelOperationResult> AcceptAsync(string panelRef, AcceptCaseDetails d, CancellationToken ct)
        => PostAsync("accept_case", new[]
        {
            new XElement("quoteReference", panelRef),
            new XElement("case_handler", d.CaseHandlerName),
            new XElement("case_id", d.InternalRef ?? ""),
            new XElement("case_handler_email", d.CaseHandlerEmail),
            new XElement("case_handler_tel", d.CaseHandlerTelephone),
        }, ct);

    public Task<PanelOperationResult> CancelAsync(string panelRef, string reason, CancellationToken ct)
        => PostAsync("cancel_case", new[]
        {
            new XElement("quoteReference", panelRef),
            new XElement("reason", new XCData(reason)),
        }, ct);

    public Task<PanelOperationResult> CompleteAsync(string panelRef, CancellationToken ct)
        => PostAsync("case_complete", new[]
        {
            new XElement("quoteReference", panelRef),
        }, ct);

    public Task<PanelOperationResult> DeclineAsync(string panelRef, string reason, CancellationToken ct)
        => PostAsync("decline_case", new[]
        {
            new XElement("quoteReference", panelRef),
            new XElement("reason", new XCData(reason)),
        }, ct);

    public Task<PanelOperationResult> ReactivateAsync(string panelRef, CancellationToken ct)
        => PostAsync("reactivate_case", new[]
        {
            new XElement("quoteReference", panelRef),
        }, ct);

    public Task<PanelOperationResult> SuspendAsync(string panelRef, string reason, CancellationToken ct)
        => PostAsync("suspend_case", new[]
        {
            new XElement("quoteReference", panelRef),
            new XElement("reason", new XCData(reason)),
        }, ct);

    // v0.2 handler management

    public Task<PanelOperationResult> ChangeHandlerAsync(string panelRef, AcceptCaseDetails d, CancellationToken ct)
        => PostAsync("change_case_handler", new[]
        {
            new XElement("quoteReference", panelRef),
            new XElement("case_handler", d.CaseHandlerName),
            new XElement("case_handler_tel", d.CaseHandlerTelephone),
            new XElement("case_handler_email", d.CaseHandlerEmail),
        }, ct);

    public Task<PanelOperationResult> AddHandlerAsync(HandlerDetails d, CancellationToken ct)
        => PostAsync("add_case_handler", new[]
        {
            new XElement("name", d.Name),
            new XElement("telephone", d.Telephone),
            new XElement("email", d.Email),
        }, ct);

    public Task<PanelOperationResult> EditHandlerAsync(HandlerUpdate u, CancellationToken ct)
    {
        var fields = new List<XElement> { new("email", u.Email) };
        if (u.NewTelephone is not null) fields.Add(new XElement("telephone", u.NewTelephone));
        if (u.NewMobile is not null) fields.Add(new XElement("mobile", u.NewMobile));
        if (u.NewEmail is not null) fields.Add(new XElement("new_email", u.NewEmail));
        if (u.NewPassword is not null) fields.Add(new XElement("new_password", u.NewPassword));
        return PostAsync("edit_case_handler", fields, ct);
    }

    public Task<PanelOperationResult> ListHandlersAsync(CancellationToken ct)
        => PostDataAsync("get_case_handlers", Array.Empty<XElement>(), ct);

    // v0.3 instruction lifecycle & milestones

    public Task<PanelOperationResult> FetchInstructionAsync(string panelRef, CancellationToken ct)
        => PostDataAsync("get_instruction", new[]
        {
            new XElement("quoteReference", panelRef),
        }, ct);

    public Task<PanelOperationResult> ListPendingAsync(CancellationToken ct)
        => PostDataAsync("get_instructions", Array.Empty<XElement>(), ct);

    public Task<PanelOperationResult> SetMilestoneAsync(string panelRef, string milestoneCode, bool completed, CancellationToken ct)
        => PostAsync(completed ? "mark_complete" : "mark_incomplete", new[]
        {
            new XElement("quoteReference", panelRef),
            new XElement("eventId", milestoneCode),
        }, ct);

    public Task<PanelOperationResult> SetSupplierReferenceAsync(string panelRef, string supplierReference, CancellationToken ct)
        => PostAsync("update_supplier_reference", new[]
        {
            new XElement("quoteReference", panelRef),
            new XElement("case_id", supplierReference),
        }, ct);

    // spec extras

    public Task<PanelOperationResult> FetchQuotePdfAsync(string panelRef, bool zip, CancellationToken ct)
        => PostDataAsync("get_quote_pdf", new[]
        {
            new XElement("quoteReference", panelRef),
            new XElement("zip", zip ? "1" : "0"),
        }, ct);

    public Task<PanelOperationResult> SetEstimatedCompletionAsync(string panelRef, string yyyyMmDd, CancellationToken ct)
        => PostAsync("set_estimated_completion_date", new[]
        {
            new XElement("quoteReference", panelRef),
            new XElement("date", yyyyMmDd),
        }, ct);

    public Task<PanelOperationResult> AddNoteAsync(string panelRef, string text, bool isPrivate, CancellationToken ct)
        => PostAsync("add_note", new[]
        {
            new XElement("quoteReference", panelRef),
            new XElement("text", new XCData(text)),
            new XElement("private", isPrivate ? "1" : "0"),
        }, ct);

    public Task<PanelOperationResult> RemoveNoteAsync(string noteGuid, CancellationToken ct)
        => Task.FromResult(PanelOperationResult.NotSupported("notes-remove"));

    public Task<PanelOperationResult> ListNotesAsync(CancellationToken ct)
        => PostDataAsync("get_notes", Array.Empty<XElement>(), ct);

    public Task<PanelOperationResult> MarkNoteReadAsync(string noteGuid, CancellationToken ct)
        => PostAsync("mark_note_as_read", new[] { new XElement("guid", noteGuid) }, ct);

    public Task<PanelOperationResult> ListDocumentsForCaseAsync(string panelRef, CancellationToken ct)
        => PostDataAsync("get_documents", new[]
        {
            new XElement("quoteReference", panelRef),
        }, ct);

    public Task<PanelOperationResult> ListAllDocumentsAsync(CancellationToken ct)
        => PostDataAsync("get_all_documents", Array.Empty<XElement>(), ct);

    public Task<PanelOperationResult> MarkDocumentReadAsync(string documentGuid, CancellationToken ct)
        => PostAsync("mark_document_as_read", new[] { new XElement("guid", documentGuid) }, ct);

    public Task<PanelOperationResult> ListLendersAsync(CancellationToken ct)
        => PostDataAsync("get_lenders", Array.Empty<XElement>(), ct);

    public Task<PanelOperationResult> ListMilestonesAsync(CancellationToken ct)
        => PostDataAsync("get_milestones", Array.Empty<XElement>(), ct);

    // helpers

    private async Task<string?> SendXmlAsync(string function, IEnumerable<XElement> fields, CancellationToken ct)
    {
        var post = new XElement("post",
            new XElement("username", _options.Username),
            new XElement("password", _options.Password));
        foreach (var field in fields) post.Add(field);

        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null), post);
        var xml = doc.ToString(SaveOptions.DisableFormatting);

        using var req = new HttpRequestMessage(HttpMethod.Post, function)
        {
            Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("xml", xml),
            }),
        };

        using var resp = await http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("SortRefer {Function} HTTP {Code}: {Body}",
                function, (int)resp.StatusCode, Truncate(body));
            return null;
        }

        return body;
    }

    private async Task<PanelOperationResult> PostAsync(string function, IEnumerable<XElement> fields, CancellationToken ct)
    {
        try
        {
            var body = await SendXmlAsync(function, fields, ct);
            if (body is null) return PanelOperationResult.Unavailable("SortRefer returned a non-success HTTP status.");
            return ParseStatusResponse(function, body);
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (HttpRequestException ex) { return Transport(function, ex); }
        catch (TaskCanceledException ex) { return Timeout(function, ex); }
    }

    private async Task<PanelOperationResult> PostDataAsync(string function, IEnumerable<XElement> fields, CancellationToken ct)
    {
        try
        {
            var body = await SendXmlAsync(function, fields, ct);
            if (body is null) return PanelOperationResult.Unavailable("SortRefer returned a non-success HTTP status.");

            // Some SortRefer data endpoints reply with <response><status>FAILURE</status>...</response>
            // for errors but return a domain-shaped XML body on success. Detect either.
            XDocument xdoc;
            try { xdoc = XDocument.Parse(body); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "SortRefer {Function} returned non-XML body: {Body}", function, Truncate(body));
                return PanelOperationResult.Unavailable("SortRefer returned an unparseable response.");
            }

            if (xdoc.Root?.Name.LocalName == "response")
            {
                var status = xdoc.Root.Element("status")?.Value?.Trim();
                var message = xdoc.Root.Element("message")?.Value?.Trim() ?? "";
                if (string.Equals(status, "FAILURE", StringComparison.OrdinalIgnoreCase))
                    return PanelOperationResult.Failure(message);
                // Some success responses also use the <response> envelope; treat as success.
                return PanelOperationResult.Success(string.IsNullOrEmpty(message) ? "ok" : message, body);
            }

            // Domain-shaped body (e.g. <Instruction>, <Instructions>, <case_handlers>) → success + raw XML payload.
            return PanelOperationResult.Success($"{function} returned {xdoc.Root?.Name.LocalName ?? "data"}.", body);
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (HttpRequestException ex) { return Transport(function, ex); }
        catch (TaskCanceledException ex) { return Timeout(function, ex); }
    }

    private PanelOperationResult ParseStatusResponse(string function, string body)
    {
        XDocument xdoc;
        try { xdoc = XDocument.Parse(body); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SortRefer {Function} returned non-XML body: {Body}", function, Truncate(body));
            return PanelOperationResult.Unavailable("SortRefer returned an unparseable response.");
        }

        var root = xdoc.Root;
        var status = root?.Element("status")?.Value?.Trim();
        var message = root?.Element("message")?.Value?.Trim() ?? "";

        return status?.ToUpperInvariant() switch
        {
            "SUCCESS" => PanelOperationResult.Success(message),
            "FAILURE" => PanelOperationResult.Failure(message),
            _ => PanelOperationResult.Unavailable($"Unrecognised SortRefer status '{status}' for {function}."),
        };
    }

    private PanelOperationResult Transport(string function, Exception ex)
    {
        logger.LogWarning(ex, "SortRefer {Function} transport error", function);
        return PanelOperationResult.Unavailable($"SortRefer is unreachable: {ex.Message}");
    }

    private PanelOperationResult Timeout(string function, Exception ex)
    {
        logger.LogWarning(ex, "SortRefer {Function} timed out", function);
        return PanelOperationResult.Unavailable("SortRefer request timed out.");
    }

    private static string Truncate(string s, int max = 500) =>
        s.Length <= max ? s : s[..max] + "...";
}
