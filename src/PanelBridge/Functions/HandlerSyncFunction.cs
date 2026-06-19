using System.Xml.Linq;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PanelBridge.Domain;
using PanelBridge.Panels;
using PanelBridge.Persistence;

namespace PanelBridge.Functions;

/// <summary>
/// Hourly poll of every wired panel to pull down their case handler list and
/// match into PanelBridge's casehandlers table by email. New handlers (created on the
/// panel side - happens often on Econ) appear in PanelBridge without manual entry.
///
/// Cron: 0 30 * * * * - every hour at minute 30. Offset 30m from instruction
/// polling so the two jobs don't fight for the same panel connection.
/// </summary>
public sealed class HandlerSyncFunction(
    PanelBridgeDbContext db,
    PanelClientRegistry panels,
    ILogger<HandlerSyncFunction> logger)
{
    [Function("HandlerSync")]
    public async Task Run(
        [TimerTrigger("0 30 * * * *", RunOnStartup = false)] TimerInfo timer,
        CancellationToken ct)
    {
        logger.LogInformation("HandlerSync starting at {Now}", DateTimeOffset.UtcNow);

        var panelRows = await db.Panels.AsNoTracking().ToListAsync(ct);
        foreach (var panel in panelRows)
        {
            if (!panels.TryGet(panel.Name, out var client))
            {
                logger.LogInformation("HandlerSync: no client for panel {Panel}, skipping", panel.Name);
                continue;
            }

            var result = await client.ListHandlersAsync(ct);
            if (result.Status != PanelOperationStatus.Success)
            {
                logger.LogInformation("HandlerSync: panel {Panel} returned {Status} - {Message}",
                    panel.Name, result.Status, result.Message);
                continue;
            }

            var parsed = result.Data switch
            {
                IEnumerable<PanelBridge.Panels.Econ.EconHandler> econHandlers => ParseEconHandlers(econHandlers),
                string xml => ParseHandlers(xml),
                _ => new List<ParsedHandler>(),
            };
            logger.LogInformation("HandlerSync: panel {Panel} returned {Count} handler(s)",
                panel.Name, parsed.Count);

            await UpsertHandlersAsync(panel, parsed, ct);
        }

        logger.LogInformation("HandlerSync done at {Now}", DateTimeOffset.UtcNow);
    }

    private async Task UpsertHandlersAsync(Panel panel, List<ParsedHandler> parsed, CancellationToken ct)
    {
        if (parsed.Count == 0) return;

        // Pull all matching handlers in one query to avoid N+1.
        var emails = parsed.Select(p => p.Email).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existing = await db.CaseHandlers
            .Where(h => emails.Contains(h.Email))
            .ToListAsync(ct);
        var byEmail = existing.ToDictionary(h => h.Email, StringComparer.OrdinalIgnoreCase);

        var existingMemberships = await db.CaseHandlerPanels
            .Where(chp => chp.PanelId == panel.Id)
            .Select(chp => chp.CaseHandlerId)
            .ToListAsync(ct);
        var membershipSet = existingMemberships.ToHashSet();

        int created = 0, linked = 0;
        foreach (var p in parsed)
        {
            if (!byEmail.TryGetValue(p.Email, out var handler))
            {
                handler = new CaseHandler
                {
                    FirstName = p.FirstName,
                    LastName = p.LastName,
                    Email = p.Email,
                    Telephone = p.Telephone,
                };
                db.CaseHandlers.Add(handler);
                byEmail[p.Email] = handler;
                created++;
            }

            // Save to assign handler.Id before adding membership join row.
            if (handler.Id == 0)
                await db.SaveChangesAsync(ct);

            if (!membershipSet.Contains(handler.Id))
            {
                db.CaseHandlerPanels.Add(new CaseHandlerPanel
                {
                    CaseHandlerId = handler.Id,
                    PanelId = panel.Id,
                });
                membershipSet.Add(handler.Id);
                linked++;
            }
        }
        await db.SaveChangesAsync(ct);

        logger.LogInformation("HandlerSync: panel {Panel} created {Created}, linked {Linked} new memberships",
            panel.Name, created, linked);
    }

    private static List<ParsedHandler> ParseHandlers(string? xml)
    {
        var list = new List<ParsedHandler>();
        if (string.IsNullOrWhiteSpace(xml)) return list;

        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch { return list; }

        // SortRefer wraps in <CaseHandlers><CaseHandler>...</CaseHandler></CaseHandlers>.
        var root = doc.Root;
        if (root is null) return list;

        var handlerNodes = root.Name.LocalName.Equals("CaseHandlers", StringComparison.OrdinalIgnoreCase)
            ? root.Elements()
            : root.Descendants(XName.Get("CaseHandler"));

        foreach (var node in handlerNodes)
        {
            var email = node.Element(XName.Get("email"))?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(email)) continue;

            var name = node.Element(XName.Get("name"))?.Value?.Trim() ?? "";
            var tel = node.Element(XName.Get("telephone"))?.Value?.Trim()
                      ?? node.Element(XName.Get("mobile"))?.Value?.Trim()
                      ?? "";

            var (first, last) = SplitName(name);
            list.Add(new ParsedHandler(first, last, email, tel));
        }
        return list;
    }

    /// <summary>
    /// Econ exposes FullName + PersonRef per handler - no email or phone. Synthesises an
    /// email-shaped key from PersonRef so the casehandlers row (which requires a unique
    /// non-empty Email) can be created and matched on subsequent syncs.
    /// </summary>
    private static List<ParsedHandler> ParseEconHandlers(IEnumerable<PanelBridge.Panels.Econ.EconHandler> handlers)
    {
        var list = new List<ParsedHandler>();
        foreach (var h in handlers)
        {
            if (string.IsNullOrWhiteSpace(h.PersonRef)) continue;
            // PersonRef can contain chars that aren't valid in an email local-part (e.g. '!').
            // Replace with '-' to keep the key syntactically email-shaped.
            var safe = new string(h.PersonRef.Select(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' ? c : '-').ToArray());
            var email = $"{safe}@econ.utd";
            var (first, last) = SplitName(h.FullName);
            list.Add(new ParsedHandler(first, last, email, ""));
        }
        return list;
    }

    private static (string First, string Last) SplitName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return ("(unknown)", "");
        var trimmed = name.Trim();
        var space = trimmed.IndexOf(' ');
        return space < 0 ? (trimmed, "") : (trimmed[..space], trimmed[(space + 1)..]);
    }

    private sealed record ParsedHandler(string FirstName, string LastName, string Email, string Telephone);
}
