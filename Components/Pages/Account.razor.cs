using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace PetShop_PRN222.Components.Pages
{
    /// <summary>
    /// Phần code-behind của <c>Account.razor</c>.
    /// Tách riêng để file .razor không vượt quá 300 dòng (quy ước trong AGENTS.md).
    ///
    /// Lưu ý về avatar (Q9): nguồn OJT lưu avatar trong localStorage
    /// (<c>userProfile:{userId}</c>). Đích giữ nguyên hành vi đó (parity UI 100%)
    /// và đồng thời đồng bộ sang cột <c>Customer.avatar_url</c> để avatar không mất
    /// khi xoá localStorage hoặc đăng nhập trên thiết bị khác.
    ///
    /// Lưu ý về ngày sinh (Q22): nguồn OJT (<c>ProfileEditPage.jsx</c>) có ô "Ngày sinh"
    /// nhưng chỉ lưu trong localStorage. Đích giữ nguyên hành vi đó và đồng thời đồng bộ
    /// sang cột <c>Customer.birth_date</c> để ngày sinh không mất khi xoá localStorage
    /// hoặc đăng nhập trên thiết bị khác.
    /// </summary>
    public partial class Account
    {
        private bool loaded;
        private bool pendingLoad = true;
        private bool saving;
        private string fullName = "";
        private string phone = "";
        private string email = "";
        private string address = "";
        private string birthDate = "";
        private string avatarUrl = "";
        private string? error;
        private string? success;

        private string Initial
        {
            get
            {
                var source = string.IsNullOrWhiteSpace(fullName) ? email : fullName;
                return string.IsNullOrWhiteSpace(source)
                    ? "U"
                    : source.Trim()[0].ToString().ToUpperInvariant();
            }
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!pendingLoad)
            {
                return;
            }

            pendingLoad = false;
            await LoadProfileAsync();
            StateHasChanged();
        }

        private async Task LoadProfileAsync()
        {
            error = null;
            success = null;
            await UserSession.LoadAsync();
            var user = UserSession.CurrentUser;
            if (user is null)
            {
                loaded = true;
                return;
            }

            var draft = await ReadDraftAsync(user.UserId);
            fullName = FirstNonEmpty(draft?.FullName, user.Name);
            email = FirstNonEmpty(draft?.Email, user.Email);
            phone = draft?.Phone ?? "";
            address = draft?.Address ?? "";

            // Thứ tự ưu tiên ngày sinh: localStorage draft → Customer.birth_date trong DB → rỗng.
            // Đọc DB giúp ngày sinh sống sót khi user xoá localStorage hoặc đăng nhập trên máy/tab khác.
            var draftBirthDate = draft?.BirthDate ?? "";
            birthDate = string.IsNullOrWhiteSpace(draftBirthDate)
                ? await ReadBirthDateFromServerAsync()
                : NormalizeBirthDateForDisplay(draftBirthDate);

            // Thứ tự ưu tiên avatar: localStorage draft → Customer.avatar_url trong DB → rỗng
            // (UI fallback về chữ cái đầu). Đọc DB giúp avatar sống sót khi user xoá
            // localStorage hoặc đăng nhập trên máy/tab khác.
            var draftAvatar = draft?.AvatarUrl ?? "";
            avatarUrl = !string.IsNullOrWhiteSpace(draftAvatar)
                ? draftAvatar
                : await ReadAvatarFromServerAsync();

            loaded = true;
        }

        /// <summary>
        /// Đọc ngày sinh từ server (cột <c>Customer.birth_date</c>).
        /// Lỗi mạng/API không được làm hỏng trang — trả về chuỗi rỗng để UI fallback.
        /// </summary>
        private async Task<string> ReadBirthDateFromServerAsync()
        {
            try
            {
                var result = await AuthClient.GetBirthDateAsync();
                if (result.IsSuccess && result.Data is DateTime value)
                {
                    return value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
                }
            }
            catch (Exception)
            {
                // Bỏ qua — ngày sinh trên DB là dữ liệu phụ, không chặn hiển thị trang.
            }

            return "";
        }

        /// <summary>
        /// Đọc ảnh đại diện từ server (cột <c>Customer.avatar_url</c>).
        /// Lỗi mạng/API không được làm hỏng trang — trả về chuỗi rỗng để UI fallback.
        /// </summary>
        private async Task<string> ReadAvatarFromServerAsync()
        {
            try
            {
                var result = await AuthClient.GetAvatarAsync();
                if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.Data))
                {
                    return result.Data!;
                }
            }
            catch (Exception)
            {
                // Bỏ qua — avatar trên DB là dữ liệu phụ, không chặn hiển thị trang.
            }

            return "";
        }

        private async Task SaveAsync()
        {
            if (!loaded || saving)
            {
                return;
            }

            var user = UserSession.CurrentUser;
            if (user is null)
            {
                Navigation.NavigateTo("/login", forceLoad: true);
                return;
            }

            error = null;
            success = null;
            var trimmedBirthDate = birthDate.Trim();
            if (!string.IsNullOrWhiteSpace(trimmedBirthDate) && !IsValidDmy(trimmedBirthDate))
            {
                error = "Ngày sinh không tồn tại";
                return;
            }

            saving = true;
            try
            {
                var draft = new ProfileDraft(
                    fullName.Trim(),
                    phone.Trim(),
                    email.Trim(),
                    address.Trim(),
                    trimmedBirthDate,
                    avatarUrl,
                    DateTime.UtcNow);
                await JS.InvokeVoidAsync("localStorage.setItem", ProfileKey(user.UserId), JsonSerializer.Serialize(draft));

                // Đồng bộ avatar xuống DB để không mất khi xoá localStorage / đăng nhập máy khác.
                await SyncAvatarToServerAsync(avatarUrl);

                // Đồng bộ ngày sinh xuống DB (cột Customer.birth_date) — giống cách làm với avatar.
                await SyncBirthDateToServerAsync(trimmedBirthDate);

                success = "Đã lưu thông tin cá nhân.";
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            finally
            {
                saving = false;
            }
        }

        private async Task OnAvatarSelectedAsync(InputFileChangeEventArgs e)
        {
            error = null;
            success = null;
            var file = e.File;
            if (file is null)
            {
                return;
            }

            if (!file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                error = "Chỉ hỗ trợ file ảnh.";
                return;
            }

            const long maxBytes = 4 * 1024 * 1024;
            if (file.Size > maxBytes)
            {
                error = "Dung lượng file tối đa 4 MB.";
                return;
            }

            await using var stream = file.OpenReadStream(maxBytes);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            avatarUrl = $"data:{file.ContentType};base64,{Convert.ToBase64String(ms.ToArray())}";

            // Vẫn giữ parity UI 100% với nguồn (localStorage là nguồn chính),
            // đồng thời đẩy xuống DB để avatar tồn tại qua nhiều thiết bị.
            await SyncAvatarToServerAsync(avatarUrl);
        }

        /// <summary>
        /// Đẩy avatar xuống server (cột <c>Customer.avatar_url</c>).
        /// Bọc try/catch — lỗi API không được làm hỏng UI hoặc chặn lưu profile.
        /// </summary>
        private async Task SyncAvatarToServerAsync(string? value)
        {
            try
            {
                await AuthClient.UpdateAvatarAsync(value);
            }
            catch (Exception)
            {
                // Bỏ qua — localStorage vẫn giữ avatar, người dùng vẫn dùng được.
            }
        }

        /// <summary>
        /// Đẩy ngày sinh xuống server (cột <c>Customer.birth_date</c>).
        /// Chuỗi rỗng ⇒ gửi null để xoá ngày sinh trên DB.
        /// Bọc try/catch — lỗi API không được làm hỏng UI hoặc chặn lưu profile.
        /// </summary>
        private async Task SyncBirthDateToServerAsync(string? value)
        {
            try
            {
                DateTime? parsed = null;
                if (!string.IsNullOrWhiteSpace(value) && IsValidDmy(value.Trim()))
                {
                    parsed = DateTime.ParseExact(value.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture);
                }

                await AuthClient.UpdateBirthDateAsync(parsed);
            }
            catch (Exception)
            {
                // Bỏ qua — localStorage vẫn giữ ngày sinh, người dùng vẫn dùng được.
            }
        }

        private async Task<ProfileDraft?> ReadDraftAsync(int userId)
        {
            try
            {
                var raw = await JS.InvokeAsync<string?>("localStorage.getItem", ProfileKey(userId));
                return string.IsNullOrWhiteSpace(raw)
                    ? null
                    : JsonSerializer.Deserialize<ProfileDraft>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                return null;
            }
        }

        private void GoPets() => Navigation.NavigateTo("/account/pets");

        private void GoOrders() => Navigation.NavigateTo("/account/orders");

        private void GoBack() => Navigation.NavigateTo(UserSession.CurrentUser?.Role?.Equals("staff", StringComparison.OrdinalIgnoreCase) == true ? "/system" : "/");

        private static string ProfileKey(int userId) => $"userProfile:{userId.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()}";

        private static string FirstNonEmpty(params string?[] values) =>
            values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "";

        private static string MaskDigitsToDmy(string value)
        {
            var digits = new string((value ?? "").Where(char.IsDigit).Take(8).ToArray());
            var dd = digits.Length >= 2 ? digits[..2] : digits;
            var mm = digits.Length >= 4 ? digits[2..4] : digits.Length > 2 ? digits[2..] : "";
            var yyyy = digits.Length > 4 ? digits[4..] : "";
            if (digits.Length <= 2) return dd;
            if (digits.Length <= 4) return $"{dd}/{mm}";
            return $"{dd}/{mm}/{yyyy}";
        }

        private static string NormalizeBirthDateForDisplay(string value)
        {
            var raw = (value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(raw)) return "";
            if (IsValidDmy(raw)) return raw;
            return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                : "";
        }

        private static bool IsValidDmy(string value) =>
            DateTime.TryParseExact(value, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

        private sealed record ProfileDraft(
            string? FullName,
            string? Phone,
            string? Email,
            string? Address,
            string? BirthDate,
            string? AvatarUrl,
            DateTime UpdatedAt);
    }
}
