using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PetShop.Data;
using Microsoft.Extensions.Logging;
using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Services
{
    public sealed partial class PayOSService : IPayOSService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<PayOSService> _logger;
        private readonly PayOSOptions _options;
        private readonly ShopPetDatabaseContext _db;
        private readonly IEmailService _emailService;
        private string? _lastError;
        private string? _lastResponse;

        public PayOSService(HttpClient httpClient, IOptions<PayOSOptions> options, ShopPetDatabaseContext db, IEmailService emailService,
            ILogger<PayOSService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _options = options.Value;
            _db = db;
            _emailService = emailService;
        }

        public async Task<PayOSCheckoutResult?> CreatePaymentLinkAsync(CreatePayOSPaymentRequest request, CancellationToken ct = default)
        {
            _lastError = null;
            _lastResponse = null;

            var payload = BuildPaymentPayload(request);
            if (payload is null)
            {
                _lastError = "Invalid payload";
                return null;
            }

            var json = JsonSerializer.Serialize(payload, JsonSerializerOptionsFactory());
            var baseUri = _options.BaseUrl.TrimEnd('/') + "/";
            var fullUrl = new Uri(new Uri(baseUri), "payment-requests").ToString();
            _logger.Line($"[PayOS] Calling URL: {fullUrl}");
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, fullUrl);
            httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");
            httpRequest.Headers.Add("x-client-id", _options.ClientId);
            httpRequest.Headers.Add("x-api-key", _options.ApiKey);

            try
            {
                using var response = await _httpClient.SendAsync(httpRequest, ct);
                var responseText = await response.Content.ReadAsStringAsync(ct);
                _lastResponse = responseText;

                if (!response.IsSuccessStatusCode)
                {
                    _lastError = $"HTTP {(int)response.StatusCode}";
                    return null;
                }

                var root = JsonSerializer.Deserialize<PayOSApiResponse>(responseText, JsonSerializerOptionsFactory());
                if (root is null
                    || !IsSuccessResponseCode(root.Code)
                    || root.Data?.CheckoutUrl is null)
                {
                    _lastError = root?.Desc ?? "Missing checkoutUrl";
                    return null;
                }

                return new PayOSCheckoutResult(root.Data.CheckoutUrl, request.OrderCode, request.Amount, request.Description);
            }
            catch (HttpRequestException ex)
            {
                _lastError = ex.Message;
                if (_options.BaseUrl.Contains("api.payos.vn", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                var fallback = "https://api.payos.vn/v2";
                var fallbackBase = fallback.EndsWith("/") ? fallback : fallback + "/";
                using var fallbackRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(fallbackBase), "payment-requests"));
                fallbackRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");
                fallbackRequest.Headers.Add("x-client-id", _options.ClientId);
                fallbackRequest.Headers.Add("x-api-key", _options.ApiKey);

                using var response = await _httpClient.SendAsync(fallbackRequest, ct);
                var responseText = await response.Content.ReadAsStringAsync(ct);
                _lastResponse = responseText;

                if (!response.IsSuccessStatusCode)
                {
                    _lastError = $"HTTP {(int)response.StatusCode}";
                    return null;
                }

                var root = JsonSerializer.Deserialize<PayOSApiResponse>(responseText, JsonSerializerOptionsFactory());
                if (root is null
                    || !IsSuccessResponseCode(root.Code)
                    || root.Data?.CheckoutUrl is null)
                {
                    _lastError = root?.Desc ?? "Missing checkoutUrl";
                    return null;
                }

                return new PayOSCheckoutResult(root.Data.CheckoutUrl, request.OrderCode, request.Amount, request.Description);
            }
        }

        /// <summary>
        /// Xác thực chữ ký webhook PayOS.
        ///
        /// <para>
        /// <b>Thuật toán đúng (theo tài liệu PayOS + SDK chính thức):</b>
        /// </para>
        /// <list type="number">
        ///   <item>Lấy <c>data</c> (object giao dịch) và <c>signature</c> từ envelope.</item>
        ///   <item>Sắp xếp các key của <c>data</c> theo alphabet.</item>
        ///   <item>Ghép thành <c>key1=value1&amp;key2=value2...</c>
        ///   (giá trị <c>null</c> ⇒ chuỗi rỗng; mảng ⇒ JSON compact).</item>
        ///   <item><c>HMAC-SHA256(checksumKey, data)</c> → hex lowercase.</item>
        ///   <item>So sánh với <c>signature</c> nhận được (không phân biệt hoa/thường).</item>
        /// </list>
        ///
        /// <para>
        /// <b>LỖI ĐÃ SỬA:</b> bản cũ ký trên <b>raw body</b> của cả envelope
        /// ⇒ không bao giờ khớp với chữ ký PayOS gửi (PayOS ký trên <c>data</c>)
        /// ⇒ webhook luôn bị từ chối (401) ⇒ đơn hàng không bao giờ được đánh dấu
        /// "đã thanh toán" khi khách quét QR xong.
        /// </para>
        /// </summary>
        public bool VerifyWebhook(string rawData, string? signature)
        {
            if (string.IsNullOrWhiteSpace(rawData) || string.IsNullOrWhiteSpace(signature))
            {
                return false;
            }

            var computed = ComputeWebhookSignature(rawData);
            if (string.IsNullOrEmpty(computed))
            {
                return false;
            }

            return string.Equals(computed, signature.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Tính chữ ký HMAC-SHA256 của payload webhook theo đúng thuật toán PayOS.
        ///
        /// <para>
        /// Trả về chuỗi rỗng nếu payload không phải envelope PayOS hợp lệ
        /// (thiếu <c>data</c> hoặc không parse được JSON).
        /// </para>
        /// </summary>
        public string ComputeWebhookSignature(string rawData)
        {
            if (string.IsNullOrWhiteSpace(rawData))
            {
                return string.Empty;
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(rawData);
            }
            catch (JsonException)
            {
                return string.Empty;
            }

            using (doc)
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return string.Empty;
                }

                if (!doc.RootElement.TryGetProperty("data", out var dataElement)
                    || dataElement.ValueKind != JsonValueKind.Object)
                {
                    return string.Empty;
                }

                var canonical = BuildCanonicalWebhookData(dataElement);
                return ComputeHmac(canonical, _options.ChecksumKey);
            }
        }

        /// <summary>
        /// Dựng chuỗi canonical của object <c>data</c> để ký:
        /// key sắp xếp alphabet, nối bằng <c>&amp;</c>, giá trị <c>null</c> ⇒ rỗng.
        /// </summary>
        private static string BuildCanonicalWebhookData(JsonElement data)
        {
            var pairs = new List<KeyValuePair<string, string>>();

            foreach (var property in data.EnumerateObject())
            {
                pairs.Add(new KeyValuePair<string, string>(property.Name, ToSignatureValue(property.Value)));
            }

            pairs.Sort(static (a, b) => string.CompareOrdinal(a.Key, b.Key));

            return string.Join("&", pairs.Select(p => $"{p.Key}={p.Value}"));
        }

        /// <summary>
        /// Chuyển giá trị JSON thành chuỗi dùng khi tính chữ ký.
        /// </summary>
        private static string ToSignatureValue(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    return string.Empty;
                case JsonValueKind.String:
                    return element.GetString() ?? string.Empty;
                case JsonValueKind.Number:
                    // Giữ nguyên dạng số gốc (tránh mất ".0" hoặc sai định dạng số thập phân).
                    return element.GetRawText();
                case JsonValueKind.True:
                    return "true";
                case JsonValueKind.False:
                    return "false";
                case JsonValueKind.Array:
                case JsonValueKind.Object:
                    // Mảng/obj ⇒ JSON compact (PayOS dùng dạng này, ví dụ transactions = []).
                    return element.GetRawText();
                default:
                    return element.GetRawText();
            }
        }

        public async Task<PayOSPaymentStatusResult?> GetPaymentStatusAsync(int orderCode, CancellationToken ct = default)
        {
            _lastError = null;
            _lastResponse = null;

            var baseUri = _options.BaseUrl.TrimEnd('/') + "/";
            var fullUrl = new Uri(new Uri(baseUri), $"payment-requests/{orderCode}").ToString();

            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, fullUrl);
            httpRequest.Headers.Add("x-client-id", _options.ClientId);
            httpRequest.Headers.Add("x-api-key", _options.ApiKey);

            try
            {
                using var response = await _httpClient.SendAsync(httpRequest, ct);
                var responseText = await response.Content.ReadAsStringAsync(ct);
                _lastResponse = responseText;

                if (!response.IsSuccessStatusCode)
                {
                    _lastError = $"HTTP {(int)response.StatusCode}";
                    return null;
                }

                var root = JsonSerializer.Deserialize<PayOSApiResponse>(responseText, JsonSerializerOptionsFactory());
                if (root?.Data is null)
                {
                    _lastError = "No data in response";
                    return null;
                }

            var status = root.Data.Status ?? "unknown";
            var isPaid = string.Equals(status, "PAID", StringComparison.OrdinalIgnoreCase)
                || IsSuccessCode(status)
                || string.Equals(status, "00", StringComparison.OrdinalIgnoreCase);

                return new PayOSPaymentStatusResult(
                    orderCode,
                    status,
                    isPaid,
                    root.Data.Amount,
                    root.Data.CheckoutUrl);
            }
            catch (HttpRequestException ex)
            {
                _lastError = ex.Message;
                return null;
            }
        }

            /// <summary>
        /// Kiểm tra mã kết quả PayOS có báo thành công hay không.
        ///
    /// <para>
    /// PayOS trả <c>code = "00"</c> (chuỗi) trong hầu hết trường hợp, nhưng biến thể
    /// sandbox/legacy trả <c>code = 0</c> (số). Cả hai đều là "thành công".
    /// </para>
        /// </summary>
        private static bool IsSuccessCode(string? code)
        {
     if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

            var trimmed = code.Trim();
   return string.Equals(trimmed, "00", StringComparison.Ordinal)
   || string.Equals(trimmed, "0", StringComparison.Ordinal);
        }

      /// <summary>
        /// Mã trả về của API tạo payment link: "00" (chuỗi), 0 (số) hoặc "200" (một số bản).
        /// </summary>
        private static bool IsSuccessResponseCode(string? code)
  {
            if (string.IsNullOrWhiteSpace(code))
            {
  return false;
    }

     var trimmed = code.Trim();
            return string.Equals(trimmed, "00", StringComparison.Ordinal)
    || string.Equals(trimmed, "0", StringComparison.Ordinal)
      || string.Equals(trimmed, "200", StringComparison.Ordinal);
        }

    public async Task<PayOSWebhookResult> HandleWebhookAsync(string rawData, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(rawData))
            {
                return new PayOSWebhookResult(false, "Empty payload");
            }

            PayOSWebhookPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<PayOSWebhookPayload>(rawData, JsonSerializerOptionsFactory());
            }
            catch
            {
                return new PayOSWebhookResult(false, "Invalid payload");
            }

            var data = payload?.Data;
            if (data is null)
            {
                return new PayOSWebhookResult(false, "Missing data");
            }

            // Xác định trạng thái thanh toán.
            //
            // Mẫu webhook chính thức của PayOS: { code:"00", desc:"success", success:true,
            // data:{ orderCode, amount, description, accountNumber, reference,
            //         transactionDateTime, currency, paymentLinkId, code:"00", desc:"Thành công", ... } }
            // ⇒ KHÔNG có field "status" trong data.
            //
            // Vì vậy: đã thanh toán ⇔ success == true HOẶC data.code == "00" HOẶC
            // data.status == "PAID". Ngược lại (success == false) ⇒ đơn bị hủy/hết hạn.
            //
            // LƯU Ý: biến thể PayOS/legacy gửi code dạng SỐ (0) thay vì chuỗi ("00").
            // Chuỗi hoá về "0" ⇒ vẫn phải được coi là THÀNH CÔNG (tương đương "00").
            var status = data.Status ?? data.Code ?? payload?.Code;
            var isPaid = payload?.Success == true
                || IsSuccessCode(data.Code)
                || string.Equals(status, "PAID", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "00", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "0", StringComparison.Ordinal)
                || string.Equals(status, "success", StringComparison.OrdinalIgnoreCase);

            _logger.Line($"[PayOS] Webhook received. orderCode: {data.OrderCode}, amount: {data.Amount?.ToString() ?? "(null)"}, status: {status ?? "(null)"}, success: {payload?.Success}");

            var payment = await _db.Payments
                .AsTracking()
                .FirstOrDefaultAsync(p => p.PayosOrderCode == data.OrderCode, ct);
            if (payment is null)
            {
                _logger.Line("[PayOS] Payment not found in DB.");
                return new PayOSWebhookResult(false, "Payment not found");
            }

            _logger.Line($"[PayOS] Payment found. Type: {payment.PaymentType}, Status: {payment.PaymentStatus}, RefId: {payment.ReferenceId}");

            // Đối chiếu SỐ TIỀN: webhook có amount ⇒ phải khớp số tiền ta gửi cho PayOS.
            // Không khớp ⇒ từ chối (không đánh dấu đã thanh toán) để tránh đánh dấu đơn
            // đã thanh toán với số tiền sai.
            if (data.Amount.HasValue && Math.Abs((decimal)data.Amount.Value - payment.Amount) > 0.01m)
            {
                _logger.Line(
                    $"[PayOS] Amount mismatch. Webhook={data.Amount.Value}, DB={payment.Amount}. Rejecting.");
                return new PayOSWebhookResult(false, "Amount mismatch");
            }

            // Khi khach hang huy QR hoac het han, Cap nhat Payment & Order
            if (!isPaid)
            {
                _logger.Line($"[PayOS] Payment cancelled/expired. Status: {status}. Updating to cancelled.");
                payment.PaymentStatus = "cancelled";
                await _db.SaveChangesAsync(ct);

                if (string.Equals(payment.PaymentType, "order", StringComparison.OrdinalIgnoreCase))
                {
                    await CancelOrderAsync(payment.ReferenceId, ct);
                }
                else if (string.Equals(payment.PaymentType, "spa", StringComparison.OrdinalIgnoreCase))
                {
                    await UpdateSpaBookingsStatusAsync(payment, "Đã hủy", ct);
                    await SendSpaInvoicesEmailAsync(payment, "Đã hủy", ct);
                    _logger.Line("[PayOS] Spa cancellation email sent.");
                }
                else if (string.Equals(payment.PaymentType, "boarding", StringComparison.OrdinalIgnoreCase))
                {
                    await UpdateBoardingBookingStatusAsync(payment.ReferenceId, "Đã hủy", ct);
                }

                return new PayOSWebhookResult(true, "Cancelled");
            }

            var wasPending = string.Equals(payment.PaymentStatus, "pending", StringComparison.OrdinalIgnoreCase);
            if (wasPending)
            {
                payment.PaymentStatus = "paid";
                payment.PaidAt = DateTime.Now;
                await _db.SaveChangesAsync(ct);
                _logger.Line("[PayOS] Payment updated to paid.");
            }

            if (string.Equals(payment.PaymentType, "spa", StringComparison.OrdinalIgnoreCase))
            {
                await UpdateSpaBookingsStatusAsync(payment, "Đã thanh toán", ct);
                _logger.Line("[PayOS] Spa booking status updated.");
                if (wasPending)
                {
                    await SendSpaInvoicesEmailAsync(payment, "Đã thanh toán", ct);
                    _logger.Line("[PayOS] Spa invoice email sent.");
                }
            }
            else if (string.Equals(payment.PaymentType, "order", StringComparison.OrdinalIgnoreCase))
            {
                await UpdateOrderStatusAsync(payment.ReferenceId, ct);
                _logger.Line("[PayOS] Order status updated.");
            }
            else if (string.Equals(payment.PaymentType, "boarding", StringComparison.OrdinalIgnoreCase))
            {
                await UpdateBoardingBookingStatusAsync(payment.ReferenceId, "Đã thanh toán", ct);
                _logger.Line("[PayOS] Boarding booking status updated.");
            }

            return new PayOSWebhookResult(true, "OK");
        }

        public async Task<PayOSWebhookResult> HandleWebhookForStatusAsync(int orderCode, PayOSPaymentStatusResult status, CancellationToken ct = default)
        {
            if (!status.IsPaid)
            {
                return new PayOSWebhookResult(true, "No action for non-paid status");
            }

            var payment = await _db.Payments
                .AsTracking()
                .FirstOrDefaultAsync(p => p.PayosOrderCode == orderCode, ct);
            if (payment is null)
            {
                return new PayOSWebhookResult(false, "Payment not found");
            }

            var wasPending = string.Equals(payment.PaymentStatus, "pending", StringComparison.OrdinalIgnoreCase);
            if (wasPending)
            {
                payment.PaymentStatus = "paid";
                payment.PaidAt = DateTime.Now;
                await _db.SaveChangesAsync(ct);
            }

            if (string.Equals(payment.PaymentType, "spa", StringComparison.OrdinalIgnoreCase))
            {
                await UpdateSpaBookingsStatusAsync(payment, "Đã thanh toán", ct);
                if (wasPending)
                {
                    await SendSpaInvoicesEmailAsync(payment, "Đã thanh toán", ct);
                }
            }
            else if (string.Equals(payment.PaymentType, "order", StringComparison.OrdinalIgnoreCase))
            {
                await UpdateOrderStatusAsync(payment.ReferenceId, ct);
            }
            else if (string.Equals(payment.PaymentType, "boarding", StringComparison.OrdinalIgnoreCase))
            {
                await UpdateBoardingBookingStatusAsync(payment.ReferenceId, "Đã thanh toán", ct);
            }

            return new PayOSWebhookResult(true, "OK");
        }

        public string NormalizeDescription(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var step1 = text.Replace('đ', 'd').Replace('Đ', 'D');
            var normalized = step1.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(c);
                }
            }

            var cleaned = Regex.Replace(sb.ToString(), "[^a-zA-Z0-9 #]", string.Empty);
            cleaned = cleaned.Trim();
            if (cleaned.Length > 25)
            {
                cleaned = cleaned[..25].Trim();
            }

            return cleaned;
        }

        private PayOSPaymentRequest? BuildPaymentPayload(CreatePayOSPaymentRequest request)
        {
            if (request.Amount <= 0)
            {
                return null;
            }

            var amount = Convert.ToInt32(Math.Round(request.Amount));
            var description = NormalizeDescription(request.Description);

            var payload = new PayOSPaymentRequest
            {
                Amount = amount,
                CancelUrl = request.CancelUrl,
                Description = description,
                Items = Array.Empty<object>(),
                OrderCode = request.OrderCode,
                ReturnUrl = request.ReturnUrl
            };

            var signature = GenerateSignature(payload);
            if (string.IsNullOrWhiteSpace(signature))
            {
                return null;
            }

            payload.Signature = signature;
            return payload;
        }

        private string GenerateSignature(PayOSPaymentRequest payload)
        {
            var sorted = new SortedDictionary<string, string>
            {
                ["amount"] = payload.Amount.ToString(),
                ["cancelUrl"] = payload.CancelUrl,
                ["description"] = payload.Description,
                ["orderCode"] = payload.OrderCode.ToString(),
                ["returnUrl"] = payload.ReturnUrl
            };

            var data = string.Join("&", sorted.Select(kv => $"{kv.Key}={kv.Value}"));
            return ComputeHmac(data, _options.ChecksumKey);
        }

        private static string ComputeHmac(string data, string key)
        {
            var keyBytes = Encoding.UTF8.GetBytes(key);
            using var hmac = new HMACSHA256(keyBytes);
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
            return ConvertToHex(hash);
        }

        private static string ConvertToHex(byte[] hash)
        {
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }

        private static JsonSerializerOptions JsonSerializerOptionsFactory()
        {
            return new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Converters = { new FlexibleStringConverter() }
            };
        }

        public string? GetLastError() => _lastError;

        public string? GetLastResponse() => _lastResponse;

        private sealed class PayOSApiResponse
        {
      [JsonConverter(typeof(FlexibleStringConverter))]
            public string? Code { get; set; }

            [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Desc { get; set; }
    public PayOSApiData? Data { get; set; }
        }

        private sealed class PayOSApiData
        {
            public string? CheckoutUrl { get; set; }

       [JsonConverter(typeof(FlexibleStringConverter))]
        public string? Status { get; set; }
       public int Amount { get; set; }
        }

        private sealed class PayOSPaymentRequest
        {
            [JsonPropertyName("amount")]
            public int Amount { get; set; }

            [JsonPropertyName("cancelUrl")]
            public string CancelUrl { get; set; } = string.Empty;

            [JsonPropertyName("description")]
            public string Description { get; set; } = string.Empty;

            [JsonPropertyName("items")]
            public object[] Items { get; set; } = Array.Empty<object>();

            [JsonPropertyName("orderCode")]
            public int OrderCode { get; set; }

            [JsonPropertyName("returnUrl")]
            public string ReturnUrl { get; set; } = string.Empty;

            [JsonPropertyName("signature")]
            public string? Signature { get; set; }
        }
    }
}