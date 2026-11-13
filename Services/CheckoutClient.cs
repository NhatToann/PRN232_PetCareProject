using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Services;

public sealed class CheckoutClient : ICheckoutClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CheckoutClient> _logger;

    /// <summary>
    /// Session scoped của circuit Blazor.
    ///
    /// <para>
    /// Header phiên đăng nhập phải được gắn Ở ĐÂY (trong client, cùng scope với
    /// component) chứ không gắn ở <c>DelegatingHandler</c>: handler do
    /// <c>IHttpClientFactory</c> tạo nằm ở scope riêng, không thấy được
    /// <see cref="UserSessionService"/> của circuit ⇒ <c>CurrentUser</c> luôn null
    /// ⇒ header không được gắn ⇒ <c>api/checkout</c> (có <c>[Authorize]</c>) trả 401
    /// ⇒ "luồng mua hàng không ổn".
    /// </para>
    /// </summary>
    private readonly UserSessionService _userSession;

    public CheckoutClient(HttpClient httpClient,
        ILogger<CheckoutClient> logger,
        UserSessionService userSession)
    {
        _httpClient = httpClient;
        _logger = logger;
        _userSession = userSession;
    }

    public async Task<FrontendApiResult<FrontendCheckoutResponse>> CheckoutAsync(FrontendCheckoutRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "api/checkout");
            SessionHeader.Attach(httpRequest, _userSession.CurrentUser);
            httpRequest.Content = JsonContent.Create(request);

            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return FrontendApiResult<FrontendCheckoutResponse>.Failure(await ReadErrorAsync(response, cancellationToken), (int)response.StatusCode);
            }

            var data = await response.Content.ReadFromJsonAsync<FrontendCheckoutResponse>(cancellationToken);
            return data is null
                ? FrontendApiResult<FrontendCheckoutResponse>.Failure("Checkout API returned an empty response.")
                : FrontendApiResult<FrontendCheckoutResponse>.Success(data);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LineError(ex, ex.Message);
            return FrontendApiResult<FrontendCheckoutResponse>.Failure(ex.Message);
        }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(body) ? $"API returned {(int)response.StatusCode}." : body;
    }
}
