using System.ServiceModel;
using System.ServiceModel.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PanelBridge.Domain;
using PanelBridge.Panels.Econ.Generated;
using Task = System.Threading.Tasks.Task;

namespace PanelBridge.Panels.Econ;

/// <summary>
/// Single Econ case handler entry returned by GetCaseHandlers. Email and telephone aren't
/// exposed by the supplier contract - PersonRef is the panel-side identity. EconClient surfaces
/// this typed shape via PanelOperationResult.Data so HandlerSync can synthesise an email
/// for PanelBridge's casehandlers table.
/// </summary>
public sealed record EconHandler(string FullName, string PersonRef);

public sealed class EconClient(
    IOptions<EconOptions> options,
    ILogger<EconClient> logger) : IPanelClient
{
    private readonly EconOptions _options = options.Value;
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private (string Username, string Password)? _cachedSession;
    private DateTimeOffset _sessionExpiresAt = DateTimeOffset.MinValue;

    public string Key => PanelKey.Econ;

    public Task<PanelOperationResult> AcceptAsync(string panelRef, AcceptCaseDetails details, CancellationToken ct)
        => InvokeAsync("accept", async client =>
        {
            var result = await client.AcceptInstructionAsync(new AcceptInstructionArgs
            {
                InstructionRef = panelRef,
                ExternalInstructionReference = details.InternalRef ?? "",
            });
            return Map(result.Result, result.Failures);
        });

    public Task<PanelOperationResult> CancelAsync(string panelRef, string reason, CancellationToken ct)
        => InvokeAsync("cancel", async client =>
        {
            var result = await client.CancelCasesAsync(new CancelCasesArgs
            {
                InstructionRef = panelRef,
                CaseRefs = new[] { panelRef },
                Reason = reason,
            });
            return Map(result.Result, result.Failures);
        });

    public Task<PanelOperationResult> CompleteAsync(string panelRef, CancellationToken ct)
        => InvokeAsync("complete", async client =>
        {
            var result = await client.CompleteCasesAsync(new CompleteCasesArgs
            {
                InstructionRef = panelRef,
                CaseCompletions = new[]
                {
                    new CaseCompletion
                    {
                        CaseCompletionIndex = 0,
                        CaseRef = panelRef,
                    },
                },
            });
            return Map(result.Result, result.Failures);
        });

    public Task<PanelOperationResult> DeclineAsync(string panelRef, string reason, CancellationToken ct)
        => InvokeAsync("decline", async client =>
        {
            var result = await client.DeclineInstructionAsync(new DeclineInstructionArgs
            {
                InstructionRef = panelRef,
                Reason = reason,
            });
            return Map(result.Result, result.Failures);
        });

    public Task<PanelOperationResult> ReactivateAsync(string panelRef, CancellationToken ct)
        => Task.FromResult(PanelOperationResult.NotSupported("reactivate"));

    public Task<PanelOperationResult> SuspendAsync(string panelRef, string reason, CancellationToken ct)
        => Task.FromResult(PanelOperationResult.NotSupported("suspend"));

    // v0.2 / v0.3 stubs — Econ implementations deferred until credentials + supplier IP whitelist land.
    // SOAP contracts exist in Panels/Econ/Generated/InstructionManagementClient.cs; wire when needed.

    public Task<PanelOperationResult> ChangeHandlerAsync(string panelRef, AcceptCaseDetails details, CancellationToken ct)
        => Task.FromResult(PanelOperationResult.NotSupported("change-handler"));

    public Task<PanelOperationResult> AddHandlerAsync(HandlerDetails details, CancellationToken ct)
        => Task.FromResult(PanelOperationResult.NotSupported("handler-add"));

    public Task<PanelOperationResult> EditHandlerAsync(HandlerUpdate update, CancellationToken ct)
        => Task.FromResult(PanelOperationResult.NotSupported("handler-edit"));

    public Task<PanelOperationResult> ListHandlersAsync(CancellationToken ct)
        => InvokeAsync("handler-list", async client =>
        {
            var result = await client.GetCaseHandlersAsync(new GetCaseHandlersArgs());
            var handlers = (result.CaseHandlers ?? Array.Empty<Generated.CaseHandler>())
                .Where(h => !string.IsNullOrWhiteSpace(h.PersonRef))
                .Select(h => new EconHandler(h.FullName ?? "", h.PersonRef))
                .ToArray();

            logger.LogInformation(
                "Econ GetCaseHandlers returned Result={Result}, handlers={Count}, failures={FailureCount}",
                result.Result, handlers.Length, result.Failures?.Length ?? 0);

            if (handlers.Length == 0 && result.Result != 0)
                return Map(result.Result, result.Failures);

            return PanelOperationResult.Success(
                $"{handlers.Length} Econ handler(s) (Result={result.Result}).",
                handlers);
        });

    public Task<PanelOperationResult> FetchInstructionAsync(string panelRef, CancellationToken ct)
        => Task.FromResult(PanelOperationResult.NotSupported("fetch"));

    public Task<PanelOperationResult> ListPendingAsync(CancellationToken ct)
        => Task.FromResult(PanelOperationResult.NotSupported("list-pending"));

    public Task<PanelOperationResult> SetMilestoneAsync(string panelRef, string milestoneCode, bool completed, CancellationToken ct)
        => Task.FromResult(PanelOperationResult.NotSupported("milestone-update"));

    public Task<PanelOperationResult> SetSupplierReferenceAsync(string panelRef, string supplierReference, CancellationToken ct)
        => Task.FromResult(PanelOperationResult.NotSupported("set-supplier-reference"));

    // spec extras — stubs, wire when Econ goes live.

    public Task<PanelOperationResult> FetchQuotePdfAsync(string panelRef, bool zip, CancellationToken ct)
        => Task.FromResult(PanelOperationResult.NotSupported("fetch-quote"));

    public Task<PanelOperationResult> SetEstimatedCompletionAsync(string panelRef, string yyyyMmDd, CancellationToken ct)
        => Task.FromResult(PanelOperationResult.NotSupported("set-est-completion"));

    public Task<PanelOperationResult> AddNoteAsync(string panelRef, string text, bool isPrivate, CancellationToken ct)
        => Task.FromResult(PanelOperationResult.NotSupported("notes-add"));

    public Task<PanelOperationResult> RemoveNoteAsync(string noteGuid, CancellationToken ct)
        => Task.FromResult(PanelOperationResult.NotSupported("notes-remove"));

    public Task<PanelOperationResult> ListNotesAsync(CancellationToken ct)
        => Task.FromResult(PanelOperationResult.NotSupported("notes-list"));

    public Task<PanelOperationResult> MarkNoteReadAsync(string noteGuid, CancellationToken ct)
        => Task.FromResult(PanelOperationResult.NotSupported("notes-mark-read"));

    public Task<PanelOperationResult> ListDocumentsForCaseAsync(string panelRef, CancellationToken ct)
        => Task.FromResult(PanelOperationResult.NotSupported("documents-list"));

    public Task<PanelOperationResult> ListAllDocumentsAsync(CancellationToken ct)
        => Task.FromResult(PanelOperationResult.NotSupported("documents-list-all"));

    public Task<PanelOperationResult> MarkDocumentReadAsync(string documentGuid, CancellationToken ct)
        => Task.FromResult(PanelOperationResult.NotSupported("documents-mark-read"));

    public Task<PanelOperationResult> ListLendersAsync(CancellationToken ct)
        => Task.FromResult(PanelOperationResult.NotSupported("lenders-list"));

    public Task<PanelOperationResult> ListMilestonesAsync(CancellationToken ct)
        => Task.FromResult(PanelOperationResult.NotSupported("milestones-list"));

    private async Task<PanelOperationResult> InvokeAsync(
        string action,
        Func<InstructionManagementServiceClient, Task<PanelOperationResult>> call)
    {
        InstructionManagementServiceClient? client = null;
        try
        {
            var binding = new BasicHttpBinding(BasicHttpSecurityMode.TransportWithMessageCredential)
            {
                MaxReceivedMessageSize = 52428800,
                MaxBufferSize = 52428800,
                SendTimeout = TimeSpan.FromSeconds(30),
                ReceiveTimeout = TimeSpan.FromSeconds(30),
            };
            binding.Security.Message.ClientCredentialType = BasicHttpMessageCredentialType.UserName;

            // Route outbound through the egress proxy VM when configured, so supplier sees the
            // proxy's static IP. Proxy basic-auth creds are embedded in the URI userinfo.
            if (!string.IsNullOrWhiteSpace(_options.OutboundProxyUrl))
            {
                binding.ProxyAddress = BuildProxyUri();
                binding.UseDefaultWebProxy = false;
            }

            var endpoint = new EndpointAddress(_options.InstructionManagementUrl);
            client = new InstructionManagementServiceClient(binding, endpoint);

            // Use the cached session credentials (from StartSession) for InstructionManagement.
            // The raw EconOptions.Username/Password are only used inside GetSessionAsync.
            var (sessionUser, sessionPwd) = await GetSessionAsync();
            client.ClientCredentials.UserName.UserName = sessionUser;
            client.ClientCredentials.UserName.Password = sessionPwd;

            var result = await call(client);
            await client.CloseAsync();
            return result;
        }
        catch (EndpointNotFoundException ex)
        {
            logger.LogWarning(ex, "Econ {Action} endpoint not reachable", action);
            client?.Abort();
            return PanelOperationResult.Unavailable($"Econ endpoint not reachable: {ex.Message}");
        }
        catch (MessageSecurityException ex)
        {
            logger.LogWarning(ex, "Econ {Action} authentication failed", action);
            client?.Abort();
            return PanelOperationResult.Unavailable($"Econ authentication failed: {ex.Message}");
        }
        catch (TimeoutException ex)
        {
            logger.LogWarning(ex, "Econ {Action} timed out", action);
            client?.Abort();
            return PanelOperationResult.Unavailable("Econ request timed out.");
        }
        catch (CommunicationException ex)
        {
            logger.LogWarning(ex, "Econ {Action} transport error", action);
            client?.Abort();
            return PanelOperationResult.Unavailable($"Econ transport error: {ex.Message}");
        }
    }

    private static PanelOperationResult Map(int resultCode, Failure[]? failures)
    {
        // Econ result code 0 = success; non-zero indicates the call did not fully succeed.
        if (resultCode == 0 && (failures is null || failures.Length == 0))
            return PanelOperationResult.Success("Econ call succeeded.");

        var message = failures is { Length: > 0 }
            ? string.Join("; ", failures.Select(FormatFailure))
            : $"Econ returned non-success result code {resultCode}.";

        return PanelOperationResult.Failure(message);
    }

    private static string FormatFailure(Failure f)
    {
        // Failure inherits from FailureBase; the base fields aren't directly visible here,
        // so fall back to ToString(). When live testing produces real failures, refine.
        return f.ToString() ?? "Econ failure (no detail).";
    }

    private async Task<(string Username, string Password)> GetSessionAsync()
    {
        if (_cachedSession is { } cached && DateTimeOffset.UtcNow < _sessionExpiresAt)
            return cached;

        await _sessionLock.WaitAsync();
        try
        {
            if (_cachedSession is { } cached2 && DateTimeOffset.UtcNow < _sessionExpiresAt)
                return cached2;

            var binding = BuildBinding();
            var endpoint = new EndpointAddress(_options.StartSessionUrl);
            var startClient = new StartSessionServiceClient(binding, endpoint);
            startClient.ClientCredentials.UserName.UserName = _options.Username;
            startClient.ClientCredentials.UserName.Password = _options.Password;

            try
            {
                var result = await startClient.StartSessionAsync(new SessionStartArgs());
                await startClient.CloseAsync();

                if (result is null
                    || string.IsNullOrEmpty(result.SessionUserName)
                    || string.IsNullOrEmpty(result.SessionPassword))
                {
                    throw new InvalidOperationException(
                        $"Econ StartSession did not return session credentials: result={result?.Result}");
                }

                // Result != 0 means "ok with warnings" (e.g. password near expiry); session is still usable.
                if (result.Result != 0)
                {
                    logger.LogWarning(
                        "Econ StartSession returned warning result={Result} (sessionUser='{User}', daysBeforePwdExpiry={Days}). Continuing with the session.",
                        result.Result, result.SessionUserName, result.DaysBeforePasswordExpiry);
                }

                _cachedSession = (result.SessionUserName, result.SessionPassword);
                _sessionExpiresAt = DateTimeOffset.UtcNow + _options.SessionLifetime;

                logger.LogInformation("Econ session established; cached until {ExpiresAt}",
                    _sessionExpiresAt);
                return _cachedSession.Value;
            }
            catch
            {
                startClient.Abort();
                _cachedSession = null;
                _sessionExpiresAt = DateTimeOffset.MinValue;
                throw;
            }
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    private BasicHttpBinding BuildBinding()
    {
        var binding = new BasicHttpBinding(BasicHttpSecurityMode.TransportWithMessageCredential)
        {
            MaxReceivedMessageSize = 52428800,
            MaxBufferSize = 52428800,
            SendTimeout = TimeSpan.FromSeconds(30),
            ReceiveTimeout = TimeSpan.FromSeconds(30),
        };
        binding.Security.Message.ClientCredentialType = BasicHttpMessageCredentialType.UserName;
        if (!string.IsNullOrWhiteSpace(_options.OutboundProxyUrl))
        {
            binding.ProxyAddress = BuildProxyUri();
            binding.UseDefaultWebProxy = false;
        }
        return binding;
    }

    private Uri BuildProxyUri()
    {
        var raw = _options.OutboundProxyUrl ?? "";
        if (string.IsNullOrWhiteSpace(_options.OutboundProxyUsername))
            return new Uri(raw);

        var builder = new UriBuilder(raw)
        {
            UserName = Uri.EscapeDataString(_options.OutboundProxyUsername),
            Password = Uri.EscapeDataString(_options.OutboundProxyPassword ?? ""),
        };
        return builder.Uri;
    }
}
