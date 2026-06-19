namespace PanelBridge.Domain;

public class Milestone
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int PanelId { get; set; }
    public int CaseTypeId { get; set; }
    public int? RegionId { get; set; }

    /// <summary>
    /// Panel-native milestone identifier - SortRefer integers (e.g. "26"),
    /// Econ string codes (e.g. "CASECREATED"). Stored as string to cover both.
    /// </summary>
    public required string PanelMilestoneCode { get; set; }
}
