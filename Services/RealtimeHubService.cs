using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using PetShop.Hubs;
using PetShop.Models;

namespace PetShop.Services;

/// <summary>
/// Quản lý MỘT kết nối SignalR dùng chung cho mọi component realtime trong
/// một circuit Blazor (chuông thông báo + widget chat).
///
/// <para>
/// <b>Vì sao cần (tối ưu RAM + độ trễ):</b>
/// Trước đây <c>NotificationBell</c> và <c>ChatWidget</c> MỖI component tự tạo
/// một <see cref="HubConnection"/> riêng. Một circuit Blazor vậy mở 2 WebSocket,
/// mỗi cái có buffer riêng + bộ đệm message + timer reconnect ⇒ tiêu tốn RAM và
/// tạo 2 vòng handshake WebSocket mỗi lần mở trang (nguồn gây "lag" rõ rệt).
/// </para>
///
/// <para>
/// Nay service này (SCOPED theo circuit) tạo đúng 1 connection và chia sẻ cho
/// mọi component ⇒:
/// </para>
/// <list type="bullet">
/// <item>1 WebSocket thay vì 2 ⇒ giảm ~50% bộ nhớ realtime của mỗi circuit.</item>
/// <item>1 lần handshake thay vì 2 ⇒ mở trang nhanh hơn.</item>
/// <item>Component không cần tự quản lý vòng đời connection nữa.</item>
/// </list>
///
/// <para>
/// <b>Quy tắc:</b> service là <c>Scoped</c> (mỗi circuit 1 instance) vì
/// <c>userId</c> lấy từ session của circuit. Các subscriber nhận event qua
/// event C# thay vì mỗi component tự đăng ký handler SignalR riêng.
/// </para>
/// </summary>
public sealed class RealtimeHubService : IAsyncDisposable
{
    /// <summary>Đường dẫn hub — phải khớp với <c>app.MapHub&lt;NotificationHub&gt;("/hubs/notification")</c>.</summary>
    public const string HubPath = "/hubs/notification";

    private readonly ILogger<RealtimeHubService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private HubConnection? _connection;
    private int _joinedUserId;

    /// <summary>Sự kiện nhận thông báo mới (từ server push).</summary>
    public event Action<RealtimeNotificationDto>? NotificationReceived;

    /// <summary>Sự kiện nhận tin nhắn chat mới (từ server push).</summary>
    public event Action<ChatEnvelope>? ChatReceived;

    /// <summary>Trạng thái realtime hiện tại (để hiển thị trên UI).</summary>
    public bool IsConnected => _connection is not null && _connection.State == HubConnectionState.Connected;

    public RealtimeHubService(ILogger<RealtimeHubService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Đảm bảo đã có kết nối SignalR cho userId và đã join group <c>user:{userId}</c>.
    ///
    /// <para>
    /// Gọi nhiều lần vẫn chỉ tạo MỘT connection (idempotent).
    /// </para>
    /// </summary>
    /// <param name="hubUrl">URL đầy đủ của hub (do component dựng từ <c>Navigation.BaseUri</c>).</param>
    /// <param name="userId">Id người dùng để join group.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task EnsureConnectedAsync(string hubUrl, int userId, CancellationToken ct = default)
    {
        if (userId <= 0 || string.IsNullOrWhiteSpace(hubUrl))
        {
            return;
        }

        // ⚠️ SỬA BUG (11/11/2026) — circuit có thể dispose service khi đang chạy.
        //
        // LOG THẬT:
        //   [NotificationBell] Không kết nối được SignalR (fallback sang HTTP):
        //   ObjectDisposedException — Cannot access a disposed object.
        //   Object name: 'System.Threading.SemaphoreSlim'.
        //
        // VÌ SAO:
        //   `RealtimeHubService` là SCOPED theo circuit Blazor. Khi circuit kết
        //   thúc, DI gọi DisposeAsync() ⇒ `_gate.Dispose()`.
        //   Nhưng NotificationBell/ChatWidget có thể gọi EnsureConnectedAsync
        //   (qua HandleSessionChanged) NGAY LÚC circuit đang kết thúc ⇒
        //   `_gate.WaitAsync()` ném ObjectDisposedException ⇒ ném ra ngoài ⇒
        //   component crash ⇒ circuit chết.
        //
        // ⇒ Phải bắt ObjectDisposedException và coi như "không kết nối được".
        try
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Service đã bị dispose cùng circuit ⇒ không còn gì để làm.
            _logger.LineWarning("[RealtimeHubService] Service đã dispose (circuit kết thúc) — bỏ qua kết nối SignalR.");
            return;
        }

        try
        {
            // Đã có connection đúng user ⇒ KHÔNG tạo connection mới.
            //
            // ⚠️ SỬA BUG (10/11/2026) — chống churn kết nối SignalR.
            // Guard cũ chỉ chấp nhận trạng thái `Connected`. Khi mạng chập chờn,
            // connection rơi vào trạng thái `Reconnecting`/`Connecting` ⇒ guard
            // cũ cho qua ⇒ **tạo connection MỚI** và huỷ connection cũ ⇒ mỗi lần
            // render lại lặp lại ⇒ log server ghi hàng trăm dòng
            // "[NotificationHub] OnConnected" mà không có "JoinGroup" tương ứng
            // ⇒ rò rỉ WebSocket, tốn RAM, và làm circuit dễ bị đóng.
            //
            // Nay: chỉ tạo lại connection khi trạng thái thực sự là `Disconnected`.
            if (_connection is not null
                && _joinedUserId == userId
                && _connection.State != HubConnectionState.Disconnected)
            {
                return;
            }

            // Đổi user (đăng nhập/đăng xuất) ⇒ huỷ connection cũ.
            if (_connection is not null)
            {
                await DisposeConnectionAsync();
            }

            // ⚠️ SỬA BUG (11/11/2026) — chống churn kết nối SignalR (lần 2).
            //
            // Guard phía trên CHƯA đủ: `_connection` chỉ được gán SAU khi
            // `StartAsync` + `InvokeAsync("JoinGroup")` đã xong. Trong khoảng
            // thời gian đó, `NotificationBell` VÀ `ChatWidget` (cùng nằm trong
            // `StaffWorkspaceLayout`) đều gọi `EnsureConnectedAsync` ⇒ mỗi
            // lần đều thấy `_connection == null` ⇒ mỗi lần tạo MỘT connection
            // mới ⇒ server log ra nhiều dòng "[NotificationHub] OnConnected"
            // mà không có "JoinGroup" tương ứng ⇒ rò rỉ WebSocket.
            //
            // Nay: gán `_connection` + `_joinedUserId` NGAY khi tạo connection
            // (trước `StartAsync`) ⇒ lần gọi thứ hai thấy state = `Connecting`
            // (không phải `Disconnected`) ⇒ guard trả về ⇒ chỉ 1 connection.
            var connection = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .WithAutomaticReconnect(new[]
                {
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(10),
                })
                .Build();

            _connection = connection;
            _joinedUserId = userId;

            connection.On<RealtimeNotificationDto>(
                NotificationHub.NotificationReceivedEvent,
                notification => NotificationReceived?.Invoke(notification));

            connection.On<ChatEnvelope>(
                NotificationHub.ChatReceivedEvent,
                chat => ChatReceived?.Invoke(chat));

            connection.Closed += _ =>
            {
                _logger.LineWarning("[RealtimeHubService] Kết nối SignalR đã đóng — sẽ tự kết nối lại ở lần render sau.");
                return Task.CompletedTask;
            };

            await connection.StartAsync(ct).ConfigureAwait(false);
            await connection.InvokeAsync("JoinGroup", userId, ct).ConfigureAwait(false);

            _logger.Line($"[RealtimeHubService] Đã kết nối SignalR và join group user:{userId} (dùng chung cho chuông + chat).");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ObjectDisposedException)
        {
            // Circuit kết thúc giữa chừng ⇒ coi như không kết nối được, KHÔNG ném ra ngoài.
            _logger.LineWarning("[RealtimeHubService] SignalR bị dispose khi circuit kết thúc — bỏ qua.");
        }
        catch (Exception ex)
        {
            _logger.LineWarning($"[RealtimeHubService] Không kết nối được SignalR (fallback sang HTTP): {ex.GetType().Name} — {ex.Message}");
            await DisposeConnectionAsync();
        }
        finally
        {
            try
            {
                _gate.Release();
            }
            catch (ObjectDisposedException)
            {
                // Service bị dispose trong lúc đang chờ ⇒ bỏ qua.
            }
        }
    }

    /// <summary>
    /// Ngắt kết nối (khi user đăng xuất hoặc component cuối cùng bị dispose).
    /// </summary>
    public async Task DisconnectAsync()
    {
        try
        {
            await _gate.WaitAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Đã dispose rồi ⇒ không cần làm gì.
            return;
        }

        try
        {
            await DisposeConnectionAsync();
        }
        finally
        {
            try
            {
                _gate.Release();
            }
            catch (ObjectDisposedException)
            {
                // Bỏ qua.
            }
        }
    }

    private async Task DisposeConnectionAsync()
    {
        var connection = _connection;
        _connection = null;
        _joinedUserId = 0;

        if (connection is null)
        {
            return;
        }

        try
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LineWarning($"[RealtimeHubService] Lỗi khi huỷ kết nối SignalR: {ex.GetType().Name}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        try
        {
            _gate.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Đã dispose rồi.
        }
    }
}
