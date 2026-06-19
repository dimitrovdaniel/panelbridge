using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PanelBridge.Models.Responses;
using PanelBridge.Panels;

namespace PanelBridge.Functions;

internal static class HttpResults
{
    public static IActionResult ToActionResult(PanelOperationResult result, string panel, string action) =>
        result.Status switch
        {
            PanelOperationStatus.Success => new OkObjectResult(
                ApiResponse.Success(panel, action, result.Message, result.Data)),

            PanelOperationStatus.Failure => new OkObjectResult(
                ApiResponse.Failure(panel, action, result.Message)),

            PanelOperationStatus.NotSupported => new BadRequestObjectResult(
                ApiResponse.NotSupportedOnPanel(panel, action)),

            PanelOperationStatus.Unavailable => new ObjectResult(
                ApiResponse.PanelUnavailable(panel, action, result.Message))
            { StatusCode = StatusCodes.Status502BadGateway },

            _ => new ObjectResult(ApiResponse.Failure(panel, action, "Unknown panel result."))
            { StatusCode = StatusCodes.Status500InternalServerError },
        };

    public static IActionResult InvalidRequest(string action, string message) =>
        new BadRequestObjectResult(ApiResponse.InvalidRequest(action, message));

    public static IActionResult NotFound(string action, string message) =>
        new NotFoundObjectResult(ApiResponse.NotFound(action, message));
}
