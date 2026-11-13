namespace PetShop_PRN222.Components.Pages
{
    /// <summary>
    /// Phần form thanh toán + validate của <c>Checkout.razor</c> — tách riêng để giữ
    /// file chính dưới ngưỡng 500 dòng (quy ước AGENTS.md §2.3).
    ///
    /// Chứa: danh sách quận/phường Đà Nẵng, state của form, validate từng field,
    /// chuẩn hoá key field giữa form thanh toán và modal địa chỉ.
    /// </summary>
    public partial class Checkout
    {
        private static readonly IReadOnlyList<string> DaNangDistricts = new[]
        {
            "Quận Hải Châu",
            "Quận Thanh Khê",
            "Quận Sơn Trà",
            "Quận Ngũ Hành Sơn",
            "Quận Liên Chiểu",
            "Quận Cẩm Lệ",
            "Huyện Hòa Vang",
            "Huyện Hoàng Sa",
        };

        private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> DaNangWardsByDistrict =
            new Dictionary<string, IReadOnlyList<string>>
        {
            ["Quận Hải Châu"] = new[] { "Phường Thạch Thang", "Phường Hải Châu I", "Phường Hải Châu II", "Phường Phước Ninh", "Phường Hùng Vương", "Phường Chính Gián", "Phường Vĩnh Trung", "Phường Thanh Bình", "Phường Thuận Phước", "Phường Thủy Thuỷ", "Phường Hòa Cường Bắc", "Phường Hòa Cường Nam", "Phường Nam Dương", "Phường Ong Ích Khiêm" },
            ["Quận Thanh Khê"] = new[] { "Phường An Xuân", "Phường Tam Thuận", "Phường Thanh Khê Tây", "Phường Thanh Khê Đông", "Phường Kim Ly", "Phường Hòa Khê", "Phường Xuân Hà", "Phường Tân Chính", "Phường Bình Hiên", "Phường Bình Thuận" },
            ["Quận Sơn Trà"] = new[] { "Phường Thọ Quang", "Phường Mân Thái", "Phường Nại Hiên Đông", "Phường Phước Mỹ", "Phường An Hải Bắc", "Phường An Hải Nam", "Phường An Hải Tây", "Phường Mỹ An", "Phường Khuê Trung" },
            ["Quận Ngũ Hành Sơn"] = new[] { "Phường Mỹ An", "Phường Khuê Mỹ", "Phường Hoà Quý", "Phường Hoà Hải", "Phường Khuê Trung" },
            ["Quận Liên Chiểu"] = new[] { "Phường Hòa Khánh Bắc", "Phường Hòa Khánh Nam", "Phường Hòa Minh", "Phường Hòa Bắc", "Phường Hòa Liên", "Phường Hòa Nhơn" },
            ["Quận Cẩm Lệ"] = new[] { "Phường Hòa An", "Phường Hòa Phát", "Phường Khuê Trung", "Phường Hoà Quý", "Phường Hòa Khê", "Phường Khuê Mỹ", "Phường An Lạc" },
            ["Huyện Hòa Vang"] = new[] { "Phường Hòa Phước", "Phường Hòa Sơn", "Phường Hòa Tiến", "Phường Hòa Khương", "Phường Hòa Phong", "Phường Hòa Ninh", "Phường Hòa Phú", "Phường Hòa Bắc", "Phường Hòa Liên", "Phường Hòa Nhơn", "Phường Hòa Bình", "Phường Hòa Thọ Bắc", "Phường Hòa Thọ Đông", "Phường Hòa Thọ Nam" },
            ["Huyện Hoàng Sa"] = Array.Empty<string>(),
        };

        private readonly CheckoutForm form = new();
        private readonly Dictionary<string, string> errors = new();
        private readonly HashSet<string> touched = new();

        /// <summary>
        /// Đã có đủ thông tin địa chỉ giao hàng hay chưa (port <c>hasAddress</c> của nguồn).
        /// </summary>
        private bool HasAddress =>
            !string.IsNullOrWhiteSpace(form.FullName) &&
            !string.IsNullOrWhiteSpace(form.Phone) &&
            !string.IsNullOrWhiteSpace(form.District) &&
            !string.IsNullOrWhiteSpace(form.Ward) &&
            !string.IsNullOrWhiteSpace(form.AddressDetail);

        private IReadOnlyList<string> WardsForSelectedDistrict =>
            DaNangWardsByDistrict.TryGetValue(form.District, out var list) ? list : Array.Empty<string>();

        /// <summary>
        /// Chuẩn hoá tên field giữa form thanh toán (<c>form.FullName</c>) và modal
        /// địa chỉ (<c>fullName</c>) về CÙNG một key.
        ///
        /// <para>
        /// <b>VÌ SAO CẦN:</b> nếu không chuẩn hoá, lỗi do AddressModal đẩy về (key
        /// <c>fullName</c>, <c>phone</c>, <c>email</c>, <c>district</c>, <c>ward</c>,
        /// <c>address</c>) nằm song song với lỗi do <see cref="ValidateField"/> sinh ra
        /// (key <c>form.FullName</c>, <c>form.Phone</c>, …). <see cref="ValidateAll"/>
        /// kiểm tra <c>errors.Count == 0</c> ⇒ bộ lỗi "rác" từ modal làm
        /// <b>ValidateAll luôn trả về false</b> ⇒ nút "Đặt hàng" và "Lưu địa chỉ"
        /// không bao giờ hoạt động.
        /// </para>
        /// </summary>
        private static string NormalizeFieldKey(string field) => field switch
        {
            "fullName" or nameof(form.FullName) => nameof(form.FullName),
            "phone" or nameof(form.Phone) => nameof(form.Phone),
            "email" or nameof(form.Email) => nameof(form.Email),
            "province" or nameof(form.Province) => nameof(form.Province),
            "district" or nameof(form.District) => nameof(form.District),
            "ward" or nameof(form.Ward) => nameof(form.Ward),
            "address" or nameof(form.AddressDetail) => nameof(form.AddressDetail),
            _ => field,
        };

        private void ValidateField(string field)
        {
            field = NormalizeFieldKey(field);
            errors.Remove(field);
            switch (field)
            {
                case nameof(form.FullName):
                    if (string.IsNullOrWhiteSpace(form.FullName))
                        errors[field] = "Vui lòng nhập họ và tên.";
                    else if (form.FullName.Trim().Length < 2)
                        errors[field] = "Họ và tên phải có ít nhất 2 ký tự.";
                    else if (form.FullName.Length > 100)
                        errors[field] = "Họ và tên tối đa 100 ký tự.";
                    break;
                case nameof(form.Phone):
                    if (string.IsNullOrWhiteSpace(form.Phone))
                        errors[field] = "Vui lòng nhập số điện thoại.";
                    else if (!IsValidPhone(form.Phone))
                        errors[field] = "Số điện thoại phải bắt đầu bằng 0 và có 10-11 chữ số.";
                    break;
                case nameof(form.Email):
                    if (!string.IsNullOrWhiteSpace(form.Email) && !IsValidEmail(form.Email))
                        errors[field] = "Email không hợp lệ.";
                    break;
                case nameof(form.Province):
                    if (string.IsNullOrWhiteSpace(form.Province))
                        errors[field] = "Vui lòng chọn tỉnh/thành phố.";
                    else if (!string.Equals(form.Province.Trim(), "Đà Nẵng", StringComparison.OrdinalIgnoreCase))
                        errors[field] = "Hiện tại chỉ hỗ trợ giao hàng tại Đà Nẵng.";
                    break;
                case nameof(form.District):
                    if (string.IsNullOrWhiteSpace(form.District))
                        errors[field] = "Vui lòng chọn quận/huyện.";
                    else if (!DaNangDistricts.Contains(form.District))
                        errors[field] = "Vui lòng chọn quận/huyện từ danh sách.";
                    break;
                case nameof(form.Ward):
                    if (string.IsNullOrWhiteSpace(form.Ward))
                        errors[field] = "Vui lòng chọn phường/xã.";
                    else if (!WardsForSelectedDistrict.Contains(form.Ward))
                        errors[field] = "Phường/xã không hợp lệ cho quận/huyện đã chọn.";
                    break;
                case nameof(form.AddressDetail):
                    if (string.IsNullOrWhiteSpace(form.AddressDetail))
                        errors[field] = "Vui lòng nhập địa chỉ chi tiết.";
                    else if (form.AddressDetail.Trim().Length < 5)
                        errors[field] = "Địa chỉ phải có ít nhất 5 ký tự.";
                    else if (form.AddressDetail.Length > 200)
                        errors[field] = "Địa chỉ tối đa 200 ký tự.";
                    break;
            }
            StateHasChanged();
        }

        private bool ValidateAll()
        {
            foreach (var field in new[] {
                nameof(form.FullName), nameof(form.Phone), nameof(form.Email),
                nameof(form.Province), nameof(form.District), nameof(form.Ward),
                nameof(form.AddressDetail) })
            {
                ValidateField(field);
            }
            return errors.Count == 0;
        }

        /// <summary>
        /// Dựng chuỗi địa chỉ đầy đủ theo định dạng <c>"Số nhà, Phường, Quận, Tỉnh"</c>.
        /// Trả về chuỗi rỗng nếu thiếu bất kỳ thành phần nào.
        /// </summary>
        private string BuildFullAddress()
        {
            if (string.IsNullOrWhiteSpace(form.AddressDetail) ||
                string.IsNullOrWhiteSpace(form.Ward) ||
                string.IsNullOrWhiteSpace(form.District) ||
                string.IsNullOrWhiteSpace(form.Province))
            {
                return string.Empty;
            }
            return $"{form.AddressDetail.Trim()}, {form.Ward.Trim()}, {form.District.Trim()}, {form.Province.Trim()}";
        }

        private static bool IsValidPhone(string phone)
        {
            var clean = (phone ?? string.Empty).Replace(" ", string.Empty);
            return System.Text.RegularExpressions.Regex.IsMatch(clean, @"^0[0-9]{9,10}$");
        }

        private static bool IsValidEmail(string email)
        {
            return System.Text.RegularExpressions.Regex.IsMatch(email ?? string.Empty, @"^[^\s@]+@[^\s@]+\.[^\s@]+$");
        }

        /// <summary>Nhận bộ lỗi mới từ AddressModal (port <c>onSetErrors</c>).</summary>
        private void SetErrors(IReadOnlyDictionary<string, string> newErrors)
        {
            errors.Clear();
            foreach (var pair in newErrors)
            {
                // BỎ QUA lỗi RỐNG: AddressModal dùng "" để đánh dấu "đã hết lỗi"
                // (vd khi chọn quận ⇒ newErrors["district"] = "").
                // Nếu giữ lại, ValidateAll() (kiểm tra errors.Count == 0) sẽ LUÔN
                // thất bại ⇒ nút "Đặt hàng"/lưu địa chỉ không bao giờ chạy được.
                if (string.IsNullOrWhiteSpace(pair.Value))
                {
                    continue;
                }

                errors[NormalizeFieldKey(pair.Key)] = pair.Value;
            }

            StateHasChanged();
        }

        /// <summary>Nhận bộ field đã chạm mới từ AddressModal (port <c>onSetTouched</c>).</summary>
        private void SetTouched(IReadOnlySet<string> newTouched)
        {
            touched.Clear();
            foreach (var field in newTouched)
            {
                touched.Add(NormalizeFieldKey(field));
            }

            StateHasChanged();
        }

        private sealed class CheckoutForm
        {
            public string FullName { get; set; } = string.Empty;
            public string Phone { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string Province { get; set; } = string.Empty;
            public string District { get; set; } = string.Empty;
            public string Ward { get; set; } = string.Empty;
            public string AddressDetail { get; set; } = string.Empty;
            public string PaymentMethod { get; set; } = "COD";
            public string Note { get; set; } = string.Empty;
        }
    }
}
