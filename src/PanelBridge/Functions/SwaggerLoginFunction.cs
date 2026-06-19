using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace PanelBridge.Functions;

/// <summary>
/// Registers the /api/swagger-login route so the Functions host invokes the worker
/// middleware pipeline for it. The actual login logic lives in
/// <see cref="PanelBridge.Security.AuthMiddleware"/>, which intercepts before this body runs.
/// </summary>
public sealed class SwaggerLoginFunction
{
    [Function("SwaggerLogin")]
    public IActionResult Handle(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "swagger-login")] HttpRequest req)
        // Should never be reached: AuthMiddleware short-circuits both GET (renders form)
        // and POST (validates and sets the cookie) before the body executes.
        => new StatusCodeResult(StatusCodes.Status500InternalServerError);
}
