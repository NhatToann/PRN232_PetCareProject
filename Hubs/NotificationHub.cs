using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PetShop.Data;
using PetShop.Models;
using PetShop.Services;

namespace PetShop.Hubs;

/// <summary>
/// Hub SignalR cho thông báo realtime + chat 1-1 (LS-11).
///
/// Được đăng ký tại <c>/hubs/notification</c> (xem <c>Program.cs</c>).
///
/// <para><b>Cơ chế nhận diện người dùng (3 tầng, theo thứ tự ưu tiên):</b></para>
/// <list type="number">
///   <item>
///     <description>
///     <c>Authorization: Bearer &lt;JWT&gt;</c> hoặc header session nội bộ
///     (<c>X-User-Id</c>) ⇒ đọc claim <see cref="ClaimTypes.NameIdentifier"/>.
///     Đây là danh tính <b>thật</b> — dùng để ghi lịch sử chat.
///     </description>
///   </item>
///   <item>
///     <description>
///     Query string <c>?userId=...</c> ⇒ danh tính <b>phi xác thực</b> (LOCAL DEMO).
///     Chỉ dùng để vào group nhận thông báo; <b>không</b> ghi vào bảng
///     <c>ChatMessages</c> để không ghi nhận tin nhắn của người chưa xác thực.
///     Cùng mức độ "không phải cơ chế bảo mật production" với
///     <c>SessionHeaderAuthenticationHandler</c> (xem AGENTS.md §0).
///     </description>
///   </item>
///   <item>
///     <description>
///     Không có gì ⇒ <see cref="HubException"/>, không cho tham gia group nào.
///     </description>
///   </item>
/// </list>
///
/// <para>
/// Nhóm (group) theo user: <c>"user:{userId}"</c>. Server push qua
/// <c>IHubContext&lt;NotificationHub&gt;</c> — xem
/// <c>Services/RealtimeNotificationService.cs</c>.
/// </para>
/// </summary>
public sealed class NotificationHub : Hub
{
    /// <summary>Prefix tên group theo user — dùng chung với service phía server.</summary>
    public const string UserGroupPrefix = "user:";

    /// <summary>Tên event mà client nhận thông báo mới.</summary>
    public const string NotificationReceivedEvent = "NotificationReceived";

    /// <summary>Tên event mà client nhận tin nhắn chat.</summary>
    public const string ChatReceivedEvent = "ChatReceived";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationHub> _logger;

    public NotificationHub(IServiceScopeFactory scopeFactory, ILogger<NotificationHub> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Tên group của một user — dùng để join/leave và để service push tới đúng người.
    /// </summary>
    public static string UserGroup(int userId) => $"{UserGroupPrefix}{userId}";

    /// <summary>
    /// Vào group nhận thông báo của <paramref name="userId"/>.
    ///
    /// <para>
    /// Người dùng chỉ được vào group của chính mình: nếu request đã xác thực
    /// (Bearer/session header) thì <paramref name="userId"/> phải khớp claim
    /// <see cref="ClaimTypes.NameIdentifier"/>, nếu không sẽ ném
    /// <see cref="HubException"/>. Ở chế độ query string (chưa xác thực) thì
    /// dùng luôn <paramref name="userId"/> do client truyền — xem ghi chú ở
    /// phần tổng quan về chế độ LOCAL DEMO.
    /// </para>
    /// </summary>
    public async Task JoinGroup(int userId)
    {
        if (userId <= 0)
        {
            throw new HubException("userId không hợp lệ.");
        }

        var identity = ResolveIdentity(userId);

        if (identity.IsAuthenticated && identity.UserId != userId)
        {
            throw new HubException("Không được tham gia group của người khác.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));

        _logger.Line($"[NotificationHub] JoinGroup: connection={Context.ConnectionId}, userId={userId}, authenticated={identity.IsAuthenticated}");
    }

    /// <summary>
    /// Rời group của <paramref name="userId"/>.
    /// </summary>
    public async Task LeaveGroup(int userId)
    {
        if (userId <= 0)
        {
            throw new HubException("userId không hợp lệ.");
        }

        var identity = ResolveIdentity(userId);

        if (identity.IsAuthenticated && identity.UserId != userId)
        {
            throw new HubException("Không được thao tác group của người khác.");
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, UserGroup(userId));

        _logger.Line($"[NotificationHub] LeaveGroup: connection={Context.ConnectionId}, userId={userId}");
    }

    /// <summary>
    /// Gửi tin nhắn chat 1-1 tới <paramref name="userId"/>.
    ///
    /// <para>
    /// Tin nhắn được gửi tới group <c>user:{userId}</c> và echo lại cho người gọi
    /// qua <c>Clients.Caller</c> (event <see cref="ChatReceivedEvent"/>) để người gửi
    /// cũng thấy tin nhắn của mình.
    /// </para>
    ///
    /// <para>
    /// <b>Chống giả mạo:</b> người gửi được lấy từ claim
    /// <see cref="ClaimTypes.NameIdentifier"/> (hoặc query string ở chế độ demo) —
    /// <b>không</b> tin <paramref name="userId"/> do client gửi cho việc ghi log.
    /// </para>
    /// </summary>
    public async Task SendMessage(int userId, string message)
    {
        if (userId <= 0)
        {
            throw new HubException("userId không hợp lệ.");
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new HubException("Nội dung tin nhắn không được để trống.");
        }

        var identity = ResolveIdentity(userId);

        if (!identity.IsAuthenticated)
        {
            // LOCAL DEMO: chỉ cho "nhận" tin nhắn, không ghi log chat để tránh
            // lẫn dữ liệu của người chưa xác thực vào bảng ChatMessages.
            await Clients.Caller.SendAsync(ChatReceivedEvent, new ChatEnvelope(
                userId,
                identity.UserId,
                message,
                DateTime.Now,
                false));

            _logger.LineWarning($"[NotificationHub] SendMessage bị từ chối ghi log: connection={Context.ConnectionId} chưa xác thực.");

            return;
        }

        var senderId = identity.UserId;

        // Ghi lịch sử vào bảng ChatMessages (bảng đã có sẵn trong schema).
        await SaveChatMessageAsync(senderId, userId, message);

        await Clients.Group(UserGroup(userId)).SendAsync(ChatReceivedEvent, new ChatEnvelope(
            userId,
            senderId,
            message,
            DateTime.Now,
            true));

        // Echo lại cho người gửi (nếu người gửi cũng đang ở group của chính mình).
        await Clients.Caller.SendAsync(ChatReceivedEvent, new ChatEnvelope(
            senderId,
            senderId,
            message,
            DateTime.Now,
            true));

        _logger.Line($"[NotificationHub] SendMessage: from={senderId}, to={userId}, authenticated={identity.IsAuthenticated}");
    }

    /// <summary>
    /// Được gọi khi client kết nối. Không chặn kết nối: nếu chưa xác thực thì client
    /// tự gọi <see cref="JoinGroup"/> sau khi negotiate.
    /// </summary>
    public override Task OnConnectedAsync()
    {
        var identity = ResolveIdentity(0);

        _logger.Line($"[NotificationHub] OnConnected: connection={Context.ConnectionId}, authenticated={identity.IsAuthenticated}, userId={identity.UserId}");

        return base.OnConnectedAsync();
    }

    /// <summary>
    /// Ghi một tin nhắn chat vào bảng <c>ChatMessages</c>.
    ///
    /// <para>
    /// Bảng có cột <c>CustomerID</c> (bắt buộc, FK sang <c>Customer</c>) và
    /// <c>StaffID</c> (nullable, FK sang <c>Admin</c>). Vì đây là chat giữa các
    /// nhân viên, ta ghi <c>CustomerId</c> = id người nhận và <c>StaffId</c> =
    /// id người gửi (nếu id người gửi có tồn tại trong bảng <c>Staff</c>).
    /// </para>
    /// </summary>
    private async Task SaveChatMessageAsync(int senderId, int receiverId, string message)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ShopPetDatabaseContext>();

            var receiverExists = await db.Customers
                .AsNoTracking()
                .AnyAsync(c => c.CustomerId == receiverId);

            if (!receiverExists)
            {
                _logger.LineWarning($"[NotificationHub] Bỏ qua ghi log chat: Customer #{receiverId} không tồn tại.");
                return;
            }

            var senderIsStaff = await db.Staff
                .AsNoTracking()
                .AnyAsync(s => s.StaffId == senderId);

            var entity = new ChatMessage
            {
                CustomerId = receiverId,
                StaffId = senderIsStaff ? senderId : null,
                SenderType = senderIsStaff ? "staff" : "customer",
                Message = message,
                SentAt = DateTime.Now,
                IsRead = false,
            };

            db.ChatMessages.Add(entity);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Lỗi ghi log chat KHÔNG được làm hỏng luồng gửi tin nhắn.
            _logger.LineError(ex, $"[NotificationHub] Không ghi được lịch sử chat (from={senderId}, to={receiverId}): {ex.Message}");
        }
    }

    /// <summary>
    /// Xác định danh tính của connection hiện tại.
    /// </summary>
    private HubIdentity ResolveIdentity(int requestedUserId)
    {
        var principal = Context.User;

        if (principal?.Identity?.IsAuthenticated == true)
        {
            var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(raw, out var userId) && userId > 0)
            {
                return new HubIdentity(userId, true);
            }
        }

        // LOCAL DEMO — chưa xác thực: đọc userId từ query string của lúc negotiate.
        var query = Context.GetHttpContext()?.Request.Query;
        if (query is not null)
        {
            var rawQuery = query["userId"].ToString();
            if (int.TryParse(rawQuery, out var queryUserId) && queryUserId > 0)
            {
                return new HubIdentity(queryUserId, false);
            }
        }

        return new HubIdentity(requestedUserId, false);
    }

    /// <summary>
    /// Kết quả resolve danh tính của một connection.
    /// </summary>
    /// <param name="UserId">Id người dùng suy ra được.</param>
    /// <param name="IsAuthenticated"><c>true</c> nếu danh tính đến từ claim hợp lệ.</param>
    private readonly record struct HubIdentity(int UserId, bool IsAuthenticated);
}
