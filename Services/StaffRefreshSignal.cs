namespace PetShop.Services;

/// <summary>
/// Tín hiệu "làm mới dữ liệu" dùng chung cho khu vực nhân viên (staff workspace).
///
/// <para>
/// <b>Vì sao cần:</b> thanh trên của workspace có đồng hồ đếm ngược "Làm mới: 30 s".
/// Trước đây đồng hồ này chỉ đếm và về lại 30 — <b>không làm gì cả</b>, tức là một
/// thông số giả gây hiểu nhầm cho người dùng. Nay đồng hồ thực sự phát tín hiệu
/// để các trang staff nạp lại dữ liệu từ DB.
/// </para>
///
/// <para>
/// Đăng ký là <c>Scoped</c> ⇒ mỗi circuit Blazor có 1 instance riêng, nên tín hiệu
/// chỉ ảnh hưởng tới những trang đang mở trong cùng circuit đó (không "bắn" sang
/// các tab của người dùng khác).
/// </para>
/// </summary>
public sealed class StaffRefreshSignal
{
    /// <summary>Sự kiện phát ra khi tới giờ làm mới.</summary>
    public event Action? RefreshRequested;

    /// <summary>Phát tín hiệu làm mới tới toàn bộ trang đang subscribe.</summary>
    public void RequestRefresh()
    {
        try
        {
            RefreshRequested?.Invoke();
        }
        catch
        {
            // Trang đã dispose trong lúc nhận tín hiệu ⇒ bỏ qua, không làm hỏng cả vòng.
        }
    }

    /// <summary>Đăng ký handler; trả về disposable để hủy đăng ký khi trang bị dispose.</summary>
    public IDisposable Subscribe(Action handler)
    {
        if (handler is null)
        {
            return NullDisposable.Instance;
        }

        RefreshRequested += handler;
        return new Subscription(this, handler);
    }

    private sealed class Subscription : IDisposable
    {
        private readonly StaffRefreshSignal _owner;
        private readonly Action _handler;

        public Subscription(StaffRefreshSignal owner, Action handler)
        {
            _owner = owner;
            _handler = handler;
        }

        public void Dispose() => _owner.RefreshRequested -= _handler;
    }

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
