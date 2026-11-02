using System.Globalization;
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
    /// Pháº§n code-behind cá»§a <c>ServiceDetail.razor</c>.
    /// TĂ¡ch riĂªng Ä‘á»ƒ file .razor khĂ´ng vÆ°á»£t quĂ¡ 500 dĂ²ng (quy Æ°á»›c trong AGENTS.md Â§2.3).
    /// </summary>
    public partial class ServiceDetail
    {    private const string PlaceholderImage = "/images/placeholder.jpg";

    private readonly Dictionary<string, string[]> serviceImageMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["spa cao cấp"] = new[] { "/images/spa-caocap1.jpg", "/images/spa-caocap2.jpg", "/images/spa-caocap3.jpg" },
        ["tắm + vệ sinh cơ bản"] = new[] { "/images/bath1.png", "/images/bath2.jpg", "/images/bath3.jpg" },
        ["cắt tỉa lông chuyên nghiệp"] = new[] { "/images/cut1.jpg", "/images/cut2.jpg", "/images/cut3.jpg" },
        ["vệ sinh răng miệng"] = new[] { "/images/teeth1.jpg", "/images/teeth2.jpg", "/images/teeth3.jpg" },
    };

    [Parameter] public int Id { get; set; }

    private PetShop.Models.PetServiceDetailDto? detail;
    private bool isLoading = true;
    private bool notFound;
    private string? error;
    private int selectedImageIndex;

    private bool reviewEligible;
    private int? reviewBookingId;
    private bool reviewEligibilityPending;
    private int reviewRating = 5;
    private int reviewHover;
    private string reviewComment = "";
    private string? reviewError;
    private string? reviewSuccess;
    private bool reviewSubmitting;

    protected override async Task OnParametersSetAsync()
    {
        isLoading = true;
        notFound = false;
        error = null;
        detail = null;
        selectedImageIndex = 0;
        ResetReviewState();
        reviewEligibilityPending = false;

        if (Id <= 0)
        {
            notFound = true;
            isLoading = false;
            return;
        }

        var result = await PetServiceClient.GetDetailAsync(Id);
        if (!result.IsSuccess || result.Data is null)
        {
            if (result.StatusCode == 404)
            {
                notFound = true;
            }
            else
            {
                error = result.Error ?? "Không tải được chi tiết dịch vụ.";
            }
        }
        else
        {
            detail = result.Data;
            reviewEligibilityPending = true;
        }

        isLoading = false;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!reviewEligibilityPending || detail?.Service is null)
        {
            return;
        }

        reviewEligibilityPending = false;
        try
        {
            await UserSession.LoadAsync();
            await CheckReviewEligibilityAsync();
            StateHasChanged();
        }
        catch (Exception ex)
        {
            Logger.LineWarning($"[ServiceDetail.razor] OnAfterRenderAsync threw: {ex.GetType().Name} {ex.Message}");
        }
    }

    private void ResetReviewState()
    {
        reviewEligible = false;
        reviewBookingId = null;
        reviewEligibilityPending = false;
        reviewRating = 5;
        reviewHover = 0;
        reviewComment = "";
        reviewError = null;
        reviewSuccess = null;
        reviewSubmitting = false;
    }

    private async Task CheckReviewEligibilityAsync()
    {
        reviewEligible = false;
        reviewBookingId = null;
        var customerId = UserSession.CurrentUser?.UserId ?? 0;
        if (customerId <= 0 || detail?.Service is null)
        {
            return;
        }

        try
        {
            var history = await SpaClient.GetHistoryAsync(customerId);
            if (!history.IsSuccess || history.Data is null)
            {
                return;
            }

            var serviceId = detail.Service.ServiceId;
            var eligible = history.Data
                .Where(b => b.Items?.Any(i => i.ServiceId == serviceId) == true && IsReviewEligibleStatus(b.Status))
                .OrderByDescending(b => b.AppointmentStart)
                .ThenByDescending(b => b.CreatedAt ?? DateTime.MinValue)
                .FirstOrDefault();
            if (eligible is not null)
            {
                reviewEligible = true;
                reviewBookingId = eligible.BookingId;
            }
        }
        catch (Exception ex)
        {
            Logger.LineWarning($"[ServiceDetail.razor] CheckReviewEligibilityAsync threw: {ex.GetType().Name} {ex.Message}");
        }
    }

    private static bool IsReviewEligibleStatus(string? status)
    {
        var s = (status ?? string.Empty).Trim().ToLowerInvariant();
        return s == "hoàn thành"
            || s == "completed"
            || s == "đã thanh toán"
            || s == "chờ xác nhận"
            || s == "đã xác nhận";
    }

    private async Task SubmitReviewAsync()
    {
        var customerId = UserSession.CurrentUser?.UserId ?? 0;
        if (!reviewEligible || reviewBookingId is null || customerId <= 0 || detail?.Service is null)
        {
            reviewError = "Bạn chưa đủ điều kiện để đánh giá dịch vụ này.";
            return;
        }

        if (reviewRating < 1 || reviewRating > 5)
        {
            reviewError = "Vui lòng chọn số sao từ 1 đến 5.";
            return;
        }

        reviewSubmitting = true;
        reviewError = null;
        reviewSuccess = null;

        try
        {
            var req = new PetShop.Models.CreateSpaReviewRequest(
                reviewBookingId.Value,
                detail.Service.ServiceId,
                reviewRating,
                reviewComment ?? "");
            var result = await SpaClient.SubmitReviewAsync(customerId, req);
            if (result.IsSuccess)
            {
                reviewSuccess = "Cảm ơn bạn! Đánh giá đã được ghi nhận.";
                reviewComment = "";
                var refreshed = await PetServiceClient.GetDetailAsync(Id);
                if (refreshed.IsSuccess && refreshed.Data is not null)
                {
                    detail = refreshed.Data;
                }
            }
            else
            {
                reviewError = result.Error ?? "Không thể gửi đánh giá.";
            }
        }
        catch (Exception ex)
        {
            reviewError = ex.Message;
        }
        finally
        {
            reviewSubmitting = false;
        }
    }

    private string[] GetServiceImages(string? serviceName)
    {
        var name = (serviceName ?? string.Empty).Trim();
        return serviceImageMap.TryGetValue(name, out var images)
            ? images
            : new[] { PlaceholderImage };
    }

    private void PrevImage()
    {
        var images = GetServiceImages(detail?.Service?.Name);
        selectedImageIndex = (selectedImageIndex - 1 + images.Length) % images.Length;
    }

    private void NextImage()
    {
        var images = GetServiceImages(detail?.Service?.Name);
        selectedImageIndex = (selectedImageIndex + 1) % images.Length;
    }

    private async Task AddToCart()
    {
        if (!UserSession.IsAuthenticated)
        {
            await JS.InvokeVoidAsync("alert", "Bạn cần đăng nhập để thêm dịch vụ vào giỏ spa.");
            Navigation.NavigateTo("/login");
            return;
        }

        if (detail?.Service is null)
        {
            return;
        }

        await JS.InvokeAsync<int[]>("spaCart.add", detail.Service.ServiceId);
        await JS.InvokeVoidAsync("alert", "Thêm dịch vụ vào giỏ spa thành công.");
    }

    private async Task BookNow()
    {
        if (!UserSession.IsAuthenticated)
        {
            await JS.InvokeVoidAsync("alert", "Bạn cần đăng nhập để đặt dịch vụ.");
            Navigation.NavigateTo("/login");
            return;
        }

        if (detail?.Service is null)
        {
            return;
        }

        await JS.InvokeAsync<int[]>("spaCart.add", detail.Service.ServiceId);
        Navigation.NavigateTo($"/spa-cart?serviceId={Id}");
    }

    private static MarkupString RenderStars(int rating)
    {
        var safe = Math.Clamp(rating, 0, 5);
        var html = new System.Text.StringBuilder("<span class=\"pd-stars\" aria-label=\"");
        html.Append(safe);
        html.Append(" sao\">");
        for (var i = 0; i < 5; i++)
        {
            html.Append(i < safe ? "<span class=\"on\">★</span>" : "<span>★</span>");
        }
        html.Append("</span>");
        return new MarkupString(html.ToString());
    }

    private static string Vnd(decimal amount) =>
        string.Format(new CultureInfo("vi-VN"), "{0:N0} ₫", amount);

    private static string FormatDateTime(DateTime value) =>
        value.ToString("g", new CultureInfo("vi-VN"));
    }
}
