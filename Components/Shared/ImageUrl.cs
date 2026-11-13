using System.Text.RegularExpressions;

namespace PetShop.Components.Shared;

/// <summary>
/// Helper chuẩn hoá URL ảnh trước khi gán vào thẻ <c>&lt;img src="..."&gt;</c>.
///
/// <para>
/// <b>VÌ SAO CẦN:</b> khi người dùng gõ URL ảnh vào ô input (vd ở trang thú cưng,
/// trang tạo sản phẩm…), Blazor re-render sau MỖI phím bấm và gán trực tiếp giá trị
/// đang gõ vào <c>src</c>. Trình duyệt lập tức gửi HTTP GET tới đường dẫn tương đối
/// ⇒ console đầy lỗi 404 kiểu <c>GET /a</c>, <c>GET /%C3%A1</c>, <c>GET /%C3%A1d</c>
/// (mỗi phím gõ 1 request thất bại). Dữ liệu rác trong DB (vd <c>image_path =
/// "ðdasdadaasdasd"</c>) cũng tạo ra 404 tương tự.
/// </para>
///
/// <para>
/// <b>CÁCH SỬA:</b> chỉ gán <c>src</c> khi URL <b>hợp lệ</b>:
/// </para>
/// <list type="bullet">
/// <item>bắt đầu bằng <c>http://</c> hoặc <c>https://</c> (URL tuyệt đối),</item>
/// <item>bắt đầu bằng <c>/</c> (đường dẫn tuyệt đối trong site),</item>
/// <item>bắt đầu bằng <c>data:image/</c> (ảnh inline base64),</item>
/// <item>bắt đầu bằng <c>images/</c> hoặc <c>uploads/</c> (đường dẫn tương đối hợp lệ).</item>
/// </list>
///
/// Nếu không hợp lệ ⇒ trả về <c>null</c> để component <b>không render thẻ img</b>
/// (hiện placeholder/fallback thay vì), tuyệt đối không gán giá trị rác vào <c>src</c>.
/// </para>
/// </summary>
public static class ImageUrl
{
    /// <summary>Ảnh dự phòng khi URL không hợp lệ hoặc rỗng.</summary>
    public const string Placeholder = "/images/placeholder.jpg";

    /// <summary>Các tiền tố đường dẫn tương đối hợp lệ trong <c>wwwroot</c>.</summary>
    private static readonly string[] AllowedRelativePrefixes = { "images/", "uploads/", "img/", "css/", "assets/" };

    /// <summary>
    /// Kiểm tra một chuỗi có phải URL ảnh hợp lệ để gán vào <c>&lt;img src&gt;</c> hay không.
    /// </summary>
    /// <param name="value">Chuỗi do người dùng nhập hoặc lấy từ DB.</param>
    /// <returns><c>true</c> nếu an toàn để gán vào <c>src</c>.</returns>
    public static bool IsSafe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var v = value.Trim();

        // URL tuyệt đối.
        if (v.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            v.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return Uri.TryCreate(v, UriKind.Absolute, out _);
        }

        // Ảnh inline (base64).
        if (v.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Đường dẫn tuyệt đối trong site.
        if (v.StartsWith('/'))
        {
            // "/a", "/á" vẫn là đường dẫn hợp lệ về cú pháp nhưng gần như chắc chắn
            // là rác ⇒ chỉ chấp nhận nếu có ít nhất 1 ký tự trước "/" và có phần mở rộng file.
            return v.Length > 1 && HasImageExtension(v);
        }

        // Đường dẫn tương đối hợp lệ.
        foreach (var prefix in AllowedRelativePrefixes)
        {
            if (v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return HasImageExtension(v);
            }
        }

        return false;
    }

    /// <summary>
    /// Chuẩn hoá URL ảnh: trả về URL đã trim nếu hợp lệ, ngược lại trả về
    /// <see cref="Placeholder"/> để thẻ <c>img</c> không bao giờ gửi request 404.
    ///
    /// <para>
    /// <b>LƯU Ý:</b> đường dẫn tương đối kiểu <c>images/pets/mit.jpg</c> được
    /// chuyển thành <c>/images/pets/mit.jpg</c>. Nếu giữ nguyên dạng tương đối,
    /// trình duyệt resolve theo URL trang hiện tại (vd trang <c>/account/pets</c> ⇒
    /// <c>/account/images/pets/mit.jpg</c>) ⇒ 404 dù file có tồn tại trong
    /// <c>wwwroot</c>.
    /// </para>
    /// </summary>
    public static string Safe(string? value) =>
        IsSafe(value) ? Normalize(value!) : Placeholder;

    /// <summary>
    /// Chuẩn hoá URL ảnh nhưng trả về <c>null</c> khi không hợp lệ — dùng khi muốn
    /// render placeholder bằng markup riêng thay vì ảnh.
    /// </summary>
    public static string? SafeOrNull(string? value) => IsSafe(value) ? Normalize(value!) : null;

    /// <summary>
    /// Chuyển đường dẫn tương đối trong <c>wwwroot</c> thành đường dẫn tuyệt đối
    /// (thêm dấu <c>/</c> ở đầu) để không bị resolve sai theo URL trang.
    /// </summary>
    private static string Normalize(string value)
    {
        var v = value.Trim();

        foreach (var prefix in AllowedRelativePrefixes)
        {
            if (v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return "/" + v;
            }
        }

        return v;
    }

    /// <summary>
    /// Kiểm tra chuỗi có phần mở rộng file ảnh hợp lệ ở cuối không.
    /// </summary>
    private static bool HasImageExtension(string value)
    {
        var idx = value.LastIndexOf('.');
        if (idx < 0 || idx == value.Length - 1)
        {
            return false;
        }

        // Không chấp nhận phần mở rộng chứa ký tự lạ (chống rác kiểu "a.b" từ tiếng Việt).
        var ext = value[(idx + 1)..].Trim().ToLowerInvariant();
        if (ext.Length is < 2 or > 5)
        {
            return false;
        }

        return Regex.IsMatch(ext, "^[a-z0-9]+$");
    }
}
