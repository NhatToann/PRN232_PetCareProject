using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Services;

/// <summary>
/// Client HTTP gọi API chat 1-1 của ứng dụng (Q21).
///
/// <para>
/// <b>Vì sao tự gắn header phiên đăng nhập:</b> các endpoint <c>api/chat/*</c>
/// có <c>[Authorize]</c> và lấy danh tính người dùng từ <b>claims</b> ⇒ request
/// phải có header <c>X-User-Id</c> / <c>X-User-Role</c>. Client gắn header trực
/// tiếp từ <see cref="UserSessionService"/> (scoped) — cùng pattern với
/// <c>PetsClient</c>. <c>SpaAuthHeaderHandler</c> không dùng được vì handler
/// được tạo ngoài scope Blazor nên không thấy được <c>CurrentUser</c>.
/// </para>
///
/// <para>
/// Mọi lỗi đều được quy về <see cref="FrontendApiResult{T}"/> thay vì ném
/// exception ⇒ widget chat không bao giờ bị hỏng vì lỗi mạng.
/// </para>
/// </summary>
public sealed class ChatClient : IChatClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ChatClient> _logger;
    private readonly UserSessionService _userSession;

    public ChatClient(
        HttpClient httpClient,
        ILogger<ChatClient> logger,
        UserSessionService userSession)
    {
        _httpClient = httpClient;
        _logger = logger;
        _userSession = userSession;
    }

    /// <inheritdoc />
    public Task<FrontendApiResult<IReadOnlyList<ChatPeerDto>>> GetPeersAsync(CancellationToken ct = default) =>
        GetJsonAsync<IReadOnlyList<ChatPeerDto>>("api/chat/peers", Array.Empty<ChatPeerDto>(), ct);

    /// <inheritdoc />
    public Task<FrontendApiResult<IReadOnlyList<ChatMessageDto>>> GetHistoryAsync(
        int peerId,
        string? peerKind = null,
        CancellationToken ct = default)
    {
        var uri = "api/chat/history/" + peerId;

        if (!string.IsNullOrWhiteSpace(peerKind))
        {
            uri += "?peerKind=" + Uri.EscapeDataString(peerKind);
        }

        return GetJsonAsync<IReadOnlyList<ChatMessageDto>>(uri, Array.Empty<ChatMessageDto>(), ct);
    }

    /// <inheritdoc />
    public Task<FrontendApiResult<ChatSendResultDto>> SendMessageAsync(
        ChatSendRequestDto request,
        CancellationToken ct = default) =>
        SendJsonAsync<ChatSendResultDto>(HttpMethod.Post, "api/chat/send", request, ct);

    /// <summary>
    /// Tạo request có gắn header phiên đăng nhập
    /// (<c>X-User-Id</c> / <c>X-Customer-Id</c> / <c>X-User-Role</c>).
    /// </summary>
    private HttpRequestMessage CreateAuthedRequest(HttpMethod method, string uri)
    {
        var request = new HttpRequestMessage(method, uri);

        var user = _userSession.CurrentUser;
        if (user is not null && user.UserId > 0)
        {
            var userId = user.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture);

            request.Headers.TryAddWithoutValidation("X-User-Id", userId);
            request.Headers.TryAddWithoutValidation("X-Customer-Id", userId);

            if (!string.IsNullOrWhiteSpace(user.Role))
            {
                request.Headers.TryAddWithoutValidation("X-User-Role", user.Role);
            }
        }

        return request;
    }

    private async Task<FrontendApiResult<T>> GetJsonAsync<T>(
        string uri,
        T emptyValue,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = CreateAuthedRequest(HttpMethod.Get, uri);
            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return FrontendApiResult<T>.Failure(
                    await response.Content.ReadAsStringAsync(cancellationToken),
                    (int)response.StatusCode);
            }

            var data = await response.Content.ReadFromJsonAsync<T>(cancellationToken);
            return data is null
                ? FrontendApiResult<T>.Failure("API returned an empty response.", (int)response.StatusCode)
                : FrontendApiResult<T>.Success(data);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LineError(ex, $"[ChatClient] Lỗi gọi GET {uri}: {ex.Message}");
            return FrontendApiResult<T>.Failure(ex.Message);
        }
    }

    private async Task<FrontendApiResult<T>> SendJsonAsync<T>(
        HttpMethod method,
        string uri,
        object body,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = CreateAuthedRequest(method, uri);
            request.Content = JsonContent.Create(body);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return FrontendApiResult<T>.Failure(
                    await response.Content.ReadAsStringAsync(cancellationToken),
                    (int)response.StatusCode);
            }

            var data = await response.Content.ReadFromJsonAsync<T>(cancellationToken);
            return data is null
                ? FrontendApiResult<T>.Failure("API returned an empty response.", (int)response.StatusCode)
                : FrontendApiResult<T>.Success(data);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LineError(ex, $"[ChatClient] Lỗi gọi {method} {uri}: {ex.Message}");
            return FrontendApiResult<T>.Failure(ex.Message);
        }
    }
}
