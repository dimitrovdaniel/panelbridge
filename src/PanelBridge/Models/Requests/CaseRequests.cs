using System.ComponentModel.DataAnnotations;

namespace PanelBridge.Models.Requests;

public sealed class AcceptCaseRequest
{
    /// <summary>
    /// Email of an existing PanelBridge case handler. Name/telephone are looked up from PanelBridge's
    /// casehandlers table and forwarded to the panel.
    /// </summary>
    [Required, EmailAddress] public string CaseHandlerEmail { get; set; } = "";

    /// <summary>
    /// Integrating system's own case reference. Required - SortRefer's accept_case mandates
    /// case_id and the panel will refuse the request without it. Also stored on the
    /// caselookup row as internalRef on success.
    /// </summary>
    [Required, MinLength(1)] public string InternalRef { get; set; } = "";
}

public sealed class ChangeHandlerRequest
{
    /// <summary>
    /// Email of an existing PanelBridge case handler. Name/telephone are looked up from PanelBridge's
    /// casehandlers table and forwarded to the panel.
    /// </summary>
    [Required, EmailAddress] public string CaseHandlerEmail { get; set; } = "";
}

public sealed class ReasonOnlyRequest
{
    [Required, MinLength(1)] public string Reason { get; set; } = "";
}

public sealed class SetUniversalIdRequest
{
    /// <summary>Integrating system's own case reference (stored as internalRef). Optional.</summary>
    public string? InternalReference { get; set; }

    /// <summary>Name of the panel (e.g. "sortrefer", "econ").</summary>
    [Required, MinLength(1)] public string PanelName { get; set; } = "";

    /// <summary>Panel's own reference for this case (SortRefer's quoteReference).</summary>
    [Required, MinLength(1)] public string PanelReference { get; set; } = "";
}

public sealed class QueryCasesRequest
{
    public Guid? UniversalId { get; set; }
    public string? Panel { get; set; }
    public string? PanelRef { get; set; }
    public string? InternalRef { get; set; }
    public string? Status { get; set; }

    [Range(1, 200)] public int Take { get; set; } = 50;
    [Range(0, int.MaxValue)] public int Skip { get; set; } = 0;
}

public sealed class MilestoneUpdateRequest
{
    [Required, MinLength(1)] public string MilestoneCode { get; set; } = "";
    public bool Completed { get; set; } = true;
}

public sealed class SetSupplierReferenceRequest
{
    [Required, MinLength(1)] public string SupplierReference { get; set; } = "";
}

public sealed class AddHandlerRequest
{
    [Required] public string FirstName { get; set; } = "";
    [Required] public string LastName { get; set; } = "";
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required] public string Telephone { get; set; } = "";

    /// <summary>
    /// Panel names the handler should be registered on (e.g. ["sortrefer"]).
    /// PanelBridge writes the casehandlers + casehandlers_panels rows and dispatches add_case_handler
    /// to each panel listed. Empty/null = PanelBridge-only registration with no panel propagation.
    /// </summary>
    public string[] MemberPanels { get; set; } = Array.Empty<string>();
}

public sealed class EditHandlerRequest
{
    /// <summary>Existing handler's email - used as the lookup key.</summary>
    [Required, EmailAddress] public string Email { get; set; } = "";

    public string? NewEmail { get; set; }
    public string? NewTelephone { get; set; }
    public string? NewMobile { get; set; }
    public string? NewPassword { get; set; }

    /// <summary>
    /// Panel names to *additionally* register this handler on (idempotent).
    /// PanelBridge inserts casehandlers_panels rows and dispatches add_case_handler per panel.
    /// </summary>
    public string[] AddMemberPanel { get; set; } = Array.Empty<string>();
}

public sealed class PanelOnlyRequest
{
    [Required] public string Panel { get; set; } = "";
}

public sealed class PanelGuidRequest
{
    [Required] public string Panel { get; set; } = "";
    [Required, MinLength(1)] public string Guid { get; set; } = "";
}

public sealed class FetchQuoteRequest
{
    public bool Zip { get; set; } = false;
}

public sealed class SetEstimatedCompletionRequest
{
    [Required, RegularExpression(@"^\d{4}-\d{2}-\d{2}$",
        ErrorMessage = "Date must be in yyyy-MM-dd format.")]
    public string Date { get; set; } = "";
}

public sealed class AddNoteRequest
{
    [Required] public Guid UniversalId { get; set; }
    [Required, MinLength(1)] public string Text { get; set; } = "";
    public bool IsPrivate { get; set; } = true;
}
