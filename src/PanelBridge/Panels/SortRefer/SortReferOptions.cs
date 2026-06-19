using System.ComponentModel.DataAnnotations;

namespace PanelBridge.Panels.SortRefer;

public sealed class SortReferOptions
{
    public const string SectionName = "SortRefer";

    [Required, Url] public string BaseUrl { get; set; } = "";
    [Required] public string Username { get; set; } = "";
    [Required] public string Password { get; set; } = "";
}
