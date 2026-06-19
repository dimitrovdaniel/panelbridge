namespace PanelBridge.Panels;

public sealed record AcceptCaseDetails(
    string CaseHandlerName,
    string CaseHandlerEmail,
    string CaseHandlerTelephone,
    string? InternalRef);

public sealed record HandlerDetails(
    string Name,
    string Email,
    string Telephone);

public sealed record HandlerUpdate(
    string Email,
    string? NewEmail = null,
    string? NewTelephone = null,
    string? NewMobile = null,
    string? NewPassword = null);

public interface IPanelClient
{
    string Key { get; }

    // v0.1 - lifecycle
    Task<PanelOperationResult> AcceptAsync(string panelRef, AcceptCaseDetails details, CancellationToken ct);
    Task<PanelOperationResult> CancelAsync(string panelRef, string reason, CancellationToken ct);
    Task<PanelOperationResult> CompleteAsync(string panelRef, CancellationToken ct);
    Task<PanelOperationResult> DeclineAsync(string panelRef, string reason, CancellationToken ct);
    Task<PanelOperationResult> ReactivateAsync(string panelRef, CancellationToken ct);
    Task<PanelOperationResult> SuspendAsync(string panelRef, string reason, CancellationToken ct);

    // v0.2 - handler management
    Task<PanelOperationResult> ChangeHandlerAsync(string panelRef, AcceptCaseDetails details, CancellationToken ct);
    Task<PanelOperationResult> AddHandlerAsync(HandlerDetails details, CancellationToken ct);
    Task<PanelOperationResult> EditHandlerAsync(HandlerUpdate update, CancellationToken ct);
    Task<PanelOperationResult> ListHandlersAsync(CancellationToken ct);

    // v0.3 - instruction lifecycle & milestones
    Task<PanelOperationResult> FetchInstructionAsync(string panelRef, CancellationToken ct);
    Task<PanelOperationResult> ListPendingAsync(CancellationToken ct);
    Task<PanelOperationResult> SetMilestoneAsync(string panelRef, string milestoneCode, bool completed, CancellationToken ct);
    Task<PanelOperationResult> SetSupplierReferenceAsync(string panelRef, string supplierReference, CancellationToken ct);

    // Spec extras - quote, notes, est-completion, documents, lenders
    Task<PanelOperationResult> FetchQuotePdfAsync(string panelRef, bool zip, CancellationToken ct);
    Task<PanelOperationResult> SetEstimatedCompletionAsync(string panelRef, string yyyyMmDd, CancellationToken ct);

    Task<PanelOperationResult> AddNoteAsync(string panelRef, string text, bool isPrivate, CancellationToken ct);
    Task<PanelOperationResult> RemoveNoteAsync(string noteGuid, CancellationToken ct);
    Task<PanelOperationResult> ListNotesAsync(CancellationToken ct);
    Task<PanelOperationResult> MarkNoteReadAsync(string noteGuid, CancellationToken ct);

    Task<PanelOperationResult> ListDocumentsForCaseAsync(string panelRef, CancellationToken ct);
    Task<PanelOperationResult> ListAllDocumentsAsync(CancellationToken ct);
    Task<PanelOperationResult> MarkDocumentReadAsync(string documentGuid, CancellationToken ct);

    Task<PanelOperationResult> ListLendersAsync(CancellationToken ct);
    Task<PanelOperationResult> ListMilestonesAsync(CancellationToken ct);
}
