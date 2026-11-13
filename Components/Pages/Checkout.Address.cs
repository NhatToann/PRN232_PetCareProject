using System.Text.Json;
using Microsoft.JSInterop;
using PetShop.Services;
using PetShop_PRN222.Components.Checkout;

namespace PetShop_PRN222.Components.Pages
{
    /// <summary>
    /// Phần quản lý địa chỉ giao hàng của <c>Checkout.razor</c> — tách riêng để
    /// giữ file chính dưới ngưỡng 500 dòng (quy ước AGENTS.md §2.3).
    ///
    /// <para>
    /// <b>VẤN ĐỀ ĐÃ SỬA:</b> trước đây danh sách địa chỉ chỉ nằm trong RAM của
    /// component ⇒ đóng tab là mất, đặt hàng xong cũng mất, lần sau phải nhập lại
    /// từ đầu. Nay địa chỉ được lưu ở <b>3 tầng</b>:
    /// </para>
    /// <list type="number">
    /// <item><b>localStorage <c>savedDeliveryAddresses</c></b> — danh sách địa chỉ
    /// (port đúng hành vi nguồn OJT <c>CheckoutPage.jsx</c>).</item>
    /// <item><b>DB — cột <c>Customer.address_Customer</c></b> — địa chỉ mặc định /
    /// địa chỉ dùng lần đặt hàng gần nhất, để sống sót khi xoá localStorage hoặc
    /// đăng nhập trên máy/tab khác. Cột này <b>đã có sẵn</b> trong schema ⇒
    /// không cần thêm cột mới.</item>
    /// <item><b>auto-save khi đặt hàng</b> — nếu địa chỉ vừa nhập chưa có trong
    /// danh sách thì tự động thêm vào (port hành vi
    /// <c>if (existingIndex === -1) saveAddress(addressData)</c> của nguồn).</item>
    /// </list>
    /// </summary>
    public partial class Checkout
    {
        /// <summary>Key localStorage lưu danh sách địa chỉ giao hàng (giống nguồn OJT).</summary>
        private const string SavedAddressesStorageKey = "savedDeliveryAddresses";

        /// <summary>
        /// Option JSON cho danh sách địa chỉ: camelCase (khớp nguồn OJT) và
        /// case-insensitive khi đọc (đọc được cả list do bản cũ ghi).
        /// </summary>
        private static readonly JsonSerializerOptions SavedAddressesJsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        /// <summary>
        /// Nạp danh sách địa chỉ đã lưu (localStorage → DB) và điền sẵn form.
        ///
        /// <para>
        /// Thứ tự ưu tiên: localStorage (nhiều địa chỉ) → DB <c>address_Customer</c>
        /// (một địa chỉ) → rỗng. Nếu tìm được địa chỉ mặc định thì đổ vào form
        /// thanh toán để khách không phải nhập lại.
        /// </para>
        /// </summary>
        private async Task LoadSavedAddressesAsync()
        {
            try
            {
                var raw = await JS.InvokeAsync<string?>("localStorage.getItem", SavedAddressesStorageKey);
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    var list = JsonSerializer.Deserialize<List<SavedAddress>>(raw, SavedAddressesJsonOptions);
                    if (list is { Count: > 0 })
                    {
                        savedAddresses = list;
                        ApplyDefaultAddressToForm();
                        return;
                    }
                }
            }
            catch (JsonException ex)
            {
                Logger.LineWarning($"[Checkout] Không đọc được danh sách địa chỉ trong localStorage: {ex.Message}");
            }
            catch (Exception ex)
            {
                Logger.LineWarning($"[Checkout] LoadSavedAddressesAsync (localStorage) lỗi: {ex.GetType().Name} {ex.Message}");
            }

            // Fallback: đọc địa chỉ mặc định từ DB (sống sót khi xoá localStorage).
            await LoadDefaultAddressFromServerAsync();
        }

        /// <summary>
        /// Đọc địa chỉ mặc định từ DB (<c>Customer.address_Customer</c>) và dựng
        /// thành một mục trong danh sách địa chỉ đã lưu.
        ///
        /// <para>
        /// Số điện thoại đọc kèm từ <c>Customer.phone</c> vì cột địa chỉ không
        /// chứa số điện thoại; thiếu nó thì form thanh toán không hợp lệ ⇒ giao diện
        /// hiện "Chưa có địa chỉ giao hàng" dù DB có địa chỉ.
        /// </para>
        /// </summary>
        private async Task LoadDefaultAddressFromServerAsync()
        {
            try
            {
                var result = await AuthClient.GetAddressAsync();
                var address = result.IsSuccess ? result.Data : null;
                if (string.IsNullOrWhiteSpace(address))
                {
                    return;
                }

                var parsed = ParseFullAddress(address!);
                if (parsed is null)
                {
                    Logger.LineWarning($"[Checkout] Không parse được địa chỉ từ server: {address}");
                    return;
                }

                // Số điện thoại: ưu tiên DB, fallback về số đã có trong form.
                var phoneResult = await AuthClient.GetPhoneAsync();
                var phoneFromDb = phoneResult.IsSuccess ? phoneResult.Data : null;
                var phone = !string.IsNullOrWhiteSpace(phoneFromDb)
                    ? phoneFromDb!
                    : (string.IsNullOrWhiteSpace(form.Phone) ? null : form.Phone);

                var created = parsed with
                {
                    Id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    IsDefault = true,
                    Phone = phone ?? string.Empty,
                };

                savedAddresses = new List<SavedAddress> { created };
                selectedSavedId = created.Id;
                ApplyAddressToForm(created);

                // Ghi luôn xuống localStorage ⇒ lần sau mở checkout không phải gọi
                // API lại, và địa chỉ vẫn còn khi offline / server lỗi.
                await PersistSavedAddressesAsync();
            }
            catch (Exception ex)
            {
                Logger.LineWarning($"[Checkout] Không đọc được địa chỉ mặc định từ server: {ex.GetType().Name} {ex.Message}");
            }
        }

        /// <summary>
        /// Ghi danh sách địa chỉ xuống localStorage.
        /// Lỗi JS interop không được làm hỏng luồng đặt hàng ⇒ bọc try/catch + log.
        /// </summary>
        private async Task PersistSavedAddressesAsync()
        {
            try
            {
                await JS.InvokeVoidAsync(
                    "localStorage.setItem",
                    SavedAddressesStorageKey,
                    JsonSerializer.Serialize(savedAddresses, SavedAddressesJsonOptions));
            }
            catch (Exception ex)
            {
                Logger.LineWarning($"[Checkout] Không lưu được danh sách địa chỉ xuống localStorage: {ex.GetType().Name} {ex.Message}");
            }
        }

        /// <summary>
        /// Đồng bộ địa chỉ giao hàng xuống DB (cột <c>Customer.address_Customer</c>)
        /// kèm số điện thoại (cột <c>Customer.phone</c>).
        ///
        /// <para>
        /// <paramref name="fullAddress"/> = null ⇒ xoá địa chỉ trên DB.
        /// Bọc try/catch — lỗi API không được làm hỏng UI hoặc chặn việc đặt hàng
        /// (giống cách làm với avatar / ngày sinh ở trang hồ sơ).
        /// </para>
        /// </summary>
        private async Task SyncAddressToServerAsync(string? fullAddress, string? phone = null)
        {
            try
            {
                // Client trả về result (không throw) khi API lỗi ⇒ phải kiểm tra
                // tường minh, nếu không lỗi sẽ bị nuốt im lặng và DB không được cập nhật.
                var result = string.IsNullOrWhiteSpace(phone)
                    ? await AuthClient.UpdateAddressAsync(fullAddress)
                    : await AuthClient.UpdateAddressWithPhoneAsync(fullAddress, phone);

                if (result is null || !result.IsSuccess)
                {
                    Logger.LineWarning(
                        $"[Checkout] Đồng bộ địa chỉ xuống server thất bại: " +
                        $"status={result?.StatusCode?.ToString() ?? "null"}, " +
                        $"error={result?.Error ?? "null"}");
                    return;
                }

                Logger.Line($"[Checkout] Đã đồng bộ địa chỉ xuống server: {fullAddress}");
            }
            catch (Exception ex)
            {
                Logger.LineWarning($"[Checkout] Không đồng bộ được địa chỉ xuống server: {ex.GetType().Name} {ex.Message}");
            }
        }

        /// <summary>
        /// Dựng chuỗi địa chỉ đầy đủ (<c>"Số nhà, Phường, Quận, Tỉnh"</c>) từ một mục
        /// trong danh sách địa chỉ đã lưu.
        /// </summary>
        private static string BuildFullAddressFor(SavedAddress addr)
        {
            if (string.IsNullOrWhiteSpace(addr.Address)
                || string.IsNullOrWhiteSpace(addr.Ward)
                || string.IsNullOrWhiteSpace(addr.District)
                || string.IsNullOrWhiteSpace(addr.Province))
            {
                return string.Empty;
            }

            return $"{addr.Address.Trim()}, {addr.Ward.Trim()}, {addr.District.Trim()}, {addr.Province.Trim()}";
        }

        /// <summary>
        /// Bảo đảm địa chỉ đang dùng có trong danh sách địa chỉ đã lưu.
        ///
        /// <para>
        /// Port hành vi nguồn OJT: trong <c>handleSubmit</c> có
        /// <c>if (existingIndex === -1) saveAddress(addressData)</c> ⇒ địa chỉ vừa
        /// nhập ở đơn hàng đầu tiên cũng được <b>tự động lưu</b> cho lần sau.
        /// </para>
        /// </summary>
        private async Task EnsureCurrentAddressSavedAsync()
        {
            if (!HasAddress)
            {
                return;
            }

            var current = BuildAddressFromForm();
            var exists = savedAddresses.Any(a => IsSameAddress(a, current));
            if (exists)
            {
                // Địa chỉ đã có trong danh sách ⇒ chỉ đồng bộ DB để tồn tại qua thiết bị khác.
                await SyncAddressToServerAsync(BuildFullAddress(), form.Phone);
                return;
            }

            var created = current with
            {
                Id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                IsDefault = savedAddresses.Count == 0,
            };

            savedAddresses.Add(created);
            selectedSavedId = created.Id;

            await PersistSavedAddressesAsync();
            await SyncAddressToServerAsync(BuildFullAddress(), form.Phone);
        }

        /// <summary>
        /// Lưu địa chỉ từ modal (port <c>handleAddressModalSave</c>).
        /// Ghi xuống localStorage + đồng bộ DB để không mất.
        /// </summary>
        private async Task SaveAddressAsync(SavedAddress data)
        {
            if (editingAddressId is not null)
            {
                var index = savedAddresses.FindIndex(a => a.Id == editingAddressId.Value);
                if (index >= 0)
                {
                    savedAddresses[index] = data with { Id = editingAddressId.Value, IsDefault = savedAddresses[index].IsDefault };
                }

                selectedSavedId = editingAddressId;
            }
            else
            {
                var created = data with
                {
                    Id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    IsDefault = savedAddresses.Count == 0,
                };
                savedAddresses.Add(created);
                selectedSavedId = created.Id;
            }

            ApplyAddressToForm(data);
            addressModalView = "list";

            await PersistSavedAddressesAsync();
            await SyncAddressToServerAsync(BuildFullAddress(), data.Phone);
        }

        /// <summary>
        /// Xoá một địa chỉ đã lưu (port <c>deleteAddress</c>).
        /// </summary>
        private async Task DeleteAddressAsync(long id)
        {
            var removed = savedAddresses.FirstOrDefault(a => a.Id == id);
            var updated = savedAddresses.Where(a => a.Id != id).ToList();

            // Nguồn: nếu địa chỉ vừa xoá là mặc định thì địa chỉ đầu tiên còn lại thành mặc định.
            if (updated.Count > 0 && removed is not null && removed.IsDefault)
            {
                updated[0] = updated[0] with { IsDefault = true };
            }

            savedAddresses = updated;

            if (selectedSavedId == id)
            {
                selectedSavedId = updated.Count > 0 ? updated[0].Id : null;
                if (updated.Count > 0)
                {
                    ApplyAddressToForm(updated[0]);
                }
                else
                {
                    form.FullName = string.Empty;
                    form.Phone = string.Empty;
                    form.District = string.Empty;
                    form.Ward = string.Empty;
                    form.AddressDetail = string.Empty;
                }
            }

            if (savedAddresses.Count == 0)
            {
                StartAddAddress();
            }
            else
            {
                addressModalView = "list";
                editingAddressId = null;
            }

            await PersistSavedAddressesAsync();

            // Đồng bộ DB: giữ địa chỉ mặc định còn lại, hoặc xoá nếu không còn địa chỉ nào.
            var remainingDefault = savedAddresses.FirstOrDefault(a => a.IsDefault) ?? savedAddresses.FirstOrDefault();
            await SyncAddressToServerAsync(
                remainingDefault is null ? null : BuildFullAddressFor(remainingDefault),
                remainingDefault?.Phone);
        }

        /// <summary>
        /// Điền địa chỉ mặc định (hoặc địa chỉ đầu tiên) vào form thanh toán.
        /// </summary>
        private void ApplyDefaultAddressToForm()
        {
            if (savedAddresses.Count == 0)
            {
                return;
            }

            var target = savedAddresses.FirstOrDefault(a => a.IsDefault) ?? savedAddresses[0];
            selectedSavedId = target.Id;
            ApplyAddressToForm(target);
        }

        /// <summary>Dựng địa chỉ từ form thanh toán hiện tại.</summary>
        private SavedAddress BuildAddressFromForm() => new(
            0,
            form.FullName.Trim(),
            form.Phone.Trim(),
            form.Email.Trim(),
            string.IsNullOrWhiteSpace(form.Province) ? "Đà Nẵng" : form.Province.Trim(),
            form.District.Trim(),
            form.Ward.Trim(),
            form.AddressDetail.Trim(),
            false);

        /// <summary>
        /// Hai địa chỉ được coi là giống nhau khi trùng toàn bộ thông tin nhận hàng
        /// (bỏ qua <c>Id</c> và cờ mặc định).
        /// </summary>
        private static bool IsSameAddress(SavedAddress a, SavedAddress b) =>
            string.Equals(a.FullName, b.FullName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Phone, b.Phone, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.District, b.District, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Ward, b.Ward, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Address, b.Address, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Parse chuỗi địa chỉ đầy đủ đã lưu trong DB theo định dạng
        /// <c>"Số nhà, Phường, Quận, Tỉnh"</c> về lại thành <see cref="SavedAddress"/>.
        ///
        /// Trả về <c>null</c> nếu chuỗi không khớp mẫu (vd địa chỉ cũ chỉ có số nhà).
        /// </summary>
        private SavedAddress? ParseFullAddress(string fullAddress)
        {
            var parts = fullAddress.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 4)
            {
                return null;
            }

            var detail = string.Join(", ", parts[..^3]);
            var ward = parts[^3];
            var district = parts[^2];
            var province = parts[^1];

            // Chỉ nhận địa chỉ có quận/phường hợp lệ ⇒ tránh điền rác vào form.
            if (!DaNangDistricts.Contains(district))
            {
                return null;
            }

            return new SavedAddress(
                0,
                form.FullName,
                form.Phone,
                form.Email,
                province,
                district,
                ward,
                detail,
                false);
        }
    }
}
