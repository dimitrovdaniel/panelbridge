namespace PanelBridge.Domain;

public class CaseHandlerPanel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int CaseHandlerId { get; set; }
    public int PanelId { get; set; }
}
