namespace PetShop.Services;

/// <summary>
/// Điều phối cửa sổ chat hợp nhất cho toàn site.
///
/// <para>
/// <b>THAY ĐỔI (12/11/2026 — redesign hộp thoại):</b> trước đây có 2 widget
/// riêng (<c>ChatWidget</c> + <c>AiChatWidget</c>) và 2 "tab" bên trong
/// (<c>Tin nhắn</c> / <c>Trợ lý AI</c>) ⇒ người dùng phải chuyển qua lại giữa
/// 2 mục riêng biệt. Nay gộp thành <b>MỘT</b> panel, <b>MỘT</b> luồng tin nhắn:
/// AI và nhân viên cùng xuất hiện, phân biệt bằng avatar + nhãn tên.
/// </para>
///
/// <para>
/// Việc chọn "gửi cho AI hay gửi cho nhân viên" KHÔNG nằm ở tab chia cả
/// panel nữa ⇒ nằm ở <b>chip đích đến</b> trong ô nhập, do
/// <see cref="Target"/> mô tả.
/// </para>
/// </summary>
public sealed class ChatPopupState
{
    /// <summary>Đích đến: trợ lý AI (bot).</summary>
    public const string AiTarget = "ai";

    /// <summary>Đích đến: nhân viên / bác sĩ / quản trị viên.</summary>
    public const string StaffTarget = "staff";

    /// <summary>Cửa sổ chat đang mở hay không.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>
    /// Đích đến của tin nhắn đang gõ: <see cref="AiTarget"/> hoặc
    /// <see cref="StaffTarget"/>.
    /// </summary>
    public string Target { get; private set; } = AiTarget;

    /// <summary>Sự kiện phát ra khi trạng thái cửa sổ / đích đến thay đổi.</summary>
    public event Action? Changed;

    /// <summary>Mở / đóng cửa sổ chat.</summary>
    public void Toggle()
    {
        IsOpen = !IsOpen;
        Changed?.Invoke();
    }

    /// <summary>Mở cửa sổ chat (nếu đang đóng).</summary>
    public void Open()
    {
        if (IsOpen)
        {
            return;
        }

        IsOpen = true;
        Changed?.Invoke();
    }

    /// <summary>Đóng cửa sổ chat.</summary>
    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        Changed?.Invoke();
    }

    /// <summary>Chuyển đích đến của tin nhắn đang gõ (AI hay nhân viên).</summary>
    public void SelectTarget(string target)
    {
        var next = string.Equals(target, StaffTarget, StringComparison.OrdinalIgnoreCase)
            ? StaffTarget
            : AiTarget;

        if (string.Equals(Target, next, StringComparison.Ordinal))
        {
            return;
        }

        Target = next;
        Changed?.Invoke();
    }
}
