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
    {
        private bool loaded;
    private bool isSubmitting;
    private string? error;
    private IReadOnlyList<PetShop.Models.FrontendCartItem> ItemsToCheckout { get; set; } = Array.Empty<PetShop.Models.FrontendCartItem>();

    // --- State của AddressModal (port từ CheckoutPage.jsx của nguồn OJT) ---

    /// <summary>Modal địa chỉ có đang mở hay không.</summary>
    public bool ShowAddressModal
    {
        get => showAddressModal;
        set => showAddressModal = value;
    }

    /// <summary>View hiện tại của modal: "list" hoặc "form".</summary>
    public string AddressModalView
    {
        get => addressModalView;
        set => addressModalView = value;
    }

    /// <summary>Id địa chỉ đang sửa; <c>null</c> khi thêm mới.</summary>
    public long? EditingAddressId
    {
        get => editingAddressId;
        set => editingAddressId = value;
    }

    /// <summary>Id địa chỉ đang được chọn trong danh sách.</summary>
    public long? SelectedSavedId
    {
        get => selectedSavedId;
        set => selectedSavedId = value;
    }

    /// <summary>Danh sách địa chỉ đã lưu.</summary>
    public IReadOnlyList<PetShop_PRN222.Components.Checkout.SavedAddress> SavedAddresses => savedAddresses;

    /// <summary>Bộ lỗi hiện tại (dùng cho cả form thanh toán lẫn modal địa chỉ).</summary>
    public IReadOnlyDictionary<string, string> Errors => errors;

    /// <summary>Các field đã được chạm (blur).</summary>
    public IReadOnlySet<string> Touched => touched;

    private bool showAddressModal;
    private string addressModalView = "list";
    private long? editingAddressId;
    private long? selectedSavedId;
    private List<PetShop_PRN222.Components.Checkout.SavedAddress> savedAddresses = new();

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
            // Nạp danh sách địa chỉ đã lưu (localStorage → DB) và điền sẵn form.
            // Phải gọi SAU khi form.FullName/Email đã có dữ liệu từ session để địa chỉ
            // đọc từ DB không làm mất họ tên/email của khách.
            try { await LoadSavedAddressesAsync(); } catch (Exception ex) { Logger.LineWarning($"[Checkout.razor] LoadSavedAddressesAsync threw: {ex.GetType().Name}"); }

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

    private async Task SubmitAsync()
    {
        error = null;

        if (!UserSession.IsAuthenticated || UserSession.CurrentUser is null)
        {
            error = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.";
            // Điều hướng NỘI BỘ ⇒ replace (không forceLoad) để giữ circuit Blazor.
            Navigation.NavigateTo("/login", replace: true);
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

        // Tự động lưu địa chỉ vừa nhập vào danh sách địa chỉ (port hành vi nguồn OJT:
        // "if (existingIndex === -1) saveAddress(addressData)"). Nhờ vậy địa chỉ dùng ở
        // đơn hàng đầu tiên cũng được giữ lại cho các đơn sau. Bọc try/catch: lỗi lưu
        // địa chỉ KHÔNG được chặn việc đặt hàng.
        try { await EnsureCurrentAddressSavedAsync(); } catch (Exception ex) { Logger.LineWarning($"[Checkout] EnsureCurrentAddressSavedAsync lỗi: {ex.GetType().Name} {ex.Message}"); }

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

    private void HandleCartChanged() => InvokeAsync(StateHasChanged);
    private void HandleSessionChanged() => InvokeAsync(StateHasChanged);

    // =======================================================================
    // AddressModal — port nguyên văn logic từ CheckoutPage.jsx (nguồn OJT)
    // =======================================================================

    /// <summary>Mở modal địa chỉ. Nếu chưa có địa chỉ nào thì mở thẳng form (port <c>handleOpenAddressModal</c>).</summary>
    private void HandleOpenAddressModal()
    {
        if (savedAddresses.Count == 0)
        {
            StartAddAddress();
            showAddressModal = true;
        }
        else
        {
            editingAddressId = null;
            addressModalView = "list";
            showAddressModal = true;
        }
    }

    /// <summary>Đóng modal và reset về view danh sách (port <c>closeAddressModal</c>).</summary>
    private void CloseAddressModal()
    {
        showAddressModal = false;
        addressModalView = "list";
        editingAddressId = null;
    }

    /// <summary>Bắt đầu thêm địa chỉ mới (port <c>startAddAddress</c>).</summary>
    private void StartAddAddress()
    {
        editingAddressId = null;
        form.Province = "Đà Nẵng";
        form.District = string.Empty;
        form.Ward = string.Empty;
        form.AddressDetail = string.Empty;
        errors.Clear();
        touched.Clear();

        // Nguồn: lấy email/họ tên từ authUser trong localStorage nếu có.
        if (string.IsNullOrWhiteSpace(form.Email))
        {
            form.Email = UserSession.CurrentUser?.Email ?? form.Email;
        }

        if (string.IsNullOrWhiteSpace(form.FullName))
        {
            form.FullName = UserSession.CurrentUser?.Name ?? form.FullName;
        }

        addressModalView = "form";
    }

    /// <summary>Bắt đầu sửa một địa chỉ đã lưu (port <c>startEditAddress</c>).</summary>
    private void StartEditAddress(PetShop_PRN222.Components.Checkout.SavedAddress addr)
    {
        editingAddressId = addr.Id;
        ApplyAddressToForm(addr);
        errors.Clear();
        touched.Clear();
        addressModalView = "form";
    }

    /// <summary>Chọn một địa chỉ đã lưu và đổ vào form (port <c>selectSavedAddress</c>).</summary>
    private void SelectSavedAddress(PetShop_PRN222.Components.Checkout.SavedAddress addr)
    {
        selectedSavedId = addr.Id;
        ApplyAddressToForm(addr);
        errors.Clear();
        touched.Clear();
    }

    /// <summary>Quay lại từ form về danh sách (port <c>handleBackFromForm</c>).</summary>
    private void BackFromAddressForm()
    {
        if (savedAddresses.Count == 0)
        {
            CloseAddressModal();
        }
        else
        {
            addressModalView = "list";
            editingAddressId = null;
        }
    }

    /// <summary>Lưu địa chỉ từ modal (port <c>handleAddressModalSave</c>) — xem <c>Checkout.Address.cs</c>.</summary>
    private Task SaveAddress(PetShop_PRN222.Components.Checkout.SavedAddress data) => SaveAddressAsync(data);

    /// <summary>Xoá một địa chỉ đã lưu (port <c>deleteAddress</c>) — xem <c>Checkout.Address.cs</c>.</summary>
    private Task DeleteAddress(long id) => DeleteAddressAsync(id);

    /// <summary>Đổ địa chỉ đã chọn vào form thanh toán (port <c>applyAddressToForm</c>).</summary>
    private void ApplyAddressToForm(PetShop_PRN222.Components.Checkout.SavedAddress addr)
    {
        form.FullName = addr.FullName ?? string.Empty;
        form.Phone = addr.Phone ?? string.Empty;
        form.Email = addr.Email ?? string.Empty;
        form.Province = string.IsNullOrWhiteSpace(addr.Province) ? "Đà Nẵng" : addr.Province;
        form.District = addr.District ?? string.Empty;
        form.Ward = addr.Ward ?? string.Empty;
        form.AddressDetail = addr.Address ?? string.Empty;
    }
    private static string FormatPhoneDisplay(string phone)
    {
        var d = (phone ?? string.Empty).Replace(" ", string.Empty);
        if (d.Length < 10) return phone ?? string.Empty;
        return $"{d[..4]} {d.Substring(4, 3)} {d[7..]}";
    }

    private static string FormatPrice(decimal value) =>
        PetShop.Components.Shared.Format.Vnd(value);

    public void Dispose()
    {
        CartState.Changed -= HandleCartChanged;
        UserSession.Changed -= HandleSessionChanged;
    }
    }
}
