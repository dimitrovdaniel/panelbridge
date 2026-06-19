namespace PanelBridge.Domain;

public class Lender
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int? SortReferId { get; set; }
    public int? EconId { get; set; }
}
