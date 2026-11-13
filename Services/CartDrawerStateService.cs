namespace PetShop.Services;

/// <summary>
/// Trạng thái mở/đóng của <c>CartDrawer</c> — dùng chung cho MỌI trang.
///
/// <para>
/// <b>VÌ SAO CẦN (fix nút giỏ hàng "chết" sau khi chuyển trang):</b>
/// trước đây mỗi trang tự khai báo <c>&lt;CartDrawer Open="cartDrawerOpen" /&gt;</c>
/// và truyền <c>&lt;Header OnOpenCart="OpenCart" /&gt;</c>. Nhưng chỉ **4 trang**
/// (Home / Products / Search / ProductDetail) làm vậy — các trang còn lại
/// (<c>/services</c>, <c>/about</c>, <c>/boarding</c>, <c>/blog</c>, <c>/contact</c>,
/// <c>/account</c>, <c>/services/{id}</c>…) gọi <c>&lt;Header /&gt;</c> **không có
/// tham số** ⇒ <c>EventCallback OnOpenCart</c> rỗng ⇒ bấm nút giỏ hàng **không
/// xảy ra gì cả** ⇒ người dùng phải F5 mới thấy phản hồi.
/// </para>
///
/// <para>
/// Nay drawer được render MỘT LẦN duy nhất trong <c>MainLayout</c> và điều khiển
/// qua service này ⇒ nút giỏ hàng hoạt động trên **mọi** trang, không phụ thuộc
/// trang đó có truyền <c>OnOpenCart</c> hay không.
/// </para>
/// </summary>
public sealed class CartDrawerStateService
{
    /// <summary>Sự kiện thay đổi trạng thái mở/đóng (để component re-render).</summary>
    public event Action? Changed;

    private bool _isOpen;

    /// <summary>Drawer giỏ hàng đang mở hay không.</summary>
    public bool IsOpen => _isOpen;

    /// <summary>Mở drawer giỏ hàng (no-op nếu đã mở).</summary>
    public void Open()
    {
        if (_isOpen)
        {
            return;
        }

        _isOpen = true;
        Changed?.Invoke();
    }

    /// <summary>Đóng drawer giỏ hàng (no-op nếu đã đóng).</summary>
    public void Close()
    {
        if (!_isOpen)
        {
            return;
        }

        _isOpen = false;
        Changed?.Invoke();
    }

    /// <summary>Bật/tắt drawer giỏ hàng.</summary>
    public void Toggle()
    {
        if (_isOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }
}
