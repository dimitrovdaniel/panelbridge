namespace PanelBridge.Panels;

public sealed class PanelClientRegistry(IEnumerable<IPanelClient> clients)
{
    private readonly Dictionary<string, IPanelClient> _byKey = clients
        .ToDictionary(c => c.Key, StringComparer.OrdinalIgnoreCase);

    public IPanelClient Get(string panelKey) =>
        _byKey.TryGetValue(panelKey, out var client)
            ? client
            : throw new InvalidOperationException($"No panel client registered for '{panelKey}'.");

    public bool TryGet(string panelKey, out IPanelClient client)
    {
        var found = _byKey.TryGetValue(panelKey, out var c);
        client = c!;
        return found;
    }
}
