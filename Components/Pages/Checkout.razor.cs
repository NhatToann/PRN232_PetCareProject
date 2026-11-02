using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using PetShop.Interfaces;
using PetShop.Models;
using PetShop.Services;

namespace PetShop_PRN222.Components.Pages
{
    /// <summary>
    /// Pháº§n code-behind cá»§a <c>Checkout.razor</c>.
    /// TĂ¡ch riĂªng Ä‘á»ƒ file .razor khĂ´ng vÆ°á»£t quĂ¡ 500 dĂ²ng (quy Æ°á»›c trong AGENTS.md Â§2.3).
    /// </summary>
    public partial class Checkout
    {    private static readonly IReadOnlyList<string> DaNangDistricts = new[]
    {
        "Quận Hải Châu",
        "Quận Thanh Khê",
        "Quận Sơn Trà",
        "Quận Ngũ Hành Sơn",
        "Quận Liên Chiểu",
        "Quận Cẩm Lệ",
        "Huyện Hòa Vang",
        "Huyện Hoàng Sa",
    };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> DaNangWardsByDistrict =
        new Dictionary<string, IReadOnlyList<string>>
        {
            ["Quận Hải Châu"] = new[] { "Phường Thạch Thang", "Phường Hải Châu I", "Phường Hải Châu II", "Phường Phước Ninh", "Phường Hùng Vương", "Phường Chính Gián", "Phường Vĩnh Trung", "Phường Thanh Bình", "Phường Thuận Phước", "Phường Thủy Thuỷ", "Phường Hòa Cường Bắc", "Phường Hòa Cường Nam", "Phường Nam Dương", "Phường Ong Ích Khiêm" },
            ["Quận Thanh Khê"] = new[] { "Phường An Xuân", "Phường Tam Thuận", "Phường Thanh Khê Tây", "Phường Thanh Khê Đông", "Phường Kim Ly", "Phường Hòa Khê", "Phường Xuân Hà", "Phường Tân Chính", "Phường Bình Hiên", "Phường Bình Thuận" },
            ["Quận Sơn Trà"] = new[] { "Phường Thọ Quang", "Phường Mân Thái", "Phường Nại Hiên Đông", "Phường Phước Mỹ", "Phường An Hải Bắc", "Phường An Hải Nam", "Phường An Hải Tây", "Phường Mỹ An", "Phường Khuê Trung" },
            ["Quận Ngũ Hành Sơn"] = new[] { "Phường Mỹ An", "Phường Khuê Mỹ", "Phường Hoà Quý", "Phường Hoà Hải", "Phường Khuê Trung" },
            ["Quận Liên Chiểu"] = new[] { "Phường Hòa Khánh Bắc", "Phường Hòa Khánh Nam", "Phường Hòa Minh", "Phường Hòa Bắc", "Phường Hòa Liên", "Phường Hòa Nhơn" },
            ["Quận Cẩm Lệ"] = new[] { "Phường Hòa An", "Phường Hòa Phát", "Phường Khuê Trung", "Phường Hòa Quý", "Phường Hòa Khê", "Phường Khuê Mỹ", "Phường An Lạc" },
            ["Huyện Hòa Vang"] = new[] { "Phường Hòa Phước", "Phường Hòa Sơn", "Phường Hòa Tiến", "Phường Hòa Khương", "Phường Hòa Phong", "Phường Hòa Ninh", "Phường Hòa Phú", "Phường Hòa Bắc", "Phường Hòa Liên", "Phường Hòa Nhơn", "Phường Hòa Bình", "Phường Hòa Thọ Bắc", "Phường Hòa Thọ Đông", "Phường Hòa Thọ Nam" },
            ["Huyện Hoàng Sa"] = Array.Empty<string>(),
        };

    private readonly CheckoutForm form = new();
    private readonly Dictionary<string, string> errors = new();
    private bool loaded;
    private bool isSubmitting;
    private string? error;
    private IReadOnlyList<PetShop.Models.FrontendCartItem> ItemsToCheckout { get; set; } = Array.Empty<PetShop.Models.FrontendCartItem>();

    private IReadOnlyList<string> WardsForSelectedDistrict =>
        DaNangWardsByDistrict.TryGetValue(form.District, out var list) ? list : Array.Empty<string>();

    private string SubmitButtonLabel
    {
        get
        {
            if (isSubmitting)
            {
                return form.PaymentMethod == "PayOS" ? "Đang tạo link thanh toán..." : "Đang xử lý...";
            }
            return "Đặt hàng";
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            CartState.Changed += HandleCartChanged;
            UserSession.Changed += HandleSessionChanged;

            try { await CartState.LoadAsync(); } catch (Exception ex) { Logger.LineWarning($"[Checkout.razor] CartState.LoadAsync threw: {ex.GetType().Name}"); }
            try { await UserSession.LoadAsync(); } catch (Exception ex) { Logger.LineWarning($"[Checkout.razor] UserSession.LoadAsync threw: {ex.GetType().Name}"); }

            // FIX: Đọc sessionStorage trước — Cart.razor đã lưu selected items vào đây.
            // Fallback về CartState.Items nếu sessionStorage rỗng (navigation trực tiếp hoặc hard-refresh).
            ItemsToCheckout = await LoadItemsFromSessionAsync();

            if (string.IsNullOrWhiteSpace(form.FullName))
            {
                form.FullName = UserSession.CurrentUser?.Name ?? string.Empty;
            }
            if (string.IsNullOrWhiteSpace(form.Email))
            {
                form.Email = UserSession.CurrentUser?.Email ?? string.Empty;
            }
            if (string.IsNullOrWhiteSpace(form.Province))
            {
                form.Province = "Đà Nẵng";
            }
            loaded = true;
            StateHasChanged();
        }
    }

    private async Task<IReadOnlyList<PetShop.Models.FrontendCartItem>> LoadItemsFromSessionAsync()
    {
        try
        {
            var raw = await JS.InvokeAsync<string?>("sessionStorage.getItem", "checkoutItems");
            if (!string.IsNullOrWhiteSpace(raw))
            {
                var items = System.Text.Json.JsonSerializer.Deserialize<List<CheckoutStorageItem>>(raw);
                if (items is not null && items.Count > 0)
                {
                    return items.Select(i => new PetShop.Models.FrontendCartItem(
                        i.ProductId, i.Name, i.Price, i.Quantity,
                        i.ImageUrl ?? "", 0)).ToList();
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LineWarning($"[Checkout.razor] LoadItemsFromSessionAsync threw: {ex.GetType().Name} {ex.Message}");
        }

        // Fallback: lấy toàn bộ giỏ hàng nếu không có sessionStorage (edge case: refresh trang checkout)
        return CartState.Items.ToList();
    }

    private sealed record CheckoutStorageItem(int ProductId, string Name, decimal Price, string? ImageUrl, int Quantity);

    private void SelectPayment(string method) => form.PaymentMethod = method;

    private bool CanSubmit() => !isSubmitting && loaded && ItemsToCheckout.Count > 0 && UserSession.IsAuthenticated;

    private void ValidateField(string field)
    {
        errors.Remove(field);
        switch (field)
        {
            case nameof(form.FullName):
                if (string.IsNullOrWhiteSpace(form.FullName))
                    errors[field] = "Vui lòng nhập họ và tên.";
                else if (form.FullName.Trim().Length < 2)
                    errors[field] = "Họ và tên phải có ít nhất 2 ký tự.";
                else if (form.FullName.Length > 100)
                    errors[field] = "Họ và tên tối đa 100 ký tự.";
                break;
            case nameof(form.Phone):
                if (string.IsNullOrWhiteSpace(form.Phone))
                    errors[field] = "Vui lòng nhập số điện thoại.";
                else if (!IsValidPhone(form.Phone))
                    errors[field] = "Số điện thoại phải bắt đầu bằng 0 và có 10-11 chữ số.";
                break;
            case nameof(form.Email):
                if (!string.IsNullOrWhiteSpace(form.Email) && !IsValidEmail(form.Email))
                    errors[field] = "Email không hợp lệ.";
                break;
            case nameof(form.Province):
                if (string.IsNullOrWhiteSpace(form.Province))
                    errors[field] = "Vui lòng chọn tỉnh/thành phố.";
                else if (!string.Equals(form.Province.Trim(), "Đà Nẵng", StringComparison.OrdinalIgnoreCase))
                    errors[field] = "Hiện tại chỉ hỗ trợ giao hàng tại Đà Nẵng.";
                break;
            case nameof(form.District):
                if (string.IsNullOrWhiteSpace(form.District))
                    errors[field] = "Vui lòng chọn quận/huyện.";
                else if (!DaNangDistricts.Contains(form.District))
                    errors[field] = "Vui lòng chọn quận/huyện từ danh sách.";
                break;
            case nameof(form.Ward):
                if (string.IsNullOrWhiteSpace(form.Ward))
                    errors[field] = "Vui lòng chọn phường/xã.";
                else if (!WardsForSelectedDistrict.Contains(form.Ward))
                    errors[field] = "Phường/xã không hợp lệ cho quận/huyện đã chọn.";
                break;
            case nameof(form.AddressDetail):
                if (string.IsNullOrWhiteSpace(form.AddressDetail))
                    errors[field] = "Vui lòng nhập địa chỉ chi tiết.";
                else if (form.AddressDetail.Trim().Length < 5)
                    errors[field] = "Địa chỉ phải có ít nhất 5 ký tự.";
                else if (form.AddressDetail.Length > 200)
                    errors[field] = "Địa chỉ tối đa 200 ký tự.";
                break;
        }
        StateHasChanged();
    }

    private bool ValidateAll()
    {
        foreach (var field in new[] {
            nameof(form.FullName), nameof(form.Phone), nameof(form.Email),
            nameof(form.Province), nameof(form.District), nameof(form.Ward),
            nameof(form.AddressDetail) })
        {
            ValidateField(field);
        }
        return errors.Count == 0;
    }

    private string BuildFullAddress()
    {
        if (string.IsNullOrWhiteSpace(form.AddressDetail) ||
            string.IsNullOrWhiteSpace(form.Ward) ||
            string.IsNullOrWhiteSpace(form.District) ||
            string.IsNullOrWhiteSpace(form.Province))
        {
            return string.Empty;
        }
        return $"{form.AddressDetail.Trim()}, {form.Ward.Trim()}, {form.District.Trim()}, {form.Province.Trim()}";
    }

    private async Task SubmitAsync()
    {
        error = null;

        if (!UserSession.IsAuthenticated || UserSession.CurrentUser is null)
        {
            error = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.";
            Navigation.NavigateTo("/login", forceLoad: true);
            return;
        }

        if (ItemsToCheckout.Count == 0)
        {
            error = "Giỏ hàng trống. Vui lòng thêm sản phẩm trước khi thanh toán.";
            return;
        }

        if (!ValidateAll())
        {
            error = "Vui lòng kiểm tra lại các trường được đánh dấu.";
            return;
        }

        if (!CanSubmit())
        {
            return;
        }

        // Guard against accidental double submit. The backend also has no
        // idempotency token today; this client-side guard prevents the most
        // common user double-click. See PHASE 7 red-team row.
        if (isSubmitting)
        {
            return;
        }

        isSubmitting = true;

        var fullAddress = BuildFullAddress();
        var request = new PetShop.Models.FrontendCheckoutRequest(
            UserSession.CurrentUser.UserId,
            UserSession.CurrentUser.Email,
            form.FullName.Trim(),
            fullAddress,
            form.PaymentMethod,
            ItemsToCheckout.Select(item => new PetShop.Models.FrontendCheckoutItem(item.ProductId, item.Name, item.Quantity, item.Price)).ToList(),
            Navigation.ToAbsoluteUri("/checkout/invoice?payos=success").ToString(),
            Navigation.ToAbsoluteUri("/checkout/invoice?payos=cancel").ToString());

        var result = await CheckoutClient.CheckoutAsync(request);
        isSubmitting = false;

        if (!result.IsSuccess || result.Data is null)
        {
            error = FormatBackendError(result.StatusCode, result.Error);
            // Cart is intentionally NOT cleared on failure.
            return;
        }

        if (string.Equals(result.Data.PaymentMethod, "PayOS", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(result.Data.RedirectUrl))
        {
            // Save pending order to localStorage so checkout/invoice can retrieve it after PayOS redirect.
            var payosTotal = ItemsToCheckout.Sum(i => i.Price * i.Quantity);
            var orderCodeVal = int.TryParse(result.Data.OrderCode, out var oc) ? oc : 0;
            var pendingOrder = new PendingOrderInfo(result.Data.OrderId, orderCodeVal, payosTotal);
            await JS.InvokeVoidAsync("localStorage.setItem", "pendingPayOSOrder", System.Text.Json.JsonSerializer.Serialize(pendingOrder));

            // Navigation is a hard reload so the user lands on the PayOS checkout page.
            Navigation.NavigateTo(result.Data.RedirectUrl, forceLoad: true);
            return;
        }

        // COD: backend has accepted the order.
        // Save order info to localStorage for the cod-success confirmation page,
        // then clear the cart and redirect to /checkout/cod-success.
        var orderTotal = ItemsToCheckout.Sum(i => i.Price * i.Quantity);
        var codOrderCode = int.TryParse(result.Data.OrderCode, out var codOc) ? codOc : 0;
        var lastCodOrder = new LastCodOrderInfo(
            result.Data.OrderId,
            codOrderCode,
            form.FullName,
            form.Phone,
            BuildFullAddress(),
            orderTotal);
        await JS.InvokeVoidAsync("localStorage.setItem",
            "lastCodOrder",
            System.Text.Json.JsonSerializer.Serialize(lastCodOrder));
        await CartState.ClearAsync();
        Navigation.NavigateTo("/checkout/cod-success", forceLoad: true);
    }

    private sealed record PendingOrderInfo(int orderId, int orderCode, decimal totalAmount);
    private sealed record LastCodOrderInfo(int orderId, int orderCode, string fullName, string phone, string address, decimal totalAmount);

    private static string FormatBackendError(int? statusCode, string? raw)
    {
        var detail = string.IsNullOrWhiteSpace(raw) ? "Vui lòng thử lại." : raw;
        return statusCode switch
        {
            400 => $"Đơn hàng chưa hợp lệ: {detail}",
            401 => "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.",
            403 => "Bạn không có quyền đặt đơn hàng này.",
            404 => "Không tìm thấy sản phẩm hoặc tài nguyên. Vui lòng tải lại trang.",
            409 => $"Xung đột đơn hàng: {detail}",
            500 or 502 or 503 or 504 => "Máy chủ đang gặp sự cố. Vui lòng thử lại sau vài phút.",
            _ => $"Thanh toán thất bại: {detail}",
        };
    }

    private static bool IsValidPhone(string phone)
    {
        var clean = (phone ?? string.Empty).Replace(" ", string.Empty);
        return System.Text.RegularExpressions.Regex.IsMatch(clean, @"^0[0-9]{9,10}$");
    }

    private static bool IsValidEmail(string email)
    {
        return System.Text.RegularExpressions.Regex.IsMatch(email ?? string.Empty, @"^[^\s@]+@[^\s@]+\.[^\s@]+$");
    }

    private void HandleCartChanged() => InvokeAsync(StateHasChanged);
    private void HandleSessionChanged() => InvokeAsync(StateHasChanged);

    private static string FormatPrice(decimal value) =>
        string.Create(System.Globalization.CultureInfo.GetCultureInfo("vi-VN"), $"{value:C0}");

    public void Dispose()
    {
        CartState.Changed -= HandleCartChanged;
        UserSession.Changed -= HandleSessionChanged;
    }

    private sealed class CheckoutForm
    {
        public string FullName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Province { get; set; } = string.Empty;
        public string District { get; set; } = string.Empty;
        public string Ward { get; set; } = string.Empty;
        public string AddressDetail { get; set; } = string.Empty;
        public string PaymentMethod { get; set; } = "COD";
        public string Note { get; set; } = string.Empty;
    }
    }
}
