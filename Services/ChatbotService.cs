using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PetShop.Data;
using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Services;

public sealed class ChatbotService : IChatbotService
{
    private readonly ShopPetDatabaseContext _db;
    private readonly ChatbotOptions _options;
    private readonly IAiProviderService _aiProvider;
    private readonly ILogger<ChatbotService> _logger;

    public ChatbotService(
        ShopPetDatabaseContext db,
        IOptions<ChatbotOptions> options,
        IAiProviderService aiProvider,
        ILogger<ChatbotService> logger)
    {
        _db = db;
        _options = options.Value;
        _aiProvider = aiProvider;
        _logger = logger;
    }

    public async Task<ChatMessageResponseDto> GetResponseAsync(ChatMessageRequestDto request, CancellationToken ct = default)
    {
        var message = request.Message?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(message))
        {
            return new ChatMessageResponseDto { Reply = "Bạn nhập nội dung cần hỏi giúp mình nhé.", Source = "fallback" };
        }

        var common = TryAnswerCommon(message);
        if (common is not null)
        {
            return common;
        }

        var direct = await TryAnswerFromDatabaseAsync(message, request.CustomerId, ct);
        if (direct is not null)
        {
            return direct;
        }

        var smartDatabase = await TryHandlePetShopAgentQueryAsync(message, ct);
        if (smartDatabase is not null)
        {
            return smartDatabase;
        }

        if (!_aiProvider.IsAvailable)
        {
            return await BuildUnavailableAiResponseAsync(message, request.CustomerId, ct);
        }

        var context = await GetContextAsync(request.CustomerId, ct);
        var prompt = await BuildSystemPromptAsync(context, ct);
        var history = new List<ChatHistoryItemDto>(request.History ?? new List<ChatHistoryItemDto>())
        {
            new() { Role = "user", Content = message }
        };

        try
        {
            var reply = await _aiProvider.GenerateTextAsync(
                _options.DefaultModel,
                prompt,
                history,
                _options.Temperature,
                _options.MaxTokens,
                ct);

            return new ChatMessageResponseDto
            {
                Reply = string.IsNullOrWhiteSpace(reply) ? "Mình chưa có câu trả lời phù hợp. Bạn thử hỏi cụ thể hơn nhé." : reply.Trim(),
                Source = _aiProvider.ProviderName,
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chatbot AI provider failed");
            return await BuildUnavailableAiResponseAsync(message, request.CustomerId, ct);
        }
    }

    public async Task<AiIntentResult> AnalyzeIntentWithAi(string message, CancellationToken ct = default)
    {
        if (!_aiProvider.IsAvailable)
        {
            return new AiIntentResult { Intent = "unknown" };
        }

        var prompt =
            "Phân tích câu sau và chỉ trả về JSON hợp lệ:\n" +
            "{\n" +
            "  \"intent\": \"greeting|product_search|product_count|service_inquiry|booking|order_status|pet_info|payment|support|unknown\",\n" +
            "  \"parameters\": {}\n" +
            "}\n" +
            $"Câu: \"{message}\"";

        try
        {
            var raw = await _aiProvider.GenerateTextAsync(
                _options.DefaultModel,
                string.Empty,
                new List<ChatHistoryItemDto> { new() { Role = "user", Content = prompt } },
                0.2,
                200,
                ct);
            var start = raw.IndexOf('{');
            var end = raw.LastIndexOf('}');
            if (start >= 0 && end >= start)
            {
                return JsonSerializer.Deserialize<AiIntentResult>(
                    raw[start..(end + 1)],
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new AiIntentResult { Intent = "unknown" };
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Intent detection failed");
        }

        return new AiIntentResult { Intent = "unknown" };
    }

    public async Task<ConversationContextDto> GetContextAsync(int? customerId, CancellationToken ct = default)
    {
        var context = new ConversationContextDto { CustomerId = customerId };

        if (customerId.HasValue)
        {
            context.CustomerName = await _db.Customers
                .AsNoTracking()
                .Where(c => c.CustomerId == customerId.Value)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(ct);

            context.AvailablePets = await _db.Pets
                .AsNoTracking()
                .Where(p => p.CustomerId == customerId.Value && p.DeletedAt == null)
                .OrderByDescending(p => p.Id)
                .Take(10)
                .Select(p => $"{p.PetName} ({p.Age} tuổi)")
                .ToListAsync(ct);

            context.RecentOrders = await _db.Orders
                .AsNoTracking()
                .Where(o => o.CustomerId == customerId.Value)
                .OrderByDescending(o => o.OrderDate)
                .Take(5)
                .Select(o => $"Đơn hàng #{o.OrderId} - {o.Status} - {o.TotalAmount:N0}đ")
                .ToListAsync(ct);

            context.RecentBookings = await _db.Bookings
                .AsNoTracking()
                .Where(b => b.CustomerId == customerId.Value)
                .OrderByDescending(b => b.CreatedAt)
                .Take(5)
                .Select(b => $"Lịch hẹn #{b.BookingId} - {b.Status} - {b.AppointmentStart:dd/MM/yyyy HH:mm}")
                .ToListAsync(ct);
        }

        context.Products = await _db.Products
            .AsNoTracking()
            .Where(p => p.StockQuantity > 0)
            .OrderByDescending(p => p.ProductId)
            .Take(50)
            .Select(p => $"{p.Name} - {p.Price:N0}đ (Còn {p.StockQuantity})")
            .ToListAsync(ct);

        context.Services = await _db.PetServices
            .AsNoTracking()
            .Where(s => s.Status == "active")
            .OrderBy(s => s.Name)
            .Select(s => $"{s.Name} - {s.Price:N0}đ - {s.Duration} phút")
            .ToListAsync(ct);

        context.ProductCategories = await _db.ProductCategories.AsNoTracking().Select(c => c.Name).ToListAsync(ct);
        context.Species = await _db.Species.AsNoTracking().Select(s => s.SpeciesName).ToListAsync(ct);
        context.StaffList = await _db.Staff.AsNoTracking().Select(s => $"{s.Name} - {s.Position}").ToListAsync(ct);
        context.DoctorList = await _db.Doctors.AsNoTracking().Select(d => $"{d.Name} - {d.Specialization}").ToListAsync(ct);
        context.BoardingRooms = await _db.BoardingRooms
            .AsNoTracking()
            .Select(r => $"{r.RoomName} - {r.RoomType} - {r.PricePerDay:N0}đ/ngày")
            .ToListAsync(ct);

        return context;
    }

    public Task<string> BuildSystemPromptAsync(ConversationContextDto? context, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Bạn là LeadBot, agent tư vấn chuyên biệt của Pet Toy Shop.");
        sb.AppendLine("Trả lời bằng tiếng Việt, thân thiện, ngắn gọn, đúng trọng tâm.");
        sb.AppendLine("Luôn ưu tiên dữ liệu database trong ngữ cảnh khi nói về sản phẩm, dịch vụ, phòng lưu trú, đơn hàng hoặc lịch hẹn.");
        sb.AppendLine("Không bịa sản phẩm, giá, số lượng tồn kho, nhân viên, bác sĩ, đơn hàng hoặc lịch hẹn ngoài dữ liệu đã cung cấp.");
        sb.AppendLine("Nếu dữ liệu không có trong ngữ cảnh, nói rõ hiện chưa tìm thấy thông tin đó trong hệ thống và gợi ý khách hỏi theo tên sản phẩm/dịch vụ cụ thể hơn.");
        sb.AppendLine("Không chẩn đoán y khoa cụ thể; nếu liên quan sức khỏe hãy khuyên khách gặp bác sĩ thú y.");
        sb.AppendLine();

        if (context is not null)
        {
            if (!string.IsNullOrWhiteSpace(context.CustomerName)) sb.AppendLine($"Khách hàng: {context.CustomerName}");
            if (context.AvailablePets.Count > 0) sb.AppendLine($"Thú cưng: {string.Join(", ", context.AvailablePets)}");
            AppendList(sb, "Sản phẩm", context.Products.Take(30));
            AppendList(sb, "Dịch vụ", context.Services);
            AppendList(sb, "Phòng lưu trú", context.BoardingRooms);
            AppendList(sb, "Đơn hàng gần đây", context.RecentOrders);
            AppendList(sb, "Lịch hẹn gần đây", context.RecentBookings);
            AppendList(sb, "Nhân viên", context.StaffList);
            AppendList(sb, "Bác sĩ", context.DoctorList);
        }

        return Task.FromResult(sb.ToString());
    }

    private async Task<ChatMessageResponseDto?> TryAnswerFromDatabaseAsync(string message, int? customerId, CancellationToken ct)
    {
        var text = message.ToLowerInvariant();

        if (text.Contains("bao nhiêu sản phẩm") || text.Contains("số lượng sản phẩm"))
        {
            var count = await _db.Products.CountAsync(ct);
            return new ChatMessageResponseDto { Reply = $"Hiện cửa hàng có {count} sản phẩm trong hệ thống.", Source = "database" };
        }

        if (text.Contains("dịch vụ") || text.Contains("spa") || text.Contains("grooming"))
        {
            var services = await _db.PetServices
                .AsNoTracking()
                .Where(s => s.Status == "active")
                .OrderBy(s => s.Name)
                .Take(8)
                .Select(s => $"- {s.Name}: {s.Price:N0}đ ({s.Duration} phút)")
                .ToListAsync(ct);
            if (services.Count > 0)
            {
                return new ChatMessageResponseDto { Reply = "Các dịch vụ đang có:\n" + string.Join("\n", services), Source = "database" };
            }
        }

        if (text.Contains("phòng") || text.Contains("boarding") || text.Contains("lưu trú") || text.Contains("khách sạn"))
        {
            var rooms = await _db.BoardingRooms
                .AsNoTracking()
                .OrderBy(r => r.PricePerDay)
                .Take(8)
                .Select(r => $"- {r.RoomName} ({r.RoomType}): {r.PricePerDay:N0}đ/ngày - còn {r.Rooms} phòng")
                .ToListAsync(ct);
            if (rooms.Count > 0)
            {
                return new ChatMessageResponseDto { Reply = "Các phòng lưu trú đang có:\n" + string.Join("\n", rooms), Source = "database" };
            }
        }

        if ((text.Contains("đơn hàng") || text.Contains("order")) && customerId.HasValue)
        {
            var orders = await _db.Orders
                .AsNoTracking()
                .Where(o => o.CustomerId == customerId.Value)
                .OrderByDescending(o => o.OrderDate)
                .Take(5)
                .Select(o => $"- #{o.OrderId}: {o.Status} - {o.TotalAmount:N0}đ")
                .ToListAsync(ct);
            return new ChatMessageResponseDto
            {
                Reply = orders.Count == 0 ? "Bạn chưa có đơn hàng nào trong hệ thống." : "Đơn hàng gần đây:\n" + string.Join("\n", orders),
                Source = "database"
            };
        }

        var keywords = ExtractKeywords(text);
        if (keywords.Count == 0) return null;

        var productsQuery = _db.Products.Include(p => p.Category).AsNoTracking().AsQueryable();
        foreach (var keyword in keywords.Take(3))
        {
            var local = keyword;
            productsQuery = productsQuery.Where(p =>
                p.Name.Contains(local) ||
                (p.Description != null && p.Description.Contains(local)) ||
                (p.Category != null && p.Category.Name.Contains(local)));
        }

        var products = await productsQuery
            .Take(6)
            .Select(p => $"- {p.Name}: {p.Price:N0}đ (Còn {p.StockQuantity})")
            .ToListAsync(ct);

        if (products.Count > 0)
        {
            return new ChatMessageResponseDto { Reply = "Mình tìm thấy sản phẩm phù hợp:\n" + string.Join("\n", products), Source = "database" };
        }

        return null;
    }

    private static ChatMessageResponseDto? TryAnswerCommon(string message)
    {
        var text = Normalize(message);
        var greetingWords = new[] { "hi", "hello", "chao", "xin chao", "alo", "hey" };
        if (greetingWords.Any(g => text == g || text.StartsWith(g + " ")))
        {
            return new ChatMessageResponseDto
            {
                Reply = "Chào bạn! Mình là LeadBot, trợ lý tư vấn của Pet Toy Shop. Bạn có thể hỏi mình về sản phẩm, giá, tồn kho, dịch vụ spa/grooming, phòng lưu trú, đơn hàng hoặc lịch hẹn nhé.",
                Source = "agent"
            };
        }

        if (text.Contains("ban la ai") || text.Contains("chatbot") || text.Contains("leadbot"))
        {
            return new ChatMessageResponseDto
            {
                Reply = "Mình là LeadBot, agent hỗ trợ Pet Toy Shop. Mình ưu tiên trả lời theo dữ liệu thật trong hệ thống để tránh tư vấn sai sản phẩm, giá hoặc lịch hẹn.",
                Source = "agent"
            };
        }

        return null;
    }

    private async Task<ChatMessageResponseDto?> TryHandlePetShopAgentQueryAsync(string message, CancellationToken ct)
    {
        var keywords = ExtractKeywords(Normalize(message));
        if (keywords.Count == 0)
        {
            return null;
        }

        var products = await FindProductsAsync(keywords, ct);
        var services = await FindServicesAsync(keywords, ct);
        var rooms = await FindRoomsAsync(keywords, ct);

        if (products.Count == 0 && services.Count == 0 && rooms.Count == 0)
        {
            if (LooksLikeShopQuestion(message))
            {
                return new ChatMessageResponseDto
                {
                    Reply = "Mình chưa tìm thấy dữ liệu phù hợp trong hệ thống Pet Toy Shop. Bạn thử hỏi theo tên sản phẩm, loại dịch vụ, hoặc từ khóa cụ thể hơn nhé.",
                    Source = "database"
                };
            }

            return null;
        }

        var sb = new StringBuilder();
        if (products.Count > 0)
        {
            sb.AppendLine("Mình tìm thấy sản phẩm phù hợp trong hệ thống:");
            foreach (var product in products.Take(6))
            {
                sb.AppendLine($"- {product.Name}: {product.Price:N0}đ (Còn {product.StockQuantity})");
            }
        }

        if (services.Count > 0)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine("Dịch vụ liên quan:");
            foreach (var service in services.Take(5))
            {
                sb.AppendLine($"- {service.Name}: {service.Price:N0}đ ({service.Duration} phút)");
            }
        }

        if (rooms.Count > 0)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine("Phòng lưu trú liên quan:");
            foreach (var room in rooms.Take(5))
            {
                sb.AppendLine($"- {room.RoomName} ({room.RoomType}): {room.PricePerDay:N0}đ/ngày - còn {room.Rooms} phòng");
            }
        }

        return new ChatMessageResponseDto
        {
            Reply = sb.ToString().Trim(),
            Source = "database",
            Timestamp = DateTime.UtcNow
        };
    }

    private async Task<ChatMessageResponseDto> BuildUnavailableAiResponseAsync(string message, int? customerId, CancellationToken ct)
    {
        var context = await GetContextAsync(customerId, ct);
        var totalProducts = await _db.Products.AsNoTracking().CountAsync(ct);
        var totalServices = context.Services.Count;

        return new ChatMessageResponseDto
        {
            Reply = "Mình chưa tìm thấy câu trả lời chính xác trong dữ liệu hiện có. Hiện mình vẫn có thể tra cứu trực tiếp trong hệ thống: " +
                    $"{totalProducts} sản phẩm, {totalServices} dịch vụ, phòng lưu trú, đơn hàng và lịch hẹn của bạn. " +
                    "Bạn hãy hỏi cụ thể hơn như tên sản phẩm, loại dịch vụ, giá, tồn kho, đơn hàng hoặc lịch hẹn nhé.",
            Source = "agent",
            Timestamp = DateTime.UtcNow
        };
    }

    private async Task<List<Product>> FindProductsAsync(List<string> keywords, CancellationToken ct)
    {
        var products = new List<Product>();
        foreach (var keyword in keywords.Take(6))
        {
            var local = keyword;
            products.AddRange(await _db.Products
                .Include(p => p.Category)
                .AsNoTracking()
                .Where(p =>
                    p.Name.Contains(local) ||
                    (p.Description != null && p.Description.Contains(local)) ||
                    (p.Category != null && p.Category.Name.Contains(local)))
                .Take(10)
                .ToListAsync(ct));
        }

        return products
            .DistinctBy(p => p.ProductId)
            .OrderByDescending(p => p.StockQuantity > 0)
            .ThenBy(p => p.Price)
            .ToList();
    }

    private async Task<List<PetService>> FindServicesAsync(List<string> keywords, CancellationToken ct)
    {
        var services = new List<PetService>();
        foreach (var keyword in keywords.Take(6))
        {
            var local = keyword;
            services.AddRange(await _db.PetServices
                .AsNoTracking()
                .Where(s =>
                    s.Status == "active" &&
                    (s.Name.Contains(local) ||
                     s.ServiceType.Contains(local) ||
                     (s.Description != null && s.Description.Contains(local))))
                .Take(10)
                .ToListAsync(ct));
        }

        return services
            .DistinctBy(s => s.ServiceId)
            .OrderBy(s => s.Price)
            .ToList();
    }

    private async Task<List<BoardingRoom>> FindRoomsAsync(List<string> keywords, CancellationToken ct)
    {
        var rooms = new List<BoardingRoom>();
        foreach (var keyword in keywords.Take(6))
        {
            var local = keyword;
            rooms.AddRange(await _db.BoardingRooms
                .AsNoTracking()
                .Where(r =>
                    r.RoomName.Contains(local) ||
                    r.RoomType.Contains(local) ||
                    (r.Description != null && r.Description.Contains(local)))
                .Take(10)
                .ToListAsync(ct));
        }

        return rooms
            .DistinctBy(r => r.RoomId)
            .OrderBy(r => r.PricePerDay)
            .ToList();
    }

    private static bool LooksLikeShopQuestion(string message)
    {
        var text = Normalize(message);
        var shopWords = new[]
        {
            "san pham", "do choi", "thuc an", "phu kien", "thuoc", "vitamin", "gia", "ton kho",
            "dich vu", "spa", "grooming", "tam", "cat tia", "booking", "lich hen", "don hang",
            "phong", "boarding", "luu tru", "bac si", "nhan vien", "cho", "meo"
        };

        return shopWords.Any(text.Contains);
    }

    private static string Normalize(string value)
    {
        var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        return Regex.Replace(sb.ToString().Normalize(NormalizationForm.FormC), @"\s+", " ");
    }

    private static List<string> ExtractKeywords(string text)
    {
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "cho", "cua", "của", "toi", "tôi", "ban", "bạn", "shop", "co", "có", "khong", "không",
            "gia", "giá", "bao", "nhieu", "nhiêu", "san", "sản", "pham", "phẩm", "can", "cần",
            "muon", "muốn", "tim", "tìm", "mua", "la", "là"
        };

        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(w => w.Trim(',', '.', '?', '!', ':', ';'))
            .Where(w => w.Length >= 2 && !stopWords.Contains(w))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();
    }

    private static void AppendList(StringBuilder sb, string title, IEnumerable<string> items)
    {
        var list = items.Where(i => !string.IsNullOrWhiteSpace(i)).ToList();
        if (list.Count == 0) return;

        sb.AppendLine(title + ":");
        foreach (var item in list)
        {
            sb.AppendLine("- " + item);
        }
        sb.AppendLine();
    }
}
