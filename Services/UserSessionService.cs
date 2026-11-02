using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using PetShop.Models;

namespace PetShop.Services;

public sealed class UserSessionService
{
    private const string AuthKey = "authUser";
    private readonly ProtectedLocalStorage _storage;
    private readonly ILogger<UserSessionService> _logger;

    public UserSessionService(ProtectedLocalStorage storage, ILogger<UserSessionService> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    public event Action? Changed;

    public AuthResponseDto? CurrentUser { get; private set; }

    public bool IsAuthenticated => CurrentUser is not null;

    private Task? _loadTask;

    public async Task<AuthResponseDto?> LoadAsync()
    {
        // Share the in-flight load across concurrent callers.
        // The first caller populates _loadTask; everyone else awaits the same task.
        if (_loadTask is null)
        {
            _loadTask = LoadCoreAsync();
        }

        await _loadTask;
        return CurrentUser;
    }

    private async Task LoadCoreAsync()
    {
        try
        {
            var result = await _storage.GetAsync<AuthResponseDto>(AuthKey);
            CurrentUser = result.Success ? result.Value : null;
            NotifyChangedSafe();
        }
        catch (InvalidOperationException)
        {
            // Protected browser storage is only available after the interactive circuit starts.
            // Do not silently recurse or retry here.
        }
        catch (JSDisconnectedException)
        {
            // Circuit disconnected; session will be reloaded on reconnect.
        }
    }

    public async Task SignInAsync(AuthResponseDto user)
    {
        CurrentUser = user ?? throw new ArgumentNullException(nameof(user));
        // The storage roundtrip is awaited but never silently swallowed.
        try
        {
            await _storage.SetAsync(AuthKey, user);
        }
        catch (Exception ex) when (ex is JSDisconnectedException or InvalidOperationException)
        {
            // Storage unavailable (reconnect / pre-render). The in-memory session is already updated.
            _logger.Line($"[UserSession] SignInAsync storage write skipped: {ex.GetType().Name}");
        }
        NotifyChangedSafe();
    }

    public async Task SignOutAsync()
    {
        CurrentUser = null;
        try
        {
            await _storage.DeleteAsync(AuthKey);
        }
        catch (Exception ex) when (ex is JSDisconnectedException or InvalidOperationException)
        {
            _logger.Line($"[UserSession] SignOutAsync storage delete skipped: {ex.GetType().Name}");
        }
        NotifyChangedSafe();
    }

    private void NotifyChangedSafe()
    {
        try
        {
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            // A subscriber may be unsubscribed or in a bad state — never let the storage op failure
            // propagate because of event fan-out.
            _logger.Line($"[UserSession] Changed handler threw: {ex.GetType().Name}");
        }
    }
}
