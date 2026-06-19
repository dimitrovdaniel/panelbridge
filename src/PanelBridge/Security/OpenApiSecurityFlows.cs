using Microsoft.OpenApi.Models;

namespace PanelBridge.Security;

/// <summary>
/// Declares the OAuth2 client_credentials flow for the OpenAPI/Swagger spec.
/// Values are read from app settings (env vars) at construction time so the
/// generated spec is per-environment without code changes.
/// </summary>
public sealed class OpenApiSecurityFlows : OpenApiOAuthFlows
{
    public OpenApiSecurityFlows()
    {
        var tenant = Environment.GetEnvironmentVariable("PanelBridge__Entra__TenantId")
                  ?? "00000000-0000-0000-0000-000000000000";
        var clientId = Environment.GetEnvironmentVariable("PanelBridge__Entra__ClientId")
                    ?? "<set PanelBridge__Entra__ClientId in app settings>";

        ClientCredentials = new OpenApiOAuthFlow
        {
            TokenUrl = new Uri($"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token"),
            Scopes = new Dictionary<string, string>
            {
                [$"{clientId}/.default"] = "Default access to PanelBridge (Entra ID client_credentials)",
            },
        };
    }
}
