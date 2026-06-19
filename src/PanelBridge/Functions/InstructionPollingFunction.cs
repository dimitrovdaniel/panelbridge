using System.Xml.Linq;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PanelBridge.Domain;
using PanelBridge.Panels;
using PanelBridge.Persistence;

namespace PanelBridge.Functions;

/// <summary>
/// Hourly poll of every wired panel to pull pending instructions and register
/// any that aren't yet in PanelBridge's caselookup (generating a fresh universalId).
///
/// Cron: 0 0 * * * * - top of every hour. Dedup is by (panelId, panelRef) which
/// already has a unique index on caselookup.
/// </summary>
public sealed class InstructionPollingFunction(
    PanelBridgeDbContext db,
    PanelClientRegistry panels,
    ILogger<InstructionPollingFunction> logger)
{
    [Function("InstructionPolling")]
    public async Task Run(
        [TimerTrigger("0 0 * * * *", RunOnStartup = false)] TimerInfo timer,
        CancellationToken ct)
    {
        logger.LogInformation("InstructionPolling starting at {Now}", DateTimeOffset.UtcNow);

        var panelRows = await db.Panels.AsNoTracking().ToListAsync(ct);
        foreach (var panel in panelRows)
        {
            if (!panels.TryGet(panel.Name, out var client))
            {
                logger.LogInformation("InstructionPolling: no client for panel {Panel}, skipping", panel.Name);
                continue;
            }

            var result = await client.ListPendingAsync(ct);
            if (result.Status != PanelOperationStatus.Success)
            {
                logger.LogInformation("InstructionPolling: panel {Panel} returned {Status} - {Message}",
                    panel.Name, result.Status, result.Message);
                continue;
            }

            var refs = ParseInstructionRefs(result.Data as string);
            logger.LogInformation("InstructionPolling: panel {Panel} returned {Count} instruction(s)",
                panel.Name, refs.Count);

            await UpsertCasesAsync(panel, refs, ct);
        }

        logger.LogInformation("InstructionPolling done at {Now}", DateTimeOffset.UtcNow);
    }

    private async Task UpsertCasesAsync(Panel panel, List<ParsedInstruction> instructions, CancellationToken ct)
    {
        if (instructions.Count == 0) return;

        var refs = instructions.Select(i => i.PanelRef).ToHashSet();
        var existing = await db.CaseLookups
            .Where(c => c.PanelId == panel.Id && refs.Contains(c.PanelRef))
            .Select(c => c.PanelRef)
            .ToListAsync(ct);
        var existingSet = existing.ToHashSet();

        int created = 0;
        foreach (var i in instructions)
        {
            if (existingSet.Contains(i.PanelRef)) continue;

            db.CaseLookups.Add(new CaseLookup
            {
                UniversalId = Guid.NewGuid(),
                PanelRef = i.PanelRef,
                PanelId = panel.Id,
                CaseType = PanelRefClassifier.Classify(i.PanelRef),
                Status = CaseStatus.Pending,
            });
            existingSet.Add(i.PanelRef);
            created++;
        }
        if (created > 0) await db.SaveChangesAsync(ct);

        logger.LogInformation("InstructionPolling: panel {Panel} created {Created} new caselookup row(s)",
            panel.Name, created);
    }

    private static List<ParsedInstruction> ParseInstructionRefs(string? xml)
    {
        var list = new List<ParsedInstruction>();
        if (string.IsNullOrWhiteSpace(xml)) return list;

        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch { return list; }

        var root = doc.Root;
        if (root is null) return list;

        // SortRefer wraps in <Instructions><Instruction><Ref/>...</Instruction></Instructions>.
        foreach (var node in root.Descendants(XName.Get("Instruction")))
        {
            var refValue = node.Element(XName.Get("Ref"))?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(refValue)) continue;
            list.Add(new ParsedInstruction(refValue));
        }
        return list;
    }

    private sealed record ParsedInstruction(string PanelRef);
}
