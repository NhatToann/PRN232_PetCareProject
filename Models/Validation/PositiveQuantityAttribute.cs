using System.ComponentModel.DataAnnotations;

namespace PetShop.Models.Validation
{
    /// <summary>
    /// SL-05 — Custom validation attribute (slide PRN232 Ch05 S28).
    ///
    /// <para>
    /// Slide yêu cầu: tạo một class kế thừa <see cref="ValidationAttribute"/> và override
    /// phương thức <c>IsValid(object value, ValidationContext validationContext)</c>.
    /// </para>
    ///
    /// <para>
    /// Attribute này kiểm tra số lượng tồn kho phải lớn hơn 0 — dùng cho field demo
    /// <c>StockQuantity</c> của DTO sản phẩm. Thông báo lỗi viết bằng tiếng Việt.
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter,
        AllowMultiple = false, Inherited = true)]
    public sealed class PositiveQuantityAttribute : ValidationAttribute
    {
        /// <summary>Thông báo mặc định khi không truyền message riêng.</summary>
        public const string DefaultErrorMessage = "Số lượng tồn kho phải lớn hơn 0.";

        public PositiveQuantityAttribute()
            : base(DefaultErrorMessage)
        {
        }

        public PositiveQuantityAttribute(string errorMessage)
            : base(errorMessage)
        {
        }

        /// <summary>
        /// Override <c>IsValid(object, ValidationContext)</c> — đúng yêu cầu của slide.
        /// </summary>
        /// <param name="value">Giá trị của property cần kiểm tra.</param>
        /// <param name="validationContext">Ngữ cảnh chứa model đã được model binding.</param>
        /// <returns><c>true</c> nếu hợp lệ; ngược lại trả về <see cref="ValidationResult"/>.</returns>
        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            // Giá trị null được xử lý bởi [Required] (nếu có) — không coi là lỗi của attribute này.
            if (value is null)
            {
                return ValidationResult.Success;
            }

            if (!TryConvertToInt(value, out var quantity))
            {
                return new ValidationResult(
                    "Số lượng tồn kho phải là số nguyên.",
                    validationContext.MemberName != null
                        ? new[] { validationContext.MemberName }
                        : null);
            }

            if (quantity <= 0)
            {
                return new ValidationResult(
                    ErrorMessage ?? DefaultErrorMessage,
                    validationContext.MemberName != null
                        ? new[] { validationContext.MemberName }
                        : null);
            }

            return ValidationResult.Success;
        }

        /// <summary>
        /// Chuyển giá trị sang số nguyên. Chấp nhận cả kiểu số nguyên lẫn chuỗi số
        /// (trường hợp model binding từ form/query-string).
        /// </summary>
        private static bool TryConvertToInt(object value, out int quantity)
        {
            switch (value)
            {
                case int i:
                    quantity = i;
                    return true;
                case long l when l >= int.MinValue && l <= int.MaxValue:
                    quantity = (int)l;
                    return true;
                case short s:
                    quantity = s;
                    return true;
                case decimal d when d == decimal.Truncate(d):
                    quantity = (int)d;
                    return true;
                case double dbl when dbl == Math.Truncate(dbl):
                    quantity = (int)dbl;
                    return true;
                case string text when int.TryParse(text.Trim(), out var parsed):
                    quantity = parsed;
                    return true;
                default:
                    quantity = 0;
                    return false;
            }
        }
    }
}
