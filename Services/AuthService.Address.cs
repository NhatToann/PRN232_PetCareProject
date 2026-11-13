using PetShop.Interfaces;

namespace PetShop.Services
{
    /// <summary>
    /// Phần logic địa chỉ giao hàng của khách hàng — tách riêng để giữ
    /// <see cref="AuthService"/> dưới ngưỡng độ dài và gom validate về một chỗ.
    ///
    /// Quy ước về giá trị <c>address_Customer</c>:
    /// - <c>null</c> hoặc chuỗi rỗng ⇒ xoá địa chỉ (khách chưa cung cấp).
    /// - Chuỗi dài hơn 255 ký tự ⇒ bị từ chối (khớp <c>NVARCHAR(255)</c> của cột DB).
    ///
    /// <para>
    /// <b>VÌ SAO CẦN:</b> địa chỉ giao hàng nhập ở trang checkout trước đây chỉ
    /// nằm trong RAM của component ⇒ mất trắng khi đóng tab, và không bao giờ
    /// được ghi xuống DB ⇒ lần đặt hàng sau khách phải nhập lại từ đầu.
    /// Nay địa chỉ được ghi vào <c>Customer.address_Customer</c> (đã có sẵn trong
    /// schema, **không** cần thêm cột) và được đọc lại để điền sẵn form.
    /// </para>
    /// </summary>
    public sealed partial class AuthService
    {
        /// <summary>Độ dài tối đa của địa chỉ — khớp cột <c>Customer.address_Customer NVARCHAR(255)</c>.</summary>
        public const int MaxAddressLength = 255;

        /// <inheritdoc />
        public async Task<bool> UpdateAddressAsync(int customerId, string? address, CancellationToken ct = default)
        {
            if (customerId <= 0)
            {
                return false;
            }

            var normalized = NormalizeAddress(address);

            return await _authRepository.UpdateCustomerAddressAsync(customerId, normalized, ct);
        }

        /// <inheritdoc />
        public Task<string?> GetAddressAsync(int customerId, CancellationToken ct = default)
        {
            return _authRepository.GetCustomerAddressAsync(customerId, ct);
        }

        /// <inheritdoc />
        public async Task<bool> UpdateAddressWithPhoneAsync(
            int customerId,
            string? address,
            string? phone,
            CancellationToken ct = default)
        {
            if (customerId <= 0)
            {
                return false;
            }

            var normalizedAddress = NormalizeAddress(address);
            var normalizedPhone = NormalizePhone(phone);

            // Cập nhật địa chỉ trước — nếu khách hàng không tồn tại thì dừng luôn.
            var updated = await _authRepository.UpdateCustomerAddressAsync(customerId, normalizedAddress, ct);
            if (!updated)
            {
                return false;
            }

            // Phone = null/rỗng ⇒ KHÔNG ghi đè số điện thoại đang có.
            if (normalizedPhone is not null)
            {
                await _authRepository.UpdateCustomerPhoneAsync(customerId, normalizedPhone, ct);
            }

            return true;
        }

        /// <inheritdoc />
        public Task<string?> GetPhoneAsync(int customerId, CancellationToken ct = default)
        {
            return _authRepository.GetCustomerPhoneAsync(customerId, ct);
        }

        /// <summary>
        /// Chuẩn hoá + kiểm tra địa chỉ trước khi ghi xuống DB.
        /// Ném <see cref="ArgumentException"/> với thông báo tiếng Việt nếu giá trị không hợp lệ.
        /// </summary>
        private static string? NormalizeAddress(string? address)
        {
            // null / rỗng ⇒ xoá địa chỉ.
            if (string.IsNullOrWhiteSpace(address))
            {
                return null;
            }

            var trimmed = address.Trim();

            if (trimmed.Length > MaxAddressLength)
            {
                throw new ArgumentException(
                    $"Địa chỉ không được vượt quá {MaxAddressLength} ký tự.");
            }

            return trimmed;
        }

        /// <summary>
        /// Chuẩn hoá số điện thoại (bỏ khoảng trắng thừa).
        /// Chuỗi rỗng ⇒ trả về <c>null</c> (không ghi đè số điện thoại đang có).
        /// </summary>
        private static string? NormalizePhone(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone))
            {
                return null;
            }

            var trimmed = phone.Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }
    }
}
