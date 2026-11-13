using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Services;

/// <summary>
/// Xử lý callback của Google OAuth (Authorization Code flow).
///
/// <para>
/// Tách riêng thành service vì callback có thể được gọi từ <b>hai</b> nơi:
/// </para>
/// <list type="bullet">
///   <item>Endpoint <c>GET /api/auth/google-callback</c> (khi truy cập trực tiếp).</item>
///   <item>
///     Middleware <see cref="Middleware.GoogleOAuthCallbackRewriteMiddleware"/> — bắt buộc vì
///     <c>redirect_uri</c> đăng ký ở Google Cloud Console thường là <b>gốc</b> origin
///     (vd <c>http://localhost:5286</c>, không có path). Google sẽ redirect về
///     <c>http://localhost:5286/?code=...</c> — request đó rơi vào route <c>/</c> của Blazor
///     nếu không có middleware đổi path.
///   </item>
/// </list>
///
/// <para>
/// Vì routing của <c>WebApplication</c> được chạy <b>trước</b> user middleware,
/// việc chỉ đổi <c>Request.Path</c> không đủ ⇒ middleware phải tự xử lý và
/// short-circuit (trả redirect) mà không cần đi qua endpoint.
/// </para>
/// </summary>
public sealed class GoogleOAuthCallbackService
{
    private readonly IAuthService _authService;
    private readonly GoogleOAuthStateStore _stateStore;

    public GoogleOAuthCallbackService(IAuthService authService, GoogleOAuthStateStore stateStore)
    {
        _authService = authService;
        _stateStore = stateStore;
    }

    /// <summary>
    /// Xử lý tham số callback của Google và trả về URL mà người dùng cần được chuyển tới.
    /// </summary>
    /// <param name="code">Authorization code do Google cấp (có thể null nếu user từ chối).</param>
    /// <param name="state">State đã sinh trước đó (chứa origin + nonce).</param>
    /// <param name="error">Mã lỗi do Google trả về (vd <c>access_denied</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<string> HandleAsync(
        string? code,
        string? state,
        string? error,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            return BuildLoginRedirect(null, error);
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return BuildLoginRedirect(null, "missing_code");
        }

        var payload = _stateStore.ValidateState(state);
        if (payload is null)
        {
            return BuildLoginRedirect(null, "invalid_state");
        }

        try
        {
            var user = await _authService.LoginWithGoogleCodeAsync(
                new GoogleCodeLoginRequestDto(code, payload.Origin, state), ct);

            var oneTimeToken = _stateStore.StoreCompletion(user);
            return BuildLoginRedirect(payload.Origin, null, oneTimeToken);
        }
        catch (UnauthorizedAccessException ex)
        {
            return BuildLoginRedirect(payload.Origin, ex.Message);
        }
        catch (Exception ex)
        {
            return BuildLoginRedirect(payload.Origin, ex.Message);
        }
    }

    /// <summary>
    /// Dựng URL quay lại trang đăng nhập của origin ban đầu, mang theo kết quả.
    /// </summary>
    public static string BuildLoginRedirect(string? origin, string? error, string? token = null)
    {
        var target = string.IsNullOrWhiteSpace(origin)
            ? "http://localhost:5286/login"
            : $"{origin.TrimEnd('/')}/login";

        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(token)) query.Add($"token={Uri.EscapeDataString(token)}");
        if (!string.IsNullOrWhiteSpace(error)) query.Add($"googleError={Uri.EscapeDataString(error)}");

        return query.Count > 0 ? $"{target}?{string.Join("&", query)}" : target;
    }
}
