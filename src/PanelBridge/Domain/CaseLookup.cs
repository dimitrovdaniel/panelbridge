namespace PanelBridge.Domain;

public class CaseLookup
{
    public Guid UniversalId { get; set; } = Guid.NewGuid();

    /// <summary>Panel's own reference (SortRefer's quoteReference, Econ's PortalId).</summary>
    public required string PanelRef { get; set; }

    public int PanelId { get; set; }

    /// <summary>Integrating system's own reference (SortRefer's case_id).</summary>
    public string? InternalRef { get; set; }

    public int? RegionId { get; set; }
    public int CaseTypeId { get; set; }

    /// <summary>
    /// Sale / Purchase / Remortgage. Derived from the panel ref suffix (S/FS, P/FP, R/FR)
    /// at create-time via PanelRefClassifier. Unknown for refs that don't fit the pattern
    /// (e.g. all Econ refs).
    /// </summary>
    public CaseTransactionType CaseType { get; set; } = CaseTransactionType.Unknown;

    public CaseStatus Status { get; set; } = CaseStatus.Pending;

    public int? AssignedCaseHandlerId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
