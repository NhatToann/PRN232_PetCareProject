using System.Text.Json;

namespace PetShop.Services;

/// <summary>
/// Đọc nội dung lỗi từ HTTP response và chuyển thành thông điệp
/// <b>tiếng Việt dễ đọc</b> để hiển thị cho người dùng.
///
/// <para>
/// VÌ SAO CẦN class này:
///
/// <para>
/// Backend trả về 3 dạng body khác nhau tuỳ endpoint:
///
/// </para>
/// <list type="number">
///   <item>
///     <description>
///     <b>ValidationProblemDetails</b> — do <c>[ApiController]</c> tự sinh khi model
///     không hợp lệ (400):
///     <c>{ "errors": { "Password": ["Mật khẩu phải có ít nhất 6 ký tự."] } }</c>.
///     Đây chính là body thô mà user thấy trong báo lỗi trước đây.
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>Convention của controller</b>: <c>{ "message": "..." }</c>.
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>ProblemDetails (RFC 7807)</b> — do <c>ApiExceptionMiddleware</c> sinh:
///     <c>{ "type": ..., "title": ..., "status": ..., "detail": ..., "traceId": ... }</c>.
///     </description>
///   </item>
/// </list>
///
/// <para>
/// Trước đây mỗi client tự đọc body và <b>trả nguyên JSON thô</b> cho UI ⇒
/// người dùng thấy một mảng JSON khó hiểu thay vì thông báo lỗi tiếng Việt.
/// Class này gom logic về 1 chỗ để mọi client trả về message đúng nghĩa.
/// </para>
/// </summary>
public static class ApiErrorMessageReader
{
    /// <summary>
    /// Đọc body của response lỗi và trả về message dễ đọc.
    /// </summary>
    /// <param name="response">Response lỗi (status &gt;= 400).</param>
    /// <param name="cancellationToken">Token hủy.</param>
    /// <returns>
    /// Message tiếng Việt đã rút gọn; nếu body rỗng thì trả về
    /// <c>"API returned {status}."</c>.
    /// </returns>
    public static async Task<string> ReadAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken = default)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return $"API returned {(int)response.StatusCode}.";
        }

        var extracted = TryExtractFromJson(body);
        return extracted ?? body;
    }

    /// <summary>
    /// Rút message từ body JSON. Trả <c>null</c> nếu body không phải JSON hoặc
    /// không chứa trường message nào nhận diện được.
    /// </summary>
    public static string? TryExtractFromJson(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // 1) ValidationProblemDetails — gom TẤT CẢ message lỗi field.
            if (root.TryGetProperty("errors", out var errorsElement)
                && errorsElement.ValueKind == JsonValueKind.Object)
            {
                var validationMessages = new List<string>();

                foreach (var field in errorsElement.EnumerateObject())
                {
                    switch (field.Value.ValueKind)
                    {
                        case JsonValueKind.Array:
                            foreach (var item in field.Value.EnumerateArray())
                            {
                                if (item.ValueKind == JsonValueKind.String)
                                {
                                    var text = item.GetString();
                                    if (!string.IsNullOrWhiteSpace(text))
                                    {
                                        validationMessages.Add(text!);
                                    }
                                }
                            }

                            break;

                        case JsonValueKind.String:
                            var single = field.Value.GetString();
                            if (!string.IsNullOrWhiteSpace(single))
                            {
                                validationMessages.Add(single!);
                            }

                            break;
                    }
                }

                if (validationMessages.Count > 0)
                {
                    return string.Join(" ", validationMessages.Distinct());
                }
            }

            // 2) Convention của controller: { "message": "..." }
            if (root.TryGetProperty("message", out var messageElement)
                && messageElement.ValueKind == JsonValueKind.String)
            {
                var message = messageElement.GetString();
                if (!string.IsNullOrWhiteSpace(message))
                {
                    return message;
                }
            }

            // 3) ProblemDetails (RFC 7807): "detail" là thông điệp chi tiết nhất.
            if (root.TryGetProperty("detail", out var detailElement)
                && detailElement.ValueKind == JsonValueKind.String)
            {
                var detail = detailElement.GetString();
                if (!string.IsNullOrWhiteSpace(detail))
                {
                    return detail;
                }
            }

            if (root.TryGetProperty("title", out var titleElement)
                && titleElement.ValueKind == JsonValueKind.String)
            {
                var title = titleElement.GetString();
                if (!string.IsNullOrWhiteSpace(title))
                {
                    return title;
                }
            }
        }
        catch (JsonException)
        {
            // Body không phải JSON — caller trả nguyên văn.
        }

        return null;
    }
}
