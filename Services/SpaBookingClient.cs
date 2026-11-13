using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Services;

public sealed class SpaBookingClient : ISpaBookingClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SpaBookingClient> _logger;

    /// <summary>
    /// Session scoped của circuit Blazor — dùng để gắn header phiên đăng nhập.
    ///
    /// <para>
    /// KHÔNG dùng <c>SpaAuthHeaderHandler</c> ở đây: handler do
    /// <c>IHttpClientFactory</c> tạo nằm ở scope riêng, không thấy được
    /// <see cref="UserSessionService"/> của circuit ⇒ <c>CurrentUser</c> luôn null
    /// ⇒ header không được gắn ⇒ các endpoint cần danh tính trả về dữ liệu sai
    /// (rất dễ thành hiện tượng "đăng nhập bằng tài khoản khác" vì controller
    /// fallback về customerId trong query string).
    /// </para>
    /// </summary>
    private readonly UserSessionService _userSession;

    public SpaBookingClient(HttpClient httpClient,
        ILogger<SpaBookingClient> logger,
        UserSessionService userSession)
    {
        _httpClient = httpClient;
        _logger = logger;
        _userSession = userSession;
    }

    /// <summary>
    /// Tạo request đã gắn header phiên đăng nhập (X-User-Id / X-Customer-Id / X-User-Role).
    /// </summary>
    private HttpRequestMessage CreateAuthedRequest(HttpMethod method, string uri)
    {
        var request = new HttpRequestMessage(method, uri);
        return SessionHeader.Attach(request, _userSession.CurrentUser);
    }

    public async Task<FrontendApiResult<IReadOnlyList<PetSummaryDto>>> GetPetsAsync(int currentCustomerId, CancellationToken ct = default)
    {
        if (currentCustomerId <= 0)
        {
            return FrontendApiResult<IReadOnlyList<PetSummaryDto>>.Success(Array.Empty<PetSummaryDto>());
        }

        try
        {
            using var request = CreateAuthedRequest(HttpMethod.Get, $"api/spa-booking/pets?customerId={currentCustomerId}");
            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                return FrontendApiResult<IReadOnlyList<PetSummaryDto>>.Failure(
                    await ReadErrorAsync(response, ct),
                    (int)response.StatusCode);
            }

            var data = await response.Content.ReadFromJsonAsync<IReadOnlyList<PetSummaryDto>>(ct);
            return data is null
                ? FrontendApiResult<IReadOnlyList<PetSummaryDto>>.Failure("Spa pets API returned an empty response.")
                : FrontendApiResult<IReadOnlyList<PetSummaryDto>>.Success(data);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LineError(ex, ex.Message);
            return FrontendApiResult<IReadOnlyList<PetSummaryDto>>.Failure(ex.Message);
        }
    }

    public Task<FrontendApiResult<IReadOnlyList<SpaServiceDto>>> GetSpaServicesAsync(CancellationToken ct = default)
    {
        return GetJsonAsync<IReadOnlyList<SpaServiceDto>>("api/spa-booking/services", Array.Empty<SpaServiceDto>(), ct);
    }

    public async Task<FrontendApiResult<SpaAvailabilityResponse>> CheckAvailabilityAsync(
        DateTime start, int durationMin, int quantity, CancellationToken ct = default)
    {
        var payload = new { start, durationMin, quantity };

        try
        {
            using var request = CreateAuthedRequest(HttpMethod.Post, "api/spa-booking/availability");
            request.Content = JsonContent.Create(payload);
            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                return FrontendApiResult<SpaAvailabilityResponse>.Failure(
                    await ReadErrorAsync(response, ct),
                    (int)response.StatusCode);
            }

            var data = await response.Content.ReadFromJsonAsync<SpaAvailabilityResponse>(ct);
            return data is null
                ? FrontendApiResult<SpaAvailabilityResponse>.Failure("Availability API returned an empty response.")
                : FrontendApiResult<SpaAvailabilityResponse>.Success(data);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LineError(ex, ex.Message);
            return FrontendApiResult<SpaAvailabilityResponse>.Failure(ex.Message);
        }
    }

    public async Task<FrontendApiResult<SpaBookingEstimateDto>> EstimateAsync(
        int currentCustomerId, SpaBookingEstimateRequest req, CancellationToken ct = default)
    {
        if (currentCustomerId <= 0)
        {
            return FrontendApiResult<SpaBookingEstimateDto>.Failure("Thiếu thông tin khách hàng.");
        }

        return await PostJsonAsync<SpaBookingEstimateDto>($"api/spa-booking/estimate?customerId={currentCustomerId}", req, ct);
    }

    public async Task<FrontendApiResult<SpaBookingDto>> CreateAsync(
        int currentCustomerId, CreateSpaBookingRequest req, CancellationToken ct = default)
    {
        if (currentCustomerId <= 0)
        {
            return FrontendApiResult<SpaBookingDto>.Failure("Thiếu thông tin khách hàng.");
        }

        return await PostJsonAsync<SpaBookingDto>($"api/spa-booking?customerId={currentCustomerId}", req, ct);
    }

    public Task<FrontendApiResult<IReadOnlyList<SpaBookingDto>>> GetHistoryAsync(int currentCustomerId, CancellationToken ct = default)
    {
        if (currentCustomerId <= 0)
        {
            return Task.FromResult(FrontendApiResult<IReadOnlyList<SpaBookingDto>>.Success(Array.Empty<SpaBookingDto>()));
        }

        return GetJsonAsync<IReadOnlyList<SpaBookingDto>>($"api/spa-booking/history?customerId={currentCustomerId}", Array.Empty<SpaBookingDto>(), ct);
    }

    public async Task<FrontendApiResult<SpaBookingInvoiceDto?>> GetInvoiceAsync(int bookingId, int currentCustomerId, CancellationToken ct = default)
    {
        if (bookingId <= 0)
        {
            return FrontendApiResult<SpaBookingInvoiceDto?>.Failure("Booking id is invalid.");
        }

        try
        {
            using var request = CreateAuthedRequest(HttpMethod.Get, $"api/spa-booking/{bookingId}/invoice");
            using var response = await _httpClient.SendAsync(request, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return FrontendApiResult<SpaBookingInvoiceDto?>.Success(null);
            }

            if (!response.IsSuccessStatusCode)
            {
                return FrontendApiResult<SpaBookingInvoiceDto?>.Failure(
                    await ReadErrorAsync(response, ct),
                    (int)response.StatusCode);
            }

            var data = await response.Content.ReadFromJsonAsync<SpaBookingInvoiceDto?>(ct);
            return data is null
                ? FrontendApiResult<SpaBookingInvoiceDto?>.Failure("Invoice API returned an empty response.")
                : FrontendApiResult<SpaBookingInvoiceDto?>.Success(data);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LineError(ex, ex.Message);
            return FrontendApiResult<SpaBookingInvoiceDto?>.Failure(ex.Message);
        }
    }

    public async Task<FrontendApiResult<object>> UpdateStatusAsync(
        int bookingId, int currentCustomerId, string status, CancellationToken ct = default)
    {
        if (bookingId <= 0)
        {
            return FrontendApiResult<object>.Failure("Booking id is invalid.");
        }

        var payload = new { status };
        return await PatchJsonAsync<object>($"api/spa-booking/{bookingId}/status", payload, ct);
    }

    /// <summary>
    /// Khách hàng tự huỷ lịch spa — gọi endpoint <c>/cancel</c> (không yêu cầu Staff).
    /// </summary>
    public async Task<FrontendApiResult<object>> CancelBookingAsync(
        int bookingId, int currentCustomerId, CancellationToken ct = default)
    {
        if (bookingId <= 0)
        {
            return FrontendApiResult<object>.Failure("Booking id is invalid.");
        }

        var uri = $"api/spa-booking/{bookingId}/cancel?customerId={currentCustomerId}";

        try
        {
            using var httpRequest = CreateAuthedRequest(HttpMethod.Patch, uri);
            using var response = await _httpClient.SendAsync(httpRequest, ct);
            if (!response.IsSuccessStatusCode)
            {
                return FrontendApiResult<object>.Failure(
                    await ReadErrorAsync(response, ct),
                    (int)response.StatusCode);
            }

            if (response.Content.Headers.ContentLength is 0)
            {
                return FrontendApiResult<object>.Success(default!);
            }

            var data = await response.Content.ReadFromJsonAsync<object>(ct);
            return data is null
                ? FrontendApiResult<object>.Failure("API returned an empty response.")
                : FrontendApiResult<object>.Success(data);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LineError(ex, ex.Message);
            return FrontendApiResult<object>.Failure(ex.Message);
        }
    }

    /// <summary>
    /// Tra cứu booking spa theo mã đơn PayOS — khôi phục mã booking khi
    /// đường dẫn redirect về trang hóa đơn bị mất query string.
    /// </summary>
    public async Task<FrontendApiResult<SpaBookingDto?>> GetLatestPayOSBookingAsync(
        int currentCustomerId, CancellationToken ct = default)
    {
        if (currentCustomerId <= 0)
        {
            return FrontendApiResult<SpaBookingDto?>.Failure("Customer id is invalid.");
        }

        try
        {
            using var request = CreateAuthedRequest(
                HttpMethod.Get,
                $"api/spa-booking/latest-payos?customerId={currentCustomerId}");

            using var response = await _httpClient.SendAsync(request, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return FrontendApiResult<SpaBookingDto?>.Success(null);
            }

            if (!response.IsSuccessStatusCode)
            {
                return FrontendApiResult<SpaBookingDto?>.Failure(
                    await ReadErrorAsync(response, ct),
                    (int)response.StatusCode);
            }

            var data = await response.Content.ReadFromJsonAsync<SpaBookingDto?>(ct);
            return data is null
                ? FrontendApiResult<SpaBookingDto?>.Failure("API returned an empty response.")
                : FrontendApiResult<SpaBookingDto?>.Success(data);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LineError(ex, ex.Message);
            return FrontendApiResult<SpaBookingDto?>.Failure(ex.Message);
        }
    }

    public async Task<FrontendApiResult<SpaBookingDto?>> GetByOrderCodeAsync(
        int orderCode, int currentCustomerId, CancellationToken ct = default)
    {
        if (orderCode <= 0)
        {
            return FrontendApiResult<SpaBookingDto?>.Failure("Order code is invalid.");
        }

        try
        {
            using var request = CreateAuthedRequest(
                HttpMethod.Get,
                $"api/spa-booking/by-order/{orderCode}?customerId={currentCustomerId}");

            using var response = await _httpClient.SendAsync(request, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return FrontendApiResult<SpaBookingDto?>.Success(null);
            }

            if (!response.IsSuccessStatusCode)
            {
                return FrontendApiResult<SpaBookingDto?>.Failure(
                    await ReadErrorAsync(response, ct),
                    (int)response.StatusCode);
            }

            var data = await response.Content.ReadFromJsonAsync<SpaBookingDto?>(ct);
            return data is null
                ? FrontendApiResult<SpaBookingDto?>.Failure("API returned an empty response.")
                : FrontendApiResult<SpaBookingDto?>.Success(data);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LineError(ex, ex.Message);
            return FrontendApiResult<SpaBookingDto?>.Failure(ex.Message);
        }
    }

    /// <summary>
    /// Đồng bộ trạng thái thanh toán spa từ PayOS.
    ///
    /// <para>
    /// Server tự tra payment trong DB và hỏi PayOS trực tiếp ⇒ không tin client.
    /// </para>
    /// </summary>
    public async Task<FrontendApiResult<object>> SyncPaymentAsync(
        int bookingId, int currentCustomerId, CancellationToken ct = default)
    {
        if (bookingId <= 0)
        {
            return FrontendApiResult<object>.Failure("Booking id is invalid.");
        }

        try
        {
            using var request = CreateAuthedRequest(
                HttpMethod.Post,
                $"api/spa-booking/{bookingId}/sync-payment?customerId={currentCustomerId}");

            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                return FrontendApiResult<object>.Failure(
                    await ReadErrorAsync(response, ct),
                    (int)response.StatusCode);
            }

            var data = await response.Content.ReadFromJsonAsync<object>(ct);
            return data is null
                ? FrontendApiResult<object>.Failure("Sync payment API returned an empty response.")
                : FrontendApiResult<object>.Success(data);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LineError(ex, ex.Message);
            return FrontendApiResult<object>.Failure(ex.Message);
        }
    }

    public async Task<FrontendApiResult<object>> SubmitReviewAsync(
        int currentCustomerId, CreateSpaReviewRequest req, CancellationToken ct = default)
    {
        if (currentCustomerId <= 0)
        {
            return FrontendApiResult<object>.Failure("Thiếu thông tin khách hàng.");
        }

        return await PostJsonAsync<object>($"api/spa-booking/review?customerId={currentCustomerId}", req, ct);
    }

    public async Task<FrontendApiResult<PayOSCheckoutResult>> CreateSpaPayOSPaymentAsync(
        int bookingId, IReadOnlyList<int>? bookingIds, string returnUrl, string cancelUrl, CancellationToken ct = default)
    {
        var payload = new
        {
            bookingId,
            bookingIds,
            returnUrl,
            cancelUrl
        };

        try
        {
            using var request = CreateAuthedRequest(HttpMethod.Post, "api/payos/spa/create");
            request.Content = JsonContent.Create(payload);
            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                return FrontendApiResult<PayOSCheckoutResult>.Failure(
                    await ReadErrorAsync(response, ct),
                    (int)response.StatusCode);
            }

            var data = await response.Content.ReadFromJsonAsync<PayOSCheckoutResult>(ct);
            return data is null
                ? FrontendApiResult<PayOSCheckoutResult>.Failure("PayOS spa create returned an empty response.")
                : FrontendApiResult<PayOSCheckoutResult>.Success(data);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LineError(ex, ex.Message);
            return FrontendApiResult<PayOSCheckoutResult>.Failure(ex.Message);
        }
    }

    public async Task<FrontendApiResult<PayOSPaymentStatusResult>> GetPayOSStatusAsync(int orderCode, CancellationToken ct = default)
    {
        try
        {
            using var request = CreateAuthedRequest(HttpMethod.Get, $"api/payos/status/{orderCode}");
            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                return FrontendApiResult<PayOSPaymentStatusResult>.Failure(
                    await ReadErrorAsync(response, ct),
                    (int)response.StatusCode);
            }

            var data = await response.Content.ReadFromJsonAsync<PayOSPaymentStatusResult>(ct);
            return data is null
                ? FrontendApiResult<PayOSPaymentStatusResult>.Failure("PayOS status returned an empty response.")
                : FrontendApiResult<PayOSPaymentStatusResult>.Success(data);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LineError(ex, ex.Message);
            return FrontendApiResult<PayOSPaymentStatusResult>.Failure(ex.Message);
        }
    }

    private async Task<FrontendApiResult<T>> GetJsonAsync<T>(string uri, T emptyValue, CancellationToken cancellationToken)
    {
        try
        {
            using var request = CreateAuthedRequest(HttpMethod.Get, uri);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return FrontendApiResult<T>.Failure(await ReadErrorAsync(response, cancellationToken), (int)response.StatusCode);
            }

            var data = await response.Content.ReadFromJsonAsync<T>(cancellationToken);
            return data is null
                ? FrontendApiResult<T>.Failure("API returned an empty response.")
                : FrontendApiResult<T>.Success(data);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LineError(ex, ex.Message);
            return FrontendApiResult<T>.Failure(ex.Message);
        }
    }

    private async Task<FrontendApiResult<T>> PostJsonAsync<T>(string uri, object request, CancellationToken cancellationToken)
    {
        try
        {
            using var httpRequest = CreateAuthedRequest(HttpMethod.Post, uri);
            httpRequest.Content = JsonContent.Create(request);
            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return FrontendApiResult<T>.Failure(await ReadErrorAsync(response, cancellationToken), (int)response.StatusCode);
            }

            var data = await response.Content.ReadFromJsonAsync<T>(cancellationToken);
            return data is null
                ? FrontendApiResult<T>.Failure("API returned an empty response.")
                : FrontendApiResult<T>.Success(data);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LineError(ex, ex.Message);
            return FrontendApiResult<T>.Failure(ex.Message);
        }
    }

    private async Task<FrontendApiResult<T>> PatchJsonAsync<T>(string uri, object request, CancellationToken cancellationToken)
    {
        try
        {
            using var httpRequest = CreateAuthedRequest(HttpMethod.Patch, uri);
            httpRequest.Content = JsonContent.Create(request);
            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return FrontendApiResult<T>.Failure(await ReadErrorAsync(response, cancellationToken), (int)response.StatusCode);
            }

            if (response.Content.Headers.ContentLength is 0)
            {
                return FrontendApiResult<T>.Success(default!);
            }

            var data = await response.Content.ReadFromJsonAsync<T>(cancellationToken);
            return data is null
                ? FrontendApiResult<T>.Failure("API returned an empty response.")
                : FrontendApiResult<T>.Success(data);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LineError(ex, ex.Message);
            return FrontendApiResult<T>.Failure(ex.Message);
        }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(body)
            ? $"API returned {(int)response.StatusCode} {response.ReasonPhrase}."
            : body;
    }
}
