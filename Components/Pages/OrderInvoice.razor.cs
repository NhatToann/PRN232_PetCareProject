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
    /// Pháº§n code-behind cá»§a <c>OrderInvoice.razor</c>.
    /// TĂ¡ch riĂªng Ä‘á»ƒ file .razor khĂ´ng vÆ°á»£t quĂ¡ 500 dĂ²ng (quy Æ°á»›c trong AGENTS.md Â§2.3).
    /// </summary>
    public partial class OrderInvoice
    {    [SupplyParameterFromQuery(Name = "orderId")] private int? OrderId { get; set; }
    [SupplyParameterFromQuery(Name = "payos")] private string? Payos { get; set; }
    [SupplyParameterFromQuery(Name = "cancel")] private string? Cancel { get; set; }
    [SupplyParameterFromQuery(Name = "status")] private string? PayOsStatus { get; set; }
    [SupplyParameterFromQuery(Name = "orderCode")] private string? PayOsOrderCode { get; set; }

    private FrontendOrderDetail? order;
    private bool isLoading = true;
    private string? error;
    private string? checkoutUrl;
    private string? payosGatewayStatus;
    private int resolvedOrderId;
    private int resolvedOrderCode;
    private InvoicePaymentState paymentState = InvoicePaymentState.Unknown;

    private int CurrentCustomerId => UserSession.CurrentUser?.UserId ?? 0;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        try { await UserSession.LoadAsync(); }
        catch (Exception ex)
        {
            Logger.LineWarning($"[OrderInvoice] UserSession.LoadAsync: {ex.Message}");
        }

        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        isLoading = true;
        error = null;
        StateHasChanged();

        if (CurrentCustomerId <= 0)
        {
            error = "Bạn cần đăng nhập để xem hóa đơn.";
            isLoading = false;
            StateHasChanged();
            return;
        }

        var storage = await ReadStoredOrderAsync("pendingPayOSOrder")
            ?? await ReadStoredOrderAsync("lastPayOSOrderInvoice");

        resolvedOrderId = OrderId is int queryOrderId && queryOrderId > 0
            ? queryOrderId
            : storage?.orderId ?? 0;
        resolvedOrderCode = ParseOrderCode(PayOsOrderCode)
            ?? storage?.orderCode ?? 0;

        if (resolvedOrderId <= 0)
        {
            error = "Không tìm thấy thông tin đơn hàng PayOS để hiển thị hóa đơn.";
            isLoading = false;
            StateHasChanged();
            return;
        }

        paymentState = InferStateFromReturnQuery();
        await SyncPayOSStatusAsync(storage);

        var orderRes = await OrderClient.GetDetailAsync(resolvedOrderId, CurrentCustomerId);
        if (!orderRes.IsSuccess || orderRes.Data is null)
        {
            error = orderRes.StatusCode == 404
                ? "Không tìm thấy đơn hàng."
                : orderRes.Error ?? "Không tải được hóa đơn đơn hàng.";
        }
        else
        {
            order = orderRes.Data;
            if (paymentState == InvoicePaymentState.Unknown)
            {
                paymentState = InferStateFromOrder(order);
            }
        }

        isLoading = false;
        StateHasChanged();
    }

    private async Task SyncPayOSStatusAsync(PayOSOrderStorage? storage)
    {
        if (resolvedOrderCode <= 0)
        {
            return;
        }

        var result = await PaymentClient.GetPayOSStatusAsync(resolvedOrderCode);
        if (!result.IsSuccess || result.Data is null)
        {
            if (paymentState != InvoicePaymentState.Cancelled)
            {
                error = result.Error ?? "Không thể xác nhận trạng thái thanh toán PayOS.";
            }
            else
            {
                await PersistLastInvoiceAsync(storage, "cancel");
                await JS.InvokeVoidAsync("localStorage.removeItem", "pendingPayOSOrder");
            }
            return;
        }

        var data = result.Data;
        payosGatewayStatus = data.Status;
        checkoutUrl = data.CheckoutUrl;

        if (data.IsPaid)
        {
            paymentState = InvoicePaymentState.Paid;
            await CartState.ClearAsync();
            await JS.InvokeVoidAsync("localStorage.removeItem", "checkoutItems");
            await PersistLastInvoiceAsync(storage, "success");
            await JS.InvokeVoidAsync("localStorage.removeItem", "pendingPayOSOrder");
            return;
        }

        if (IsCancelledStatus(data.Status))
        {
            paymentState = InvoicePaymentState.Cancelled;
            await PersistLastInvoiceAsync(storage, "cancel");
            await JS.InvokeVoidAsync("localStorage.removeItem", "pendingPayOSOrder");
            return;
        }

        if (IsPendingStatus(data.Status))
        {
            if (paymentState == InvoicePaymentState.Cancelled)
            {
                await PersistLastInvoiceAsync(storage, "cancel");
                await JS.InvokeVoidAsync("localStorage.removeItem", "pendingPayOSOrder");
                return;
            }

            paymentState = InvoicePaymentState.Pending;
            return;
        }

        paymentState = InvoicePaymentState.Unknown;
        error = $"PayOS trả về trạng thái chưa hỗ trợ: {data.Status}.";
    }

    private async Task<PayOSOrderStorage?> ReadStoredOrderAsync(string key)
    {
        try
        {
            var raw = await JS.InvokeAsync<string?>("localStorage.getItem", key);
            if (string.IsNullOrWhiteSpace(raw)) return null;
            return System.Text.Json.JsonSerializer.Deserialize<PayOSOrderStorage>(raw);
        }
        catch (Exception ex)
        {
            Logger.LineWarning($"[OrderInvoice] ReadStoredOrderAsync({key}): {ex.Message}");
            return null;
        }
    }

    private async Task PersistLastInvoiceAsync(PayOSOrderStorage? storage, string payosState)
    {
        try
        {
            var snapshot = new PayOSOrderStorage
            {
                orderId = resolvedOrderId,
                orderCode = resolvedOrderCode,
                totalAmount = storage?.totalAmount ?? order?.TotalAmount ?? 0m,
                payos = payosState
            };
            var json = System.Text.Json.JsonSerializer.Serialize(snapshot);
            await JS.InvokeVoidAsync("localStorage.setItem", "lastPayOSOrderInvoice", json);
        }
        catch (Exception ex)
        {
            Logger.LineWarning($"[OrderInvoice] PersistLastInvoiceAsync: {ex.Message}");
        }
    }

    private InvoicePaymentState InferStateFromReturnQuery()
    {
        if (string.Equals(Cancel, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Payos, "cancel", StringComparison.OrdinalIgnoreCase)
            || IsCancelledStatus(PayOsStatus))
        {
            return InvoicePaymentState.Cancelled;
        }

        return InvoicePaymentState.Unknown;
    }

    private static InvoicePaymentState InferStateFromOrder(FrontendOrderDetail detail)
    {
        if (LooksPaid(detail.PaymentStatus)) return InvoicePaymentState.Paid;
        if (LooksCancelled(detail.PaymentStatus) || LooksCancelled(detail.Status)) return InvoicePaymentState.Cancelled;
        if (LooksPending(detail.PaymentStatus) || LooksPending(detail.Status)) return InvoicePaymentState.Pending;
        return InvoicePaymentState.Unknown;
    }

    private string GetStatusLabel()
    {
        return paymentState switch
        {
            InvoicePaymentState.Paid => "Đã thanh toán",
            InvoicePaymentState.Cancelled => "Đã hủy",
            InvoicePaymentState.Pending => "Chờ thanh toán",
            _ => order?.PaymentStatus ?? order?.Status ?? "Chưa rõ"
        };
    }

    private string GetStatusClass()
    {
        return paymentState switch
        {
            InvoicePaymentState.Paid => "is-paid",
            InvoicePaymentState.Cancelled => "is-cancel",
            _ => ""
        };
    }

    private void RetryPayment()
    {
        if (!string.IsNullOrWhiteSpace(checkoutUrl))
        {
            Navigation.NavigateTo(checkoutUrl, forceLoad: true);
        }
    }

    private async Task PrintInvoice() => await JS.InvokeVoidAsync("window.print");

    private static int? ParseOrderCode(string? value)
    {
        return int.TryParse(value, out var code) && code > 0 ? code : null;
    }

    private static bool IsCancelledStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var s = value.Trim().ToUpperInvariant();
        return s is "CANCELLED" or "CANCELED" or "EXPIRED" or "FAILED" or "ERROR" or "CANCEL";
    }

    private static bool IsPendingStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.Trim().Equals("PENDING", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksPaid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var s = value.Trim().ToLowerInvariant();
        return s.Contains("paid") || s.Contains("đã thanh toán") || s.Contains("da thanh toan");
    }

    private static bool LooksCancelled(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var s = value.Trim().ToLowerInvariant();
        return s.Contains("cancel") || s.Contains("hủy") || s.Contains("huy");
    }

    private static bool LooksPending(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var s = value.Trim().ToLowerInvariant();
        return s.Contains("pending") || s.Contains("chờ thanh toán") || s.Contains("cho thanh toan");
    }

    private sealed class PayOSOrderStorage
    {
        public int orderId { get; set; }
        public int orderCode { get; set; }
        public decimal totalAmount { get; set; }
        public string? payos { get; set; }
    }

    private enum InvoicePaymentState
    {
        Unknown,
        Pending,
        Paid,
        Cancelled
    }
    }
}
