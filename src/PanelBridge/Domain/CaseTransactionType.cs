namespace PanelBridge.Domain;

public enum CaseTransactionType
{
    Unknown = 0,
    Sale = 1,
    Purchase = 2,
    Remortgage = 3,
}

public static class PanelRefClassifier
{
    /// <summary>
    /// Derives a transaction type from a SortRefer-style panel reference by reading the
    /// letter suffix after the final '-'. Mapping: S/FS = Sale, P/FP = Purchase,
    /// R/FR = Remortgage. Anything else (including all Econ refs) = Unknown.
    /// </summary>
    public static CaseTransactionType Classify(string? panelRef)
    {
        if (string.IsNullOrWhiteSpace(panelRef)) return CaseTransactionType.Unknown;
        var idx = panelRef.LastIndexOf('-');
        if (idx < 0 || idx == panelRef.Length - 1) return CaseTransactionType.Unknown;
        var suffix = panelRef[(idx + 1)..].Trim().ToUpperInvariant();
        return suffix switch
        {
            "S" or "FS" => CaseTransactionType.Sale,
            "P" or "FP" => CaseTransactionType.Purchase,
            "R" or "FR" => CaseTransactionType.Remortgage,
            _ => CaseTransactionType.Unknown,
        };
    }
}
