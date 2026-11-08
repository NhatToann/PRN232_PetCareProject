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

        public ChatbotService(
            IChatbotRepository repo,
            IOptions<ChatbotOptions> options,
            IAiProviderService aiProvider,
            ILogger<ChatbotService> logger)
        {
            _repo = repo;
            _options = options.Value;
            _aiProvider = aiProvider;
            _logger = logger;
        }

        public async Task<ChatMessageResponseDto> GetResponseAsync(
            ChatMessageRequestDto request,
            CancellationToken ct = default)
        {
            var message = request.Message?.ToLower() ?? "";

            // 1. Phân tích intent bằng AI
            var aiIntent = await AnalyzeIntentWithAi(message, ct);

            // 2. Xử lý các intent tĩnh (tự giới thiệu)
            if (aiIntent.Intent == "introduction")
            {
                return new ChatMessageResponseDto
                {
                    Reply = "Mình là trợ lý ảo của PetShop 🐾 Mình có thể giúp bạn tìm sản phẩm, đặt lịch, kiểm tra đơn hàng và tư vấn chăm sóc sức khỏe cho thú cưng...",
                    Source = "ai"
                };
            }

            // 3. Xử lý product_count (đếm sản phẩm theo điều kiện)
            if (aiIntent.Intent == "product_count")
            {
                aiIntent.Parameters.TryGetValue("product_type", out var type);
                aiIntent.Parameters.TryGetValue("pet", out var pet);

                var count = await _repo.CountProductsAsync(
                    string.IsNullOrEmpty(type) ? null : type,
                    string.IsNullOrEmpty(pet) ? null : pet,
                    ct);

                return new ChatMessageResponseDto
                {
                    Reply = $"🐾 Hiện cửa hàng đang có {count} sản phẩm phù hợp với nhu cầu của bạn.",
                    Source = "database"
                };
            }

            // 4. Nếu là các intent khác (product_search, service_inquiry, booking, order_status...),
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

                var messagesForAi = new List<ChatHistoryItemDto>(request.History ?? new List<ChatHistoryItemDto>())
                {
                    new ChatHistoryItemDto { Role = "user", Content = request.Message! }
                };

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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling AI provider");
                return new ChatMessageResponseDto
                {
                    Reply = "Error: " + ex.Message + (ex.InnerException != null ? " | Inner: " + ex.InnerException.Message : ""),
                    Source = "fallback",
                    Timestamp = DateTime.UtcNow
                };
            }
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
            var prompt = $@"
Phân tích câu sau và trả về JSON:

- intent: một trong các giá trị:
  greeting, product_search, product_count, service_inquiry, booking, order_status, introduction, pet_info, payment, support, unknown

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

                int startIndex = raw.IndexOf('{');
                int endIndex = raw.LastIndexOf('}');

                if (startIndex >= 0 && endIndex >= startIndex)
                {
                    var cleanJson = raw.Substring(startIndex, endIndex - startIndex + 1);
                    return System.Text.Json.JsonSerializer.Deserialize<AiIntentResult>(cleanJson,
                        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? new AiIntentResult { Intent = "unknown" };
                }

                return new AiIntentResult { Intent = "unknown" };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to analyze intent with AI");
                return new AiIntentResult { Intent = "unknown" };
            }
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
                    .Where(p => p.Age > 0 && p.Age < 50)
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

            return context;
        }

        public async Task<string> BuildSystemPromptAsync(ConversationContextDto? context, CancellationToken ct = default)
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
                foreach (var product in context.Products.Take(30))
                    sb.AppendLine($"- {product}");
                if (context.Products.Count > 30)
                    sb.AppendLine($"... và còn {context.Products.Count - 30} sản phẩm khác");
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
            sb.AppendLine("THÔNG TIN LIÊN HỆ:");
            sb.AppendLine("- Địa chỉ: [Quận/Huyện], TP.HCM");
            sb.AppendLine("- Hotline: 0901 XXX XXX");
            sb.AppendLine("- Giờ hoạt động: 8:00 - 20:00 (Thứ 2 - CN)");
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
                            string.Join("\n", products.Select(p => $"- {p.Name}: {p.Price:N0}đ (Còn {p.StockQuantity})"));
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
                        string.Join("\n", services.Select(s => $"- {s.Name}: {s.Price:N0}đ ({s.Duration} phút)"));

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
                            $"- #{o.OrderId}: {o.Status} - {o.TotalAmount:N0}đ"));

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
        private async Task<ChatMessageResponseDto?> TryHandleProductQueryDirectlyAsync(string message, CancellationToken ct)
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
                    ctx.AppendLine($"  • {p.Name} | Danh mục: {p.CategoryName ?? "N/A"} | Giá: {p.Price:N0}đ | Tồn kho: {p.StockQuantity}");
            }

            if (services.Count > 0)
            {
                ctx.AppendLine("\n🛁 DỊCH VỤ LIÊN QUAN (dữ liệu thực từ database):");
                foreach (var s in services.Take(5))
                    ctx.AppendLine($"  • {s.Name} | Loại: {s.ServiceType} | Giá: {s.Price:N0}đ | Thời gian: {s.Duration} phút");
            }

            if (rooms.Count > 0)
            {
                ctx.AppendLine("\n🏠 PHÒNG LƯU TRÚ LIÊN QUAN (dữ liệu thực từ database):");
                foreach (var r in rooms.Take(5))
                    ctx.AppendLine($"  • {r.RoomName} | Loại: {r.RoomType} | Giá: {r.PricePerDay:N0}đ/ngày | Số phòng trống: {r.Rooms}");
            }

            ctx.AppendLine("\nHƯỚNG DẪN TRẢ LỜI (BẮT BUỘC):");
            ctx.AppendLine("1. Trả lời trực tiếp câu hỏi (tồn kho, giá, thông tin) dựa vào dữ liệu thực ở trên");
            ctx.AppendLine("2. Mô tả đặc điểm, công dụng, cách dùng của sản phẩm/dịch vụ (dùng kiến thức tổng quát)");
            ctx.AppendLine("3. Nêu giá thị trường thông thường để khách so sánh với giá shop");
            ctx.AppendLine("4. Gợi ý sản phẩm/dịch vụ phù hợp nhất từ danh sách trên");
            ctx.AppendLine("5. Ngắn gọn dưới 200 từ, thân thiện, dùng emoji 🐾");
            ctx.AppendLine("NGHIÊM CẤM: Không bịa sản phẩm/dịch vụ không có trong danh sách trên.");

            // === 7. Gọi AI ===
            try
            {
                if (_aiProvider?.IsAvailable == true)
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
                fb.AppendLine($"� Sản phẩm liên quan ({inStock.Count}/{products.Count} còn hàng):");
                foreach (var p in inStock.Take(8))
                    fb.AppendLine($"- {p.Name}: {p.Price:N0}đ (Còn {p.StockQuantity})");
                if (inStock.Count > 8) fb.AppendLine($"... và còn {inStock.Count - 8} sản phẩm khác");
            }
            if (services.Count > 0)
            {
                fb.AppendLine($"\n🛁 Dịch vụ liên quan ({services.Count}):");
                foreach (var s in services.Take(5))
                    fb.AppendLine($"- {s.Name}: {s.Price:N0}đ ({s.Duration} phút)");
            }
            if (rooms.Count > 0)
            {
                fb.AppendLine($"\n🏠 Phòng lưu trú ({rooms.Count}):");
                foreach (var r in rooms.Take(5))
                    fb.AppendLine($"- {r.RoomName} ({r.RoomType}): {r.PricePerDay:N0}đ/ngày");
            }
            return new ChatMessageResponseDto { Reply = fb.ToString().Trim(), Source = "database", Timestamp = DateTime.UtcNow };
        }
    }
}
