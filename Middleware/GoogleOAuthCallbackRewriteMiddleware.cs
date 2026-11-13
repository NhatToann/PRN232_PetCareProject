using PetShop.Services;

namespace PetShop.Middleware;

/// <summary>
/// Bắt và xử lý luồng Google OAuth callback (Authorization Code flow).
///
/// <para>
/// Vấn đề: trong Google Cloud Console, <c>redirect_uri</c> được đăng ký là
/// <c>http://localhost:5286</c> — tức là <b>gốc</b> của origin, không có path.
/// Google sẽ redirect về đúng URL đó kèm <c>?code=...&amp;state=...</c>:
/// <c>http://localhost:5286/?code=...</c>.
/// </para>
///
/// <para>
/// Nếu không có middleware này, request đó rơi vào route <c>/</c> của Blazor
/// (trang chủ) và query string bị bỏ qua ⇒ mất code ⇒ không đăng nhập được.
/// </para>
///
/// <para>
/// VÌ SAO KHÔNG CHỈ ĐỔI <c>Request.Path</c>:
/// routing của <c>WebApplication</c> được chạy <b>trước</b> user middleware, nên
/// endpoint đã được chọn xong ⇒ đổi path cũng vô tác dụng. Vì vậy middleware
/// tự gọi <see cref="GoogleOAuthCallbackService"/> và short-circuit bằng redirect.
/// </para>
///
/// <para>
/// Query string được giữ nguyên nên <c>code</c>/<c>state</c>/<c>error</c> vẫn tới
/// được service xử lý đúng.
/// </para>
/// </summary>
public sealed class GoogleOAuthCallbackRewriteMiddleware
{
    private readonly RequestDelegate _next;
    private readonly GoogleOAuthCallbackService _callbackService;
    private readonly string _configuredRedirectPath;

    public GoogleOAuthCallbackRewriteMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        GoogleOAuthCallbackService callbackService)
    {
        _next = next;
        _callbackService = callbackService;

        var redirectUri = (configuration["GoogleAuth:RedirectUri"] ?? string.Empty).Trim();

        _configuredRedirectPath = "/";
        if (!string.IsNullOrWhiteSpace(redirectUri)
            && Uri.TryCreate(redirectUri, UriKind.Absolute, out var parsed)
            && !string.IsNullOrWhiteSpace(parsed.AbsolutePath))
        {
            _configuredRedirectPath = parsed.AbsolutePath.TrimEnd('/');
            if (_configuredRedirectPath.Length == 0) _configuredRedirectPath = "/";
        }
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (ShouldHandle(context.Request))
        {
            var code = context.Request.Query["code"].ToString();
            var state = context.Request.Query["state"].ToString();
            var error = context.Request.Query["error"].ToString();

            var redirectUrl = await _callbackService.HandleAsync(code, state, error, context.RequestAborted);

            context.Response.Redirect(redirectUrl);
            return;
        }

        await _next(context);
    }

    private bool ShouldHandle(HttpRequest request)
    {
        // Chỉ xét các request có query mang dấu hiệu OAuth callback.
        if (!request.Query.ContainsKey("code") && !request.Query.ContainsKey("error"))
        {
            return false;
        }

        // Path hiện tại phải khớp đúng path của redirect_uri đã cấu hình
        // (mặc định là gốc "/" vì redirect_uri thường đăng ký không có path).
        var current = request.Path.Value?.TrimEnd('/');
        if (string.IsNullOrEmpty(current)) current = "/";

        return string.Equals(current, _configuredRedirectPath, StringComparison.OrdinalIgnoreCase);
    }
}
