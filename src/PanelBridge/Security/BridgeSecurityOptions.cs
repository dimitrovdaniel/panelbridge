using System.ComponentModel.DataAnnotations;

namespace PanelBridge.Security;

public sealed class BridgeSecurityOptions
{
    public const string SectionName = "PanelBridge";

    /// <summary>
    /// Shared secret callers send in the X-API-Key header on every /api/case/* request.
    /// </summary>
    [Required, MinLength(16)]
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Primary swagger user (kept for backwards compatibility with single-user setups).
    /// </summary>
    [Required]
    public string SwaggerUsername { get; set; } = "";

    [Required, MinLength(8)]
    public string SwaggerPassword { get; set; } = "";

    /// <summary>
    /// Optional list of additional swagger users, each with their own password.
    /// Bound from "PanelBridge:SwaggerUsers:0:Username", "PanelBridge:SwaggerUsers:0:Password", etc.
    /// </summary>
    public List<SwaggerUser> SwaggerUsers { get; set; } = new();

    /// <summary>
    /// HMAC key used to sign the swagger session cookie. Must be stable across restarts
    /// (rotating it invalidates all sessions). Random 32+ char string is fine.
    /// </summary>
    [Required, MinLength(32)]
    public string SwaggerCookieKey { get; set; } = "";
}

public sealed class SwaggerUser
{
    [Required] public string Username { get; set; } = "";
    [Required, MinLength(8)] public string Password { get; set; } = "";
}
