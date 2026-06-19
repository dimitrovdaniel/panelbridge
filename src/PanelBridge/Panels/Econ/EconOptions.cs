using System.ComponentModel.DataAnnotations;

namespace PanelBridge.Panels.Econ;

public sealed class EconOptions
{
    public const string SectionName = "Econ";

    /// <summary>
    /// Full URL of the InstructionManagementService endpoint.
    /// Staging: https://<panel-host>/v1_00/InstructionManagementService.svc
    /// </summary>
    [Required, Url] public string InstructionManagementUrl { get; set; } = "";

    /// <summary>
    /// Full URL of the StartSessionService endpoint. supplier's auth flow requires us to call
    /// StartSession first to get a session username/password, then use those (NOT the
    /// configured username/password) in subsequent InstructionManagementService calls.
    /// Staging: https://<panel-host>/v1_00/StartSessionService.svc
    /// </summary>
    [Required, Url] public string StartSessionUrl { get; set; } = "";

    /// <summary>
    /// How long an Econ session is reused before re-authenticating. supplier does not publish
    /// an absolute session TTL, so this is a conservative client-side ceiling.
    /// </summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromMinutes(15);

    [Required] public string Username { get; set; } = "";
    [Required] public string Password { get; set; } = "";

    /// <summary>
    /// Outbound HTTP proxy URL (e.g. http://<egress-proxy-host>:3128). Set when supplier requires
    /// a static IP whitelist and we route Econ SOAP through the egress proxy VM.
    /// Leave null/empty to skip the proxy (direct outbound).
    /// </summary>
    public string? OutboundProxyUrl { get; set; }

    /// <summary>Basic-auth username for the egress proxy. Optional.</summary>
    public string? OutboundProxyUsername { get; set; }

    /// <summary>Basic-auth password for the egress proxy. Optional.</summary>
    public string? OutboundProxyPassword { get; set; }
}
