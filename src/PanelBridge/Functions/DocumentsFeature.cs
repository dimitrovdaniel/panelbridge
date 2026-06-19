using Microsoft.Extensions.Configuration;

namespace PanelBridge.Functions;

/// <summary>
/// Whether the /documents/* endpoints are enabled in this environment.
/// Controlled by config key "Documents:Enabled" (env var "Documents__Enabled").
/// Defaults to true so existing dev / local installs keep working unchanged;
/// prod App Settings overrides to false to disable.
/// </summary>
public sealed class DocumentsFeature(IConfiguration configuration)
{
    public bool Enabled { get; } =
        configuration.GetValue("Documents:Enabled", defaultValue: true);
}
