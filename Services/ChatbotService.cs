using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Services
{
    /// <summary>
    /// Chatbot AI: phân tích intent → dựng context từ DB → system prompt → gọi Gemini.
    /// Port nguyên văn từ OJT (BackEnd/Services/ChatbotService.cs).
    /// Truy vấn DB thực hiện qua IChatbotRepository (slide Ch02 3-layer).
    /// </summary>
    public class ChatbotService : IChatbotService
    {
        private readonly IChatbotRepository _repo;
        private readonly ChatbotOptions _options;
        private readonly IAiProviderService _aiProvider;
        private readonly ILogger<ChatbotService> _logger;
        private readonly ChatbotIntentCache _intentCache;

        public ChatbotService(
            IChatbotRepository repo,
            IOptions<ChatbotOptions> options,
            IAiProviderService aiProvider,
            ILogger<ChatbotService> logger,
            ChatbotIntentCache intentCache)
        {
            _repo = repo;
            _options = options.Value;
            _aiProvider = aiProvider;
            _logger = logger;
            _intentCache = intentCache;
        }

        /// <summary>
        /// Định dạng số tiền theo chuẩn VND (dấu phân cách nghìn kiểu Việt + ký hiệu ₫).
        /// Trước đây dùng <c>{value:N0}đ</c> với culture **mặc định của server** (tiếng Anh)
        /// ⇒ ra "35,000đ" (dấu phân cách Mỹ) lẫn với "35.000 ₫" ở các trang khác.
        /// </summary>
        private static string Vnd(decimal value) => PetShop.Components.Shared.Format.Vnd(value);

        public async Task<ChatMessageResponseDto> GetResponseAsync(
            ChatMessageRequestDto request,
            CancellationToken ct = default)
        {
            try
            {
                // Mỗi lượt hội thoại đều đi qua Gemini. Context được nạp mới từ DB
                // để câu trả lời luôn dựa trên tài khoản và dữ liệu cửa hàng hiện tại.
                var context = await GetContextAsync(request.CustomerId, ct);
                var systemPrompt = await BuildSystemPromptAsync(context, ct);
                var answer = await CallAiProviderAsync(
                    systemPrompt,
                    BuildMessagesForAi(request),
                    ct);

                return new ChatMessageResponseDto
                {
                    Reply = answer,
                    Source = "ai+database",
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (AiProviderException ex)
            {
                _logger.LogWarning(ex, "Gemini không trả lời được câu hỏi ({Kind})", ex.Kind);
                return new ChatMessageResponseDto
                {
                    Reply = ex.UserMessage,
                    Source = "fallback",
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Không thể tạo câu trả lời chatbot từ Gemini và dữ liệu DB");
                return new ChatMessageResponseDto
                {
                    Reply = "Xin lỗi, hiện mình chưa thể xử lý câu hỏi. Bạn thử lại sau nhé.",
                    Source = "fallback",
                    Timestamp = DateTime.UtcNow
                };
            }
        }

        // Giữ luồng cũ trong source để tham khảo khi cần khôi phục từng intent;
        // entry point đang dùng là GetResponseAsync ở trên.
        private async Task<ChatMessageResponseDto> GetResponseLegacyAsync(
            ChatMessageRequestDto request,
            CancellationToken ct = default)
        {
            var message = request.Message?.ToLower() ?? "";

            // 0. FAST-PATH CỤC BỘ — 0 request Gemini.
            //
            //    Câu hỏi về sản phẩm có ràng buộc giá/từ khoá rõ ràng ("món nào dưới
            //    20k", "giá thức ăn cho chó") được trả lời THẲNG từ SQL.
            //
            //    VÌ SAO ĐẶT Ở ĐẦU (trước cả phân tích intent):
            //      Gemini free tier chỉ 20 request/ngày. Kiến trúc cũ tốn 2 request
            //      cho MỖI câu hỏi (1 phân tích intent + 1 sinh câu trả lời) ⇒ hết
            //      quota sau ~10 câu ⇒ chatbot "không hoạt động".
            //      Ở đây câu hỏi sản phẩm tốn 0 request, kết quả lại CHÍNH XÁC
            //      tuyệt đối (Gemini hay bịa tên/giá sản phẩm không tồn tại).
            var localProductReply = await TryHandleProductQuestionLocallyAsync(request, ct);
            if (localProductReply is not null)
            {
                return localProductReply;
            }

            // 1. Phân tích intent.
            //
            // THỨ TỰ ưu tiên (tối ưu quota + độ trễ):
            //   a) Fast-path cục bộ (không tốn request AI) cho câu có mẫu rõ.
            //   b) Cache intent (câu hỏi lặp lại).
            //   c) Gọi AI (tốn 1 request Gemini).
            //
            // Nhờ vậy mỗi câu hỏi thường chỉ tốn 1 request thay vì 2 ⇒ Gemini
            // free tier (20 request/ngày) phục vụ được gấp đôi số câu hỏi.
            AiIntentResult aiIntent;
            var aiUnavailable = false;
            try
            {
                aiIntent = await ResolveIntentAsync(request.Message, ct);
            }
            catch (AiProviderException ex)
            {
                // Hết quota / rate limit / timeout LÚC PHÂN TÍCH INTENT.
                // KHÔNG để cả chatbot chết theo ⇒ hạ xuống intent "unknown"
                // và tiếp tục trả lời bằng DỮ LIỆU DB THẬT.
                _logger.LogWarning(ex, "[ChatbotService] Phân tích intent thất bại, chuyển sang trả lời từ DB: {Kind}", ex.Kind);
                aiIntent = new AiIntentResult { Intent = "unknown" };

                // Đánh dấu AI KHÔNG khả dụng ⇒ bỏ qua lời gọi sinh câu trả lời
                // phía dưới (sẽ chắc chắn fail và tốn thêm 1 request vô ích).
                aiUnavailable = true;
            }

            // 2. Xử lý các intent tĩnh (tự giới thiệu)
            if (aiIntent.Intent == "introduction")
            {
                return await BuildIntroductionReplyAsync(ct);
            }

            // 2b. Thông tin liên hệ của shop — lấy thẳng từ SystemSettings.
            if (aiIntent.Intent == "shop_contact")
            {
                return await BuildShopContactReplyAsync(ct);
            }

            // 2c. Gộp thêm tín hiệu CỤC BỘ (giá / loại sản phẩm / loài thú) vào intent.
            //
            //     VÌ SAO CẦN (lỗi thật 11/11/2026):
            //       - Câu hỏi nối tiếp ("chỉ 1 món?", "còn món nào nữa?") KHÔNG
            //         chứa điều kiện ⇒ intent từ AI rơi về "product_count" với
            //         tham số rỗng ⇒ chatbot đếm TOÀN BỘ sản phẩm (42) và trả về
            //         câu template cố định ⇒ người dùng tưởng bị hardcode.
            //       - Gemini đôi lúc quên mất tham số giá trong JSON ⇒ cũng dẫn tới
            //         đếm sai (tổng số sản phẩm thay vì số món dưới 20k).
            //     ⇒ Gộp tín hiệu parse cục bộ (đáng tin cậy, không tốn quota)
            //       từ câu hiện tại VÀ từ lượt hỏi gần nhất trong lịch sử.
            EnrichIntentWithLocalSignals(aiIntent, request);

            // 3. Xử lý product_count (đếm sản phẩm theo điều kiện)
            if (aiIntent.Intent == "product_count")
            {
                aiIntent.Parameters.TryGetValue("product_type", out var type);
                aiIntent.Parameters.TryGetValue("pet", out var pet);

                var (minPrice, maxPrice, maxInclusive) = ReadPriceParameters(aiIntent.Parameters);

                var count = await _repo.CountProductsAsync(
                    string.IsNullOrEmpty(type) ? null : type,
                    string.IsNullOrEmpty(pet) ? null : pet,
                    minPrice,
                    maxPrice,
                    maxInclusive,
                    ct);

                // Mô tả điều kiện để câu trả lời KHÔNG bị chung chung — người dùng
                // phải thấy đúng điều mình vừa hỏi (vd "dưới 20.000 ₫").
                var condition = BuildProductConditionDescription(type, pet, minPrice, maxPrice, maxInclusive);

                // Liệt kê vài sản phẩm thật để câu trả lời có căn cứ (không chỉ là
                // một con số chung chung).
                var samples = await _repo.FindProductsByPriceRangeAsync(
                    minPrice,
                    maxPrice,
                    maxInclusive,
                    string.IsNullOrEmpty(type) ? null : type,
                    string.IsNullOrEmpty(pet) ? null : pet,
                    10,
                    ct);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"🐾 Hiện cửa hàng đang có {count} sản phẩm{condition}.");

                if (samples.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Một vài món tiêu biểu:");
                    foreach (var p in samples)
                    {
                        sb.AppendLine($"• {p.Name} — {Vnd(p.Price)} (Còn {p.StockQuantity})");
                    }
                }

                return new ChatMessageResponseDto
                {
                    Reply = sb.ToString().Trim(),
                    Source = "database"
                };
            }

            // 3b. Lọc sản phẩm theo khoảng giá ("món nào dưới 20k").
            //     Trả lời THẲNG danh sách từ DB ⇒ chính xác, không bị AI bịa giá/tên.
            if (aiIntent.Intent == "product_price_filter")
            {
                return await BuildPriceFilteredProductReplyAsync(aiIntent, ct);
            }

            // 3b2. Tìm sản phẩm theo từ khoá ("giá thức ăn cho chó").
            //      Trả lời THẲNG từ DB ⇒ tiết kiệm quota Gemini + không bị bịa giá.
            if (aiIntent.Intent == "product_search")
            {
                var searchReply = await BuildProductSearchReplyAsync(request.Message ?? string.Empty, aiIntent, ct);
                if (searchReply is not null)
                {
                    return searchReply;
                }
            }

            // 3c. Dịch vụ — trả lời THẲNG từ DB (danh sách dịch vụ thật).
            //
            //     VÌ SAO CẦN: trước đây intent "service_inquiry" KHÔNG có nhánh xử
            //     lý cục bộ ⇒ rơi thẳng vào đường gọi Gemini. Khi Gemini hết quota
            //     ⇒ fallback trả lẫn "phòng lưu trú" vào câu trả lời về dịch vụ
            //     ⇒ người dùng thấy câu trả lời "lạc đề" ⇒ tưởng chatbot hỏng.
            if (aiIntent.Intent == "service_inquiry")
            {
                var services = await _repo.GetActiveServicesAsync(20, ct);

                if (services.Count > 0)
                {
                    var sbSvc = new System.Text.StringBuilder();
                    sbSvc.AppendLine("🛁 Dịch vụ của PetShop hiện có:");

                    foreach (var s in services)
                    {
                        sbSvc.AppendLine($"• {s.Name} — {Vnd(s.Price)} ({s.Duration} phút)");
                    }

                    sbSvc.AppendLine();
                    sbSvc.AppendLine("Bạn muốn mình hướng dẫn đặt lịch dịch vụ nào cho bé cưng nhé? 🐾");

                    return new ChatMessageResponseDto
                    {
                        Reply = sbSvc.ToString().Trim(),
                        Source = "database",
                        Timestamp = DateTime.UtcNow
                    };
                }
            }

            // 3d. Trạng thái đơn hàng — trả lời thẳng từ DB.
            if (aiIntent.Intent == "order_status")
            {
                if (request.CustomerId.HasValue)
                {
                    var orders = await _repo.GetCustomerOrdersAsync(request.CustomerId.Value, 5, ct);

                    if (orders.Count > 0)
                    {
                        var sbOrd = new System.Text.StringBuilder();
                        sbOrd.AppendLine("📦 Đơn hàng gần đây của bạn:");

                        foreach (var o in orders)
                        {
                            sbOrd.AppendLine($"• Đơn #{o.OrderId} — {o.Status} — {Vnd(o.TotalAmount)}");
                        }

                        return new ChatMessageResponseDto
                        {
                            Reply = sbOrd.ToString().Trim(),
                            Source = "database",
                            Timestamp = DateTime.UtcNow
                        };
                    }
                }

                return new ChatMessageResponseDto
                {
                    Reply = "Bạn cần đăng nhập để mình kiểm tra đơn hàng giúp bạn nhé. Nếu bạn có mã đơn, hãy gửi mã đơn cho mình! 🐾",
                    Source = "database",
                    Timestamp = DateTime.UtcNow
                };
            }

            // 3e. Đặt lịch — hướng dẫn từ danh sách dịch vụ thật.
            if (aiIntent.Intent == "booking")
            {
                var services = await _repo.GetActiveServicesAsync(10, ct);

                var sbBk = new System.Text.StringBuilder();
                sbBk.AppendLine("📅 Mình có thể giúp bạn đặt lịch cho các dịch vụ sau:");

                foreach (var s in services)
                {
                    sbBk.AppendLine($"• {s.Name} — {Vnd(s.Price)} ({s.Duration} phút)");
                }

                sbBk.AppendLine();
                sbBk.AppendLine("Bạn muốn đặt dịch vụ nào, cho bé nào vào ngày nào nhé? Mình sẽ hỗ trợ bạn đặt lịch! 🐾");

                return new ChatMessageResponseDto
                {
                    Reply = sbBk.ToString().Trim(),
                    Source = "database",
                    Timestamp = DateTime.UtcNow
                };
            }

            // 3f. Fast-path CỤC BỘ cho câu hỏi về sản phẩm — TRẢ THẲNG TỪ DB,
            //     KHÔNG gọi Gemini.
            //
            //     VÌ SAO CẦN (lỗi "chatbot Gemini không hoạt động" 11/11/2026):
            //       (1) Gemini free tier chỉ 20 request/ngày. Kiến trúc cũ tốn 2
            //           request cho MỖI câu hỏi (1 phân tích intent + 1 sinh câu
            //           trả lời) ⇒ hết quota sau ~10 câu ⇒ chatbot "chết".
            //       (2) Gemini rất dễ BỊA tên/giá sản phẩm (ví dụ trả "thuốc cho
            //           chó" — sản phẩm không tồn tại trong DB).
            //     ⇒ Câu hỏi về sản phẩm trả lời 100% từ SQL: đúng tuyệt đối,
            //       0 request Gemini, tức thì.
            //
            //     Điều kiện: câu hỏi có nhắc SẢN PHẨM + CHI TIẾT (giá/tồn kho).
            if (IsProductDetailQuestion(request.Message))
            {
                var fastReply = await BuildProductSearchReplyAsync(request.Message ?? string.Empty, aiIntent, ct);
                if (fastReply is not null)
                {
                    return fastReply;
                }
            }

            // 3g. AI ĐÃ HẾT QUOTA (đã thử phân tích intent và bị 429) ⇒ KHÔNG thử
            //     gọi AI lần nữa để sinh câu trả lời (chắc chắn fail + tốn thêm
            //     1 request vô ích) ⇒ trả lời luôn bằng DỮ LIỆU DB THẬT.
            if (aiUnavailable)
            {
                var dbOnly = await BuildDatabaseFallbackReplyAsync(request, aiIntent, ct);
                if (dbOnly is not null)
                {
                    return dbOnly;
                }

                return new ChatMessageResponseDto
                {
                    Reply = "🐾 Mình đang tạm thời giới hạn phần trò chuyện tự nhiên, nhưng vẫn tra cứu được dữ liệu cửa hàng cho bạn. Bạn hỏi về sản phẩm, giá, dịch vụ hay đặt lịch nhé!",
                    Source = "database",
                    Timestamp = DateTime.UtcNow
                };
            }

            // 4. Nếu là các intent khác (product_search, pet_info, support...),
            // chúng ta sẽ nạp toàn bộ context DB cho AI trả lời tự nhiên.
            try
            {
                var context = await GetContextAsync(request.CustomerId, ct);
                var systemPrompt = await BuildSystemPromptAsync(context, ct);

                // Bơm thêm thông tin Intent AI vừa tìm được để AI phía sau trả lời chuẩn hơn
                systemPrompt += $"\n[Thông tin hỗ trợ]\nIntent người dùng: {aiIntent.Intent}";
                if (aiIntent.Parameters.Any())
                {
                    systemPrompt += $"\nTham số trích xuất: {System.Text.Json.JsonSerializer.Serialize(aiIntent.Parameters)}";
                }

                var messagesForAi = BuildMessagesForAi(request);

                var aiResponse = await CallAiProviderAsync(
                    systemPrompt,
                    messagesForAi,
                    ct);

                return new ChatMessageResponseDto
                {
                    Reply = aiResponse,
                    Source = "ai",
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (AiProviderException ex)
            {
                // Quota hết / rate limit / timeout ⇒ KHÔNG báo lỗi chết chóc.
                // Trả lời bằng DỮ LIỆU DB THẬT (sản phẩm / dịch vụ / địa chỉ shop)
                // để chatbot vẫn hữu ích khi AI tạm không khả dụng.
                _logger.LogWarning(ex, "[ChatbotService] Nhà cung cấp AI lỗi ({Kind}), chuyển sang trả lời từ DB", ex.Kind);

                var dbReply = await BuildDatabaseFallbackReplyAsync(request, aiIntent, ct);
                if (dbReply is not null)
                {
                    return dbReply;
                }

                return new ChatMessageResponseDto
                {
                    Reply = ex.UserMessage,
                    Source = "fallback",
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling AI provider");
                return new ChatMessageResponseDto
                {
                    Reply = "Xin lỗi, trợ lý đang gặp sự cố. Vui lòng thử lại sau ít phút.",
                    Source = "fallback",
                    Timestamp = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Câu trả lời cho intent <c>introduction</c> — TỰ GIỚI THIỆU DỰA TRÊN DỮ LIỆU THẬT.
        ///
        /// <para>
        /// <b>VÌ SAO KHÔNG HARDCODE (sửa lỗi 11/11/2026):</b> trước đây câu trả lời
        /// "bạn là ai" là một chuỗi cố định ⇒ người dùng nghĩ chatbot không có dữ
        /// liệu. Nay số liệu (số sản phẩm, số dịch vụ, hotline) lấy thẳng từ DB.
        /// </para>
        /// </summary>
        private async Task<ChatMessageResponseDto> BuildIntroductionReplyAsync(CancellationToken ct)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("🐾 Mình là trợ lý ảo của PetShop — mình trả lời dựa trên dữ liệu cửa hàng thật.");

            try
            {
                var productCount = await _repo.CountProductsAsync(null, null, null, null, true, ct);
                var services = await _repo.GetActiveServicesAsync(5, ct);

                sb.AppendLine($"• 📦 Cửa hàng hiện có {productCount} sản phẩm đang bán.");

                if (services.Count > 0)
                {
                    sb.AppendLine($"• 🛁 Có {services.Count}+ dịch vụ chăm sóc, ví dụ: {string.Join(", ", services.Take(3).Select(s => s.Name))}.");
                }

                var contact = await _repo.GetShopContactSettingsAsync(ct);
                if (contact.TryGetValue("ShopHotline", out var hotline) && !string.IsNullOrWhiteSpace(hotline))
                {
                    sb.AppendLine($"• 📞 Hotline: {hotline.Trim()}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ChatbotService] Không lấy được số liệu tự giới thiệu");
            }

            sb.AppendLine();
            sb.AppendLine("Mình có thể giúp bạn: tìm sản phẩm theo giá/loại, xem giá, liệt kê dịch vụ, đặt lịch chăm sóc và kiểm tra đơn hàng. Bạn cứ hỏi nhé! 🐾");

            return new ChatMessageResponseDto
            {
                Reply = sb.ToString().Trim(),
                Source = "database",
                Timestamp = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Gộp tín hiệu CỤC BỘ (ràng buộc giá / loại sản phẩm / loài thú) vào intent
        /// đã phân định, kể cả khi intent đến từ Gemini.
        ///
        /// <para>
        /// <b>VÌ SAO CẦN (lỗi thật 11/11/2026):</b>
        /// </para>
        /// <list type="bullet">
        ///   <item>
        ///     Câu hỏi nối tiếp ("chỉ 1 món?", "còn món nào nữa?") không chứa điều
        ///     kiện ⇒ chatbot mất ngữ cảnh và đếm TOÀN BỘ sản phẩm (42) ⇒ câu
        ///     trả lời trông như template cố định.
        ///   </item>
        ///   <item>
        ///     Gemini đôi lúc quên tham số giá trong JSON ⇒ đếm sai.
        ///   </item>
        /// </list>
        ///
        /// <para>
        /// Nguồn tín hiệu: (1) câu hỏi hiện tại, (2) lượt hỏi gần nhất trong lịch sử
        /// hội thoại (do widget gửi kèm). Tín hiệu cục bộ luôn ĐƯỢC ƯU TIÊN vì
        /// parse bằng regex chắc chắn hơn việc đoán của model.
        /// </para>
        /// </summary>
        private static void EnrichIntentWithLocalSignals(AiIntentResult intent, ChatMessageRequestDto request)
        {
            intent.Parameters ??= new Dictionary<string, string>(StringComparer.Ordinal);

            // 1. Tín hiệu từ câu hỏi hiện tại (ưu tiên cao nhất).
            ApplyLocalSignals(intent, request.Message);

            // 2. Tín hiệu từ lượt hỏi gần nhất trong lịch sử (chỉ bổ sung cho
            //    tham số còn thiếu ⇒ không ghi đè thông tin mới hơn).
            //
            //    DUYỆT TỪ MỚI NHẤT ĐẾN CŨ NHẤT (sửa lỗi 11/11/2026): trước đây chỉ
            //    lấy ĐÚNG 1 lượt user gần nhất ⇒ câu nối tiếp ("chỉ 1 món?") bị mất
            //    ràng buộc giá của câu hỏi trước đó ⇒ chatbot đếm toàn bộ sản phẩm
            //    trong shop (42) ⇒ câu trả lời trông như template cố định.
            var historyUserMessages = request.History?
                .Where(h => string.Equals(h.Role, "user", StringComparison.OrdinalIgnoreCase))
                .Select(h => h.Content)
                .Where(c => !string.IsNullOrWhiteSpace(c)
                    && !string.Equals(c.Trim(), request.Message?.Trim(), StringComparison.OrdinalIgnoreCase))
                .Reverse()
                .ToList();

            if (historyUserMessages is not null)
            {
                foreach (var previousMessage in historyUserMessages)
                {
                    ApplyLocalSignals(intent, previousMessage, onlyIfMissing: true);
                }
            }
        }

        /// <summary>
        /// Trích tín hiệu cục bộ từ một câu hỏi rồi ghi vào <c>intent.Parameters</c>.
        /// </summary>
        /// <param name="intent">Intent đang xử lý (được cập nhật tại chỗ).</param>
        /// <param name="message">Câu hỏi cần trích tín hiệu.</param>
        /// <param name="onlyIfMissing">
        /// <c>true</c> ⇒ chỉ điền tham số còn thiếu (dùng cho lịch sử);
        /// <c>false</c> ⇒ ghi đè (dùng cho câu hỏi hiện tại).
        /// </param>
        private static void ApplyLocalSignals(AiIntentResult intent, string? message, bool onlyIfMissing = false)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            // --- Ràng buộc giá ---
            //     Parse trên bản GỐC (chưa bỏ dấu) vì đơn vị "nghìn/triệu" có dấu.
            var price = PriceConstraintParser.Parse(message);
            if (price.HasValue)
            {
                if (!onlyIfMissing || !intent.Parameters.ContainsKey("min_price"))
                {
                    if (price.Min.HasValue)
                    {
                        intent.Parameters["min_price"] = price.Min.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                }

                if (!onlyIfMissing || !intent.Parameters.ContainsKey("max_price"))
                {
                    if (price.Max.HasValue)
                    {
                        intent.Parameters["max_price"] = price.Max.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        intent.Parameters["max_price_inclusive"] = price.MaxInclusive ? "true" : "false";
                    }
                }
            }

            // --- Loại sản phẩm / loài thú ---
            var (productType, pet) = ExtractProductTypeAndPetLocal(message);

            if (productType is not null && (!onlyIfMissing || !intent.Parameters.ContainsKey("product_type")))
            {
                intent.Parameters["product_type"] = productType;
            }

            if (pet is not null && (!onlyIfMissing || !intent.Parameters.ContainsKey("pet")))
            {
                intent.Parameters["pet"] = pet;
            }
        }

        /// <summary>
        /// Trích loại sản phẩm + loài thú từ câu hỏi (bản sao cục bộ của
        /// <see cref="LocalIntentDetector"/> để tránh phụ thuộc class khác).
        /// </summary>
    private static (string? ProductType, string? Pet) ExtractProductTypeAndPetLocal(string text)
    {
        string? productType = null;
        string? pet = null;

        var folded = TextNormalizer.Fold(text);

        if (ContainsAnyLocal(text, "thức ăn", "pate", "hạt"))
        {
            productType = "thức ăn";
        }
        else if (ContainsAnyLocal(text, "đồ chơi"))
        {
            productType = "đồ chơi";
        }
        else if (ContainsAnyLocal(text, "phụ kiện", "dây cổ", "áo cho"))
        {
            productType = "phụ kiện";
        }
        else if (ContainsAnyLocal(text, "thuốc", "vitamin"))
        {
            productType = "thuốc";
        }

        // Loài thú: dùng hàm chuyên biệt vì "chó" bỏ dấu thành "cho" trùng với
        // giới từ "cho tôi" ⇒ nếu so khớp thẳng sẽ lọc nhầm sản phẩm cho chó.
        if (TextNormalizer.MentionsDog(folded))
        {
            pet = "chó";
        }
        else if (TextNormalizer.MentionsCat(folded))
        {
            pet = "mèo";
        }

        return (productType, pet);
    }

        private static bool ContainsAnyLocal(string text, params string[] keywords)
        {
            // So khớp KHÔNG phân biệt dấu + theo ranh giới từ: "môn" (thiếu dấu
            // sắc) vẫn phải được hiểu là "món", nhưng "do" trong "dờ" thì không.
            var haystack = TextNormalizer.Fold(text);

            foreach (var keyword in keywords)
            {
                if (TextNormalizer.ContainsPhrase(haystack, TextNormalizer.Fold(keyword)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Đọc ràng buộc giá từ <c>parameters</c> của intent.
        /// </summary>
        private static (decimal? Min, decimal? Max, bool MaxInclusive) ReadPriceParameters(
            IReadOnlyDictionary<string, string>? parameters)
        {
            if (parameters is null)
            {
                return (null, null, true);
            }

            decimal? min = null;
            decimal? max = null;
            var maxInclusive = true;

            if (parameters.TryGetValue("min_price", out var minRaw) &&
                decimal.TryParse(minRaw, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var minVal))
            {
                min = minVal;
            }

            if (parameters.TryGetValue("max_price", out var maxRaw) &&
                decimal.TryParse(maxRaw, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var maxVal))
            {
                max = maxVal;
            }

            if (parameters.TryGetValue("max_price_inclusive", out var incRaw))
            {
                maxInclusive = !string.Equals(incRaw, "false", StringComparison.OrdinalIgnoreCase);
            }

            return (min, max, maxInclusive);
        }

        /// <summary>
        /// Mô tả điều kiện lọc bằng tiếng Việt để câu trả lời cụ thể (không chung chung).
        /// </summary>
        private static string BuildProductConditionDescription(
            string? type,
            string? pet,
            decimal? minPrice,
            decimal? maxPrice,
            bool maxInclusive)
        {
            var parts = new List<string>();

            if (!string.IsNullOrEmpty(type))
            {
                parts.Add($"loại {type}");
            }

            if (!string.IsNullOrEmpty(pet))
            {
                parts.Add($"cho {pet}");
            }

            if (minPrice.HasValue && maxPrice.HasValue)
            {
                parts.Add($"từ {Vnd(minPrice.Value)} đến {Vnd(maxPrice.Value)}");
            }
            else if (maxPrice.HasValue)
            {
                parts.Add(maxInclusive
                    ? $"có giá tối đa {Vnd(maxPrice.Value)}"
                    : $"có giá dưới {Vnd(maxPrice.Value)}");
            }
            else if (minPrice.HasValue)
            {
                parts.Add($"có giá từ {Vnd(minPrice.Value)} trở lên");
            }

            return parts.Count == 0 ? string.Empty : " " + string.Join(" ", parts);
        }

        /// <summary>
        /// Dựng danh sách messages gửi lên Gemini từ lịch sử hội thoại.
        ///
        /// <para>
        /// <b>SỬA LỖI (11/11/2026) — mất ngữ cảnh:</b> widget giờ gửi CẢ lượt user
        /// lẫn lượt AI (đúng thứ tự hội thoại) ⇒ nếu ta append thêm câu hỏi hiện
        /// tại ở cuối, câu hỏi đó bị LẶP 2 lần ⇒ Gemini thường bị lệch ngữ cảnh
        /// và trả lời sai. Nay chỉ append khi câu hỏi hiện tại CHƯA có trong
        /// lịch sử.
        /// </para>
        /// </summary>
        private static List<ChatHistoryItemDto> BuildMessagesForAi(ChatMessageRequestDto request)
        {
            var history = new List<ChatHistoryItemDto>(request.History ?? new List<ChatHistoryItemDto>());

            var current = request.Message?.Trim() ?? string.Empty;

            var alreadyPresent = history.Any(h =>
                string.Equals(h.Role, "user", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(h.Content?.Trim(), current, StringComparison.OrdinalIgnoreCase));

            if (!alreadyPresent && current.Length > 0)
            {
                history.Add(new ChatHistoryItemDto { Role = "user", Content = current });
            }

            return history;
        }

        /// <summary>
        /// Fast-path CỤC BỘ: trả lời câu hỏi về sản phẩm thẳng từ DB (0 request AI).
        ///
        /// <para>
        /// <b>VÌ SAO CẦN (lỗi "chatbot Gemini không hoạt động" 11/11/2026):</b>
        /// Gemini free tier chỉ 20 request/ngày; kiến trúc cũ tốn 2 request cho mỗi
        /// câu hỏi (phân tích intent + sinh câu trả lời) ⇒ hết quota sau ~10 câu ⇒
        /// chatbot chết. Câu hỏi về sản phẩm là loại phổ biến nhất ⇒ xử lý tại
        /// chỗ, không tốn request nào, và không bao giờ bị AI bịa giá/tên.
        /// </para>
        /// </summary>
        /// <returns>Câu trả lời, hoặc <c>null</c> nếu câu hỏi không phải về sản phẩm.</returns>
        private async Task<ChatMessageResponseDto?> TryHandleProductQuestionLocallyAsync(
            ChatMessageRequestDto request,
            CancellationToken ct)
        {
            var message = request.Message;
            if (string.IsNullOrWhiteSpace(message))
            {
                return null;
            }

            var text = message.ToLowerInvariant();

            // Chỉ xử lý câu hỏi về SẢN PHẨM (không phải dịch vụ / đặt lịch / liên hệ).
            if (!IsProductDetailQuestion(text))
            {
                return null;
            }

            try
            {
                var price = PriceConstraintParser.Parse(text);
                var (productType, pet) = ExtractProductTypeAndPetLocal(text);

                IReadOnlyList<ChatbotProductItem> products;

                if (price.HasValue)
                {
                    // Có ràng buộc giá ⇒ lọc theo giá (chính xác tuyệt đối).
                    products = await _repo.FindProductsByPriceRangeAsync(
                        price.Min,
                        price.Max,
                        price.MaxInclusive,
                        productType,
                        pet,
                        20,
                        ct);
                }
                else
                {
                    var keywords = ExtractSearchKeywords(text);

                    products = keywords.Count > 0
                        ? await _repo.FindProductsByKeywordsAsync(keywords, 15, ct)
                        : Array.Empty<ChatbotProductItem>();
                }

                if (products.Count == 0)
                {
                    return null;
                }

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("🐾 Mình tìm thấy các sản phẩm sau cho bé cưng:");

                foreach (var p in products)
                {
                    sb.AppendLine($"• {p.Name} — {Vnd(p.Price)} (Còn {p.StockQuantity})");
                }

                sb.AppendLine();
                sb.AppendLine("Bạn muốn mình tư vấn thêm về món nào đó không? 🐾");

                return new ChatMessageResponseDto
                {
                    Reply = sb.ToString().Trim(),
                    Source = "database",
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ChatbotService] Fast-path sản phẩm thất bại, chuyển sang AI");
                return null;
            }
        }

        /// <summary>
        /// Câu hỏi này có phải hỏi về SẢN PHẨM (có thể trả lời thẳng từ DB) không?
        ///
        /// <para>
        /// Điều kiện: nhắc "sản phẩm" + nhắc chi tiết (giá / tồn kho / có sẵn).
        /// Câu hỏi về dịch vụ / đặt lịch / liên hệ KHÔNG rơi vào đây.
        /// </para>
        /// </summary>
        private static bool IsProductDetailQuestion(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            // So khớp KHÔNG phân biệt dấu + theo ranh giới từ: "môn" (thiếu dấu
            // sắc) vẫn phải được hiểu là "món" ⇒ nếu không câu nối tiếp sẽ rơi
            // xuống Gemini.
            var text = TextNormalizer.Fold(message);

            var mentionsProduct = TextNormalizer.ContainsPhrase(text, "san pham")
                || TextNormalizer.ContainsPhrase(text, "mon")
                || TextNormalizer.ContainsPhrase(text, "thuc an")
                || TextNormalizer.ContainsPhrase(text, "do choi")
                || TextNormalizer.ContainsPhrase(text, "phu kien")
                || TextNormalizer.ContainsPhrase(text, "thuoc")
                || TextNormalizer.ContainsPhrase(text, "vitamin");

            if (!mentionsProduct)
            {
                return false;
            }

            foreach (var keyword in LocalIntentDetector.ProductDetailKeywords)
            {
                if (TextNormalizer.ContainsPhrase(text, TextNormalizer.Fold(keyword)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Câu trả lời cho intent <c>product_search</c> — tìm sản phẩm theo từ khoá
        /// trong câu hỏi, trả thẳng từ DB.
        ///
        /// <para>
        /// <b>VÌ SAO TRẢ THẲNG TỪ DB (không gọi Gemini):</b>
        /// (1) Gemini free tier chỉ 20 request/ngày — gọi AI cho mọi câu hỏi sản
        /// phẩm sẽ hết quota rất nhanh ⇒ chatbot "chết"; (2) Gemini rất dễ BỊA
        /// tên/giá sản phẩm (ví dụ trả "thuốc cho chó" — không tồn tại trong DB).
        /// </para>
        /// </summary>
        /// <returns>Câu trả lời, hoặc <c>null</c> nếu không tìm thấy món nào (khi đó
        /// giao tiếp cho AI để tư vấn tự nhiên).</returns>
        private async Task<ChatMessageResponseDto?> BuildProductSearchReplyAsync(
            string message,
            AiIntentResult intent,
            CancellationToken ct)
        {
            try
            {
                intent.Parameters.TryGetValue("product_type", out var type);
                intent.Parameters.TryGetValue("pet", out var pet);

                var (minPrice, maxPrice, maxInclusive) = ReadPriceParameters(intent.Parameters);

                // 1. Lọc theo khoảng giá nếu có.
                var products = await _repo.FindProductsByPriceRangeAsync(
                    minPrice,
                    maxPrice,
                    maxInclusive,
                    string.IsNullOrEmpty(type) ? null : type,
                    string.IsNullOrEmpty(pet) ? null : pet,
                    15,
                    ct);

                // 2. Nếu không có ràng buộc giá ⇒ tìm theo từ khoá trong câu hỏi.
                if (products.Count == 0 && !minPrice.HasValue && !maxPrice.HasValue)
                {
                    var keywords = ExtractSearchKeywords(message);

                    if (keywords.Count > 0)
                    {
                        var found = await _repo.FindProductsByKeywordsAsync(keywords, 15, ct);
                        products = found.ToList();
                    }
                }

                if (products.Count == 0)
                {
                    return null;
                }

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("🐾 Mình tìm thấy các sản phẩm sau cho bé cưng:");

                foreach (var p in products)
                {
                    sb.AppendLine($"• {p.Name} — {Vnd(p.Price)} (Còn {p.StockQuantity})");
                }

                sb.AppendLine();
                sb.AppendLine("Bạn muốn mình tư vấn thêm về món nào đó không? 🐾");

                return new ChatMessageResponseDto
                {
                    Reply = sb.ToString().Trim(),
                    Source = "database",
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ChatbotService] Không dựng được câu trả lời tìm sản phẩm từ DB");
                return null;
            }
        }

        /// <summary>
        /// Trích từ khoá có nghĩa từ câu hỏi (bỏ từ dừng tiếng Việt).
        /// </summary>
        private static List<string> ExtractSearchKeywords(string message)
        {
            var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "bao", "nhiêu", "mấy", "nào", "gì", "không", "chưa", "về", "biết",
                "cho", "của", "với", "trong", "ngoài", "trên", "dưới", "ở", "và", "hoặc", "hay", "thì", "mà", "nhé",
                "tôi", "bạn", "mình", "họ", "nó", "shop", "cửa", "hàng",
                "có", "còn", "là", "đang", "được", "muốn", "cần", "tìm", "hỏi", "xem", "thấy",
                "sản", "phẩm", "mục", "loại", "hiện", "tại", "này", "đó",
                "cái", "con", "chiếc", "túi", "hộp", "gói", "cục", "bộ", "đôi", "cân", "lít", "chai", "lon", "gam",
                "một", "hai", "ba", "bốn", "năm", "sáu", "bảy", "tám", "chín", "mười",
                "giá", "tiền", "đồng", "tốn", "mua", "bán", "vẫn", "hết", "số", "lượng", "tồn", "kho"
            };

            return message
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length >= 2 && !stopWords.Contains(w))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToList();
        }

        /// <summary>
        /// Câu trả lời cho intent <c>product_price_filter</c> — danh sách sản phẩm
        /// thật trong khoảng giá khách hỏi.
        ///
        /// <para>
        /// <b>VÌ SAO TRẢ THẲNG TỪ DB (không gọi Gemini):</b> nếu đưa danh sách lên
        /// Gemini, model rất dễ BỊA tên/giá (ví dụ trả "thuốc cho chó" — sản phẩm
        /// không tồn tại trong DB). Trả thẳng từ SQL ⇒ chắc chắn đúng, và không
        /// tốn request Gemini (free tier chỉ 20 request/ngày).
        /// </para>
        /// </summary>
        private async Task<ChatMessageResponseDto> BuildPriceFilteredProductReplyAsync(
            AiIntentResult intent,
            CancellationToken ct)
        {
            intent.Parameters.TryGetValue("product_type", out var type);
            intent.Parameters.TryGetValue("pet", out var pet);

            var (minPrice, maxPrice, maxInclusive) = ReadPriceParameters(intent.Parameters);

            var products = await _repo.FindProductsByPriceRangeAsync(
                minPrice,
                maxPrice,
                maxInclusive,
                string.IsNullOrEmpty(type) ? null : type,
                string.IsNullOrEmpty(pet) ? null : pet,
                20,
                ct);

            var condition = BuildProductConditionDescription(type, pet, minPrice, maxPrice, maxInclusive);

            if (products.Count == 0)
            {
                return new ChatMessageResponseDto
                {
                    Reply = $"😢 Rất tiếc, hiện cửa hàng không có sản phẩm{condition}. Bạn có thể xem thêm danh mục thức ăn, đồ chơi, phụ kiện và thuốc nhé! 🐾",
                    Source = "database",
                    Timestamp = DateTime.UtcNow
                };
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"🐾 Có {products.Count} sản phẩm{condition}:");

            foreach (var p in products)
            {
                sb.AppendLine($"• {p.Name} — {Vnd(p.Price)} (Còn {p.StockQuantity})");
            }

            if (products.Count >= 20)
            {
                sb.AppendLine("… Bạn có thể hỏi cụ thể hơn để mình lọc chính xác hơn nhé!");
            }

            return new ChatMessageResponseDto
            {
                Reply = sb.ToString().Trim(),
                Source = "database",
                Timestamp = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Dựng câu trả lời TỪ DỮ LIỆU DB THẬT khi nhà cung cấp AI không khả dụng
        /// (hết quota Gemini / rate limit / timeout).
        ///
        /// <para>
        /// <b>VÌ SAO CẦN:</b> Gemini free tier chỉ 20 request/ngày. Khi hết quota,
        /// chatbot trước đây trả về thông báo lỗi và người dùng không dùng được
        /// gì. Nay chatbot vẫn trả lời được bằng dữ liệu thật trong DB
        /// (sản phẩm, dịch vụ, phòng lưu trú, thông tin liên hệ, đơn hàng,
        /// lịch hẹn) — chỉ mất phần "hội thoại tự nhiên" của AI.
        /// </para>
        /// </summary>
        /// <returns>Câu trả lời, hoặc <c>null</c> nếu không có dữ liệu nào phù hợp.</returns>
        private async Task<ChatMessageResponseDto?> BuildDatabaseFallbackReplyAsync(
            ChatMessageRequestDto request,
            AiIntentResult intent,
            CancellationToken ct)
        {
            try
            {
                // Chỉ dùng fallback DB khi intent/câu hỏi khớp dữ liệu đó.
                // Trước đây mọi câu hỏi không hiểu được đều nhận lần lượt đơn hàng,
                // dịch vụ hoặc liên hệ shop, khiến chatbot trông như hardcode.
                if (intent.Intent == "shop_contact")
                {
                    return await BuildShopContactReplyAsync(ct);
                }

                if (intent.Intent is "product_search" or "product_count" or "product_price_filter"
                    || IsProductDetailQuestion(request.Message))
                {
                    // allowAi = false: Gemini đang lỗi nên chỉ tra DB.
                    var direct = await TryHandleProductQueryDirectlyAsync(
                        request.Message ?? string.Empty,
                        ct,
                        allowAi: false);
                    if (direct is not null && !string.IsNullOrWhiteSpace(direct.Reply))
                    {
                        direct.Source = "database";
                        return direct;
                    }
                }

                // Không tiết lộ/đưa dữ liệu đơn hàng nếu người dùng không hỏi đơn.
                if (intent.Intent == "order_status" && request.CustomerId.HasValue)
                {
                    var orders = await _repo.GetCustomerOrdersAsync(request.CustomerId.Value, 3, ct);
                    if (orders.Count > 0)
                    {
                        var reply = "📦 Đơn hàng gần đây của bạn:\n" +
                            string.Join("\n", orders.Select(o => $"- #{o.OrderId}: {o.Status} - {Vnd(o.TotalAmount)}"));
                        return new ChatMessageResponseDto { Reply = reply, Source = "database", Timestamp = DateTime.UtcNow };
                    }
                }

                if (intent.Intent is "service_inquiry" or "booking")
                {
                    var services = await _repo.GetActiveServicesAsync(10, ct);
                    if (services.Count > 0)
                    {
                        var reply = "🛁 Dịch vụ của shop:\n" +
                            string.Join("\n", services.Select(s => $"- {s.Name}: {Vnd(s.Price)} ({s.Duration} phút)"));
                        return new ChatMessageResponseDto { Reply = reply, Source = "database", Timestamp = DateTime.UtcNow };
                    }
                }

                // Không có dữ liệu phù hợp thì để caller báo AI không khả dụng;
                // tránh bịa ngữ cảnh bằng một câu trả lời chung cho mọi tin nhắn.
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ChatbotService] Không dựng được fallback từ DB");
                return null;
            }
        }

        /// <summary>
        /// Câu trả lời thông tin liên hệ — đọc thẳng từ <c>SystemSettings</c>.
        /// Không hardcode (trước đây chatbot tự bịa địa chỉ/hotline).
        /// </summary>
        private async Task<ChatMessageResponseDto> BuildShopContactReplyAsync(CancellationToken ct)
        {
            var contact = await _repo.GetShopContactSettingsAsync(ct);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("📍 Thông tin liên hệ của PetShop:");

            AppendIfPresent(sb, "Địa chỉ", contact, "ShopAddress");
            AppendIfPresent(sb, "Hotline", contact, "ShopHotline");
            AppendIfPresent(sb, "Email", contact, "ShopEmail");
            AppendIfPresent(sb, "Giờ hoạt động", contact, "ShopWorkingHours");

            if (sb.Length <= "📍 Thông tin liên hệ của PetShop:\n".Length)
            {
                sb.AppendLine("Hiện chưa cập nhật thông tin liên hệ. Bạn vui lòng gọi hotline trên banner để được hỗ trợ.");
            }

            return new ChatMessageResponseDto
            {
                Reply = sb.ToString().Trim(),
                Source = "database",
                Timestamp = DateTime.UtcNow
            };
        }

        private static void AppendIfPresent(
            System.Text.StringBuilder sb,
            string label,
            IReadOnlyDictionary<string, string> source,
            string key)
        {
            if (source.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                sb.AppendLine($"- {label}: {value.Trim()}");
            }
        }

        /// <summary>
        /// Xác định intent theo thứ tự ưu tiên: fast-path cục bộ → cache → AI.
        ///
        /// <para>
        /// Logic fast-path + cache nằm trong <see cref="AnalyzeIntentWithAi"/> để
        /// endpoint <c>/api/chatbot/intent</c> cũng hưởng lẫn (nhất quán giữa
        /// 2 entry point) — hàm này chỉ là wrapper cho log thống kê.
        /// </para>
        /// </summary>
        private async Task<AiIntentResult> ResolveIntentAsync(string? message, CancellationToken ct)
        {
            var intent = await AnalyzeIntentWithAi(message ?? string.Empty, ct);
            _logger.LogInformation("[ChatbotService] Intent đã phân định: {Intent}", intent.Intent);
            return intent;
        }

        private async Task<string> CallAiProviderAsync(
            string systemPrompt,
            List<ChatHistoryItemDto> messages,
            CancellationToken ct)
        {
            if (_aiProvider == null || !_aiProvider.IsAvailable)
            {
                throw new InvalidOperationException("Gemini is not available. Please configure the Gemini provider in appsettings.json.");
            }

            _logger.LogInformation("Using AI provider: {Provider}", _aiProvider.ProviderName);

            return await _aiProvider.GenerateTextAsync(
                _options.DefaultModel,
                systemPrompt,
                messages,
                _options.Temperature,
                _options.MaxTokens,
                ct);
        }

        public async Task<AiIntentResult> AnalyzeIntentWithAi(string message, CancellationToken ct = default)
        {
            // FAST-PATH cục bộ (không tốn request AI) cho câu có mẫu rõ.
            var local = LocalIntentDetector.Detect(message);
            if (local is not null)
            {
                _intentCache.Set(ChatbotIntentCache.Normalize(message), local);
                return local;
            }

            // TRA CACHE TRƯỚC (tiết kiệm 1 request Gemini cho câu hỏi lặp lại).
            var normalized = ChatbotIntentCache.Normalize(message);
            var cached = _intentCache.Get(normalized);
            if (cached is not null)
            {
                _logger.LogInformation("[ChatbotService] Intent lấy từ cache: {Intent}", cached.Intent);
                return cached;
            }

            var prompt = $@"
Phân tích câu sau và trả về JSON:

- intent: một trong các giá trị:
  greeting, product_search, product_count, service_inquiry, booking, order_status, introduction, pet_info, payment, shop_contact, support, unknown

- parameters:
  + product_type (ví dụ: đồ chơi, thức ăn)
  + pet (chó, mèo)
  + keywords (chuỗi cách nhau bằng dấu phẩy)

Chỉ trả về JSON hợp lệ, không bọc trong markdown (không dùng ```json), không giải thích.

Câu: ""{message}""
";

            try
            {
                var raw = await _aiProvider.GenerateTextAsync(
                    _options.DefaultModel,
                    string.Empty,
                    new List<ChatHistoryItemDto> { new ChatHistoryItemDto { Role = "user", Content = prompt } },
                    0.2,
                    200,
                    ct
                );

                var intent = ParseIntentJson(raw);
                if (intent is not null)
                {
                    _intentCache.Set(normalized, intent);
                    return intent;
                }

                _logger.LogWarning(
                    "[ChatbotService] Không phân tích được JSON intent từ phản hồi Gemini. Raw={Raw}",
                    TruncateForLog(raw));
                return new AiIntentResult { Intent = "unknown" };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (AiProviderException ex)
            {
                // Quota hết / rate limit / timeout ⇒ KHÔNG nên gọi tiếp Gemini cho phần
                // trả lời (sẽ chắc chắn thất bại) ⇒ ném lên để controller báo 503.
                _logger.LogWarning(ex, "[ChatbotService] Phân tích intent thất bại do nhà cung cấp AI: {Kind}", ex.Kind);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to analyze intent with AI");
                return new AiIntentResult { Intent = "unknown" };
            }
        }

        /// <summary>
        /// Parse JSON intent từ phản hồi của Gemini.
        ///
        /// <para>
        /// Phải bền trước 3 dạng đầu ra thực tế:
        /// </para>
        /// <list type="number">
        ///   <item>JSON thuần: <c>{"intent":"product_search","parameters":{...}}</c></item>
        ///   <item>JSON bọc code fence: <c>```json\n{...}\n```</c></item>
        ///   <item>JSON kèm text thừa trước/sau: <c>"Dưới đây là kết quả:\n{...}</c></item>
        /// </list>
        ///
        /// <para>
        /// Trước đây dùng <c>IndexOf('{')</c> / <c>LastIndexOf('}')</c> ⇒ với dạng (3)
        /// (text thừa chứa ngoặc) sẽ cắt sai ⇒ deserialize lỗi ⇒ intent = "unknown".
        /// </para>
        /// </summary>
        public static AiIntentResult? ParseIntentJson(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            var json = ExtractJsonObject(raw);
            if (json is null)
            {
                return null;
            }

            try
            {
                var result = System.Text.Json.JsonSerializer.Deserialize<AiIntentResult>(json,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result is null || string.IsNullOrWhiteSpace(result.Intent))
                {
                    return null;
                }

                result.Parameters ??= new Dictionary<string, string>();
                return result;
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Bóc ra đúng một object JSON từ chuỗi trả về của model (bỏ code fence,
        /// bỏ text thừa trước/sau, cân bằng ngoặc).
        /// </summary>
        private static string? ExtractJsonObject(string raw)
        {
            var text = raw.Trim();

            // Bỏ code fence ```json ... ``` hoặc ``` ... ``` nếu có.
            var fenceStart = text.IndexOf("```", StringComparison.Ordinal);
            if (fenceStart >= 0)
            {
                var fenceEnd = text.IndexOf("```", fenceStart + 3, StringComparison.Ordinal);
                text = fenceEnd > fenceStart
                    ? text[(fenceStart + 3)..fenceEnd]
                    : text[(fenceStart + 3)..];

                // Bỏ tiền tố "json" còn sót lại.
                text = text.Trim();
                if (text.StartsWith("json", StringComparison.OrdinalIgnoreCase))
                {
                    text = text[4..].Trim();
                }
            }

            var start = text.IndexOf('{');
            if (start < 0)
            {
                return null;
            }

            // Quét cân bằng ngoặc (bỏ qua ngoặc trong chuỗi) ⇒ cắt đúng object đầu tiên.
            var depth = 0;
            var inString = false;
            var escaped = false;

            for (var i = start; i < text.Length; i++)
            {
                var ch = text[i];

                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (ch == '\\')
                    {
                        escaped = true;
                    }
                    else if (ch == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                switch (ch)
                {
                    case '"':
                        inString = true;
                        break;
                    case '{':
                        depth++;
                        break;
                    case '}':
                        depth--;
                        if (depth == 0)
                        {
                            return text[start..(i + 1)];
                        }

                        break;
                }
            }

            // JSON bị cắt cụt (MAX_TOKENS) ⇒ không cân bằng ⇒ trả null để caller
            // fallback về "unknown" thay vì ném lỗi.
            return null;
        }

        private static string TruncateForLog(string? text, int maxLength = 400)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return text.Length <= maxLength ? text : text[..maxLength] + "...";
        }

        public async Task<ConversationContextDto> GetContextAsync(int? customerId, CancellationToken ct = default)
        {
            var context = new ConversationContextDto { CustomerId = customerId };

            // === CUSTOMER-SPECIFIC DATA ===
            if (customerId.HasValue)
            {
                var customer = await _repo.GetCustomerNameAsync(customerId.Value, ct);

                if (!string.IsNullOrEmpty(customer))
                    context.CustomerName = customer;

                var pets = await _repo.GetCustomerPetsAsync(customerId.Value, ct);

                context.AvailablePets = pets
                    .Select(p => $"{p.PetName} ({p.Age} tuổi)")
                    .ToList();

                var recentOrders = await _repo.GetRecentOrdersAsync(customerId.Value, ct);
                context.RecentOrders = recentOrders.ToList();

                var recentBookings = await _repo.GetRecentBookingsAsync(customerId.Value, ct);
                context.RecentBookings = recentBookings.ToList();
            }

            // === SHOP DATA (All customers can see) ===
            var staff = await _repo.GetStaffListAsync(ct);
            context.StaffList = staff.ToList();

            var doctors = await _repo.GetDoctorListAsync(ct);
            context.DoctorList = doctors.ToList();

            var products = await _repo.GetProductSummariesAsync(ct);
            context.Products = products.ToList();

            var services = await _repo.GetServiceSummariesAsync(ct);
            context.Services = services.ToList();

            var categories = await _repo.GetProductCategoryNamesAsync(ct);
            context.ProductCategories = categories.ToList();

            var species = await _repo.GetSpeciesNamesAsync(ct);
            context.Species = species.ToList();

            var rooms = await _repo.GetBoardingRoomSummariesAsync(ct);
            context.BoardingRooms = rooms.ToList();

            // === THÔNG TIN LIÊN HỆ THẬT (từ SystemSettings — KHÔNG hardcode) ===
            var contact = await _repo.GetShopContactSettingsAsync(ct);
            context.ShopAddress = contact.TryGetValue("ShopAddress", out var addr) ? addr : string.Empty;
            context.ShopHotline = contact.TryGetValue("ShopHotline", out var hotline) ? hotline : string.Empty;
            context.ShopEmail = contact.TryGetValue("ShopEmail", out var email) ? email : string.Empty;
            context.ShopWorkingHours = contact.TryGetValue("ShopWorkingHours", out var hours) ? hours : string.Empty;

            return context;
        }

        public Task<string> BuildSystemPromptAsync(ConversationContextDto? context, CancellationToken ct = default)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Bạn là trợ lý AI của PetShop. Trả lời tự nhiên, trực tiếp theo câu hỏi hiện tại và lịch sử hội thoại. Dùng ngôn ngữ khách yêu cầu; nếu khách không yêu cầu ngôn ngữ khác thì trả lời bằng tiếng Việt.");
            sb.AppendLine("Dữ liệu trong các mục bên dưới được đọc từ database ở lượt hỏi này. Chỉ khẳng định thông tin có trong dữ liệu; không tự tạo tên, giá, số lượng, giờ mở cửa hoặc thông tin cá nhân.");
            sb.AppendLine("Nếu dữ liệu không có câu trả lời, hãy nói rõ điều gì chưa được lưu và hỏi một câu ngắn để làm rõ. Không lặp lại lời giới thiệu chung cho câu hỏi cụ thể.");
            sb.AppendLine("Nếu khách hỏi 'tôi là ai' hoặc 'bạn là ai', phân biệt người hỏi với trợ lý. Nếu hỏi tên thú cưng, tra mục thú cưng thuộc tài khoản hiện tại.");
            sb.AppendLine("Khi khách hỏi thú cưng của họ, chỉ dùng danh sách thú cưng thuộc tài khoản hiện tại. Khi hỏi giờ spa/dịch vụ, chỉ nêu giờ riêng nếu database có; nếu chỉ có giờ cửa hàng, hãy nói rõ đó là giờ cửa hàng.");
            sb.AppendLine("Với dịch vụ, sản phẩm, đơn hàng, lịch hẹn và liên hệ, hãy trả lời đúng dữ liệu bên dưới và chỉ nêu phần liên quan đến câu hỏi.");
            sb.AppendLine();

            if (context is not null)
            {
                sb.AppendLine("[HỒ SƠ KHÁCH HÀNG]");
                sb.AppendLine($"Tên khách: {context.CustomerName ?? "Không có thông tin khách hàng trong phiên hiện tại"}");
                sb.AppendLine(context.AvailablePets.Count > 0
                    ? $"Thú cưng đã đăng ký: {string.Join("; ", context.AvailablePets)}"
                    : "Thú cưng đã đăng ký: Không có dữ liệu trong tài khoản hiện tại");
                if (context.RecentOrders.Count > 0)
                {
                    sb.AppendLine("Đơn hàng gần đây:");
                    foreach (var order in context.RecentOrders.Take(5)) sb.AppendLine($"- {order}");
                }
                if (context.RecentBookings.Count > 0)
                {
                    sb.AppendLine("Lịch hẹn gần đây:");
                    foreach (var booking in context.RecentBookings.Take(5)) sb.AppendLine($"- {booking}");
                }

                AppendContextList(sb, "DỊCH VỤ ĐANG CÓ", context.Services);
                AppendContextList(sb, "SẢN PHẨM ĐANG CÓ", context.Products.Take(100));
                AppendContextList(sb, "NHÂN VIÊN", context.StaffList);
                AppendContextList(sb, "BÁC SĨ", context.DoctorList);
                AppendContextList(sb, "PHÒNG LƯU TRÚ", context.BoardingRooms);
                if (context.ProductCategories.Count > 0)
                    sb.AppendLine($"Danh mục sản phẩm: {string.Join(", ", context.ProductCategories)}");
                if (context.Species.Count > 0)
                    sb.AppendLine($"Loài thú hỗ trợ: {string.Join(", ", context.Species)}");

                sb.AppendLine("[THÔNG TIN CỬA HÀNG]");
                sb.AppendLine($"Địa chỉ: {ResolveOrFallback(context.ShopAddress, "Chưa cập nhật")}");
                sb.AppendLine($"Hotline: {ResolveOrFallback(context.ShopHotline, "Chưa cập nhật")}");
                sb.AppendLine($"Email: {ResolveOrFallback(context.ShopEmail, "Chưa cập nhật")}");
                sb.AppendLine($"Giờ hoạt động cửa hàng: {ResolveOrFallback(context.ShopWorkingHours, "Chưa cập nhật")}");
            }

            return Task.FromResult(sb.ToString());
        }

        private static void AppendContextList(System.Text.StringBuilder sb, string title, IEnumerable<string> values)
        {
            var rows = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
            if (rows.Count == 0) return;
            sb.AppendLine($"[{title} — DATABASE]");
            foreach (var row in rows) sb.AppendLine($"- {row}");
        }

        private async Task<string> BuildLegacySystemPromptAsync(ConversationContextDto? context, CancellationToken ct = default)
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendLine($"[THÔNG TIN HỆ THỐNG]");
            sb.AppendLine($"- Ngày giờ hiện tại (Timezone Server): {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
            sb.AppendLine();
            sb.AppendLine("NHIỆM VỤ:");
            sb.AppendLine("- BẠN PHẢI HIỂU Ý NGƯỜI DÙNG DỰA TRÊN NGỮ CẢNH, KHÔNG PHỤ THUỘC TỪ KHÓA CỐ ĐỊNH.");
            sb.AppendLine("- Nếu người dùng hỏi về bạn, hãy tự giới thiệu linh hoạt thông qua danh tính của nền tảng (Ví dụ: Bạn là AI của công ty X, nhưng đồng thời đang hỗ trợ PetShop).");
            sb.AppendLine("- Nếu người dùng hỏi số lượng sản phẩm (product_count), hãy trả lời dựa trên dữ liệu đã được cung cấp (không đếm lung tung).");
            sb.AppendLine("- Tư vấn sản phẩm cho thú cưng (thức ăn, đồ chơi, phụ kiện, thuốc, vitamin...) dựa vào danh sách.");
            sb.AppendLine("- Giới thiệu dịch vụ (Spa & Grooming, Khám bệnh, Tiêm vaccine, Tẩy giun, Booking lịch hẹn...)");
            sb.AppendLine("- Hỗ trợ đặt lịch hẹn dịch vụ spa/grooming");
            sb.AppendLine("- Trả lời câu hỏi về giá cả");
            sb.AppendLine("- Kiểm tra trạng thái đơn hàng và lịch hẹn");
            sb.AppendLine("- Cung cấp thông tin về chăm sóc, dinh dưỡng, sức khỏe thú cưng");
            sb.AppendLine("- Cung cấp thông tin về nhân viên, bác sĩ của shop");
            sb.AppendLine();
            sb.AppendLine("QUY TẮC TRẢ LỜI THÔNG MINH:");
            sb.AppendLine("- LUÔN ưu tiên dùng dữ liệu từ database được đính kèm ở dưới bên dưới (nếu có)");
            sb.AppendLine("- KHÔNG được nói 'tôi không biết' nếu thông tin đã có trong context");
            sb.AppendLine("- Nếu câu hỏi đơn giản (ngày, giờ, phép toán), trả lời trực tiếp, không lan man");
            sb.AppendLine("- Nếu có danh sách sản phẩm/dịch vụ → phải trích từ dữ liệu thật");
            sb.AppendLine("- TUYỆT ĐỐI KHÔNG BỊA GIÁ/TÊN: chỉ được nêu giá và tên lấy nguyên văn từ 'DANH SÁCH SẢN PHẨM'. Nếu khách hỏi về một mức giá cụ thể mà trong danh sách KHÔNG có món nào khớp ⇒ phải nói rõ không có món nào khớp, KHÔNG được tự bịa một món 'hợp lý'.");
            sb.AppendLine("- Khi khách hỏi về số lượng (vd 'có mấy món dưới 20k'), phải ĐỐI CHIẾU điều kiện lọc trong câu hỏi với danh sách, không được trả về tổng số sản phẩm của shop.");
            sb.AppendLine("- KHÔNG được LIỆT KÊ LẠI danh sách sản phẩm/dịch vụ như một bảng. Chỉ nhắc TỐI ĐA 3-5 món phù hợp nhất, mỗi món 1 dòng kèm giá.");
            sb.AppendLine("- Trả lời ngắn gọn, tự nhiên (dưới 150 từ), luôn kết thúc bằng 1 câu hỏi gợi ý bước tiếp theo cho khách.");
            sb.AppendLine();

            // === NHÂN VIÊN & BÁC SĨ ===
            if (context != null && (context.StaffList.Any() || context.DoctorList.Any()))
            {
                sb.AppendLine("NHÂN VIÊN & BÁC SĨ CỦA SHOP:");
                foreach (var staff in context.StaffList)
                    sb.AppendLine($"- {staff}");
                foreach (var doctor in context.DoctorList)
                    sb.AppendLine($"- Bác sĩ: {doctor}");
                sb.AppendLine();
            }

            // === DỊCH VỤ ===
            if (context != null && context.Services.Any())
            {
                sb.AppendLine("DANH SÁCH DỊCH VỤ (từ database):");
                foreach (var service in context.Services)
                    sb.AppendLine($"- {service}");
                sb.AppendLine();
            }

            // === SẢN PHẨM ===
            if (context != null && context.Products.Any())
            {
                sb.AppendLine("DANH SÁCH SẢN PHẨM (từ database):");
                foreach (var product in context.Products.Take(60))
                    sb.AppendLine($"- {product}");
                if (context.Products.Count > 60)
                    sb.AppendLine($"... và còn {context.Products.Count - 60} sản phẩm khác");
                sb.AppendLine();
            }

            // === PHÂN LOẠI SẢN PHẨM ===
            if (context != null && context.ProductCategories.Any())
            {
                sb.AppendLine($"PHÂN LOẠI SẢN PHẨM: {string.Join(", ", context.ProductCategories)}");
                sb.AppendLine();
            }

            // === LOÀI THÚ CƯNG ===
            if (context != null && context.Species.Any())
            {
                sb.AppendLine($"LOÀI THÚ CƯNG SHOP HỖ TRỢ: {string.Join(", ", context.Species)}");
                sb.AppendLine();
            }

            // === PHÒNG LƯU TRÚ ===
            if (context != null && context.BoardingRooms.Any())
            {
                sb.AppendLine("PHÒNG LƯU TRÚ (Boarding):");
                foreach (var room in context.BoardingRooms)
                    sb.AppendLine($"- {room}");
                sb.AppendLine();
            }

            sb.AppendLine("PHONG CÁCH:");
            sb.AppendLine("- Thân thiện, nhiệt tình, sử dụng emoji phù hợp 🐾🐶🐱");
            sb.AppendLine("- Ngôn ngữ tiếng Việt, tự nhiên như trò chuyện");
            sb.AppendLine("- Câu trả lời ngắn gọn, đi thẳng vào vấn đề (dưới 200 từ)");
            sb.AppendLine("- Nếu không biết, thừa nhận và gợi ý liên hệ hotline");
            sb.AppendLine();
            sb.AppendLine("THÔNG TIN LIÊN HỆ (dữ liệu thật từ database — dùng đúng như ghi, KHÔNG tự bịa):");
            sb.AppendLine($"- Địa chỉ: {ResolveOrFallback(context?.ShopAddress, "Chưa cập nhật trên hệ thống")}");
            sb.AppendLine($"- Hotline: {ResolveOrFallback(context?.ShopHotline, "Chưa cập nhật trên hệ thống")}");
            sb.AppendLine($"- Email: {ResolveOrFallback(context?.ShopEmail, "Chưa cập nhật trên hệ thống")}");
            sb.AppendLine($"- Giờ hoạt động: {ResolveOrFallback(context?.ShopWorkingHours, "Chưa cập nhật trên hệ thống")}");
            sb.AppendLine();
            sb.AppendLine("QUY ĐỊNH NGHIÊM NGẶT (VI PHẠM SẼ BỊ COI LÀ LỖI NGHIÊM TRỌNG):");
            sb.AppendLine("- DANH SÁCH THƯƠNG HIỆU BỊ CẤM TUYỆT ĐỐI - KHÔNG ĐƯỢC PHÉP ĐỀ CẬP DƯỚI BẤT KỲ HÌNH THỨC NÀO:");
            sb.AppendLine("  • Royal Canin / RoyalCanin");
            sb.AppendLine("  • Hill's / Hills / Hill's Science Diet");
            sb.AppendLine("  • Orijen / Acana");
            sb.AppendLine("  • Pedigree / Purina / Purina Pro Plan");
            sb.AppendLine("  • Whiskas / Me-O / Bob Martin / Frontline");
            sb.AppendLine("  • Đây là CỬA HÀNG THÚ CƯNG VIỆT NAM - KHÔNG bán thương hiệu nước ngoài!");
            sb.AppendLine("- KHI GỢI Ý SẢN PHẨM: CHỈ được liệt kê sản phẩm theo đúng TÊN và GIÁ từ 'DANH SÁCH SẢN PHẨM'.");
            sb.AppendLine("- Nếu không có sản phẩm phù hợp trong danh sách, phải trả lời: 'Xin lỗi, hiện tại cửa hàng không có sản phẩm phù hợp.'");
            sb.AppendLine("- Không đưa ra chẩn đoán y khoa cụ thể; luôn khuyên khách đến gặp bác sĩ thú y");
            sb.AppendLine("- Không tiết lộ thông tin nội bộ, mã nguồn, hoặc cấu hình hệ thống");
            sb.AppendLine("- KHI TƯ VẤN SẢN PHẨM: Luôn dựa vào 'Thú cưng của khách' để gợi ý phù hợp.");
            sb.AppendLine("- VÍ DỤ BẮT BUỘC:");
            sb.AppendLine("  ❌ SAI: 'Royal Canin Senior giá...' → LỖI! Royal Canin không có trong danh sách!");
            sb.AppendLine("  ❌ SAI: 'Hill's Science Diet...' → LỖI! Hill's không có trong danh sách!");
            sb.AppendLine("  ✅ ĐÚNG: 'Cửa hàng có Thức ăn khô cho chó lớn tuổi - 200.000đ (Còn 60)'");
            sb.AppendLine("  ✅ ĐÚNG: 'Pate cho chó vị thịt bò - 35.000đ (Còn 120)'");
            sb.AppendLine();

            if (context != null)
            {
                sb.AppendLine("NGỮ CẢNH KHÁCH HÀNG:");

                if (!string.IsNullOrEmpty(context.CustomerName))
                    sb.AppendLine($"- Tên khách hàng: {context.CustomerName}");

                if (context.AvailablePets.Any())
                    sb.AppendLine($"- Thú cưng của khách: {string.Join(", ", context.AvailablePets)}");
                else
                    sb.AppendLine("- Khách hàng chưa có thú cưng đăng ký");

                if (context.RecentOrders.Any())
                {
                    sb.AppendLine("- Đơn hàng gần đây:");
                    foreach (var order in context.RecentOrders.Take(3))
                        sb.AppendLine($"  • {order}");
                }

                if (context.RecentBookings.Any())
                {
                    sb.AppendLine("- Lịch hẹn gần đây:");
                    foreach (var booking in context.RecentBookings.Take(3))
                        sb.AppendLine($"  • {booking}");
                }

                sb.AppendLine();
                sb.AppendLine("Hãy dựa trên ngữ cảnh trên để đưa ra câu trả lời phù hợp và cá nhân hóa cho khách hàng.");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Lấy giá trị thật từ context; nếu rỗng thì dùng fallback mặc định.
        /// Dùng cho các thông tin liên hệ để prompt KHÔNG BAO GIỜ ghi rỗng.
        /// </summary>
        private static string ResolveOrFallback(string? value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

        private async Task<ChatMessageResponseDto?> HandleDatabaseQuery(
            ChatIntentDto intent,
            string message,
            int? customerId,
            CancellationToken ct)
        {
            // 🔹 1. Hỏi giá sản phẩm
            if (intent.Intent == "price_inquiry")
            {
                // Bỏ các từ không mang nghĩa để lấy từ khóa sản phẩm
                var stopWords = new[] { "giá", "bao nhiêu", "là", "của", "sản phẩm", "mua", "tiền", "chi phí", "cần", "muốn", "hỏi", "cho" };
                var keywords = message.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => w.Length > 2 && !stopWords.Contains(w))
                    .ToList();

                if (keywords.Any())
                {
                    var products = await _repo.FindProductsByNameAsync(keywords.Take(3).ToList(), 5, ct);
                    if (products.Any())
                    {
                        var reply = "💰 Giá sản phẩm:\n" +
                            string.Join("\n", products.Select(p => $"- {p.Name}: {Vnd(p.Price)} (Còn {p.StockQuantity})"));
                        return new ChatMessageResponseDto { Reply = reply, Source = "database", Timestamp = DateTime.UtcNow };
                    }
                }
            }

            // 🔹 2. Hỏi dịch vụ
            if (intent.Intent == "service_inquiry")
            {
                // Không trigger nếu message chứa từ khóa sản phẩm rõ ràng
                var productTriggers = new[] { "đồ chơi", "thức ăn", "thuốc", "vitamin", "phụ kiện", "sản phẩm", "hạt", "pate" };
                if (productTriggers.Any(k => message.Contains(k))) return null;

                var services = await _repo.GetActiveServicesAsync(5, ct);

                if (services.Any())
                {
                    var reply = "🛁 Dịch vụ của shop:\n" +
                        string.Join("\n", services.Select(s => $"- {s.Name}: {Vnd(s.Price)} ({s.Duration} phút)"));

                    return new ChatMessageResponseDto
                    {
                        Reply = reply,
                        Source = "database",
                        Timestamp = DateTime.UtcNow
                    };
                }
            }

            // 🔹 3. Đơn hàng
            if (intent.Intent == "order_status" && customerId.HasValue)
            {
                var orders = await _repo.GetCustomerOrdersAsync(customerId.Value, 3, ct);

                if (orders.Any())
                {
                    var reply = "📦 Đơn hàng gần đây:\n" +
                        string.Join("\n", orders.Select(o =>
                            $"- #{o.OrderId}: {o.Status} - {Vnd(o.TotalAmount)}"));

                    return new ChatMessageResponseDto
                    {
                        Reply = reply,
                        Source = "database",
                        Timestamp = DateTime.UtcNow
                    };
                }
            }

            return null;
        }

        /// <summary>
        /// Tìm kiếm thông minh: trích từ khóa → tìm trong Products + Services + BoardingRooms
        /// → đưa kết quả DB vào AI để mô tả đặc điểm, giá thị trường, tư vấn.
        /// Chạy TRƯỚC intent detection. Fallback về text thuần nếu AI không khả dụng.
        /// </summary>
        private async Task<ChatMessageResponseDto?> TryHandleProductQueryDirectlyAsync(
            string message,
            CancellationToken ct,
            bool allowAi = true)
        {
            // === 1. Stop words thuần ngữ pháp tiếng Việt ===
            var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // Từ hỏi
                "bao","nhiêu","mấy","nào","gì","không","chưa","về","biết",
                // Giới từ / liên từ
                "cho","của","với","trong","ngoài","trên","dưới","ở","và","hoặc","hay","thì","mà","nhé",
                // Đại từ
                "tôi","bạn","mình","họ","nó","shop",
                // Trợ động từ
                "có","còn","là","đang","được","muốn","cần","tìm","hỏi","xem","thấy",
                // Từ nhiễu ngữ cảnh
                "sản","phẩm","hàng","mục","loại","hiện","tại","này","đó",
                // Đơn vị / số đếm
                "cái","con","chiếc","túi","hộp","gói","cục","bộ","đôi","cân","lít","chai","lon","gam",
                "một","hai","ba","bốn","năm","sáu","bảy","tám","chín","mười",
                // Giá / tiền
                "giá","tiền","đồng","tốn","mua","bán","vẫn","hết","số","lượng","tồn","kho"
            };

            // === 2. Trích từ khóa có nghĩa (không cần trigger cứng — tìm DB với mọi từ khóa) ===
            var keywords = message
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length >= 2 && !stopWords.Contains(w))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToList();

            // Không có từ khóa nào có nghĩa → bỏ qua (lời chào, câu ngắn thuần ngữ pháp...)
            if (keywords.Count == 0) return null;

            // === 3. Tìm kiếm Products: AND trước → OR fallback ===
            var products = await _repo.FindProductsByKeywordsAsync(keywords, 0, ct);

            // === 4. Tìm kiếm Services: OR search ===
            var services = await _repo.FindServicesByKeywordsAsync(keywords, ct);

            // === 5. Tìm kiếm Boarding Rooms: OR search ===
            var rooms = await _repo.FindRoomsByKeywordsAsync(keywords, ct);

            // Không tìm thấy gì → nhường cho AI full-context
            if (products.Count == 0 && services.Count == 0 && rooms.Count == 0) return null;

            // === 6. Xây dựng system prompt tập trung cho AI ===
            var ctx = new System.Text.StringBuilder();
            ctx.AppendLine("Bạn là 'PetShop Assistant'. Trả lời câu hỏi của khách dựa trên dữ liệu thực từ database VÀ kiến thức tổng quát về sản phẩm/dịch vụ thú cưng.");
            ctx.AppendLine($"\nCÂU HỎI: \"{message}\"");
            ctx.AppendLine($"Từ khóa xác định: {string.Join(", ", keywords)}\n");

            if (products.Count > 0)
            {
                ctx.AppendLine("📦 SẢN PHẨM SHOP ĐANG CÓ (dữ liệu thực từ database):");
                foreach (var p in products.Take(10))
                    ctx.AppendLine($"  • {p.Name} | Danh mục: {p.CategoryName ?? "N/A"} | Giá: {Vnd(p.Price)} | Tồn kho: {p.StockQuantity}");
            }

            if (services.Count > 0)
            {
                ctx.AppendLine("\n🛁 DỊCH VỤ LIÊN QUAN (dữ liệu thực từ database):");
                foreach (var s in services.Take(5))
                    ctx.AppendLine($"  • {s.Name} | Loại: {s.ServiceType} | Giá: {Vnd(s.Price)} | Thời gian: {s.Duration} phút");
            }

            if (rooms.Count > 0)
            {
                ctx.AppendLine("\n🏠 PHÒNG LƯU TRÚ LIÊN QUAN (dữ liệu thực từ database):");
                foreach (var r in rooms.Take(5))
                    ctx.AppendLine($"  • {r.RoomName} | Loại: {r.RoomType} | Giá: {Vnd(r.PricePerDay)}/ngày | Số phòng trống: {r.Rooms}");
            }

            ctx.AppendLine("\nHƯỚNG DẪN TRẢ LỜI (BẮT BUỘC):");
            ctx.AppendLine("1. Trả lời trực tiếp câu hỏi (tồn kho, giá, thông tin) dựa vào dữ liệu thực ở trên");
            ctx.AppendLine("2. Mô tả đặc điểm, công dụng, cách dùng của sản phẩm/dịch vụ (dùng kiến thức tổng quát)");
            ctx.AppendLine("3. Nêu giá thị trường thông thường để khách so sánh với giá shop");
            ctx.AppendLine("4. Gợi ý sản phẩm/dịch vụ phù hợp nhất từ danh sách trên");
            ctx.AppendLine("5. Ngắn gọn dưới 200 từ, thân thiện, dùng emoji 🐾");
            ctx.AppendLine("NGHIÊM CẤM: Không bịa sản phẩm/dịch vụ không có trong danh sách trên.");

            // === 7. Gọi AI ===
            //
            //     CHỈ gọi khi allowAi = true. Nếu Gemini đã hết quota (429) thì
            //     gọi tiếp chỉ chắc chắn fail ⇒ lãng phí 1 request ⇒ bỏ qua
            //     và trả thẳng danh sách từ DB (bước 8).
            try
            {
                if (allowAi && _aiProvider?.IsAvailable == true)
                {
                    var aiReply = await _aiProvider.GenerateTextAsync(
                        _options.DefaultModel,
                        ctx.ToString(),
                        new List<ChatHistoryItemDto> { new() { Role = "user", Content = message } },
                        _options.Temperature,
                        _options.MaxTokens,
                        ct);
                    return new ChatMessageResponseDto { Reply = aiReply, Source = "ai+database", Timestamp = DateTime.UtcNow };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("AI unavailable in product query, falling back to direct response: {Msg}", ex.Message);
            }

            // === 8. Fallback thuần text nếu AI không khả dụng ===
            var fb = new System.Text.StringBuilder();
            if (products.Count > 0)
            {
                var inStock = products.Where(p => p.StockQuantity > 0).ToList();
                fb.AppendLine($"📦 Sản phẩm liên quan ({inStock.Count}/{products.Count} còn hàng):");
                foreach (var p in inStock.Take(8))
                    fb.AppendLine($"- {p.Name}: {Vnd(p.Price)} (Còn {p.StockQuantity})");
                if (inStock.Count > 8) fb.AppendLine($"... và còn {inStock.Count - 8} sản phẩm khác");
            }
            if (services.Count > 0)
            {
                fb.AppendLine($"\n🛁 Dịch vụ liên quan ({services.Count}):");
                foreach (var s in services.Take(5))
                    fb.AppendLine($"- {s.Name}: {Vnd(s.Price)} ({s.Duration} phút)");
            }
            if (rooms.Count > 0)
            {
                fb.AppendLine($"\n🏠 Phòng lưu trú ({rooms.Count}):");
                foreach (var r in rooms.Take(5))
                    fb.AppendLine($"- {r.RoomName} ({r.RoomType}): {Vnd(r.PricePerDay)}/ngày");
            }
            return new ChatMessageResponseDto { Reply = fb.ToString().Trim(), Source = "database", Timestamp = DateTime.UtcNow };
        }
    }
}
