using System.ComponentModel.DataAnnotations;

namespace PetShop.Models
{
    public sealed record ReviewDisplayDto(
        int ReviewId,
        int ServiceId,
        int Rating,
        string? Comment,
        int? BookingId,
        int? CustomerId,
        string? CustomerName,
        DateTime CreatedAt
    );

    public sealed record CreateProductReviewRequestDto(
        [Range(1, int.MaxValue, ErrorMessage = "Mã đơn hàng không hợp lệ.")]
        int OrderId,

        [Range(1, 5, ErrorMessage = "Đánh giá phải từ 1 đến 5 sao.")]
        int Rating,

        [StringLength(1000, ErrorMessage = "Nhận xét không được vượt quá 1000 ký tự.")]
        string? Comment
    );

    public sealed record ProductReviewUpsertResult(
        bool Success,
        string Message
    );
}
