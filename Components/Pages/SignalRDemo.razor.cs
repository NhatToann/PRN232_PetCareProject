using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using PetShop.Models;

namespace PetShop_PRN222.Components.Pages;

/// <summary>
/// Phần code-behind của <c>SignalRDemo.razor</c> (tách riêng theo quy ước AGENTS.md §2.3).
///
/// Trang demo cho hub SignalR <c>/hubs/notification</c> — dùng để kiểm chứng:
///   - Kết nối hub và vào group <c>user:{id}</c>.
///   - Nhận event <c>NotificationReceived</c> (server push qua IHubContext).
///   - Nhận event <c>ChatReceived</c> (chat 1-1 qua <c>SendMessage</c>).
///
/// <para>
/// Trang KHÔNG được thêm vào menu điều hướng (để không thay đổi UI gốc) —
/// truy cập trực tiếp bằng đường dẫn <c>/signalr-demo</c>.
/// </para>
/// </summary>
public partial class SignalRDemo : ComponentBase, IAsyncDisposable
{
    /// <summary>Đường dẫn hub — phải khớp với <c>app.MapHub&lt;NotificationHub&gt;("/hubs/notification")</c>.</summary>
    private const string HubPath = "/hubs/notification";

    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private HubConnection? _hubConnection;
    private readonly List<LogEntry> eventLog = new();

    private int currentUserId = 1;
    private int targetUserId = 1;
    private string message = "Xin chào từ trang demo SignalR!";
    private string notificationTitle = "Thông báo thử nghiệm";
    private bool isBusy;
    private string? errorMessage;

    /// <summary>Nhãn trạng thái kết nối hub.</summary>
    private string StatusText => _hubConnection?.State switch
    {
        HubConnectionState.Connected => "Đã kết nối",
        HubConnectionState.Connecting => "Đang kết nối…",
        HubConnectionState.Disconnected => "Chưa kết nối",
        _ => "Chưa kết nối",
    };

    /// <summary>Class Bootstrap cho badge trạng thái.</summary>
    private string StatusBadgeClass => _hubConnection?.State switch
    {
        HubConnectionState.Connected => "text-bg-success",
        HubConnectionState.Connecting => "text-bg-warning",
        _ => "text-bg-secondary",
    };

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        await ConnectAsync();
    }

    /// <summary>Kết nối hub và vào group của chính mình.</summary>
    private async Task ConnectAsync()
    {
        try
        {
            var separator = Navigation.BaseUri.Contains('?', StringComparison.Ordinal) ? '&' : '?';
            var url = $"{Navigation.BaseUri.TrimEnd('/')}{HubPath}{separator}userId={currentUserId}";

            var connection = new HubConnectionBuilder()
                .WithUrl(url)
                .WithAutomaticReconnect()
                .Build();

            connection.On<RealtimeNotificationDto>(
                "NotificationReceived",
                notification =>
                {
                    AddLog("NotificationReceived",
                        $"{notification.Title} — {notification.Message} (user:{notification.UserId}, request #{notification.RelatedRequestId})");
                });

            connection.On<ChatEnvelope>(
                "ChatReceived",
                chat =>
                {
                    AddLog("ChatReceived",
                        $"Từ user:{chat.FromUserId} → user:{chat.UserId}: {chat.Message}{(chat.IsPersisted ? string.Empty : " (chưa ghi log)")}");
                });

            await connection.StartAsync();
            await connection.InvokeAsync("JoinGroup", currentUserId);

            _hubConnection = connection;

            AddLog("Connected", $"Đã vào group user:{currentUserId} tại {url}");

            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Kết nối SignalR thất bại tại trang /signalr-demo.");
            errorMessage = ex.Message;
            await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>Gửi tin nhắn chat 1-1 tới <c>targetUserId</c>.</summary>
    private async Task SendAsync()
    {
        if (_hubConnection is null || _hubConnection.State != HubConnectionState.Connected)
        {
            errorMessage = "Chưa kết nối tới hub.";
            return;
        }

        isBusy = true;
        errorMessage = null;

        try
        {
            await _hubConnection.InvokeAsync("SendMessage", targetUserId, message);
            AddLog("SendMessage", $"→ user:{targetUserId}: {message}");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Gửi tin nhắn SignalR thất bại.");
            errorMessage = ex.Message;
        }
        finally
        {
            isBusy = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>Gửi một thông báo test tới chính mình (server push qua IHubContext).</summary>
    private async Task NotifySelfAsync()
    {
        isBusy = true;
        errorMessage = null;

        try
        {
            await Realtime.NotifyAsync(
                currentUserId,
                notificationTitle,
                "Thông báo thử nghiệm từ trang /signalr-demo.",
                null,
                CancellationToken.None);

            AddLog("NotifyAsync", $"Đã yêu cầu push thông báo tới user:{currentUserId}");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Gửi thông báo realtime thất bại.");
            errorMessage = ex.Message;
        }
        finally
        {
            isBusy = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>Ghi một dòng vào nhật ký sự kiện.</summary>
    private void AddLog(string eventName, string detail)
    {
        eventLog.Insert(0, new LogEntry(DateTime.Now, eventName, detail));

        // Giới hạn số dòng log để trang không phình vô hạn.
        if (eventLog.Count > 100)
        {
            eventLog.RemoveAt(eventLog.Count - 1);
        }

        InvokeAsync(StateHasChanged);
    }

    /// <summary>Xoá nhật ký sự kiện.</summary>
    private void ClearLog()
    {
        eventLog.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        var connection = _hubConnection;
        _hubConnection = null;

        if (connection is not null)
        {
            try
            {
                await connection.DisposeAsync();
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Lỗi khi huỷ kết nối SignalR tại trang /signalr-demo.");
            }
        }
    }

    /// <summary>Một dòng trong nhật ký sự kiện.</summary>
    private sealed record LogEntry(DateTime Timestamp, string EventName, string Detail);
}
