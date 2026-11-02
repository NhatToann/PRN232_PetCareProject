using Microsoft.Extensions.Logging;
using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Services;

/// <summary>
/// Attaches X-Customer-Id / X-User-Id / X-User-Role headers from the Blazor-scoped
/// UserSessionService to every outgoing SPA/PayOS request. Backend controllers
/// (SpaBookingController, etc.) resolve the customer from this header instead
/// of trusting query params.
/// </summary>
public sealed class SpaAuthHeaderHandler : DelegatingHandler
{
    private readonly IServiceProvider _services;
    private readonly ILogger<SpaAuthHeaderHandler> _logger;

    public SpaAuthHeaderHandler(IServiceProvider services, ILogger<SpaAuthHeaderHandler> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            // CRITICAL: We must NEVER call UserSession.LoadAsync() from the handler.
            // Doing so issues a JS interop roundtrip and ProtectedLocalStorage call
            // on every outbound HTTP request, which is what produced the original
            // observed latency. Read the already-resolved in-memory user only.
            //
            // The handler is created outside the interactive Blazor scope, so we
            // resolve the scoped UserSessionService through the root provider.
            // The user's CurrentUser is set by TopBar/Header/etc. and survives
            // across HTTP calls within the same circuit.
            AuthResponseDto? user = null;
            try
            {
                user = (_services.GetService(typeof(UserSessionService)) as UserSessionService)?.CurrentUser;
            }
            catch (Exception ex)
            {
                _logger.LineWarning($"[SpaAuthHeaderHandler] UserSession lookup failed: {ex.GetType().Name} {ex.Message}");
            }

            if (user is null)
            {
                return await base.SendAsync(request, cancellationToken);
            }

            if (!request.Headers.Contains("X-Customer-Id") && user.UserId > 0)
            {
                request.Headers.TryAddWithoutValidation("X-Customer-Id", user.UserId.ToString());
            }

            if (!request.Headers.Contains("X-User-Id") && user.UserId > 0)
            {
                request.Headers.TryAddWithoutValidation("X-User-Id", user.UserId.ToString());
            }

            if (!request.Headers.Contains("X-User-Role") && !string.IsNullOrWhiteSpace(user.Role))
            {
                request.Headers.TryAddWithoutValidation("X-User-Role", user.Role);
            }

            return await base.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LineError(ex, "[SpaAuthHeaderHandler] Failed to attach session headers");
            throw;
        }
    }
}
