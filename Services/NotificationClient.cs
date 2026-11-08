using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Services;

/// <summary>
/// Client HTTP gọi chính API thông báo của ứng dụng.
/// Port nguyên văn từ <c>Projectojt/frontend/src/api/notificationApi.js</c> (nguồn OJT).
/// </summary>
public sealed class NotificationClient : INotificationClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<NotificationClient> _logger;

    public NotificationClient(HttpClient httpClient, ILogger<NotificationClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public Task<FrontendApiResult<IReadOnlyList<NotificationItemDto>>> GetNotificationsAsync(
        int staffId,
        bool unreadOnly = false,
        CancellationToken cancellationToken = default)
    {
        var uri = unreadOnly ? $"api/notifications/{staffId}?unreadOnly=true" : $"api/notifications/{staffId}";
        return GetJsonAsync<IReadOnlyList<NotificationItemDto>>(uri, Array.Empty<NotificationItemDto>(), cancellationToken);
    }

    public Task<FrontendApiResult<UnreadCountResultDto>> GetUnreadCountAsync(
        int staffId,
        CancellationToken cancellationToken = default) =>
        GetJsonAsync<UnreadCountResultDto>($"api/notifications/{staffId}/unread-count", new UnreadCountResultDto(0), cancellationToken);

    public Task<FrontendApiResult<bool>> MarkAsReadAsync(
        int requestId,
        int staffId,
        CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"api/notifications/{requestId}/read?staffId={staffId}", cancellationToken);

    public Task<FrontendApiResult<bool>> MarkAllAsReadAsync(
        int staffId,
        CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, $"api/notifications/{staffId}/read-all", cancellationToken);

    public Task<FrontendApiResult<IReadOnlyList<PendingRequestDto>>> GetPendingRequestsAsync(
        int staffId,
        CancellationToken cancellationToken = default) =>
        GetJsonAsync<IReadOnlyList<PendingRequestDto>>(
            $"api/shift-exchange/pending?staffId={staffId}",
            Array.Empty<PendingRequestDto>(),
            cancellationToken);

    public Task<FrontendApiResult<ShiftExchangeActionResultDto>> AcceptRequestAsync(
        int requestId,
        int responderStaffId,
        CancellationToken cancellationToken = default) =>
        SendJsonAsync<ShiftExchangeActionResultDto>(
            HttpMethod.Post,
            $"api/shift-exchange/accept/{requestId}?responderStaffId={responderStaffId}",
            cancellationToken);

    public Task<FrontendApiResult<ShiftExchangeActionResultDto>> DeclineRequestAsync(
        int requestId,
        int responderStaffId,
        CancellationToken cancellationToken = default) =>
        SendJsonAsync<ShiftExchangeActionResultDto>(
            HttpMethod.Post,
            $"api/shift-exchange/decline/{requestId}?responderStaffId={responderStaffId}",
            cancellationToken);

    public Task<FrontendApiResult<IReadOnlyList<MyRequestDto>>> GetMyRequestsAsync(
        int staffId,
        CancellationToken cancellationToken = default) =>
        GetJsonAsync<IReadOnlyList<MyRequestDto>>(
            $"api/shift-exchange/my-requests?staffId={staffId}",
            Array.Empty<MyRequestDto>(),
            cancellationToken);

    private async Task<FrontendApiResult<T>> GetJsonAsync<T>(
        string uri,
        T emptyValue,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(uri, cancellationToken);
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
            _logger.LineError(ex, ex.Message);
            return FrontendApiResult<T>.Failure(ex.Message);
        }
    }

    private async Task<FrontendApiResult<bool>> SendAsync(
        HttpMethod method,
        string uri,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, uri);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return FrontendApiResult<bool>.Failure(
                    await response.Content.ReadAsStringAsync(cancellationToken),
                    (int)response.StatusCode);
            }

            return FrontendApiResult<bool>.Success(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LineError(ex, ex.Message);
            return FrontendApiResult<bool>.Failure(ex.Message);
        }
    }

    private async Task<FrontendApiResult<T>> SendJsonAsync<T>(
        HttpMethod method,
        string uri,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, uri);
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
            _logger.LineError(ex, ex.Message);
            return FrontendApiResult<T>.Failure(ex.Message);
        }
    }
}
