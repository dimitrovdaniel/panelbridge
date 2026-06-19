using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace PanelBridge.Functions;

internal static class RequestValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static async Task<(T? Value, string? Error)> ReadJsonAsync<T>(HttpRequest req, CancellationToken ct)
        where T : class
    {
        T? value;
        try
        {
            value = await JsonSerializer.DeserializeAsync<T>(req.Body, JsonOptions, ct);
        }
        catch (JsonException ex)
        {
            return (null, $"Invalid JSON body: {ex.Message}");
        }

        if (value is null)
            return (null, "Request body is required.");

        var ctx = new ValidationContext(value);
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(value, ctx, results, validateAllProperties: true))
        {
            var msg = string.Join("; ", results.Select(r => r.ErrorMessage));
            return (null, msg);
        }

        return (value, null);
    }
}
