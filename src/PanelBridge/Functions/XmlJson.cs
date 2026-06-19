using System.Xml.Linq;

namespace PanelBridge.Functions;

/// <summary>
/// Generic XML -> JSON-serialisable shape converter. Used to normalise panel-side XML
/// responses (SortRefer instruction, documents, etc.) into JSON without per-endpoint typing.
///
/// Conversion rules:
/// - A leaf element with no attributes returns its trimmed text value as a string (or null if empty).
/// - An element with children returns a Dictionary where child names are keys.
/// - Children with the same name within a parent are coalesced into an array.
/// - Attributes are surfaced as "@name" keys on the containing object.
/// - If the body fails to parse, the raw XML is returned under { rawXml }.
/// </summary>
internal static class XmlJson
{
    public static object FromXml(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return new Dictionary<string, object?>();

        try
        {
            var doc = XDocument.Parse(xml);
            if (doc.Root is null) return new Dictionary<string, object?>();
            return new Dictionary<string, object?> { [doc.Root.Name.LocalName] = Convert(doc.Root) };
        }
        catch
        {
            return new { rawXml = xml };
        }
    }

    public static object? Convert(XElement element)
    {
        var hasChildren = element.HasElements;
        var hasAttributes = element.HasAttributes;

        if (!hasChildren && !hasAttributes)
        {
            var text = element.Value;
            return string.IsNullOrEmpty(text) ? null : text;
        }

        var dict = new Dictionary<string, object?>();
        foreach (var attr in element.Attributes())
            dict[$"@{attr.Name.LocalName}"] = attr.Value;

        foreach (var group in element.Elements().GroupBy(e => e.Name.LocalName))
        {
            var items = group.ToList();
            dict[group.Key] = items.Count == 1
                ? Convert(items[0])
                : items.Select(Convert).ToArray();
        }
        return dict;
    }
}
