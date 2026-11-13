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

    /// <summary>
    /// True khi đã thử đọc phiên đăng nhập từ browser storage và đã có kết quả
    /// (kể cả khi kết quả là "không có ai đăng nhập").
    ///
    /// <para>
    /// Guard cần biết "chưa đọc được" và "đã đọc, không có ai đăng nhập" là hai
    /// trạng thái KHÁC NHAU. Nếu gộp chung, pha prerender (storage chưa sẵn sàng)
    /// sẽ bị hiểu là "chưa đăng nhập" ⇒ redirect oan về /login.
    /// </para>
    /// </summary>
    public bool SessionResolved { get; private set; }

    private Task? _loadTask;

    /// <summary>
    /// Số thế hệ session. Mỗi lần SignIn/SignOut tăng lên.
    ///
    /// <para>
    /// Dùng để phát hiện trường hợp "đăng nhập bằng tài khoản khác": nếu một
    /// lệnh đăng nhập mới xảy ra giữa lúc một thao tác bất đồng bộ đang chạy,
    /// thao tác đó phải bị hủy thay vì ghi đè session mới bằng dữ liệu cũ.
    /// </para>
    /// </summary>
    private int _generation;

    public int Generation => _generation;

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
            SessionResolved = true;
            NotifyChangedSafe();
        }
        catch (InvalidOperationException)
        {
            // Protected browser storage is only available after the interactive circuit starts.
            // Do not silently recurse or retry here.
            SessionResolved = false;
        }
        catch (JSDisconnectedException)
        {
            // Circuit disconnected; session will be reloaded on reconnect.
            SessionResolved = false;
        }
        catch (Exception ex)
        {
            // Lỗi không lười trước (vd JSON hỏng trong storage) ⇒ coi như chưa đăng nhập
            // nhưng KHÔNG để _loadTask bị "poison" (cached forever).
            _logger.LineWarning($"[UserSession] LoadCoreAsync failed: {ex.GetType().Name} {ex.Message}");
            CurrentUser = null;
            SessionResolved = true;
        }
    }

    public async Task SignInAsync(AuthResponseDto user)
    {
        ArgumentNullException.ThrowIfNull(user);

        // Tăng thế hệ TRƯỚC khi ghi ⇒ các thao tác cũ đang chạy sẽ biết mình
        // đã lỗi thời và không được ghi đè session mới.
        _generation++;

        CurrentUser = user;
        SessionResolved = true;

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

        // Reset cache ⇒ lần LoadAsync sau sẽ đọc lại từ storage (giữ 1 nguồn sự thật).
        _loadTask = null;

        NotifyChangedSafe();
    }

    public async Task SignOutAsync()
    {
        _generation++;

        CurrentUser = null;
        SessionResolved = true;

        try
        {
            await _storage.DeleteAsync(AuthKey);
        }
        catch (Exception ex) when (ex is JSDisconnectedException or InvalidOperationException)
        {
            _logger.Line($"[UserSession] SignOutAsync storage delete skipped: {ex.GetType().Name}");
        }

        _loadTask = null;

        NotifyChangedSafe();
    }

    /// <summary>
    /// Cập nhật thông tin của user đang đăng nhập (vd sau khi sửa hồ sơ) mà
    /// KHÔNG gọi lại toàn bộ luồng đăng nhập.
    /// </summary>
    public async Task UpdateCurrentUserAsync(Func<AuthResponseDto, AuthResponseDto> update)
    {
        var current = CurrentUser;
        if (current is null || update is null)
        {
            return;
        }

        var updated = update(current);
        if (updated == current)
        {
            return;
        }

        _generation++;
        CurrentUser = updated;

        try
        {
            await _storage.SetAsync(AuthKey, updated);
        }
        catch (Exception ex) when (ex is JSDisconnectedException or InvalidOperationException)
        {
            _logger.Line($"[UserSession] UpdateCurrentUserAsync storage write skipped: {ex.GetType().Name}");
        }

        _loadTask = null;
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
