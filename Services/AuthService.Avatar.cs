using PetShop.Interfaces;

namespace PetShop.Services
{
    /// <summary>
    /// Phần logic ảnh đại diện của khách hàng — tách riêng để giữ
    /// <see cref="AuthService"/> dưới ngưỡng độ dài và để tập trung validate.
    ///
    /// Quy ước về giá trị <c>avatar_url</c>:
    /// - <c>null</c> ⇒ xoá ảnh đại diện (UI fallback về chữ cái đầu).
    /// - Chuỗi bắt đầu bằng <c>data:image/</c> ⇒ ảnh nhúng base64 (upload từ máy).
    /// - Chuỗi bắt đầu bằng <c>http://</c> hoặc <c>https://</c> ⇒ ảnh từ URL.
    ///
    /// Mọi giá trị khác đều bị từ chối để chặn payload nguy hiểm
    /// (ví dụ <c>javascript:alert(1)</c>) và giới hạn độ dài 500 ký tự
    /// khớp với <c>NVARCHAR(500)</c> của cột DB.
    /// </summary>
    public sealed partial class AuthService
    {
        /// <summary>Độ dài tối đa của avatar_url — khớp cột <c>Customer.avatar_url NVARCHAR(500)</c>.</summary>
        public const int MaxAvatarUrlLength = 500;

        /// <inheritdoc />
        public async Task<bool> UpdateAvatarAsync(int customerId, string? avatarUrl, CancellationToken ct = default)
        {
            if (customerId <= 0)
            {
                return false;
            }

            var normalized = NormalizeAvatarUrl(avatarUrl);

            return await _authRepository.UpdateCustomerAvatarAsync(customerId, normalized, ct);
        }

        /// <inheritdoc />
        public Task<string?> GetAvatarAsync(int customerId, CancellationToken ct = default)
        {
            return _authRepository.GetCustomerAvatarAsync(customerId, ct);
        }

        /// <summary>
        /// Chuẩn hoá + kiểm tra giá trị avatar trước khi ghi xuống DB.
        /// Ném <see cref="ArgumentException"/> với thông báo tiếng Việt nếu giá trị không hợp lệ.
        /// </summary>
        private static string? NormalizeAvatarUrl(string? avatarUrl)
        {
            // null / rỗng ⇒ xoá avatar.
            if (string.IsNullOrWhiteSpace(avatarUrl))
            {
                return null;
            }

            var trimmed = avatarUrl.Trim();

            if (trimmed.Length > MaxAvatarUrlLength)
            {
                throw new ArgumentException(
                    $"Đường dẫn ảnh đại diện không được vượt quá {MaxAvatarUrlLength} ký tự.");
            }

            if (trimmed.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            throw new ArgumentException(
                "Ảnh đại diện phải là dữ liệu ảnh (data:image/...) hoặc đường dẫn http(s).");
        }
    }
}
