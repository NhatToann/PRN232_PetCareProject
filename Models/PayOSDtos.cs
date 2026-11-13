using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

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

    /// <summary>
    /// Envelope webhook PayOS.
    ///
    /// <para>
    /// Theo tài liệu PayOS: <c>{ code, desc, success, data, signature }</c>.
    /// </para>
    /// </summary>
    public sealed record PayOSWebhookPayload(
        bool? Success,
        [property: JsonConverter(typeof(FlexibleStringConverter))] string? Code,
        [property: JsonConverter(typeof(FlexibleStringConverter))] string? Desc,
        PayOSWebhookData? Data);

    /// <summary>
    /// Object <c>data</c> của webhook PayOS.
    ///
    /// <para>
    /// <b>Lưu ý:</b> mẫu webhook chính thức <b>KHÔNG</b> có field <c>status</c> —
    /// trạng thái thành công được suy ra từ <c>success = true</c> và
    /// <c>data.code = "00"</c>. Field <c>status</c> vẫn được giữ để tương thích
    /// với các biến thể webhook (ví dụ webhook hủy/timeout do hệ thống gửi thêm).
    /// </para>
    ///
    /// <para>
    /// <b>Lỗi đã sửa:</b> <c>Amount</c> trước đây khai báo <c>int?</c> ⇒ webhook có
    /// amount dạng thập phân (vd <c>15.99</c>) làm <c>JsonException</c> ⇒
    /// <c>HandleWebhookAsync</c> trả "Invalid payload" ⇒ thanh toán thành công
    /// KHÔNG được ghi vào DB. Đổi sang <c>decimal?</c> (nhận được cả số nguyên
    /// VND lẫn số thập phân).
    /// </para>
    /// </summary>
    public sealed record PayOSWebhookData(
        int OrderCode,
        [property: JsonConverter(typeof(FlexibleStringConverter))] string? Status,
        decimal? Amount,
        string? Description,
        [property: JsonConverter(typeof(FlexibleStringConverter))] string? Code,
        [property: JsonConverter(typeof(FlexibleStringConverter))] string? Desc,
        string? Reference,
        string? PaymentLinkId);

    public sealed record PayOSPaymentStatusResult(int OrderCode, string Status, bool IsPaid, int Amount, string? CheckoutUrl);
}
