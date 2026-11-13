using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using PetShop.Hubs;
using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Services;

/// <summary>
/// Đẩy thông báo / tin nhắn chat realtime tới client SignalR.
///
/// <para>
/// Dùng <see cref="IHubContext{THub}"/> nên có thể gọi từ bất kỳ tầng nào
/// (controller, service) — không cần giữ tham chiếu tới hub instance.
/// </para>
///
/// <para>
/// <b>Quy tắc quan trọng:</b> mọi lỗi đẩy dữ liệu đều bị nuốt kèm log
/// <c>Warning</c>. Lỗi realtime không được làm hỏng luồng nghiệp vụ chính
/// (ví dụ <c>POST /api/shift-exchange/pass</c> phải trả 200 dù hub không kết nối được).
/// </para>
/// </summary>
public sealed class RealtimeNotificationService : IRealtimeNotificationService
{
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly ILogger<RealtimeNotificationService> _logger;

    public RealtimeNotificationService(
        IHubContext<NotificationHub> hubContext,
        ILogger<RealtimeNotificationService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task NotifyAsync(
        int userId,
        string title,
        string message,
        int? relatedRequestId = null,
        CancellationToken cancellationToken = default)
    {
        if (userId <= 0)
        {
            _logger.LineWarning($"[Realtime] Bỏ qua notify vì userId không hợp lệ: {userId}");
            return;
        }

        try
        {
            var payload = new RealtimeNotificationDto(
                userId,
                title ?? string.Empty,
                message ?? string.Empty,
                relatedRequestId,
                DateTime.Now);

            await _hubContext.Clients
                .Group(NotificationHub.UserGroup(userId))
                .SendAsync(
                    NotificationHub.NotificationReceivedEvent,
                    payload,
                    cancellationToken);

            _logger.Line($"[Realtime] Đã push thông báo tới user:{userId} — {title}");
        }
        catch (OperationCanceledException)
        {
            // Huỷ có chủ đích (vd request client bị huỷ) — không log như lỗi.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LineWarning($"[Realtime] Không push được thông báo tới user:{userId}: {ex.GetType().Name} — {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task SendChatAsync(
        int toUserId,
        int fromUserId,
        string message,
        CancellationToken cancellationToken = default,
        int messageId = 0,
        string fromKind = "",
        string toKind = "",
        int toCustomerId = 0,
        string senderName = "",
        string senderRole = "")
    {
        if (toUserId <= 0)
        {
            _logger.LineWarning($"[Realtime] Bỏ qua chat vì toUserId không hợp lệ: {toUserId}");
            return;
        }

        try
        {
            var payload = new ChatEnvelope(
                toUserId,
                fromUserId,
                message ?? string.Empty,
                DateTime.Now,
                true,
                messageId,
                fromKind,
                toKind,
                toCustomerId,
                senderName,
                senderRole);

            await _hubContext.Clients
                .Group(NotificationHub.UserGroup(toUserId))
                .SendAsync(
                    NotificationHub.ChatReceivedEvent,
                    payload,
                    cancellationToken);

            // ⚠️ HỘP THƯ CHUNG (13/11/2026): đẩy thêm tới group NHÂN VIÊN
            // ⇒ mọi nhân viên đều nhận tin của khách ngay, không chỉ người
            // được chọn (nay đã bỏ khái niệm "người được chọn").
            //
            // Client nhân viên lọc theo `ToCustomerId` để chỉ hiện tin thuộc hộp
            // thư đang mở; ai đang ở trang khác thì chỉ tăng badge.
            await _hubContext.Clients
                .Group(NotificationHub.StaffGroup)
                .SendAsync(
                    NotificationHub.ChatReceivedEvent,
                    payload,
                    cancellationToken);

            _logger.Line($"[Realtime] Đã push chat tới user:{toUserId} + nhóm nhân viên từ user:{fromUserId}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LineWarning($"[Realtime] Không push được chat tới user:{toUserId}: {ex.GetType().Name} — {ex.Message}");
        }
    }
}
