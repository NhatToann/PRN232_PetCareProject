using System.Globalization;
using System.Text;
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
    /// Pháº§n code-behind cá»§a <c>Orders.razor</c>.
    /// TĂ¡ch riĂªng Ä‘á»ƒ file .razor khĂ´ng vÆ°á»£t quĂ¡ 500 dĂ²ng (quy Æ°á»›c trong AGENTS.md Â§2.3).
    /// </summary>
    public partial class Orders
    {    [Parameter] public int? OrderId { get; set; }

    private readonly (string Id, string Label)[] Tabs =
    {
        ("all", "Tất cả"),
        ("unpaid", "Chờ thanh toán"),
        ("pending_confirm", "Chờ xác nhận"),
        ("shipping", "Chờ giao hàng"),
        ("done", "Hoàn thành"),
        ("cancel", "Đã hủy"),
    };

    private bool loaded;
    private bool pendingLoad = true;
    private string? error;
    private string activeTab = "all";
    private string search = "";
    private IReadOnlyList<FrontendOrderSummary> orders = Array.Empty<FrontendOrderSummary>();
    private FrontendOrderDetail? detail;
    private int? loadedOrderId = -1;
    private bool deliveredConfirming;
    private bool reviewsLoading;
    private readonly Dictionary<int, ReviewDisplayDto?> reviewsByProductId = new();
    private readonly Dictionary<int, int> reviewRatingsByProductId = new();
    private readonly Dictionary<int, string> reviewCommentsByProductId = new();
    private readonly HashSet<int> reviewSubmittingByProductId = new();

    private List<FrontendOrderSummary> FilteredOrders =>
        orders.Where(o => MatchesOrderTab(activeTab, o) && OrderMatchesSearch(o, search)).ToList();

    protected override Task OnParametersSetAsync()
    {
        if (loadedOrderId != OrderId)
        {
            loadedOrderId = OrderId;
            loaded = false;
            pendingLoad = true;
            error = null;
            detail = null;
            orders = Array.Empty<FrontendOrderSummary>();
            ClearReviewState();
        }

        return Task.CompletedTask;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!pendingLoad)
        {
            return;
        }

        pendingLoad = false;
        await LoadAsync();
        StateHasChanged();
    }

    private async Task LoadAsync()
    {
        loaded = false;
        error = null;
        ClearReviewState();

        try
        {
            await UserSession.LoadAsync();
            var customerId = UserSession.CurrentUser?.UserId ?? 0;
            if (customerId <= 0)
            {
                loaded = true;
                return;
            }

            if (OrderId is int orderId)
            {
                var result = await OrderClient.GetDetailAsync(orderId, customerId);
                if (result.IsSuccess && result.Data is not null)
                {
                    detail = result.Data;
                    if (IsOrderCompleted(detail.Status))
                    {
                        await LoadProductReviewsAsync();
                    }
                }
                else
                {
                    error = result.Error ?? "Không tải được chi tiết đơn hàng.";
                }
            }
            else
            {
                var result = await OrderClient.GetHistoryAsync(customerId);
                if (result.IsSuccess)
                {
                    orders = result.Data ?? Array.Empty<FrontendOrderSummary>();
                }
                else
                {
                    error = result.Error ?? "Không tải được danh sách đơn hàng.";
                }
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        finally
        {
            loaded = true;
        }
    }

    private async Task MarkDeliveredAsync()
    {
        if (detail is null || deliveredConfirming)
        {
            return;
        }

        var customerId = UserSession.CurrentUser?.UserId ?? 0;
        if (customerId <= 0)
        {
            // Điều hướng NỘI BỘ ⇒ replace (không forceLoad) để giữ circuit Blazor.
            Navigation.NavigateTo("/login", replace: true);
            return;
        }

        deliveredConfirming = true;
        try
        {
            var result = await OrderClient.MarkDeliveredAsync(detail.OrderId, customerId);
            if (!result.IsSuccess)
            {
                await JS.InvokeVoidAsync("alert", result.Error ?? "Xác nhận không thành công.");
                return;
            }

            var refreshed = await OrderClient.GetDetailAsync(detail.OrderId, customerId);
            if (refreshed.IsSuccess && refreshed.Data is not null)
            {
                detail = refreshed.Data;
                if (IsOrderCompleted(detail.Status))
                {
                    await LoadProductReviewsAsync();
                }
            }
        }
        finally
        {
            deliveredConfirming = false;
        }
    }

    private async Task LoadProductReviewsAsync()
    {
        if (detail is null)
        {
            return;
        }

        var customerId = UserSession.CurrentUser?.UserId ?? 0;
        if (customerId <= 0)
        {
            return;
        }

        reviewsLoading = true;
        reviewsByProductId.Clear();
        reviewRatingsByProductId.Clear();
        reviewCommentsByProductId.Clear();

        try
        {
            foreach (var productId in detail.Items.Select(i => i.ProductId).Where(i => i > 0).Distinct())
            {
                var result = await ReviewClient.GetProductReviewsAsync(productId);
                var myReview = result.Data?.FirstOrDefault(r => r.CustomerId == customerId);
                reviewsByProductId[productId] = myReview;
                reviewRatingsByProductId[productId] = myReview?.Rating is > 0 ? myReview.Rating : 5;
                reviewCommentsByProductId[productId] = myReview?.Comment ?? "";
            }
        }
        finally
        {
            reviewsLoading = false;
        }
    }

    private async Task SubmitReviewAsync(int productId)
    {
        if (detail is null)
        {
            return;
        }

        var customerId = UserSession.CurrentUser?.UserId ?? 0;
        var rating = reviewRatingsByProductId.TryGetValue(productId, out var selectedRating) ? selectedRating : 0;
        var comment = reviewCommentsByProductId.TryGetValue(productId, out var selectedComment) ? selectedComment : "";

        if (customerId <= 0)
        {
            // Điều hướng NỘI BỘ ⇒ replace (không forceLoad) để giữ circuit Blazor.
            Navigation.NavigateTo("/login", replace: true);
            return;
        }

        if (rating < 1 || rating > 5)
        {
            await JS.InvokeVoidAsync("alert", "Vui lòng chọn số sao (1-5).");
            return;
        }

        reviewSubmittingByProductId.Add(productId);
        try
        {
            var request = new CreateProductReviewRequestDto(detail.OrderId, rating, comment);
            var result = await ReviewClient.UpsertProductReviewAsync(productId, customerId, request);
            if (!result.IsSuccess)
            {
                await JS.InvokeVoidAsync("alert", result.Error ?? "Gửi đánh giá thất bại.");
                return;
            }

            var refreshed = await ReviewClient.GetProductReviewsAsync(productId);
            var myReview = refreshed.Data?.FirstOrDefault(r => r.CustomerId == customerId);
            reviewsByProductId[productId] = myReview;
            reviewRatingsByProductId[productId] = myReview?.Rating is > 0 ? myReview.Rating : rating;
            reviewCommentsByProductId[productId] = myReview?.Comment ?? comment;
        }
        finally
        {
            reviewSubmittingByProductId.Remove(productId);
        }
    }

    private void ClearReviewState()
    {
        reviewsLoading = false;
        reviewsByProductId.Clear();
        reviewRatingsByProductId.Clear();
        reviewCommentsByProductId.Clear();
        reviewSubmittingByProductId.Clear();
    }

    private void SetReviewRating(int productId, int rating) => reviewRatingsByProductId[productId] = rating;

    private void SetReviewComment(int productId, string comment) => reviewCommentsByProductId[productId] = comment;

    private void OpenOrderOnKey(KeyboardEventArgs e, int orderId)
    {
        if (e.Key is "Enter" or " ")
        {
            NavigateToOrder(orderId);
        }
    }

    private void NavigateToOrder(int orderId) => Navigation.NavigateTo($"/account/orders/{orderId}");

    private void GoHome() => Navigation.NavigateTo("/");

    private int CountForTab(string tabId) => orders.Count(o => MatchesOrderTab(tabId, o));

    private static IEnumerable<FrontendOrderDetailItem> DistinctItems(IReadOnlyList<FrontendOrderDetailItem> items) =>
        items.GroupBy(i => i.ProductId).Select(g => g.First());

    private static bool MatchesOrderTab(string tabId, FrontendOrderSummary order)
    {
        var status = NormalizeVietnamese(order.Status);
        var payStatus = NormalizeVietnamese(order.PaymentStatus);

        return tabId switch
        {
            "all" => true,
            "cancel" => status.Contains("huy"),
            "done" => status.Contains("hoan thanh") || status.Contains("da nhan") || status.Contains("hoan tat") || status.Contains("completed"),
            "unpaid" => payStatus.Contains("chua thanh toan") || payStatus.Contains("cho thanh toan") || payStatus == "pending" || status.Contains("cho thanh toan") || status.Contains("chua thanh toan"),
            "pending_confirm" => status.Contains("cho xac nhan"),
            "shipping" => status.Contains("cho giao hang"),
            _ => true,
        };
    }

    private static bool OrderMatchesSearch(FrontendOrderSummary order, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var q = NormalizeVietnamese(query);
        return NormalizeVietnamese(order.OrderId.ToString(CultureInfo.InvariantCulture)).Contains(q)
            || NormalizeVietnamese(order.Status).Contains(q)
            || NormalizeVietnamese(order.PaymentMethod).Contains(q)
            || NormalizeVietnamese(order.PaymentStatus).Contains(q)
            || NormalizeVietnamese(PrimaryName(order)).Contains(q)
            || NormalizeVietnamese(order.OrderDate.ToString("g", new CultureInfo("vi-VN"))).Contains(q);
    }

    private static string ShipHint(string? status)
    {
        var s = NormalizeVietnamese(status);
        if (s.Contains("huy")) return "Đơn đã hủy";
        if (s.Contains("hoan thanh") || s.Contains("da nhan") || s.Contains("hoan tat")) return "Giao hàng thành công";
        if (s.Contains("cho giao")) return "Đang giao đến bạn";
        if (s.Contains("cho xac nhan")) return "Đang chờ shop xác nhận";
        return "Đang xử lý";
    }

    private static bool CanMarkDelivered(string? status) => NormalizeVietnamese(status).Trim() == "cho giao hang";

    private static bool IsOrderCompleted(string? status)
    {
        var normalized = NormalizeVietnamese(status).Trim();
        return normalized == "hoan thanh" || normalized == "completed" || normalized.Contains("da nhan") || normalized.Contains("hoan tat");
    }

    private static string StatusClass(string? status)
    {
        var normalized = NormalizeVietnamese(status);
        if (normalized.Contains("huy")) return "is-cancel";
        if (IsOrderCompleted(status)) return "is-paid";
        return "";
    }

    private static string PrimaryName(FrontendOrderSummary order) =>
        string.IsNullOrWhiteSpace(order.PrimaryProductName) ? "Sản phẩm" : order.PrimaryProductName!;

    private static int LineCount(FrontendOrderSummary order) => order.ProductLineCount > 0 ? order.ProductLineCount : 1;

    private static string FormatDateShort(DateTime value) => value.ToString("dd/MM/yyyy HH:mm", new CultureInfo("vi-VN"));

    private static string FormatDate(DateTime value) => value.ToString("g", new CultureInfo("vi-VN"));

    private static string Vnd(decimal amount) =>
        PetShop.Components.Shared.Format.Vnd(amount);

    private static string NormalizeVietnamese(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        return sb.ToString().Replace("đ", "d");
    }
    }
}
