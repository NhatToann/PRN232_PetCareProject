using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using PetShop.Services;

namespace PetShop_PRN222.Components.Checkout;

/// <summary>
/// Phần code-behind của <c>AddressModal.razor</c>.
/// Tách riêng để file .razor không vượt quá 500 dòng (quy ước trong AGENTS.md §2.3).
///
/// Port nguyên văn từ <c>Projectojt/frontend/src/components/checkout/AddressModal.jsx</c> (OJT):
/// validate, dropdown gợi ý quận/phường, hành vi open/close, callback trả địa chỉ.
/// Dữ liệu địa chính lấy từ <c>wwwroot/js/daNangData.js</c> qua JS interop.
/// </summary>
public partial class AddressModal : ComponentBase
{
    [Inject] private IJSRuntime JS { get; set; } = default!;

    [Inject] private ILogger<AddressModal> Logger { get; set; } = default!;

    /// <summary>Modal có đang mở hay không.</summary>
    [Parameter] public bool IsOpen { get; set; }

    /// <summary>View hiện tại: "list" (danh sách địa chỉ) hoặc "form" (thêm/cập nhật).</summary>
    [Parameter] public string AddressModalView { get; set; } = "list";

    /// <summary>Id của địa chỉ đang sửa; <c>null</c> khi đang thêm mới.</summary>
    [Parameter] public long? EditingAddressId { get; set; }

    /// <summary>Danh sách địa chỉ đã lưu.</summary>
    [Parameter] public IReadOnlyList<SavedAddress> SavedAddresses { get; set; } = Array.Empty<SavedAddress>();

    /// <summary>Id địa chỉ đang được chọn trong danh sách.</summary>
    [Parameter] public long? SelectedSavedId { get; set; }

    [Parameter] public string FullName { get; set; } = string.Empty;

    [Parameter] public string Phone { get; set; } = string.Empty;

    [Parameter] public string Email { get; set; } = string.Empty;

    [Parameter] public string Province { get; set; } = string.Empty;

    [Parameter] public string District { get; set; } = string.Empty;

    [Parameter] public string Ward { get; set; } = string.Empty;

    [Parameter] public string Address { get; set; } = string.Empty;

    /// <summary>Lỗi theo tên field (key: fullName/phone/email/district/ward/address).</summary>
    [Parameter] public IReadOnlyDictionary<string, string> Errors { get; set; } = new Dictionary<string, string>();

    /// <summary>Field nào đã được "chạm" (blur) — quyết định có hiện lỗi hay không.</summary>
    [Parameter] public IReadOnlySet<string> Touched { get; set; } = new HashSet<string>();

    /// <summary>
    /// Lỗi CỤC BỘ của modal (key: fullName/phone/email/district/ward/address).
    ///
    /// <para>
    /// <b>VÌ SAO CẦN STATE CỤC BỘ:</b> trước đây modal đọc lỗi/trạng thái chạm từ
    /// parameter <c>Errors</c>/<c>Touched</c> do parent truyền vào. Nhưng parent
    /// (Checkout) dùng key khác (<c>form.FullName</c>, <c>form.AddressDetail</c>…) cho
    /// form thanh toán ⇒ hai bộ key song song ⇒ lỗi của modal không bao giờ hiển thị
    /// và <c>ValidateAll()</c> của parent luôn thất bại vì còn lỗi "rác".
    /// Nay modal tự quản lý lỗi/trạng thái chạm; vẫn PHÁT sự kiện
    /// <see cref="OnSetErrors"/>/<see cref="OnSetTouched"/> để parent đồng bộ nếu cần.
    /// </para>
    /// </summary>
    private Dictionary<string, string> _localErrors = new(StringComparer.OrdinalIgnoreCase);

    private HashSet<string> _localTouched = new(StringComparer.OrdinalIgnoreCase);

    [Parameter] public EventCallback OnClose { get; set; }

    [Parameter] public EventCallback<SavedAddress> OnSelectAddress { get; set; }

    [Parameter] public EventCallback OnStartAdd { get; set; }

    [Parameter] public EventCallback<SavedAddress> OnStartEdit { get; set; }

    [Parameter] public EventCallback<long> OnDeleteAddress { get; set; }

    [Parameter] public EventCallback<SavedAddress> OnSave { get; set; }

    [Parameter] public EventCallback OnBack { get; set; }

    [Parameter] public EventCallback<string> OnFullName { get; set; }

    [Parameter] public EventCallback<string> OnPhone { get; set; }

    [Parameter] public EventCallback<string> OnEmail { get; set; }

    [Parameter] public EventCallback<string> OnDistrict { get; set; }

    [Parameter] public EventCallback<string> OnWard { get; set; }

    [Parameter] public EventCallback<string> OnAddress { get; set; }

    /// <summary>Nhận bộ lỗi mới sau khi validate (port <c>onSetErrors</c>).</summary>
    [Parameter] public EventCallback<IReadOnlyDictionary<string, string>> OnSetErrors { get; set; }

    /// <summary>Nhận bộ field đã chạm mới (port <c>onSetTouched</c>).</summary>
    [Parameter] public EventCallback<IReadOnlySet<string>> OnSetTouched { get; set; }

    private bool showWardDropdown;
    private bool showDistrictDropdown;
    private IReadOnlyList<string> wardSuggestions = Array.Empty<string>();
    private IReadOnlyList<string> districtSuggestions = Array.Empty<string>();
    private bool dataLoaded;

    /// <summary>Trạng thái mở dropdown phường/xã (dùng bởi <c>AddressModalForm</c>).</summary>
    public bool ShowWardDropdown => showWardDropdown;

    /// <summary>Trạng thái mở dropdown quận/huyện (dùng bởi <c>AddressModalForm</c>).</summary>
    public bool ShowDistrictDropdown => showDistrictDropdown;

    /// <summary>Danh sách phường/xã gợi ý (tối đa 15) — lọc theo quận đang chọn.</summary>
    public IReadOnlyList<string> WardSuggestions => wardSuggestions;

    /// <summary>Danh sách quận/huyện gợi ý (tối đa 8).</summary>
    public IReadOnlyList<string> DistrictSuggestions => districtSuggestions;

    protected override async Task OnParametersSetAsync()
    {
        // KHÔNG gọi JS interop ở đây.
        //
        // VÌ SAO: OnParametersSetAsync chạy ở giai đoạn PRERENDER (static render)
        // ⇒ JS interop bị chặn ⇒ sinh log warning "JavaScript interop calls cannot
        // be issued at this time" mỗi lần mở trang ⇒ vừa gây log rác vừa tốn
        // thêm một vòng render không cần thiết.
        //
        // Nay việc nạp dữ liệu JS được chuyển sang OnAfterRenderAsync (chỉ chạy
        // khi circuit đã interactive ⇒ interop hợp lệ).

        // Nguồn: useEffect(() => { if (!isOpen) { setShowWardDropdown(false); setShowDistrictDropdown(false) } }, [isOpen, addressModalView])
        if (!IsOpen)
        {
            showWardDropdown = false;
            showDistrictDropdown = false;

            // Reset lỗi/trạng thái chạm cục bộ khi modal đóng ⇒ mở lại không còn
            // lỗi cũ của lần trước (và không còn lỗi "rác" chặn ValidateAll ở parent).
            _localErrors.Clear();
            _localTouched.Clear();
        }

        // Nguồn: useEffect(() => {
        //   document.body.style.overflow = showAddressModal ? 'hidden' : ''
        //   return () => { document.body.style.overflow = '' }
        // }, [showAddressModal])
        await Task.CompletedTask;
    }

    /// <summary>
    /// Nạp dữ liệu từ JS + khoá cuộn trang — chỉ chạy khi circuit đã interactive.
    /// </summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await EnsureDataLoadedAsync();
        await RefreshSuggestionsAsync();
        await ApplyBodyScrollLockAsync();
    }

    /// <summary>
    /// Khoá cuộn trang khi modal mở (port <c>document.body.style.overflow</c> của nguồn).
    /// </summary>
    private async Task ApplyBodyScrollLockAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("blazorUi.setBodyScrollLock", IsOpen);
        }
        catch (Exception ex)
        {
            Logger.LineWarning($"[AddressModal] Không khoá cuộn trang được: {ex.GetType().Name}");
        }
    }

    /// <summary>
    /// Danh sách quận/huyện dự phòng (C#) — dùng khi JS interop lỗi.
    /// Tránh tình trạng dropdown quận/huyện rỗng ⇒ không lưu được địa chỉ.
    /// </summary>
    private static readonly string[] FallbackDistricts =
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

    /// <summary>
    /// Nạp danh sách quận/huyện từ <c>daNangData.js</c> một lần duy nhất.
    /// </summary>
    private async Task EnsureDataLoadedAsync()
    {
        if (dataLoaded) return;

        try
        {
            districtSuggestions = await JS.InvokeAsync<string[]>("daNangData.getDistricts") ?? Array.Empty<string>();
            dataLoaded = true;
        }
        catch (Exception ex)
        {
            Logger.LineWarning($"[AddressModal] Không nạp được danh sách quận/huyện từ daNangData.js: {ex.GetType().Name} {ex.Message}");

            // FALLBACK: dùng danh sách C# để dropdown vẫn có dữ liệu.
            districtSuggestions = FallbackDistricts;
            dataLoaded = true;
        }
    }

    /// <summary>
    /// Tính lại danh sách gợi ý theo đúng logic nguồn:
    /// <c>wardSuggestionsFor(ward, district)</c> và <c>districtSuggestionsFor(district)</c>.
    /// </summary>
    private async Task RefreshSuggestionsAsync()
    {
        try
        {
            wardSuggestions = await JS.InvokeAsync<string[]>(
                "daNangData.wardSuggestionsFor", Ward, District) ?? Array.Empty<string>();
            districtSuggestions = await JS.InvokeAsync<string[]>(
                "daNangData.districtSuggestionsFor", District) ?? Array.Empty<string>();
        }
        catch (Exception ex)
        {
            Logger.LineWarning($"[AddressModal] Không tính được danh sách gợi ý: {ex.GetType().Name} {ex.Message}");
        }
    }

    /// <summary>Trả về lỗi của field nếu đã chạm (touched) và có lỗi; ngược lại <c>null</c>.</summary>
    public string? GetFieldError(string fieldName)
    {
        if (!_localTouched.Contains(fieldName)) return null;

        // Lỗi RỖNG (chuỗi "") KHÔNG phải lỗi — nếu trả về "" thì component vẽ
        // thêm khung viền đỏ + <span class="error-message"></span> rỗng.
        if (!_localErrors.TryGetValue(fieldName, out var message) || string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        return message;
    }

    /// <summary>Đánh dấu field đã chạm và validate ngay field đó (port <c>handleBlur</c>).</summary>
    public async Task HandleBlur(string fieldName)
    {
        _localTouched.Add(fieldName);
        await OnSetTouched.InvokeAsync(new HashSet<string>(_localTouched, StringComparer.OrdinalIgnoreCase));
        await ValidateFieldAsync(fieldName);
    }

    /// <summary>Validate một field và phát sự kiện OnSetErrors (port <c>validateField</c>).</summary>
    private async Task ValidateFieldAsync(string fieldName)
    {
        var newErrors = new Dictionary<string, string>(_localErrors, StringComparer.OrdinalIgnoreCase);

        switch (fieldName)
        {
            case "fullName":
                if (string.IsNullOrWhiteSpace(FullName))
                {
                    newErrors["fullName"] = "Vui lòng nhập họ và tên";
                }
                else if (FullName.Trim().Length < 2)
                {
                    newErrors["fullName"] = "Họ và tên phải có ít nhất 2 ký tự";
                }
                else
                {
                    newErrors.Remove("fullName");
                }

                break;
            case "phone":
                if (string.IsNullOrWhiteSpace(Phone))
                {
                    newErrors["phone"] = "Vui lòng nhập số điện thoại";
                }
                else if (!await IsValidPhoneAsync(Phone))
                {
                    newErrors["phone"] = "Số điện thoại không hợp lệ (phải bắt đầu bằng 0, có 10-11 số)";
                }
                else
                {
                    newErrors.Remove("phone");
                }

                break;
            case "email":
                if (string.IsNullOrWhiteSpace(Email))
                {
                    newErrors["email"] = "Vui lòng nhập email";
                }
                else if (!await IsValidEmailAsync(Email))
                {
                    newErrors["email"] = "Email không hợp lệ";
                }
                else
                {
                    newErrors.Remove("email");
                }

                break;
            case "district":
                if (string.IsNullOrWhiteSpace(District))
                {
                    newErrors["district"] = "Vui lòng chọn quận/huyện";
                }
                else if (!await IsKnownDistrictAsync(District))
                {
                    newErrors["district"] = "Vui lòng chọn quận/huyện từ danh sách";
                }
                else
                {
                    newErrors.Remove("district");
                }

                break;
            case "ward":
                if (string.IsNullOrWhiteSpace(Ward))
                {
                    newErrors["ward"] = "Vui lòng chọn phường/xã";
                }
                else if (!await IsWardValidForDistrictAsync(Ward, District))
                {
                    newErrors["ward"] = "Phường/xã không hợp lệ cho quận/huyện đã chọn";
                }
                else
                {
                    newErrors.Remove("ward");
                }

                break;
            case "address":
                if (string.IsNullOrWhiteSpace(Address))
                {
                    newErrors["address"] = "Vui lòng nhập địa chỉ chi tiết (số nhà, tên đường)";
                }
                else if (Address.Trim().Length < 5)
                {
                    newErrors["address"] = "Địa chỉ phải có ít nhất 5 ký tự";
                }
                else
                {
                    newErrors.Remove("address");
                }

                break;
            default:
                break;
        }

        _localErrors = newErrors;
        await OnSetErrors.InvokeAsync(newErrors);
    }

    /// <summary>
    /// Validate toàn bộ form (port <c>validateFormFields</c>). Trả về <c>true</c> nếu hợp lệ.
    /// </summary>
    public async Task<bool> ValidateFormFieldsAsync()
    {
        var newErrors = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(FullName))
        {
            newErrors["fullName"] = "Vui lòng nhập họ và tên";
        }
        else if (FullName.Trim().Length < 2)
        {
            newErrors["fullName"] = "Họ và tên phải có ít nhất 2 ký tự";
        }

        if (string.IsNullOrWhiteSpace(Phone))
        {
            newErrors["phone"] = "Vui lòng nhập số điện thoại";
        }
        else if (!await IsValidPhoneAsync(Phone))
        {
            newErrors["phone"] = "Số điện thoại không hợp lệ (phải bắt đầu bằng 0, có 10-11 số)";
        }

        if (string.IsNullOrWhiteSpace(Email))
        {
            newErrors["email"] = "Vui lòng nhập email";
        }
        else if (!await IsValidEmailAsync(Email))
        {
            newErrors["email"] = "Email không hợp lệ";
        }

        if (string.IsNullOrWhiteSpace(District))
        {
            newErrors["district"] = "Vui lòng chọn quận/huyện";
        }
        else if (!await IsKnownDistrictAsync(District))
        {
            newErrors["district"] = "Vui lòng chọn quận/huyện từ danh sách";
        }

        if (string.IsNullOrWhiteSpace(Ward))
        {
            newErrors["ward"] = "Vui lòng chọn phường/xã";
        }
        else if (!await IsWardValidForDistrictAsync(Ward, District))
        {
            newErrors["ward"] = "Phường/xã không hợp lệ cho quận/huyện đã chọn";
        }

        if (string.IsNullOrWhiteSpace(Address))
        {
            newErrors["address"] = "Vui lòng nhập địa chỉ chi tiết (số nhà, tên đường)";
        }
        else if (Address.Trim().Length < 5)
        {
            newErrors["address"] = "Địa chỉ phải có ít nhất 5 ký tự";
        }

        _localErrors = newErrors;

        _localTouched = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "fullName", "phone", "email", "district", "ward", "address",
        };

        await OnSetErrors.InvokeAsync(newErrors);
        await OnSetTouched.InvokeAsync(new HashSet<string>(_localTouched, StringComparer.OrdinalIgnoreCase));
        return newErrors.Count == 0;
    }

    /// <summary>Lưu địa chỉ (port <c>handleSave</c>) — chỉ khi form hợp lệ.</summary>
    public async Task HandleSave()
    {
        if (!await ValidateFormFieldsAsync()) return;

        await OnSave.InvokeAsync(new SavedAddress(
            EditingAddressId ?? 0,
            FullName.Trim(),
            Phone.Trim(),
            Email.Trim(),
            Province,
            District,
            Ward,
            Address.Trim(),
            false));
    }

    /// <summary>Xoá địa chỉ (port <c>handleDelete</c>) — có hộp thoại xác nhận.</summary>
    public async Task HandleDelete()
    {
        if (EditingAddressId is null) return;

        var confirmed = await JS.InvokeAsync<bool>("confirm", "Xóa địa chỉ này?");
        if (!confirmed) return;

        await OnDeleteAddress.InvokeAsync(EditingAddressId.Value);
    }

    /// <summary>Chọn quận ⇒ reset phường và đóng cả hai dropdown (port <c>handleDistrictSelect</c>).</summary>
    public async Task HandleDistrictSelect(string item)
    {
        await OnDistrict.InvokeAsync(item);
        await OnWard.InvokeAsync(string.Empty);
        showDistrictDropdown = false;
        showWardDropdown = false;

        _localTouched.Add("district");

        // Xoá lỗi "district" và "ward" (người dùng vừa chọn quận ⇒ phường cũ không còn hợp lệ).
        _localErrors.Remove("district");
        _localErrors.Remove("ward");

        await OnSetTouched.InvokeAsync(new HashSet<string>(_localTouched, StringComparer.OrdinalIgnoreCase));
        await OnSetErrors.InvokeAsync(new Dictionary<string, string>(_localErrors, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Chọn phường (port <c>handleWardSelect</c>).</summary>
    public async Task HandleWardSelect(string item)
    {
        await OnWard.InvokeAsync(item);
        showWardDropdown = false;

        _localTouched.Add("ward");
        _localErrors.Remove("ward");

        await OnSetTouched.InvokeAsync(new HashSet<string>(_localTouched, StringComparer.OrdinalIgnoreCase));
        await OnSetErrors.InvokeAsync(new Dictionary<string, string>(_localErrors, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Mở/đóng dropdown phường.</summary>
    public void SetShowWardDropdown(bool value)
    {
        showWardDropdown = value;
        StateHasChanged();
    }

    /// <summary>Mở/đóng dropdown quận.</summary>
    public void SetShowDistrictDropdown(bool value)
    {
        showDistrictDropdown = value;
        StateHasChanged();
    }

    /// <summary>Bấm vào lớp overlay ⇒ đóng modal (port <c>handleOverlayClick</c>).</summary>
    private void HandleOverlayClick(MouseEventArgs e)
    {
        // Nguồn chỉ đóng khi bấm trúng chính overlay (e.target === e.currentTarget).
        // Blazor không bubble MouseEventArgs theo cách React làm, nên sự kiện gắn
        // trên overlay chỉ nhận khi bấm vào vùng nền ⇒ tương đương điều kiện nguồn.
        _ = e;
        OnClose.InvokeAsync();
    }

    private async Task<bool> IsValidPhoneAsync(string phone)
    {
        try
        {
            return await JS.InvokeAsync<bool>("daNangData.validatePhone", phone);
        }
        catch (Exception ex)
        {
            Logger.LineWarning($"[AddressModal] validatePhone lỗi: {ex.GetType().Name}");
            return false;
        }
    }

    private async Task<bool> IsValidEmailAsync(string email)
    {
        try
        {
            return await JS.InvokeAsync<bool>("daNangData.validateEmail", email);
        }
        catch (Exception ex)
        {
            Logger.LineWarning($"[AddressModal] validateEmail lỗi: {ex.GetType().Name}");
            return false;
        }
    }

    private async Task<bool> IsKnownDistrictAsync(string district)
    {
        try
        {
            return await JS.InvokeAsync<bool>("daNangData.isKnownDistrict", district);
        }
        catch (Exception ex)
        {
            Logger.LineWarning($"[AddressModal] isKnownDistrict lỗi: {ex.GetType().Name}");
            return false;
        }
    }

    private async Task<bool> IsWardValidForDistrictAsync(string ward, string district)
    {
        try
        {
            return await JS.InvokeAsync<bool>("daNangData.isWardValidForDistrict", ward, district);
        }
        catch (Exception ex)
        {
            Logger.LineWarning($"[AddressModal] isWardValidForDistrict lỗi: {ex.GetType().Name}");
            return false;
        }
    }
}

/// <summary>
/// Một địa chỉ giao hàng đã lưu. Cấu trúc field giữ nguyên tên như nguồn OJT
/// (<c>fullName</c>, <c>phone</c>, <c>email</c>, <c>province</c>, <c>district</c>,
/// <c>ward</c>, <c>address</c>, <c>isDefault</c>).
/// </summary>
/// <param name="Id">Id địa chỉ (0 = địa chỉ mới chưa lưu).</param>
/// <param name="FullName">Họ và tên người nhận.</param>
/// <param name="Phone">Số điện thoại.</param>
/// <param name="Email">Email.</param>
/// <param name="Province">Tỉnh/Thành phố.</param>
/// <param name="District">Quận/Huyện.</param>
/// <param name="Ward">Phường/Xã.</param>
/// <param name="Address">Số nhà, tên đường.</param>
/// <param name="IsDefault">Địa chỉ mặc định.</param>
public sealed record SavedAddress(
    long Id,
    string FullName,
    string Phone,
    string Email,
    string Province,
    string District,
    string Ward,
    string Address,
    bool IsDefault);
