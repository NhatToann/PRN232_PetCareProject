using System.ComponentModel.DataAnnotations;

namespace PetShop.Models
{
    public sealed record CreatePayOSPaymentRequest(
        [Range(1, int.MaxValue, ErrorMessage = "Mã đơn hàng không hợp lệ.")]
        int OrderCode,

        [Range(1, double.MaxValue, ErrorMessage = "Số tiền thanh toán không hợp lệ.")]
        decimal Amount,

        [StringLength(255, ErrorMessage = "Mô tả không được vượt quá 255 ký tự.")]
        string Description,

        [Required(ErrorMessage = "URL trả về không được để trống.")]
        string ReturnUrl,

        [Required(ErrorMessage = "URL huỷ không được để trống.")]
        string CancelUrl
    );

    public sealed record PayOSCheckoutResult(string CheckoutUrl, int OrderCode, decimal Amount, string Description);

    public sealed record PayOSWebhookResult(bool Success, string Message);

    public sealed record PayOSWebhookPayload(bool? Success, string? Code, PayOSWebhookData? Data);

    public sealed record PayOSWebhookData(int OrderCode, string? Status);

    public sealed record PayOSPaymentStatusResult(int OrderCode, string Status, bool IsPaid, int Amount, string? CheckoutUrl);
}
