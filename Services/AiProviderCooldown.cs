namespace PetShop.Services;

/// <summary>
/// Trạng thái "cooldown" dùng chung cho nhà cung cấp AI (Gemini).
///
/// <para>
/// <b>VÌ SAO cần class này (tối ưu RAM/quota + độ trễ):</b>
/// </para>
/// <list type="bullet">
///   <item>
///     <c>GeminiProviderService</c> được đăng ký qua <c>AddHttpClient</c> ⇒ là
///     <b>transient</b> (instance mới cho mỗi scope). Nếu lưu cờ cooldown trong
///     instance thì cờ bị mất sau mỗi request ⇒ mọi lần gọi tiếp vẫn đốt quota
///     và vẫn phải chờ 30-120 giây cho một lỗi chắc chắn.
///   </item>
///   <item>
///     ⇒ Lưu state ở <b>singleton</b> ⇒ mọi request đều thấy cùng một cờ
///     chặn ⇒ lần gọi sau trả lỗi NGAY (không gọi mạng, không tốn quota).
///   </item>
/// </list>
///
/// <para>
/// Cờ tự động hết hạn theo thời gian ⇒ không cần background timer ⇒ không
/// tốn thread/CPU cho việc dọn dẹp.
/// </para>
/// </summary>
public sealed class AiProviderCooldown
{
    /// <summary>Số lần gọi bị chặn (để thống kê/log).</summary>
    private int _blockedCallCount;

    /// <summary>Thời điểm sớm nhất được gọi lại (Utc).</summary>
    public DateTime BlockedUntilUtc { get; private set; } = DateTime.MinValue;

    /// <summary>Lý do chặn gần nhất (để log + hiển thị).</summary>
    public string? Reason { get; private set; }

    /// <summary>Số lần gọi đã bị chặn từ khi bật cooldown.</summary>
    public int BlockedCallCount => _blockedCallCount;

    /// <summary>Đang trong thời gian chặn hay không.</summary>
    public bool IsBlocked => DateTime.UtcNow < BlockedUntilUtc;

    /// <summary>
    /// Bật cooldown.
    /// </summary>
    /// <param name="seconds">Thời gian chặn (giây).</param>
    /// <param name="reason">Lý do chặn.</param>
    public void Enter(int seconds, string reason)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);

        // Chỉ gia hạn nếu thời điểm mới xa hơn ⇒ tránh reset cooldown liên tục.
        if (until > BlockedUntilUtc)
        {
            BlockedUntilUtc = until;
        }

        Reason = reason;
    }

    /// <summary>
    /// Ghi nhận một lần gọi bị chặn (không gọi mạng).
    /// </summary>
    public void RecordBlockedCall()
    {
        _blockedCallCount++;
    }

    /// <summary>Xóa cooldown (dùng khi một lần gọi thành công).</summary>
    public void Reset()
    {
        BlockedUntilUtc = DateTime.MinValue;
        Reason = null;
    }
}
