using PetShop.Hubs;
using PetShop.Models;

namespace PetShop.Tests;

/// <summary>
/// Unit test cho phần logic thuần của LS-11 (SignalR realtime).
///
/// Không test hub server (cần chạy app + DB) — chỉ test phần logic tất định:
///   - Tên group theo user (dùng để join/leave/push, phải khớp ở mọi nơi).
///   - Tên event client/server dùng chung (đổi tên là gãy realtime).
///   - Cấu trúc payload (DTO) mà client giải mã được.
/// </summary>
public class NotificationHubTests
{
    [Fact]
    public void UserGroup_UsesUserPrefix_AndUserId()
    {
        // Group phải có dạng "user:{id}" — server push và client join dùng chung
        // nên lệch 1 ký tự là client không nhận được event.
        Assert.Equal("user:42", NotificationHub.UserGroup(42));
        Assert.Equal("user:1", NotificationHub.UserGroup(1));
    }

    [Fact]
    public void UserGroup_IsStable_ForSameUser()
    {
        Assert.Equal(NotificationHub.UserGroup(7), NotificationHub.UserGroup(7));
        Assert.NotEqual(NotificationHub.UserGroup(7), NotificationHub.UserGroup(8));
    }

    [Fact]
    public void EventNames_MatchTheProtocolContract()
    {
        // Client subscribe đúng 2 event này (NotificationBell + SignalRDemo).
        Assert.Equal("NotificationReceived", NotificationHub.NotificationReceivedEvent);
        Assert.Equal("ChatReceived", NotificationHub.ChatReceivedEvent);
    }

    [Fact]
    public void RealtimeNotificationDto_KeepsRelatedRequestId_AndTimestamp()
    {
        var sentAt = new DateTime(2026, 11, 8, 10, 30, 0);
        var dto = new RealtimeNotificationDto(5, "Yêu cầu đổi ca", "Nội dung", 123, sentAt);

        Assert.Equal(5, dto.UserId);
        Assert.Equal("Yêu cầu đổi ca", dto.Title);
        Assert.Equal("Nội dung", dto.Message);
        Assert.Equal(123, dto.RelatedRequestId);
        Assert.Equal(sentAt, dto.SentAt);
    }

    [Fact]
    public void RealtimeNotificationDto_AllowsNullRelatedRequestId()
    {
        var dto = new RealtimeNotificationDto(5, "Tiêu đề", "Nội dung", null, DateTime.Now);

        Assert.Null(dto.RelatedRequestId);
    }

    [Fact]
    public void ChatEnvelope_CarriesSenderAndPersistFlag()
    {
        var sentAt = new DateTime(2026, 11, 8, 10, 31, 0);
        var envelope = new ChatEnvelope(9, 3, "Chào bạn", sentAt, IsPersisted: true);

        Assert.Equal(9, envelope.UserId);
        Assert.Equal(3, envelope.FromUserId);
        Assert.Equal("Chào bạn", envelope.Message);
        Assert.True(envelope.IsPersisted);
    }

    [Fact]
    public void ChatEnvelope_FlagsDemoMode_WhenNotPersisted()
    {
        // Chế độ demo (chưa xác thực) không ghi log chat ⇒ IsPersisted = false.
        var envelope = new ChatEnvelope(1, 1, "tin nhắn", DateTime.Now, IsPersisted: false);

        Assert.False(envelope.IsPersisted);
    }
}
