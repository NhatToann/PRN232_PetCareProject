using PetShop.Interfaces;

namespace PetShop.Services
{
    /// <summary>
    /// Phần logic ngày sinh của khách hàng — tách riêng để giữ
    /// <see cref="AuthService"/> dưới ngưỡng độ dài và để tập trung validate.
    ///
    /// Quy ước về giá trị <c>birth_date</c>:
    /// - <c>null</c> ⇒ xoá ngày sinh (khách hàng chưa cung cấp).
    /// - Ngày trong tương lai ⇒ bị từ chối (ngày sinh không thể nằm ở tương lai).
    /// - Ngày trước <c>1900-01-01</c> ⇒ bị từ chối (nằm ngoài phạm vi hợp lý).
    ///
    /// Cột DB là kiểu <c>date</c> ⇒ chỉ giữ phần ngày, khớp với định dạng
    /// <c>dd/MM/yyyy</c> mà UI hiển thị.
    /// </summary>
    public sealed partial class AuthService
    {
        /// <summary>Ngày sinh tối thiểu được chấp nhận — <c>1900-01-01</c>.</summary>
        public static readonly DateTime MinBirthDate = new(1900, 1, 1);

        /// <inheritdoc />
        public async Task<bool> UpdateBirthDateAsync(int customerId, DateTime? birthDate, CancellationToken ct = default)
        {
            if (customerId <= 0)
            {
                return false;
            }

            var normalized = NormalizeBirthDate(birthDate);

            return await _authRepository.UpdateCustomerBirthDateAsync(customerId, normalized, ct);
        }

        /// <inheritdoc />
        public Task<DateTime?> GetBirthDateAsync(int customerId, CancellationToken ct = default)
        {
            return _authRepository.GetCustomerBirthDateAsync(customerId, ct);
        }

        /// <summary>
        /// Chuẩn hoá + kiểm tra ngày sinh trước khi ghi xuống DB.
        /// Ném <see cref="ArgumentException"/> với thông báo tiếng Việt nếu giá trị không hợp lệ.
        /// </summary>
        private static DateTime? NormalizeBirthDate(DateTime? birthDate)
        {
            // null ⇒ xoá ngày sinh.
            if (birthDate is null)
            {
                return null;
            }

            // Cột DB là kiểu `date` ⇒ bỏ phần giờ/phút/giây.
            var value = birthDate.Value.Date;

            if (value > DateTime.UtcNow.Date)
            {
                throw new ArgumentException("Ngày sinh không được ở tương lai.");
            }

            if (value < MinBirthDate)
            {
                throw new ArgumentException("Ngày sinh không hợp lệ.");
            }

            return value;
        }
    }
}
