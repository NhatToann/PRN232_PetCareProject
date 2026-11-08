using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using PetShop.Models.Validation;

namespace PetShop.Models
{
    /// <summary>
    /// SL-05 / SL-11 — DTO demo cho custom validation attribute và <c>[ValidateNever]</c>
    /// (slide PRN232 Ch05 S27–S28).
    ///
    /// <para>
    /// <c>StockQuantity</c> dùng custom attribute <see cref="PositiveQuantityAttribute"/>
    /// (kế thừa <c>ValidationAttribute</c>, override <c>IsValid</c>) để bắt lỗi khi
    /// số lượng ≤ 0.
    /// </para>
    ///
    /// <para>
    /// <c>AuditNote</c> gắn <see cref="ValidateNeverAttribute"/> — field do server tự điền
    /// (ví dụ ghi chú hệ thống) nên bị loại khỏi validation, không bắt buộc client gửi.
    /// </para>
    /// </summary>
    public sealed class ProductValidationDemoRequest
    {
        /// <summary>Tên sản phẩm — bắt buộc, tối đa 200 ký tự.</summary>
        [Required(ErrorMessage = "Tên sản phẩm là bắt buộc.")]
        [StringLength(200, ErrorMessage = "Tên sản phẩm không được vượt quá 200 ký tự.")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Số lượng tồn kho — dùng custom validation attribute (SL-05).
        /// </summary>
        [PositiveQuantity("Số lượng tồn kho phải lớn hơn 0.")]
        public int StockQuantity { get; set; }

        /// <summary>Giá bán — không được âm.</summary>
        [Range(0, double.MaxValue, ErrorMessage = "Giá bán không được nhỏ hơn 0.")]
        public decimal Price { get; set; }

        /// <summary>
        /// Ghi chú kiểm toán do server tự điền — <c>[ValidateNever]</c> nên không bị validate.
        /// </summary>
        [ValidateNever]
        public string? AuditNote { get; set; }
    }

    /// <summary>
    /// Kết quả kiểm tra validation (SL-13) — nội dung thông báo lỗi tiếng Việt.
    /// </summary>
    public sealed class ValidationDemoResult
    {
        /// <summary>Model có hợp lệ sau khi chạy lại validation không.</summary>
        public bool IsValid { get; set; }

        /// <summary>Danh sách lỗi sau khi chạy lại validation.</summary>
        public List<string> Errors { get; set; } = new();
    }
}
