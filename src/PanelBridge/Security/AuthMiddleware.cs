using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PanelBridge.Security;

/// <summary>
/// Gates every HTTP-triggered function:
///   - /api/swagger-login (GET) → renders an HTML login form
///   - /api/swagger-login (POST) → validates credentials, sets session cookie, redirects
///   - other Swagger/OpenAPI doc paths → require a valid session cookie; else redirect to login
///   - everything else (the API) → trust Easy Auth if it's stamped the request,
///                                  otherwise fall back to X-API-Key
/// Non-HTTP invocations are passed straight through.
/// </summary>
public sealed class AuthMiddleware(
    IOptions<BridgeSecurityOptions> options,
    ILogger<AuthMiddleware> logger) : IFunctionsWorkerMiddleware
{
    private readonly BridgeSecurityOptions _opt = options.Value;

    private const string CookieName = "omni_swagger_session";
    private const string LoginPath = "/api/swagger-login";
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);

    private static readonly string[] SwaggerPathPrefixes =
    {
        "/api/swagger",
        "/api/openapi",
        "/api/oauth2-redirect",
    };

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var http = context.GetHttpContext();
        if (http is null) { await next(context); return; }

        var path = http.Request.Path.Value ?? "";

        if (path.Equals(LoginPath, StringComparison.OrdinalIgnoreCase))
        {
            await HandleLoginAsync(http);
            return;
        }

        if (IsSwaggerPath(path))
        {
            if (IsValidCookie(http))
            {
                await next(context);
                return;
            }
            RedirectToLogin(http);
            return;
        }

        // Non-Swagger API paths.
        if (!string.IsNullOrEmpty(http.Request.Headers["X-MS-CLIENT-PRINCIPAL-ID"].ToString()))
        {
            await next(context);
            return;
        }

        if (IsValidApiKey(http))
        {
            await next(context);
            return;
        }

        await WriteUnauthorizedApiKeyAsync(http);
    }

    private async Task HandleLoginAsync(HttpContext http)
    {
        if (HttpMethods.IsGet(http.Request.Method))
        {
            var returnUrl = SanitiseReturnUrl(http.Request.Query["return"].ToString());
            await ServeLoginFormAsync(http, returnUrl, errorMessage: null);
            return;
        }

        if (HttpMethods.IsPost(http.Request.Method))
        {
            string username, password, returnUrl;
            try
            {
                var form = await http.Request.ReadFormAsync();
                username = form["username"].ToString();
                password = form["password"].ToString();
                returnUrl = SanitiseReturnUrl(form["return"].ToString());
            }
            catch
            {
                http.Response.StatusCode = StatusCodes.Status400BadRequest;
                await http.Response.WriteAsync("Invalid form submission.");
                return;
            }

            if (IsValidSwaggerLogin(username, password))
            {
                IssueCookie(http);
                http.Response.Redirect(returnUrl);
                return;
            }

            logger.LogInformation("Swagger login failed for username='{User}'", username);
            await ServeLoginFormAsync(http, returnUrl, errorMessage: "Incorrect username or password.");
            return;
        }

        http.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
        await http.Response.WriteAsync("Method not allowed.");
    }

    private void RedirectToLogin(HttpContext http)
    {
        var returnPath = http.Request.Path + http.Request.QueryString;
        var target = $"{LoginPath}?return={Uri.EscapeDataString(returnPath)}";
        http.Response.Redirect(target);
    }

    private static async Task ServeLoginFormAsync(HttpContext http, string returnUrl, string? errorMessage)
    {
        http.Response.StatusCode = errorMessage is null
            ? StatusCodes.Status200OK
            : StatusCodes.Status401Unauthorized;
        http.Response.ContentType = "text/html; charset=utf-8";
        http.Response.Headers["Cache-Control"] = "no-store";

        var errorBlock = errorMessage is null
            ? ""
            : $"<p style=\"color:#b00\">{System.Net.WebUtility.HtmlEncode(errorMessage)}</p>";

        await http.Response.WriteAsync($$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <title>PanelBridge · Sign in</title>
              <meta name="viewport" content="width=device-width,initial-scale=1">
              <style>
                body { font-family: -apple-system,Segoe UI,Roboto,sans-serif; background:#f5f5f7; display:grid; place-items:center; min-height:100vh; margin:0; }
                .card { background:#fff; padding:2rem; border-radius:8px; box-shadow:0 4px 24px rgba(0,0,0,.08); width:340px; }
                h1 { margin:0 0 1rem; font-size:1.25rem; }
                label { display:block; font-size:.85rem; margin:.75rem 0 .25rem; color:#444; }
                input { width:100%; padding:.55rem .65rem; border:1px solid #ccc; border-radius:4px; font-size:.95rem; box-sizing:border-box; }
                button { margin-top:1.25rem; width:100%; padding:.6rem; background:#111; color:#fff; border:0; border-radius:4px; font-size:.95rem; cursor:pointer; }
                button:hover { background:#000; }
              </style>
            </head>
            <body>
              <form class="card" method="POST" action="{{LoginPath}}" autocomplete="off">
                <h1>Sign in to PanelBridge</h1>
                {{errorBlock}}
                <input type="hidden" name="return" value="{{System.Net.WebUtility.HtmlEncode(returnUrl)}}">
                <label for="u">Username</label>
                <input id="u" name="username" type="text" required autofocus>
                <label for="p">Password</label>
                <input id="p" name="password" type="password" required>
                <button type="submit">Sign in</button>
              </form>
            </body>
            </html>
            """);
    }

    private static string SanitiseReturnUrl(string raw)
    {
        // Only accept same-origin relative paths under /api/.
        if (string.IsNullOrWhiteSpace(raw)) return "/api/swagger/ui";
        if (!raw.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)) return "/api/swagger/ui";
        if (raw.StartsWith("//", StringComparison.Ordinal)) return "/api/swagger/ui";
        if (raw.Contains("..", StringComparison.Ordinal)) return "/api/swagger/ui";
        return raw;
    }

    private void IssueCookie(HttpContext http)
    {
        var expiry = DateTimeOffset.UtcNow.Add(SessionLifetime).ToUnixTimeSeconds();
        var payload = expiry.ToString(CultureInfo.InvariantCulture);
        var mac = ComputeHmac(payload);
        var value = $"{payload}.{mac}";
        http.Response.Cookies.Append(CookieName, value, new CookieOptions
        {
            HttpOnly = true,
            Secure = http.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            MaxAge = SessionLifetime,
            Path = "/",
        });
    }

    private bool IsValidCookie(HttpContext http)
    {
        var raw = http.Request.Cookies[CookieName];
        if (string.IsNullOrEmpty(raw)) return false;
        var parts = raw.Split('.', 2);
        if (parts.Length != 2) return false;
        if (!long.TryParse(parts[0], CultureInfo.InvariantCulture, out var expiry)) return false;
        if (DateTimeOffset.FromUnixTimeSeconds(expiry) < DateTimeOffset.UtcNow) return false;
        var expected = ComputeHmac(parts[0]);
        return FixedTimeEquals(expected, parts[1]);
    }

    private string ComputeHmac(string input)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(_opt.SwaggerCookieKey));
        var hash = h.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static bool IsSwaggerPath(string path) =>
        SwaggerPathPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    private bool IsValidSwaggerLogin(string username, string password)
    {
        if (FixedTimeEquals(username, _opt.SwaggerUsername))
            return FixedTimeEquals(password, _opt.SwaggerPassword);

        foreach (var user in _opt.SwaggerUsers)
        {
            if (FixedTimeEquals(username, user.Username))
                return FixedTimeEquals(password, user.Password);
        }
        return false;
    }

    private bool IsValidApiKey(HttpContext http)
    {
        var header = http.Request.Headers["X-API-Key"].ToString();
        return !string.IsNullOrEmpty(header) && FixedTimeEquals(header, _opt.ApiKey);
    }

    private async Task WriteUnauthorizedApiKeyAsync(HttpContext http)
    {
        logger.LogInformation("Rejected request to {Path}: missing/invalid X-API-Key", http.Request.Path);
        http.Response.StatusCode = StatusCodes.Status401Unauthorized;
        http.Response.ContentType = "application/json";
        await http.Response.WriteAsync(
            "{\"status\":\"unauthorized\",\"message\":\"Missing or invalid X-API-Key header.\"}");
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var ab = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        return ab.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ab, bb);
    }
}
