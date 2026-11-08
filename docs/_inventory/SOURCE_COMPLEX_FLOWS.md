# Inventory — Luồng nghiệp vụ phức tạp (OJT → PRN232)

> **Nguồn (chỉ đọc, không sửa):** `Projectojt/BackEnd` + `Projectojt/frontend`
> **Phạm vi:** chatbot AI, thanh toán PayOS, OAuth/Google, email OTP + hoá đơn, upload ảnh, notification, đổi ca, đặt lịch spa.
> **Mục đích:** bản đồ "tái tạo được" để đối chiếu khi migrate sang `PRN232_PetCareProject`.
> Mọi prompt, chuỗi message tiếng Việt, URL, tên header, thuật toán ký, JSON body mẫu bên dưới được **copy NGUYÊN VĂN** từ source.

---

## 0. Bảng tổng quan endpoint

| Nhóm | Method + Path | Auth | Input chính | Output chính |
|---|---|---|---|---|
| Chatbot | `POST /api/chatbot/message` | không (chỉ customerId trong body) | `ChatMessageRequestDto` | `ChatMessageResponseDto` |
| Chatbot | `POST /api/chatbot/intent` | không | `{ message }` | `AiIntentResult` |
| Chatbot | `GET /api/chatbot/context/{customerId}` | không | path int | `ConversationContextDto` |
| Chatbot | `GET /api/chatbot/health` | không | — | `ChatbotHealthDto` |
| PayOS | `POST /api/payos/spa/create` | không | `CreateSpaPayOSRequest` | `PayOSCheckoutResult` |
| PayOS | `POST /api/payos/order/create` | không | `CreateOrderPayOSRequest` | `PayOSCheckoutResult` |
| PayOS | `POST /api/payos/webhook` | header `x-payos-signature` | raw JSON body | `{ message }` |
| Auth | `POST /api/auth/register` | không | `RegisterRequestDto` | `AuthResponseDto` |
| Auth | `POST /api/auth/login` | không | `LoginRequestDto` | `AuthResponseDto` |
| Auth | `POST /api/auth/google-login` | không | `GoogleLoginRequestDto` | `AuthResponseDto` |
| Auth | `POST /api/auth/send-email-otp` | không | `SendEmailOtpRequestDto` | `SimpleMessageDto` |
| Auth | `POST /api/auth/verify-email-otp` | không | `VerifyEmailOtpRequestDto` | `SimpleMessageDto` (chứa reset token) |
| Auth | `POST /api/auth/reset-password` | không | `ResetPasswordRequestDto` | `SimpleMessageDto` |
| Auth | `GET /api/auth/google-client-id` | không | — | `{ clientId }` |
| Pets | `POST /api/pets/upload-image` | không | multipart `image` | `{ imageUrl }` |
| Notification | `GET /api/notifications/{staffId}` | không | `unreadOnly?` | `NotificationDto[]` |
| Notification | `GET /api/notifications/{staffId}/unread-count` | không | — | `UnreadCountDto` |
| Notification | `PUT /api/notifications/{requestId}/read` | không | `staffId` query | `{ message }` |
| Notification | `PUT /api/notifications/{staffId}/read-all` | không | — | `{ message }` |
| ShiftExchange | `POST /api/shift-exchange/pass` | không | `CreatePassShiftRequestDto` | `ShiftExchangeActionResultDto` |
| ShiftExchange | `POST /api/shift-exchange/swap` | không | `CreateSwapShiftRequestDto` | `ShiftExchangeActionResultDto` |
| ShiftExchange | `POST /api/shift-exchange/accept/{requestId}` | không | `responderStaffId` query | `ShiftExchangeActionResultDto` |
| ShiftExchange | `POST /api/shift-exchange/decline/{requestId}` | không | `responderStaffId` query | `ShiftExchangeActionResultDto` |
| ShiftExchange | `POST /api/shift-exchange/cancel/{requestId}` | không | `staffId` query | `ShiftExchangeActionResultDto` |
| Spa | `POST /api/spa-booking/availability` | không | `SpaAvailabilityRequest` | `SpaAvailabilityResponse` |
| Spa | `POST /api/spa-booking/validate-slot` | không | `BookingSlotValidationRequest` | `BookingSlotValidationResult` |
| Spa | `POST /api/spa-booking/estimate` | query `customerId` | `SpaBookingEstimateRequest` | `SpaBookingEstimateDto` |
| Spa | `POST /api/spa-booking` | query `customerId` | `CreateSpaBookingRequest` | `SpaBookingDto` (+ `bookingIds[]`) |
| Spa | `PATCH /api/spa-booking/{bookingId}/status` | không | `{ status }` | `{ message: "Updated" }` |
| Spa | `POST /api/spa-booking/review` | query `customerId` | `CreateSpaReviewRequest` | `{ reviewId }` |

---

## 1. Chatbot AI

### 1.1 Kiến trúc provider

Interface duy nhất (`Interfaces/IAiProviderService.cs`):

```csharp
public interface IAiProviderService
{
    string ProviderName { get; }
    Task<string> GenerateTextAsync(
        string model,
        string systemPrompt,
        List<ChatHistoryItemDto> messages,
        double temperature = 0.7,
        int maxTokens = 2048,
        CancellationToken ct = default);
    bool IsAvailable { get; }
}
```

DI trong `Program.cs` — **chỉ Gemini được đăng ký**, không có chain fallback thật sự:

```csharp
builder.Services.AddHttpClient<PetShop.Interfaces.IAiProviderService, PetShop.Services.AI.GeminiProviderService>();
builder.Services.Configure<PetShop.Models.ChatbotOptions>(
    builder.Configuration.GetSection("Chatbot"));
```

> **Cảnh báo khi migrate:** `ChatbotOptions.DefaultProvider = "groq"` (mặc định trong class) nhưng **không có provider Groq nào trong repo** và `Program.cs` chỉ bind `GeminiProviderService`. `OllamaProviderService` tồn tại nhưng **không được đăng ký DI** → không bao giờ chạy. Trường `DefaultProviderName` không được đọc ở đâu. Cần quyết định: giữ 1 provider (Gemini) hay dựng chain fallback thật.

### 1.2 Cấu hình `appsettings.json` (section `Chatbot`)

```json
"Chatbot": {
  "DefaultModel": "gemini-flash-latest",
  "Temperature": 0.5,
  "MaxTokens": 1000,
  "Providers": {
    "gemini": {
      "BaseUrl": "https://generativelanguage.googleapis.com/v1beta/models",
      "ApiKey": "AIzaSyBwieKdmFJVUir8JvGqoS_cZs4wFgD3eXo",
      "DefaultModel": "gemini-flash-latest",
      "TimeoutSeconds": 120
    },
    "ollama": {
      "BaseUrl": "http://localhost:11434",
      "DefaultModel": "llama3.2",
      "TimeoutSeconds": 120
    }
  }
}
```

`Models/ChatbotOptions.cs`:

```csharp
public class ChatbotOptions
{
    public const string SectionName = "Chatbot";
    public const string DefaultProvider = "groq";

    public string DefaultModel { get; set; } = "llama3.2";
    public string DefaultProviderName { get; set; } = DefaultProvider;
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 1000;
    public bool EnableDatabaseQuery { get; set; } = true;
    public string SystemPromptPath { get; set; } = string.Empty;

    public Dictionary<string, AiProviderConfig> Providers { get; set; } = new();
}
```

> **`SystemPromptPath`**: thuộc tính tồn tại nhưng **không được đọc ở bất kỳ đâu** (`Grep` toàn repo chỉ trả về đúng 2 file khai báo). **KHÔNG tồn tại file prompt riêng** — system prompt được **hard-code trong code** (`ChatbotService.BuildSystemPromptAsync`). Toàn bộ prompt nằm ở mục 1.5 bên dưới.

### 1.3 Gemini provider — request/response

**URL:** `POST {BaseUrl}/{model}:generateContent`
→ thực tế: `POST https://generativelanguage.googleapis.com/v1beta/models/gemini-flash-latest:generateContent`

**Header:** `X-goog-api-key: {ApiKey}` (được set trên `HttpClient.DefaultRequestHeaders`, `Remove` trước khi `Add`).

**Body JSON mẫu (khi có systemPrompt):**

```json
{
  "system_instruction": {
    "parts": [
      { "text": "<SYSTEM PROMPT>" }
    ]
  },
  "contents": [
    { "role": "user",  "parts": [ { "text": "..." } ] },
    { "role": "model", "parts": [ { "text": "..." } ] }
  ],
  "generationConfig": {
    "temperature": 0.5,
    "maxOutputTokens": 1000
  }
}
```

**Body JSON mẫu (khi KHÔNG có systemPrompt):**

```json
{
  "contents": [ ... ],
  "generationConfig": { "temperature": 0.5, "maxOutputTokens": 1000 }
}
```

**Quy tắc mapping & lọc message:**
- `msg.Role == "assistant"` → đổi thành `"model"`; các role khác → `"user"`.
- **Bỏ qua** message có `Role`/`Content` rỗng, hoặc `Role == "string"`, hoặc `Content == "string"` (placeholder do Swagger UI sinh).
- Model: nếu `model` rỗng hoặc `"default"` → dùng `providerConfig.DefaultModel`, nếu rỗng → `"gemini-flash-latest"`.
- Temperature: `temperature > 0 && temperature <= 2` → dùng; ngược lại → `providerConfig.Temperature`.
- MaxTokens: `maxTokens > 0` → dùng; ngược lại → `providerConfig.MaxTokens`, nếu ≤ 0 → `2048`.

**Response JSON mẫu (shape được đọc):**

```json
{
  "candidates": [
    {
      "content": {
        "role": "model",
        "parts": [ { "text": "Nội dung trả lời" } ]
      }
    }
  ]
}
```

Text được lấy: `responseJson?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text`.

### 1.4 Ollama provider — request/response (đăng ký trong config nhưng KHÔNG có trong DI)

**URL:** `POST {BaseUrl}/api/chat` → `POST http://localhost:11434/api/chat`

**Body JSON mẫu:**

```json
{
  "model": "llama3.2",
  "messages": [
    { "role": "system", "content": "<SYSTEM PROMPT>" },
    { "role": "user",   "content": "..." },
    { "role": "assistant", "content": "..." }
  ],
  "temperature": 0.5,
  "options": { "num_predict": 1000 },
  "stream": false
}
```

- System prompt được chèn vào **đầu `messages`** với `role: "system"` (không dùng `system_instruction`).
- Role được hạ chữ thường: `msg.Role.ToLowerInvariant()`.
- Cùng bộ lọc placeholder (`"string"` / rỗng) như Gemini.

**Response JSON mẫu:**

```json
{ "message": { "content": "Nội dung trả lời" } }
```

### 1.5 SYSTEM PROMPT — COPY NGUYÊN VĂN

> Nguồn: `ChatbotService.BuildSystemPromptAsync` — dựng động bằng `StringBuilder`, xen kẽ dữ liệu DB. Phần tĩnh copy nguyên văn bên dưới; phần động đánh dấu `{...}`.

#### 1.5.1 Prompt phân tích intent (`AnalyzeIntentWithAi`) — NGUYÊN VĂN

Gửi với `systemPrompt = string.Empty`, `temperature = 0.2`, `maxTokens = 200`.

```
Phân tích câu sau và trả về JSON:

- intent: một trong các giá trị:
  greeting, product_search, product_count, service_inquiry, booking, order_status, introduction, pet_info, payment, support, unknown

- parameters:
  + product_type (ví dụ: đồ chơi, thức ăn)
  + pet (chó, mèo)
  + keywords (chuỗi cách nhau bằng dấu phẩy)

Chỉ trả về JSON hợp lệ, không bọc trong markdown (không dùng ```json), không giải thích.

Câu: "{message}"
```

**Parse JSON:** lấy `raw.IndexOf('{')` → `raw.LastIndexOf('}')`, cắt substring, `JsonSerializer.Deserialize<AiIntentResult>(cleanJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })`. Nếu không có `{`/`}` → `new AiIntentResult { Intent = "unknown" }`.

#### 1.5.2 System prompt chính — NGUYÊN VĂN

```
[THÔNG TIN HỆ THỐNG]
- Ngày giờ hiện tại (Timezone Server): {DateTime.Now:dd/MM/yyyy HH:mm:ss}

NHIỆM VỤ:
- BẠN PHẢI HIỂU Ý NGƯỜI DÙNG DỰA TRÊN NGỮ CẢNH, KHÔNG PHỤ THUỘC TỪ KHÓA CỐ ĐỊNH.
- Nếu người dùng hỏi về bạn, hãy tự giới thiệu linh hoạt thông qua danh tính của nền tảng (Ví dụ: Bạn là AI của công ty X, nhưng đồng thời đang hỗ trợ PetShop).
- Nếu người dùng hỏi số lượng sản phẩm (product_count), hãy trả lời dựa trên dữ liệu đã được cung cấp (không đếm lung tung).
- Tư vấn sản phẩm cho thú cưng (thức ăn, đồ chơi, phụ kiện, thuốc, vitamin...) dựa vào danh sách.
- Giới thiệu dịch vụ (Spa & Grooming, Khám bệnh, Tiêm vaccine, Tẩy giun, Booking lịch hẹn...)
- Hỗ trợ đặt lịch hẹn dịch vụ spa/grooming
- Trả lời câu hỏi về giá cả
- Kiểm tra trạng thái đơn hàng và lịch hẹn
- Cung cấp thông tin về chăm sóc, dinh dưỡng, sức khỏe thú cưng
- Cung cấp thông tin về nhân viên, bác sĩ của shop

QUY TẮC TRẢ LỜI THÔNG MINH:
- LUÔN ưu tiên dùng dữ liệu từ database được đính kèm ở dưới bên dưới (nếu có)
- KHÔNG được nói 'tôi không biết' nếu thông tin đã có trong context
- Nếu câu hỏi đơn giản (ngày, giờ, phép toán), trả lời trực tiếp, không lan man
- Nếu có danh sách sản phẩm/dịch vụ → phải trích từ dữ liệu thật
```

**Các khối động được chèn tiếp (theo thứ tự, chỉ khi context có dữ liệu):**

```
NHÂN VIÊN & BÁC SĨ CỦA SHOP:
- {staff.Name - staff.Position}
- Bác sĩ: {doctor.Name - doctor.Specialization}

DANH SÁCH DỊCH VỤ (từ database):
- {service.Name - price đ - duration phút}

DANH SÁCH SẢN PHẨM (từ database):
- {product.Name - price đ (Còn stock)}
... và còn {N} sản phẩm khác          ← chỉ khi > 30 sản phẩm

PHÂN LOẠI SẢN PHẨM: {cat1, cat2, ...}

LOÀI THÚ CƯNG SHOP HỖ TRỢ: {species1, species2, ...}

PHÒNG LƯU TRÚ (Boarding):
- {room.RoomName - room.RoomType - price đ/ngày}
```

**Phần cố định còn lại — NGUYÊN VĂN:**

```
PHONG CÁCH:
- Thân thiện, nhiệt tình, sử dụng emoji phù hợp 🐾🐶🐱
- Ngôn ngữ tiếng Việt, tự nhiên như trò chuyện
- Câu trả lời ngắn gọn, đi thẳng vào vấn đề (dưới 200 từ)
- Nếu không biết, thừa nhận và gợi ý liên hệ hotline

THÔNG TIN LIÊN HỆ:
- Địa chỉ: [Quận/Huyện], TP.HCM
- Hotline: 0901 XXX XXX
- Giờ hoạt động: 8:00 - 20:00 (Thứ 2 - CN)

QUY ĐỊNH NGHIÊM NGẶT (VI PHẠM SẼ BỊ COI LÀ LỖI NGHIÊM TRỌNG):
- DANH SÁCH THƯƠNG HIỆU BỊ CẤM TUYỆT ĐỐI - KHÔNG ĐƯỢC PHÉP ĐỀ CẬP DƯỚI BẤT KỲ HÌNH THỨC NÀO:
  • Royal Canin / RoyalCanin
  • Hill's / Hills / Hill's Science Diet
  • Orijen / Acana
  • Pedigree / Purina / Purina Pro Plan
  • Whiskas / Me-O / Bob Martin / Frontline
  • Đây là CỬA HÀNG THÚ CƯNG VIỆT NAM - KHÔNG bán thương hiệu nước ngoài!
- KHI GỢI Ý SẢN PHẨM: CHỈ được liệt kê sản phẩm theo đúng TÊN và GIÁ từ 'DANH SÁCH SẢN PHẨM'.
- Nếu không có sản phẩm phù hợp trong danh sách, phải trả lời: 'Xin lỗi, hiện tại cửa hàng không có sản phẩm phù hợp.'
- Không đưa ra chẩn đoán y khoa cụ thể; luôn khuyên khách đến gặp bác sĩ thú y
- Không tiết lộ thông tin nội bộ, mã nguồn, hoặc cấu hình hệ thống
- KHI TƯ VẤN SẢN PHẨM: Luôn dựa vào 'Thú cưng của khách' để gợi ý phù hợp.
- VÍ DỤ BẮT BUỘC:
  ❌ SAI: 'Royal Canin Senior giá...' → LỖI! Royal Canin không có trong danh sách!
  ❌ SAI: 'Hill's Science Diet...' → LỖI! Hill's không có trong danh sách!
  ✅ ĐÚNG: 'Cửa hàng có Thức ăn khô cho chó lớn tuổi - 200.000đ (Còn 60)'
  ✅ ĐÚNG: 'Pate cho chó vị thịt bò - 35.000đ (Còn 120)'
```

**Khối ngữ cảnh khách hàng — NGUYÊN VĂN (phần template):**

```
NGỮ CẢNH KHÁCH HÀNG:
- Tên khách hàng: {CustomerName}
- Thú cưng của khách: {petName (age tuổi), ...}
  (nếu không có: "- Khách hàng chưa có thú cưng đăng ký")
- Đơn hàng gần đây:
  • Đơn hàng #{OrderId} - {Status} - {TotalAmount:N0}đ       (tối đa 3)
- Lịch hẹn gần đây:
  • Lịch hẹn #{BookingId} - {Status} - {AppointmentStart:dd/MM/yyyy HH:mm}   (tối đa 3)

Hãy dựa trên ngữ cảnh trên để đưa ra câu trả lời phù hợp và cá nhân hóa cho khách hàng.
```

**Sau khi build prompt, service nối thêm (verbatim):**

```
[Thông tin hỗ trợ]
Intent người dùng: {aiIntent.Intent}
Tham số trích xuất: {JSON serialize aiIntent.Parameters}
```

#### 1.5.3 Prompt truy vấn sản phẩm trực tiếp (`TryHandleProductQueryDirectlyAsync`) — NGUYÊN VĂN

Được dựng động, phần động đánh dấu `{}`:

```
Bạn là 'PetShop Assistant'. Trả lời câu hỏi của khách dựa trên dữ liệu thực từ database VÀ kiến thức tổng quát về sản phẩm/dịch vụ thú cưng.

CÂU HỎI: "{message}"
Từ khóa xác định: {keywords}

📦 SẢN PHẨM SHOP ĐANG CÓ (dữ liệu thực từ database):
  • {p.Name} | Danh mục: {p.Category?.Name ?? "N/A"} | Giá: {p.Price:N0}đ | Tồn kho: {p.StockQuantity}

🛁 DỊCH VỤ LIÊN QUAN (dữ liệu thực từ database):
  • {s.Name} | Loại: {s.ServiceType} | Giá: {s.Price:N0}đ | Thời gian: {s.Duration} phút

🏠 PHÒNG LƯU TRÚ LIÊN QUAN (dữ liệu thực từ database):
  • {r.RoomName} | Loại: {r.RoomType} | Giá: {r.PricePerDay:N0}đ/ngày | Số phòng trống: {r.Rooms}

HƯỚNG DẪN TRẢ LỜI (BẮT BUỘC):
1. Trả lời trực tiếp câu hỏi (tồn kho, giá, thông tin) dựa vào dữ liệu thực ở trên
2. Mô tả đặc điểm, công dụng, cách dùng của sản phẩm/dịch vụ (dùng kiến thức tổng quát)
3. Nêu giá thị trường thông thường để khách so sánh với giá shop
4. Gợi ý sản phẩm/dịch vụ phù hợp nhất từ danh sách trên
5. Ngắn gọn dưới 200 từ, thân thiện, dùng emoji 🐾
NGHIÊM CẤM: Không bịa sản phẩm/dịch vụ không có trong danh sách trên.
```

### 1.6 Luồng xử lý message (Input → kiểm tra → gọi provider → format)

**Input:** `ChatMessageRequestDto { Message, CustomerId?, History? }`

**Trình tự trong `GetResponseAsync`:**

1. `message = request.Message?.ToLower() ?? ""`
2. **Gọi `AnalyzeIntentWithAi(message)`** → `AiIntentResult { Intent, Parameters }`. Nếu provider lỗi → nuốt lỗi, trả `Intent = "unknown"` (không ném ra ngoài).
3. **Intent `introduction`** → trả cứng, `Source = "ai"`:
   > `Mình là trợ lý ảo của PetShop 🐾 Mình có thể giúp bạn tìm sản phẩm, đặt lịch, kiểm tra đơn hàng và tư vấn chăm sóc sức khỏe cho thú cưng...`
4. **Intent `product_count`** → query `Products` (Include `Category`), lọc theo `product_type` (`Category.Name.Contains || Name.Contains`) và `pet` (`Name.Contains || Description.Contains`), `CountAsync`. Trả:
   > `🐾 Hiện cửa hàng đang có {count} sản phẩm phù hợp với nhu cầu của bạn.` — `Source = "database"`
5. **Các intent khác** → `GetContextAsync` → `BuildSystemPromptAsync` → nối `[Thông tin hỗ trợ]` → build `messagesForAi`:
   - `messagesForAi = new List<ChatHistoryItemDto>(request.History ?? new List<ChatHistoryItemDto>())` rồi `Add(new ChatHistoryItemDto { Role = "user", Content = request.Message })`
   - **Lịch sử chat do CLIENT gửi lên, KHÔNG đọc từ DB.**
6. `CallAiProviderAsync(systemPrompt, messagesForAi, ct)` → `Source = "ai"`, `Timestamp = DateTime.UtcNow`.

**Quy tắc provider fallback (thực tế trong source):**

- `CallAiProviderAsync` nếu `_aiProvider == null || !_aiProvider.IsAvailable` → **ném** `InvalidOperationException("Ollama is not available. Please configure the Ollama provider in appsettings.json.")` (message cứng này là **di sản từ Ollama**, dù provider đang chạy là Gemini — cần sửa khi migrate).
- Không có vòng lặp thử provider kế tiếp. **Fallback duy nhất** là `catch (Exception ex)` ở `GetResponseAsync` → `Source = "fallback"`, `Reply = "Error: " + ex.Message + (InnerException != null ? " | Inner: " + ex.InnerException.Message : "")`.
- `TryHandleProductQueryDirectlyAsync` (hàm private, **được viết nhưng KHÔNG được gọi** từ `GetResponseAsync` — dead code hiện tại) có fallback text thuần khi AI lỗi.

### 1.7 Lưu lịch sử chat vào DB

> **KẾT LUẬN: KHÔNG có luồng lưu lịch sử chatbot vào DB.**

- Bảng `ChatMessages` **có tồn tại** trong `ShopPetDatabaseContext` (`DbSet<ChatMessage> ChatMessages`), model `ChatMessage { MessageId, CustomerId, StaffId?, SenderType, Message, SentAt, IsRead }`, FK tới `Customer` và `Admin`.
- **Nhưng** `ChatbotService` **không bao giờ** gọi `_db.ChatMessages.Add(...)` / `SaveChanges` — `Grep` toàn repo cho `_db.ChatMessage` chỉ trả về `ChatbotService.cs` ở dạng `ChatMessageRequestDto`/`ChatMessageResponseDto` (DTO, không phải entity).
- Lịch sử chat **được client quản lý** và gửi lên qua `request.History` (xem `frontend/src/api/chatbotApi.js`: `sendMessage(message, customerId, history)` → `POST /chatbot/message` body `{ message, customerId, history }`).

### 1.8 Nhánh lỗi & message tiếng Việt / tiếng Anh (nguyên văn)

**Trong `ChatbotController`:**

| Điều kiện | HTTP | Body |
|---|---|---|
| `Message` rỗng | 400 | `{ "Error": "Message cannot be empty." }` |
| `Message.Length > 2000` | 400 | `{ "Error": "Message is too long. Maximum 2000 characters allowed." }` |
| `InvalidOperationException` | 503 | `{ "Error": "Chatbot service is not available.", "Details": "No AI provider is configured. Please contact the administrator." }` |
| `HttpRequestException` | 502 | `{ "Error": "AI service temporarily unavailable.", "Details": "Please try again in a few moments." }` |
| `Exception` khác | 500 | `{ "Error": "An unexpected error occurred.", "Details": "Please try again or contact support." }` |
| `intent` — `Message` rỗng | 400 | `{ "Error": "Message cannot be empty." }` |
| `intent` — lỗi | 500 | `{ "Error": "Failed to detect intent." }` |
| `context` — lỗi | 500 | `{ "Error": "Failed to retrieve context." }` |

**Health endpoint:** trả 200 với `Status = "healthy"`, `Message = "Chatbot service is running."`, `AiProviderConfigured = true`, `DatabaseConnected = true`; khi lỗi → `Status = "degraded"`, `Message = "Chatbot service is running but may have limited functionality."`, `AiProviderConfigured = false`, `DatabaseConnected = false`.

**Message tiếng Việt trong service:**

| Nơi | Message |
|---|---|
| `introduction` | `Mình là trợ lý ảo của PetShop 🐾 Mình có thể giúp bạn tìm sản phẩm, đặt lịch, kiểm tra đơn hàng và tư vấn chăm sóc sức khỏe cho thú cưng...` |
| `product_count` | `🐾 Hiện cửa hàng đang có {count} sản phẩm phù hợp với nhu cầu của bạn.` |
| `price_inquiry` | `💰 Giá sản phẩm:\n- {Name}: {Price:N0}đ (Còn {Stock})` |
| `service_inquiry` | `🛁 Dịch vụ của shop:\n- {Name}: {Price:N0}đ ({Duration} phút)` |
| `order_status` | `📦 Đơn hàng gần đây:\n- #{OrderId}: {Status} - {TotalAmount:N0}đ` |
| Fallback AI lỗi | `Error: {ex.Message}` (+ ` | Inner: {inner.Message}`) |
| Fallback text (dead code) | `� Sản phẩm liên quan ({inStock}/{total} còn hàng):` / `🛁 Dịch vụ liên quan ({n}):` / `🏠 Phòng lưu trú ({n}):` |

**Lỗi provider (English, trong provider service):**

- `Gemini is not configured. Please set ApiKey in the gemini provider config.`
- `Gemini API error: {statusCode} - {errorBody}`
- `Ollama is not configured. Please set BaseUrl in the ollama provider config.`
- `Ollama API error: {statusCode} - {errorBody}`
- `Ollama is not available. Please configure the Ollama provider in appsettings.json.` (ném ra từ `ChatbotService.CallAiProviderAsync`)

---

## 2. Thanh toán PayOS

### 2.1 Cấu hình (`appsettings.json` section `PayOS`)

```json
"PayOS": {
  "ClientId": "<your-payos-client-id>",
  "ApiKey": "<your-payos-api-key>",
  "ChecksumKey": "<your-payos-checksum-key>",
  "BaseUrl": "https://api-merchant.payos.vn/v2"
}
```

> 🔒 **Q19 (08/11/2026):** credential thật **KHÔNG** còn nằm trong file tài liệu này.
> Đặt qua User Secrets (`dotnet user-secrets set "PayOS:ClientId" "…"`) hoặc
> `appsettings.Development.json` (git-ignored) — xem README § Cấu hình & secret.


`Models/PayOSOptions.cs`:

```csharp
public sealed class PayOSOptions
{
    public const string SectionName = "PayOS";
    public string ClientId { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ChecksumKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api-merchant.payos.vn/v2";
    public string? WebhookUrl { get; set; }   // khai báo nhưng KHÔNG dùng ở đâu
}
```

### 2.2 Thuật toán ký — HMAC-SHA256

**Request signature (`GenerateSignature`):**

1. Tạo `SortedDictionary<string,string>` (sắp xếp **theo key tăng dần, phân biệt HOA/thường**):
   - `amount` → `payload.Amount.ToString()` (số nguyên, không format)
   - `cancelUrl` → `payload.CancelUrl`
   - `description` → `payload.Description`
   - `orderCode` → `payload.OrderCode.ToString()`
   - `returnUrl` → `payload.ReturnUrl`
2. Chuỗi dữ liệu ký = nối bằng `&` theo thứ tự sorted:
   ```
   amount={amount}&cancelUrl={cancelUrl}&description={description}&orderCode={orderCode}&returnUrl={returnUrl}
   ```
3. `HMACSHA256(key = UTF8(ChecksumKey))` → `ComputeHash(UTF8(data))` → hex lowercase (`b.ToString("x2")`).

**Webhook verification (`VerifyWebhook`):**
- Data ký = **raw body text** (`payload.GetRawText()`), KHÔNG phải re-serialize.
- Header: **`x-payos-signature`**.
- So sánh `computed` với `signature` bằng `StringComparison.OrdinalIgnoreCase` (không phân biệt HOA/thường).
- Nếu `rawData` hoặc `signature` rỗng → `false`.

### 2.3 Tạo đơn thanh toán

**Endpoint PayOS:** `POST {BaseUrl}/payment-requests`
→ `POST https://api-merchant.payos.vn/v2/payment-requests`

**Headers:**
```
Content-Type: application/json
x-client-id: {ClientId}
x-api-key: {ApiKey}
```

**Body JSON mẫu (chính xác — camelCase, bỏ qua null):**

```json
{
  "amount": 250000,
  "cancelUrl": "https://petshop.vn/spa-invoice?payos=cancel&bookingId=12",
  "description": "Thanh toan Spa #12",
  "items": [],
  "orderCode": 123456789012,
  "returnUrl": "https://petshop.vn/spa-invoice?payos=success&bookingId=12",
  "signature": "<hex lowercase HMAC-SHA256>"
}
```

> `items` luôn là `[]` (mảng rỗng). `JsonSerializerOptions` dùng: `PropertyNamingPolicy = CamelCase`, `DefaultIgnoreCondition = WhenWritingNull` → `signature` chỉ xuất hiện sau khi set.

**Response JSON mẫu:**

```json
{
  "code": "00",
  "desc": "Success",
  "data": {
    "checkoutUrl": "https://app.payos.vn/checkout/...",
    "status": "Pending"
  }
}
```

Điều kiện thành công: `root.Code` ∈ {`"00"`, `"200"`} **và** `root.Data.CheckoutUrl != null`.

**Normalize description** (`NormalizeDescription`) — áp dụng trước khi ký:
1. Thay `đ`→`d`, `Đ`→`D`.
2. `Normalize(NormalizationForm.FormD)` rồi bỏ mọi ký tự `NonSpacingMark`.
3. `Regex.Replace(..., "[^a-zA-Z0-9 #]", "")` — chỉ giữ chữ cái không dấu, số, space, `#`.
4. `Trim()`.
5. Cắt còn **tối đa 25 ký tự** rồi `Trim()` lại.

**Sinh `orderCode`** (`PayOSController.GenerateOrderCode`):
```csharp
var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
var code = (int)((timestamp % 1_000_000_000) * 1000 + (seed % 1000));
return Math.Abs(code);
```
(`seed` = `BookingId` hoặc `OrderId`)

### 2.4 Luồng tạo thanh toán SPA — `POST /api/payos/spa/create`

**Input:** `CreateSpaPayOSRequest(int BookingId, IReadOnlyList<int>? BookingIds, string ReturnUrl, string CancelUrl)`

**Trình tự:**
1. Gom `BookingIds` (nếu có) + `BookingId` (nếu `> 0`) → lọc `id > 0` → `Distinct()`.
2. `bookingIds.Count == 0` → **400** `{ "message": "Thiếu bookingId để tạo thanh toán." }`
3. Load bookings (`.Include(b => b.BookingServices)`). `bookings.Count != bookingIds.Count` → **404** `{ "message": "Một hoặc nhiều booking không tồn tại." }`
4. Kiểm tra cùng khách hàng: có booking khác `CustomerId` → **400** `{ "message": "Không thể gộp thanh toán cho booking của nhiều khách hàng." }`
5. `amount = bookings.SelectMany(b => b.BookingServices).Sum(bs => (bs.UnitPrice ?? 0m) * bs.Quantity)`. `amount <= 0` → **400** `{ "message": "Booking không có tổng tiền." }`
6. `orderCode = GenerateOrderCode(firstBooking.BookingId)`
7. `bookingLabel` = `#{id}` nếu 1 booking, ngược lại `#{id} (+{n-1})`
8. `description = NormalizeDescription($"Thanh toan Spa {bookingLabel}")`
9. **Side effect DB — INSERT `Payments`:**
   ```
   PaymentType    = "spa"
   ReferenceId    = firstBooking.BookingId
   CustomerId     = firstBooking.CustomerId
   Amount         = amount
   PaymentStatus  = "pending"
   PaymentMethod  = "PayOS"
   PayosOrderCode = orderCode
   Note           = "{description}; bookings={bookingIds.join(',')}"
   CreatedAt      = DateTime.Now
   ```
10. `SaveChangesAsync` **trước** khi gọi PayOS.
11. `CreatePaymentLinkAsync(...)` → nếu `null` → **400**:
    ```json
    { "message": "Không tạo được link PayOS.", "error": "{_payOS.GetLastError()}", "response": "{_payOS.GetLastResponse()}" }
    ```
    → **Lưu ý:** dòng `Payment` đã insert vẫn còn lại với status `pending` (không rollback).
12. Trả **200** `PayOSCheckoutResult { CheckoutUrl, OrderCode, Amount, Description }`.

### 2.5 Luồng tạo thanh toán đơn hàng — `POST /api/payos/order/create`

**Input:** `CreateOrderPayOSRequest(int OrderId, string ReturnUrl, string CancelUrl)`

1. Order không tồn tại → **404** `{ "message": "Order không tồn tại." }`
2. `order.TotalAmount <= 0` → **400** `{ "message": "Order không có tổng tiền." }`
3. `orderCode = GenerateOrderCode(order.OrderId)`; `description = NormalizeDescription($"Thanh toan don hang #{order.OrderId}")`
4. **INSERT `Payments`:** `PaymentType = "order"`, `ReferenceId = order.OrderId`, `CustomerId = order.CustomerId ?? 0`, `PaymentStatus = "pending"`, `PaymentMethod = "PayOS"`, `PayosOrderCode = orderCode`, `Note = description`, `CreatedAt = DateTime.Now`
5. `CreatePaymentLinkAsync` → lỗi → **400** (như trên). Trả **200** `PayOSCheckoutResult`.

### 2.6 Webhook — `POST /api/payos/webhook`

**Input:** raw JSON body + header `x-payos-signature`.

**Body JSON mẫu:**

```json
{
  "code": "00",
  "data": {
    "orderCode": 123456789012,
    "status": "PAID"
  }
}
```

> Model `PayOSWebhookPayload(string? Code, PayOSWebhookData? Data)`, `PayOSWebhookData(int OrderCode, string? Status)`.
> **Lưu ý:** body được bind vào `JsonElement` → `payload.GetRawText()` dùng làm data ký (giữ đúng byte đã nhận). Chỉ `data.orderCode` và `data.status` được đọc; `code` chỉ dùng làm fallback status.

**Trình tự xử lý:**
1. `VerifyWebhook(raw, signature)` sai → **401** `{ "message": "Invalid signature" }` (in log `[PayOS] Invalid signature.`)
2. `HandleWebhookAsync(raw)`:
   - `rawData` rỗng → `(false, "Empty payload")`
   - deserialize lỗi → `(false, "Invalid payload")`
   - `payload.Data == null` → `(false, "Missing data")`
   - `status = data.Status ?? payload.Code`
   - `status` không phải `PAID` và không phải `"00"` (case-insensitive) → **`(true, "No action for status")`** → controller trả **200** (coi như đã xử lý).
   - `status` là `PAID`/`"00"`:
     - Tìm `_db.Payments.FirstOrDefault(p => p.PayosOrderCode == data.OrderCode)` → không có → **`(false, "Payment not found")`** → controller trả **400**.
     - Nếu `payment.PaymentStatus == "pending"` → set `PaymentStatus = "paid"`, `PaidAt = DateTime.Now`, `SaveChangesAsync`. (idempotent: nếu đã `paid` thì không ghi lại)
     - Rồi rẽ nhánh theo `payment.PaymentType`:
       - **`"spa"`** → `UpdateSpaBookingStatusAsync(payment.ReferenceId, "Đã thanh toán")`:
         - `booking.Status = "Đã thanh toán"`, `booking.UpdatedAt = DateTime.Now`
         - **Nếu `wasPending`** → `SendSpaInvoiceEmailAsync(payment.ReferenceId)` (chỉ gửi 1 lần)
       - **`"order"`** → `UpdateOrderStatusAsync(payment.ReferenceId)`:
         - `order.PaymentStatus = "Đã thanh toán"`
         - `order.Status = "Chờ giao hàng"` (trừ khi đã là `"Hoàn thành"`)
         - `order.PaidAt = DateTime.Now`
     - Trả **`(true, "OK")`** → controller **200** `{ "message": "OK" }`
3. Controller trả **400** `{ "message": "{result.Message}" }` nếu `result.Success == false`.

**Log console (nguyên văn):**
```
[PayOS] Webhook received. Signature: {signature ?? "(null)"}
[PayOS] Invalid signature.
[PayOS] Webhook status: {status ?? "(null)"}, orderCode: {data.OrderCode}
[PayOS] Payment not found in DB.
[PayOS] Payment found. Type: {PaymentType}, Status: {PaymentStatus}, RefId: {ReferenceId}
[PayOS] Payment updated to paid.
[PayOS] Spa booking status updated.
[PayOS] Spa invoice email sent.
[PayOS] Order status updated.
[PayOS] HandleWebhook failed: {message}
[PayOS] Webhook handled: {message}
```

### 2.7 Fallback endpoint PayOS

Trong `CreatePaymentLinkAsync`, khi `HttpRequestException` xảy ra:
- Nếu `_options.BaseUrl` **chứa** `api.payos.vn` (case-insensitive) → trả `null` luôn (không retry).
- Ngược lại → **retry** với fallback base `https://api.payos.vn/v2` (POST `payment-requests`, cùng body + cùng headers).

> **Ghi chú:** `_lastError` / `_lastResponse` là **field instance** của service scoped → có thể bị ghi đè giữa các request đồng thời. Nên chuyển sang trả về DTO lỗi.

### 2.8 Email hoá đơn trong luồng PayOS

`SendSpaInvoiceEmailAsync(bookingId)`:
1. Load booking (`AsNoTracking`, `Include(b => b.Customer)`, `Include(b => b.BookingServices).ThenInclude(bs => bs.Service)`).
2. `booking?.Customer?.Email == null` → return im lặng (không log, không lỗi).
3. Items: `ItemName = bs.Service?.Name ?? "Dich vu spa"`, `Quantity = bs.Quantity`, `UnitPrice = bs.UnitPrice ?? 0m`.
4. `total = items.Sum(UnitPrice * Quantity)`; nếu `total <= 0` → tính lại từ `booking.BookingServices`.
5. `SendSpaInvoiceEmailAsync(booking.Customer.Email, booking.Customer.Name, $"SPA-{booking.BookingId}", items, total, ct)`.

> **Lưu ý:** web hiện tại **chỉ gửi hoá đơn SPA**. `SendOrderInvoiceEmailAsync` tồn tại trong `EmailService` nhưng **không được gọi** từ luồng PayOS order.

### 2.9 Frontend contract PayOS

`frontend/src/pages/SpaCartPage.jsx` → `POST /api/payos/spa/create`:
```js
const returnUrl = `${origin}/spa-invoice?payos=success&bookingId=${primaryBookingId}&bookingIds=${bookingIdsQuery}`
const cancelUrl = `${origin}/spa-invoice?payos=cancel&bookingId=${primaryBookingId}&bookingIds=${bookingIdsQuery}`
// body: { bookingId, bookingIds, returnUrl, cancelUrl }
```

`frontend/src/pages/SpaInvoicePage.jsx`: đọc query `payos` (`success` | `cancel`) và **tự gọi** `PATCH /api/spa-booking/{bookingId}/status` với `status = "Đã thanh toán"` hoặc `"Đã hủy"` → **client-side status update song song với webhook server-side**.

---

## 3. Auth / Google OAuth

### 3.1 Đăng ký — `POST /api/auth/register`

**Input:** `RegisterRequestDto { Name, Email, Password, Phone?, Address? }`

**Trình tự (`AuthService.RegisterAsync`):**
1. `email = NormalizeEmail(request.Email)` = `Trim().ToLowerInvariant()`.
2. `email` rỗng → **ArgumentException** `"Email không được để trống."`
3. `Password` rỗng → **ArgumentException** `"Mật khẩu không được để trống."`
4. Email đã tồn tại (`GetCustomerByEmailAsync`) → **InvalidOperationException** `"Email đã tồn tại."`
5. Tạo `Customer`:
   ```
   Name            = request.Name?.Trim() (null nếu rỗng)
   Email           = email
   Password        = BCrypt.Net.BCrypt.HashPassword(request.Password)
   Phone           = request.Phone?.Trim() (null nếu rỗng)
   AddressCustomer = request.Address?.Trim() (null nếu rỗng)
   Role            = "user"
   Status          = "inactive"
   ```
6. `CreateCustomerAsync` → insert.
7. Sinh OTP: `Random().Next(100000, 999999).ToString()` (6 chữ số, **không dùng `RandomNumberGenerator`** — yếu).
8. `created.OtpCode = otp`, `created.OtpExpiry = DateTime.UtcNow.AddMinutes(5)` → `UpdateCustomerAsync`.
9. `SendOtpEmailAsync(created.Email, otp)` — **side effect gửi email, nếu SMTP lỗi → ném `InvalidOperationException` ra ngoài** (customer đã tạo → không rollback).
10. Trả `AuthResponseDto { UserId, Name, Email, Role, Status }`.

**Mapping lỗi controller:**
- `InvalidOperationException` → **409 Conflict** `{ "message": ex.Message }`
- `ArgumentException` → **400 BadRequest** `{ "message": ex.Message }`

### 3.2 Đăng nhập — `POST /api/auth/login`

**Input:** `LoginRequestDto { Email, Password }`

**Thứ tự tra cứu (fallback chain):**
1. `identity = request.Email.Trim()`; `normalizedEmail = NormalizeEmail(request.Email)`.
2. Rỗng identity hoặc password → **UnauthorizedAccessException** `"Email hoặc mật khẩu không hợp lệ."`
3. **`Staff`** (`GetStaffByEmailAsync`): nếu tồn tại và password đúng → role = `"admin"` nếu `Position` là `"admin"` hoặc `"quản lý"` (case-insensitive), ngược lại `"staff"`; `Status = "active"`.
4. **`Doctor`** (`GetDoctorByEmailAsync`): nếu tồn tại và password đúng → role `"doctor"`, `Status = "active"`.
5. **`Admin`** (`GetAdminByEmailOrUsernameAsync` — tra theo `identity`): nếu tồn tại và password đúng → role `"admin"`, `Status = "active"`.
6. **`Customer`** (`GetCustomerByEmailAsync`):
   - không tồn tại hoặc `Password` rỗng → **UnauthorizedAccessException** `"Email hoặc mật khẩu không đúng."`
   - sai mật khẩu → **UnauthorizedAccessException** `"Email hoặc mật khẩu không đúng."`
   - `Status != "active"` → **UnauthorizedAccessException** `"Tài khoản chưa xác thực email. Vui lòng nhập OTP đã gửi qua email."`
   - → `MapToAuthResponse(customer)`

**Kiểm tra mật khẩu (`IsPasswordValid`):** nếu stored bắt đầu bằng `"$2"` → `BCrypt.Net.BCrypt.Verify(password, stored.Trim())`; ngược lại so sánh **plaintext** bằng `StringComparison.Ordinal` (hỗ trợ dữ liệu cũ chưa hash).

**Mapping lỗi controller:** `UnauthorizedAccessException` → **401** `{ "message": ex.Message }`.

### 3.3 Google OAuth — `POST /api/auth/google-login`

**Input:** `GoogleLoginRequestDto { IdToken?, AccessToken? }`

> **Không có file `AuthService.Google.cs`** — toàn bộ logic Google nằm trong `Services/AuthService.cs`.

**Trình tự (`LoginWithGoogleAsync`):**
1. `hasIdToken = !string.IsNullOrWhiteSpace(request.IdToken)`, `hasAccessToken = !string.IsNullOrWhiteSpace(request.AccessToken)`.
2. Cả hai rỗng → **ArgumentException** `"Cần cung cấp IdToken hoặc AccessToken."`
3. **Ưu tiên IdToken** nếu có:
   - `GoogleJsonWebSignature.ValidateAsync(idToken, new ValidationSettings { Audience = audiences })`
   - Lấy `Email`, `Subject` (`sub`), `Name`, `EmailVerified`
   - **Audiences** = hợp nhất `GoogleAuth:ClientId` (nếu không phải placeholder) + `GoogleAuth:ClientIds` (CSV, tách bởi `,`, trim, bỏ placeholder, loại trùng case-insensitive).
   - Không có audience → **InvalidOperationException** `"Google Login chưa cấu hình đúng. Hãy set GoogleAuth:ClientId hoặc GoogleAuth:ClientIds bằng Client ID thật từ Google Cloud Console."`
   - `InvalidJwtException` → **UnauthorizedAccessException** `$"Google IdToken không hợp lệ: {ex.Message}"`
4. **Nếu không có IdToken → dùng AccessToken:**
   - `GET https://www.googleapis.com/oauth2/v3/userinfo` với header `Authorization: Bearer {accessToken}`
   - Response không thành công → **UnauthorizedAccessException** `"Google AccessToken không hợp lệ hoặc đã hết hạn."`
   - Đọc `email`, `sub`, `name`, `email_verified`
5. `email` rỗng → **UnauthorizedAccessException** `"Không lấy được email từ Google."`
6. `emailVerified == false` → **UnauthorizedAccessException** `"Email Google chưa được xác minh."`
7. `email = NormalizeEmail(email)`
8. Tra `Customer` theo email:
   - **Chưa có** → tạo mới:
     ```
     Name     = name
     Email    = email
     GoogleId = googleSubject
     Password = null              ← KHÔNG có mật khẩu
     Role     = "user"             ← role mặc định
     Status   = "active"          ← active ngay, không cần OTP
     ```
     → `CreateCustomerAsync`; đánh dấu `isFirstGoogleLogin = true`
   - **Đã có** → `customer.GoogleId ??= googleSubject`; `customer.Name` giữ nguyên nếu đã có, ngược lại lấy `name` từ Google; `customer.Status = "active"` → `UpdateCustomerAsync`
9. Nếu `isFirstGoogleLogin` → `SendGoogleFirstLoginEmailAsync(email, name)`
10. Trả `AuthResponseDto`.

**Mapping lỗi controller (`google-login`):**
- `InvalidOperationException` → **500** `{ "message": ex.Message }`
- `ArgumentException` → **400** `{ "message": ex.Message }`
- `UnauthorizedAccessException` → **401** `{ "message": ex.Message }`

**`GET /api/auth/google-client-id`:** trả `{ "clientId": "{GoogleAuth:ClientId}" }`; rỗng → **404** `{ "message": "Google Client ID chưa được cấu hình." }`

**Frontend (`LoginPage.jsx`):** dùng **Google Identity Services** (`https://accounts.google.com/gsi/client`), `google.accounts.id.initialize({ client_id, callback })` → `response.credential` là **ID Token** → `googleLoginApi({ idToken: token })`. Lấy clientId từ `import.meta.env.VITE_GOOGLE_CLIENT_ID` trước, nếu không thì `GET /api/auth/google-client-id`.

### 3.4 OTP email — `POST /api/auth/send-email-otp`

**Input:** `SendEmailOtpRequestDto { Email }`

1. `email` rỗng → **ArgumentException** `"Email không được để trống."`
2. `EmailExistsInAnyRoleAsync(email)` false → **KeyNotFoundException** `"Email chưa đăng ký tài khoản."`
3. `GetCustomerByEmailAsync(email)` null → **KeyNotFoundException** `"Email chưa có tài khoản khách hàng để nhận OTP."`
4. Sinh OTP 6 chữ số; `customer.OtpCode = otp`; `customer.OtpExpiry = DateTime.UtcNow.AddMinutes(5)` → update.
5. `SendOtpEmailAsync(email, otp)`.
6. Trả **200** `{ "Message": "Đã gửi OTP đến email." }` (field `Message` viết hoa — `SimpleMessageDto(string Message)`).

**Mapping lỗi controller:** `KeyNotFoundException` → **404**; `InvalidOperationException` → **500**; `ArgumentException` → **400**; `Exception` khác → **500** `{ "message": $"Gửi OTP thất bại: {ex.Message}" }`.

### 3.5 Xác thực OTP — `POST /api/auth/verify-email-otp`

**Input:** `VerifyEmailOtpRequestDto { Email, OtpCode }`

1. `email` hoặc `otp` rỗng → **ArgumentException** `"Email và mã OTP không được để trống."`
2. Không tìm thấy customer → **KeyNotFoundException** `"Không tìm thấy tài khoản với email này."`
3. `ValidateOtp(customer, otp)`:
   - `OtpCode` null hoặc `OtpExpiry` null → **UnauthorizedAccessException** `"Mã OTP không hợp lệ hoặc chưa được gửi."`
   - `OtpExpiry < DateTime.UtcNow` → **UnauthorizedAccessException** `"Mã OTP đã hết hạn."`
   - `OtpCode != otp` (Ordinal) → **UnauthorizedAccessException** `"Mã OTP không đúng."`
4. `customer.Status = "active"`; `customer.ResetToken = Guid.NewGuid().ToString("N")`; `customer.ResetTokenExpiry = DateTime.UtcNow.AddMinutes(5)` → update.
5. **Trả `SimpleMessageDto` chứa `ResetToken`** → client dùng token này để reset password.

**Mapping lỗi controller:** `KeyNotFoundException` → **404**; `UnauthorizedAccessException` → **401**; `ArgumentException` → **400**.

### 3.6 Reset password — `POST /api/auth/reset-password`

**Input:** `ResetPasswordRequestDto { Email, ResetToken?, OtpCode?, NewPassword, ConfirmPassword }`

1. `email` rỗng → **ArgumentException** `"Email không được để trống."`
2. `newPassword` rỗng → **ArgumentException** `"Mật khẩu mới không được để trống."`
3. `newPassword != confirmPassword` (Ordinal) → **ArgumentException** `"Mật khẩu xác nhận không khớp."`
4. Không tìm thấy customer → **KeyNotFoundException** `"Không tìm thấy tài khoản với email này."`
5. Xác thực:
   - Nếu có `ResetToken` → `ValidateResetToken` → `usedResetToken = true`
     - `ResetToken` null hoặc `ResetTokenExpiry` null → **UnauthorizedAccessException** `"Reset token không hợp lệ hoặc chưa được cấp."`
     - Hết hạn → **UnauthorizedAccessException** `"Reset token đã hết hạn."`
     - Sai → **UnauthorizedAccessException** `"Reset token không đúng."`
   - Else nếu có `OtpCode` → `ValidateOtp` (như trên)
   - Else → **ArgumentException** `"Cần cung cấp reset token hoặc OTP."`
6. `customer.Password = BCrypt.Net.BCrypt.HashPassword(newPassword)`
7. `ConsumeOtp(customer)` → `OtpCode = null; OtpExpiry = null`
8. Nếu `usedResetToken` → `ConsumeResetToken(customer)` → `ResetToken = null; ResetTokenExpiry = null`
9. `UpdateCustomerAsync` → trả **200** `{ "Message": "Đặt lại mật khẩu thành công." }`

**Mapping lỗi controller:** `KeyNotFoundException` → **404**; `UnauthorizedAccessException` → **401**; `ArgumentException` → **400**.

### 3.7 Danh sách message tiếng Việt (auth) — nguyên văn

```
Email không được để trống.
Mật khẩu không được để trống.
Email đã tồn tại.
Email hoặc mật khẩu không hợp lệ.
Email hoặc mật khẩu không đúng.
Tài khoản chưa xác thực email. Vui lòng nhập OTP đã gửi qua email.
Cần cung cấp IdToken hoặc AccessToken.
Google Login chưa cấu hình đúng. Hãy set GoogleAuth:ClientId hoặc GoogleAuth:ClientIds bằng Client ID thật từ Google Cloud Console.
Google IdToken không hợp lệ: {ex.Message}
Google AccessToken không hợp lệ hoặc đã hết hạn.
Không lấy được email từ Google.
Email Google chưa được xác minh.
Email chưa đăng ký tài khoản.
Email chưa có tài khoản khách hàng để nhận OTP.
Email và mã OTP không được để trống.
Không tìm thấy tài khoản với email này.
Mã OTP không hợp lệ hoặc chưa được gửi.
Mã OTP đã hết hạn.
Mã OTP không đúng.
Mật khẩu mới không được để trống.
Mật khẩu xác nhận không khớp.
Cần cung cấp reset token hoặc OTP.
Reset token không hợp lệ hoặc chưa được cấp.
Reset token đã hết hạn.
Reset token không đúng.
Đã gửi OTP đến email.
Đặt lại mật khẩu thành công.
Google Client ID chưa được cấu hình.
Gửi OTP thất bại: {ex.Message}
```

---

## 4. Email Service (MailKit + Handlebars)

### 4.1 Cấu hình SMTP (`appsettings.json` section `Mail`)

```json
"Mail": {
  "SmtpHost": "smtp.gmail.com",
  "SmtpPort": "587",
  "SecureSocketOptions": "StartTls",
  "Username": "your@gmail.com",
  "Password": "<gmail-app-password>",
  "From": "your@gmail.com"
}
```

> 🔒 **Q19 (08/11/2026):** credential thật **KHÔNG** còn nằm trong file tài liệu này.
> Đặt qua User Secrets (`dotnet user-secrets set "Mail:Password" "…"`) hoặc
> `appsettings.Development.json` (git-ignored) — xem README § Cấu hình & secret.

**`GetSmtpSettings()` — validate:**
- Thiếu bất kỳ `SmtpHost`/`SmtpPort`/`Username`/`Password`/`From` → **InvalidOperationException** `"Chua cau hinh Mail trong appsettings (SmtpHost, SmtpPort, Username, Password, From)."`
- Placeholder (chứa `YOUR_`, `YOUR-`, `example`, `placeholder`, case-insensitive) → **InvalidOperationException** `"Verify Email chua cau hinh mail that. Hay thay Mail:Username, Mail:Password (App Password) va Mail:From trong appsettings."`
- `SmtpPort` không parse được int → **InvalidOperationException** `"Mail:SmtpPort khong hop le."`
- `From` fallback = `Mail:From ?? Mail:Username`.
- `SecureSocketOptions` parse: `auto`→`Auto`, `none`→`None`, `ssl`/`sslonconnect`→`SslOnConnect`, `starttls`→`StartTls`, `starttlswhenavailable`→`StartTlsWhenAvailable`, mặc định/không rõ → `StartTls`.

**Gửi mail (`SendAsync`):** `SmtpClient.ConnectAsync(host, port, secureSocketOptions, ct)` → `AuthenticateAsync(username, password, ct)` → `SendAsync` → `DisconnectAsync(true, ct)`.
- `OperationCanceledException` → rethrow nguyên vẹn.
- Exception khác → **InvalidOperationException** `"Gui email that bai. Kiem tra cau hinh Mail (Username/App Password/From) va ket noi SMTP. Chi tiet: {ex.Message}"` (InnerException = ex).

**Tạo message (`CreateBaseMessage`):** `toEmail` rỗng → **ArgumentException** `"Email nguoi nhan khong duoc de trong."`; `From` = `MailboxAddress.Parse(from)`, `To` = `MailboxAddress.Parse(toEmail.Trim())`.

### 4.2 Render Handlebars

```csharp
private HandlebarsTemplate<object, object> LoadHandlebarsTemplate(string relativePath)
{
    var fullPath = Path.Combine(_environment.ContentRootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
    if (!File.Exists(fullPath))
    {
        throw new FileNotFoundException($"Khong tim thay email template: {relativePath}");
    }

    var templateContent = File.ReadAllText(fullPath);
    return Handlebars.Compile(templateContent);
}
```

**Template path:** `EmailTemplates/OrderInvoice.hbs`, `EmailTemplates/SpaInvoice.hbs` (resolve từ `ContentRootPath`).
**Thiếu file** → `FileNotFoundException` với message `Khong tim thay email template: {relativePath}`.
**Escape:** `ItemName` được `WebUtility.HtmlEncode` trước khi truyền vào template (`Escape(string)`).

### 4.3 Biến truyền vào template

**`SendOrderInvoiceEmailAsync`** — template `EmailTemplates/OrderInvoice.hbs`:

| Biến | Nguồn |
|---|---|
| `CustomerName` | `customerName?.Trim()` hoặc `"Quy khach"` nếu rỗng |
| `OrderCode` | tham số `orderCode` |
| `Items[].ItemName` | `Escape(i.ItemName)` |
| `Items[].Quantity` | `i.Quantity` (int) |
| `Items[].UnitPrice` | `i.UnitPrice.ToString("N0")` |
| `Items[].LineTotal` | `(i.Quantity * i.UnitPrice).ToString("N0")` |
| `TotalAmount` | `totalAmount.ToString("N0")` |

**`SendSpaInvoiceEmailAsync`** — template `EmailTemplates/SpaInvoice.hbs`:

| Biến | Nguồn |
|---|---|
| `CustomerName` | `customerName?.Trim()` hoặc `"Quy khach"` |
| `BookingCode` | tham số `bookingCode` (VD: `SPA-12`) |
| `Items[].ItemName` | `Escape(i.ItemName)` |
| `Items[].Quantity` | `i.Quantity` |
| `Items[].UnitPrice` | `i.UnitPrice.ToString("N0")` |
| `Items[].LineTotal` | `(i.Quantity * i.UnitPrice).ToString("N0")` |
| `TotalAmount` | `totalAmount.ToString("N0")` |

### 4.4 Email OTP (plain text) — NGUYÊN VĂN

**Subject:** `PetShop - Xac thuc email dang ky`

**Body:**
```
Chao ban,

Cam on ban da dang ky tai khoan tai PetShop.
Ma OTP xac thuc email cua ban la: {otp}
Ma co hieu luc trong 10 phut.

Tran trong,
PetShop
```

> **Khớp với code:** `AuthService` set `OtpExpiry = DateTime.UtcNow.AddMinutes(5)` (5 phút) nhưng email nói **10 phút**. Sai lệch cần sửa khi migrate.

**Subject:** `PetShop - Dang nhap Google thanh cong`

**Body:**
```
Xin chao {displayName},

Ban vua dang nhap bang Google lan dau vao PetShop.
Neu day khong phai ban, vui long lien he ho tro ngay.

Tran trong,
PetShop
```
(`displayName` = `name?.Trim()` hoặc `"ban"` nếu rỗng)

### 4.5 Nội dung template `.hbs` — NGUYÊN VĂN

**`EmailTemplates/OrderInvoice.hbs`:**
```html
<div style='font-family:Arial,sans-serif;max-width:700px;margin:auto'>
  <h2>PetShop - Hoa don don hang</h2>
  <p>Xin chao <strong>{{CustomerName}}</strong>,</p>
  <p>Cam on ban da mua hang. Duoi day la thong tin don hang <strong>{{OrderCode}}</strong>.</p>
  <table style='width:100%;border-collapse:collapse'>
    <thead>
      <tr style='background:#f5f5f5'>
        <th style='padding:8px;border:1px solid #ddd;text-align:left'>San pham/Dich vu</th>
        <th style='padding:8px;border:1px solid #ddd;text-align:center'>So luong</th>
        <th style='padding:8px;border:1px solid #ddd;text-align:right'>Don gia</th>
        <th style='padding:8px;border:1px solid #ddd;text-align:right'>Thanh tien</th>
      </tr>
    </thead>
    <tbody>
      {{#each Items}}
      <tr>
        <td style='padding:8px;border:1px solid #ddd'>{{ItemName}}</td>
        <td style='padding:8px;border:1px solid #ddd;text-align:center'>{{Quantity}}</td>
        <td style='padding:8px;border:1px solid #ddd;text-align:right'>{{UnitPrice}} VND</td>
        <td style='padding:8px;border:1px solid #ddd;text-align:right'>{{LineTotal}} VND</td>
      </tr>
      {{/each}}
    </tbody>
  </table>
  <p style='text-align:right;margin-top:12px'><strong>Tong cong: {{TotalAmount}} VND</strong></p>
  <p style='color:#666'>Neu can ho tro, vui long lien he bo phan CSKH cua PetShop.</p>
</div>
```

**`EmailTemplates/SpaInvoice.hbs`:**
```html
<!DOCTYPE html>
<html lang="vi">
<head>
  <meta charset="UTF-8" />
  <title>Hoa don spa</title>
  <style>
    body {
      font-family: Arial, sans-serif;
      background: #f7f7f7;
      padding: 24px;
    }
    .card {
      max-width: 640px;
      margin: 0 auto;
      background: #ffffff;
      border-radius: 12px;
      padding: 24px;
      box-shadow: 0 8px 24px rgba(0, 0, 0, 0.08);
    }
    h1 {
      margin: 0 0 8px;
      color: #2b2b2b;
    }
    .subtitle {
      color: #666;
      margin-bottom: 24px;
    }
    table {
      width: 100%;
      border-collapse: collapse;
    }
    th, td {
      padding: 10px 8px;
      border-bottom: 1px solid #eee;
      text-align: left;
      font-size: 14px;
    }
    th {
      background: #fafafa;
    }
    .total {
      text-align: right;
      font-size: 16px;
      font-weight: 700;
      margin-top: 16px;
    }
    .footer {
      margin-top: 24px;
      font-size: 13px;
      color: #888;
    }
  </style>
</head>
<body>
  <div class="card">
    <h1>Hoa don dich vu Spa</h1>
    <div class="subtitle">Xin chao {{CustomerName}}, cam on ban da su dung dich vu.</div>

    <p><strong>Ma booking:</strong> {{BookingCode}}</p>

    <table>
      <thead>
        <tr>
          <th>Dich vu</th>
          <th>So luong</th>
          <th>Don gia</th>
          <th>Thanh tien</th>
        </tr>
      </thead>
      <tbody>
        {{#each Items}}
        <tr>
          <td>{{ItemName}}</td>
          <td>{{Quantity}}</td>
          <td>{{UnitPrice}} đ</td>
          <td>{{LineTotal}} đ</td>
        </tr>
        {{/each}}
      </tbody>
    </table>

    <div class="total">Tong cong: {{TotalAmount}} đ</div>

    <div class="footer">
      Neu can ho tro, vui long lien he PetShop. Chuc ban va thu cung luon khoe manh.
    </div>
  </div>
</body>
</html>
```

### 4.6 Subject email hoá đơn

- Order: `PetShop - Hoa don don hang {orderCode}`
- Spa: `PetShop - Hoa don spa {bookingCode}`

---

## 5. Upload ảnh thú cưng

**Endpoint:** `POST /api/pets/upload-image` (multipart/form-data)
**Attribute:** `[ApiExplorerSettings(IgnoreApi = true)]` → **ẩn khỏi Swagger**.
**Input field:** `image` (`[FromForm] IFormFile image`).

**Quy tắc kiểm tra (theo thứ tự):**
1. `image == null || image.Length == 0` → **400** `{ "message": "Vui lòng chọn ảnh hợp lệ." }`
2. `image.Length > 5 * 1024 * 1024` (5 MB) → **400** `{ "message": "Ảnh vượt quá 5MB." }`
3. `contentType = image.ContentType?.ToLowerInvariant()`; **không** `StartsWith("image/")` → **400** `{ "message": "Chỉ cho phép tệp ảnh." }`

> **Ghi chú bảo mật:** kiểm tra dựa hoàn toàn vào `Content-Type` do client khai báo — không kiểm magic bytes / extension thực. Cần bổ sung khi migrate.

**Sinh tên file & lưu trữ:**
```csharp
var webRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
var uploadDir = Path.Combine(webRoot, "uploads", "pets");
Directory.CreateDirectory(uploadDir);

var ext = Path.GetExtension(image.FileName);
if (string.IsNullOrWhiteSpace(ext)) { ext = ".jpg"; }
var safeExt = Regex.Replace(ext, @"[^a-zA-Z0-9\.]", string.Empty);
var fileName = $"pet_{Guid.NewGuid():N}{safeExt}";
var physicalPath = Path.Combine(uploadDir, fileName);
```
- Đường dẫn vật lý: `{CWD}/wwwroot/uploads/pets/pet_{guidN}.{ext}`
- Tên: `pet_` + `Guid.NewGuid():N` (32 ký tự hex không gạch) + extension đã lọc.
- Ghi file: `new FileStream(physicalPath, FileMode.Create)` → `image.CopyToAsync(stream, ct)`.

**Phục vụ file:** **không có endpoint riêng** → phục vụ tĩnh qua ASP.NET Core Static Files middleware (`wwwroot`). URL trả về:

```json
{ "imageUrl": "/uploads/pets/pet_{guidN}.jpg" }
```

**Side effect:** ghi file vào `wwwroot/uploads/pets/` (không ghi DB — `Pet.ImagePath` được set riêng khi tạo/sửa pet qua `UpsertPetRequestDto.ImagePath`).

**Frontend:** `frontend/src/api/petsApi.js` → `uploadPetImage(file)` → `FormData.append("image", file)` → `POST /api/pets/upload-image`.

**Phụ lục — quyền truy cập CRUD pet** (`ValidateSelfAccess`):
- Header `X-User-Role` phải là `user` hoặc `staff` → nếu không → **403** `{ "message": "Chỉ user/staff được quản lý pet của chính mình." }`
- Header `X-User-Id` phải parse được int > 0 → nếu không → **401** `{ "message": "Thiếu thông tin người dùng." }`
- `callerUserId != customerId` → **403** `{ "message": "Bạn chỉ được phép CRUD pet của chính mình." }`

*(Cơ chế trên là header tự khai — không phải JWT. Cần cân nhắc thay bằng auth middleware khi migrate.)*

---

## 6. Notification

**Nguồn dữ liệu:** bảng **`ShiftRequest`** — **KHÔNG dùng** bảng `Notifications` (comment trong controller: *"API tương thích frontend — dữ liệu lấy từ bảng ShiftRequest (không dùng bảng Notifications)."*).

### 6.1 `GET /api/notifications/{staffId}`

- Query filter: `r.ToStaffId == staffId && r.Status == "Pending"`.
- `unreadOnly == true` → thêm `r.ToNotified == false || r.ToNotified == null`.
- `.OrderByDescending(r => r.CreatedAt).Take(50)`
- **Output mapping:**
  ```
  NotificationId   = r.RequestId
  StaffId          = staffId
  Title            = r.Type == "Swap" ? "Yêu cầu đổi ca" : "Yêu cầu làm thay"
  Message          = r.Type == "Swap"
                      ? $"Yêu cầu đổi ca (Request #{r.RequestId})"
                      : $"Yêu cầu nhờ làm thay (Request #{r.RequestId})"
  IsRead           = r.ToNotified == true
  CreatedAt        = r.CreatedAt ?? DateTime.MinValue
  RelatedRequestId = r.RequestId
  IsHandled        = false
  ```

### 6.2 `GET /api/notifications/{staffId}/unread-count`

- Count: `ToStaffId == staffId && Status == "Pending" && (ToNotified == false || ToNotified == null)`
- Output: `{ "Count": N }` (`UnreadCountDto`)

### 6.3 `PUT /api/notifications/{requestId}/read?staffId=...`

- `ExecuteUpdateAsync` set `ToNotified = true` với điều kiện `RequestId == requestId && ToStaffId == staffId && Status == "Pending"`.
- `updated == 0` → **404** `{ "message": "Không tìm thấy yêu cầu hoặc đã xử lý." }`
- Ngược lại → **200** `{ "message": "Đã đánh dấu đã đọc." }`

### 6.4 `PUT /api/notifications/{staffId}/read-all`

- `ExecuteUpdateAsync` set `ToNotified = true` cho tất cả `Pending` chưa đọc của staff.
- → **200** `{ "message": "Đã đánh dấu {count} thông báo là đã đọc." }`

### 6.5 `DELETE /api/notifications/{requestId}`

- **Không xóa thật** → **200** `{ "message": "Không hỗ trợ xóa; yêu cầu được quản lý trong đổi ca." }`

> **Không có push/websocket/email notification** — cơ chế hoàn toàn pull-based theo `ShiftRequest.Status == "Pending"` + cờ `ToNotified`.

---

## 7. Đổi ca (Shift Exchange)

**Route:** `api/shift-exchange`. Bảng dùng: `ShiftRequests`, `WorkSchedules`. Kiểm tra chấm công: `IAttendanceService.HasAttendanceForDateAsync(staffId, date, ct)`.

### 7.1 Kiểm tra mạng công ty — `GET /api/shift-exchange/verify-network`

- Allowed prefixes: `appsettings.json` → `Attendance:AllowedIpPrefixes` = `["171.254.189.230", "113.22.236.208"]` (fallback code: `new[] { "192.168.1." }`).
- Thứ tự resolve IP: `X-Client-IP` → `X-Real-IP` → `HttpContext.Items["RealClientIp"]` → `X-Forwarded-For` (phần đầu sau dấu phẩy) → `Connection.RemoteIpAddress` → `"unknown"`.
- `NormalizeClientIp`: bỏ `%zone`; loopback → `"127.0.0.1"`; IPv4-mapped-IPv6 → IPv4.
- `isCompanyNetwork = allowedPrefixes.Any(prefix => ip.StartsWith(prefix, OrdinalIgnoreCase))`
- Output (`VerifyNetworkDto`): `Ip`, `IsCompanyNetwork`, `CompanyPrefix` (join `", "`), `XClientIp`, `RealClientIp`, `XForwardedFor`, `RemoteIp`, `ResolvedFrom`, `XRealIp`.

> **Ghi chú bảo mật:** IP lấy từ **header do client gửi** (`X-Client-IP` ưu tiên cao nhất) → client có thể giả mạo. Khi migrate nên chỉ tin `RemoteIpAddress` hoặc dùng reverse proxy đã được cấu hình.

### 7.2 Lấy danh sách ca

- `GET /api/shift-exchange/staff/{staffId}/my-shifts` — ca của tôi, `WorkDate >= hôm nay`, `.Take(60)`, sort `WorkDate` → `StartTime`.
- `GET /api/shift-exchange/all-shifts` — tất cả ca, `WorkDate >= hôm nay`, `.Take(300)`.
- Cả hai: `WorkScheduleDedup.ShiftOptions(shifts)` để dedupe.
- `GET /api/shift-exchange/staff/options?excludeStaffId=` — danh sách staff khác, sort theo tên.

### 7.3 Nhờ làm thay — `POST /api/shift-exchange/pass`

**Input:** `CreatePassShiftRequestDto { StaffId, FromDate, FromScheduleId, ToStaffId, Reason? }`

**Validation (theo thứ tự):**
1. `FromDate < hôm nay` → **400** `"Không thể gửi yêu cầu cho ngày trong quá khứ."`
2. `FromDate == hôm nay` → **400** `"Đã qua 0h sáng ngày hôm nay, không thể gửi yêu cầu đổi ca cùng ngày."`
3. `HasAttendanceForDateAsync(StaffId, FromDate)` → **400** `"Bạn đã chấm công ngày này rồi, không thể yêu cầu đổi ca."`
4. `WorkSchedules` tìm `ScheduleId == FromScheduleId && StaffId == StaffId` → null → **400** `"Không tìm thấy ca của bạn để nhờ làm thay."`
5. `mySchedule.WorkDate != FromDate` → **400** `"Ngày ca không khớp với ca đã chọn."`
6. Đã có `ShiftRequest` cùng `EmployeeId` + `Type == "Leave"` + `FromShiftId == FromScheduleId` + `Status == "Pending"` → **400** `"Ca (dòng lịch) này đã có yêu cầu nhờ làm thay đang chờ duyệt. Huỷ yêu cầu cũ nếu muốn gửi lại."`
7. `ToStaffId` đã có ca cùng khung giờ (`WorkDate`, `StartTime`, `EndTime`) → **400** `$"Nhân viên được nhờ đã có ca cùng khung giờ ngày {mySchedule.WorkDate:dd/MM/yyyy} rồi, không thể nhận thêm ca này."`

**Side effect — INSERT `ShiftRequests`:**
```
EmployeeId    = request.StaffId
ToStaffId     = request.ToStaffId
Type          = "Leave"
TargetDate    = request.FromDate
FromDate      = request.FromDate
FromShiftId   = mySchedule.ScheduleId
Reason        = request.Reason
Status        = "Pending"
CreatedAt     = DateTime.Now
ToNotified    = false
AdminNotified = false
ApprovedByTo  = false
```

**Output:** **200** `{ "Status": "success", "Message": "Gửi yêu cầu nhờ làm thay thành công." }`

### 7.4 Đổi ca — `POST /api/shift-exchange/swap`

**Input:** `CreateSwapShiftRequestDto { StaffId, FromDate, ToDate, FromScheduleId, ToScheduleId, ToStaffId, Reason? }`

**Validation:**
1. `FromDate < hôm nay` hoặc `ToDate < hôm nay` → **400** `"Không thể đổi ca cho ngày trong quá khứ."`
2. `FromDate == hôm nay` → **400** `"Đã qua 0h sáng ngày hôm nay, không thể gửi yêu cầu đổi ca cùng ngày."`
3. `ToDate == hôm nay` → **400** `"Đã qua 0h sáng ngày hôm nay, không thể đổi ca nhận ngày hôm nay."`
4. Requester đã chấm công `FromDate` → **400** `"Bạn đã chấm công ngày này rồi, không thể yêu cầu đổi ca."`
5. `ToStaffId` đã chấm công `ToDate` → **400** `"Nhân viên được đổi đã chấm công ngày kia rồi, không thể đổi."`
6. `fromSchedule` null → **400** `"Không tìm thấy ca của bạn để đổi."`
7. `fromSchedule.WorkDate != FromDate` → **400** `"Ngày ca của bạn không khớp."`
8. `toSchedule` null → **400** `"Không tìm thấy ca của nhân viên được chọn để đổi."`
9. `toSchedule.WorkDate != ToDate` → **400** `"Ngày ca muốn đổi không khớp."`
10. Đã có `Swap` pending cùng `FromShiftId` → **400** `"Ca (dòng lịch) này đã có yêu cầu đổi đang chờ duyệt. Huỷ hoặc đợi xử lý xong để đổi tiếp."`
11. Requester đã có ca cùng khung giờ ngày nhận → **400** `$"Bạn đã có ca cùng khung giờ ngày {toSchedule.WorkDate:dd/MM/yyyy} rồi, không thể nhận thêm ca để đổi."`
12. `ToStaffId` đã có ca cùng khung giờ ngày gửi → **400** `$"Nhân viên được đổi đã có ca cùng khung giờ ngày {fromSchedule.WorkDate:dd/MM/yyyy} rồi, không thể nhận thêm ca để đổi."`

**Side effect — INSERT `ShiftRequests`:**
```
EmployeeId    = request.StaffId
ToStaffId     = request.ToStaffId
Type          = "Swap"
TargetDate    = request.FromDate
FromDate      = request.FromDate
ToDate        = request.ToDate
FromShiftId   = fromSchedule.ScheduleId
ToShiftId     = toSchedule.ScheduleId
Reason        = request.Reason
Status        = "Pending"
CreatedAt     = DateTime.Now
ToNotified    = false
AdminNotified = false
ApprovedByTo  = false
```

**Output:** **200** `{ "Status": "success", "Message": "Gửi yêu cầu đổi ca thành công." }`

### 7.5 Danh sách yêu cầu

- `GET /api/shift-exchange/pending?staffId=` — yêu cầu `ToStaffId == staffId && Status == "Pending"`, sort `CreatedAt` desc.
- `GET /api/shift-exchange/my-requests?staffId=` — yêu cầu `EmployeeId == staffId`, `.Take(50)`, sort `CreatedAt` desc; kèm `ToStaffName` và `ApprovedByTo`.

### 7.6 Chấp nhận — `POST /api/shift-exchange/accept/{requestId}?responderStaffId=`

**Tìm request:** `RequestId == requestId && ToStaffId == responderStaffId && Status == "Pending"` → null → **400** `"Yêu cầu không tồn tại hoặc không thuộc về bạn."`

**Bước 1 — check chấm công:**
- `targetDate = req.Type == "Swap" ? req.ToDate : req.FromDate`
- Nếu responder đã chấm công `targetDate` → set `Status = "Rejected"`, `ApprovedByTo = false`, save → **400** `"Bạn đã chấm công ngày đó rồi, không thể nhận đổi."`

**Bước 2 — Swap (`Type == "Swap"`):**
- `myShift` = `WorkSchedules[ScheduleId == req.ToShiftId && StaffId == responderStaffId]` (ca responder **muốn cho**)
- `partnerShift` = `WorkSchedules[ScheduleId == req.FromShiftId && StaffId == req.EmployeeId]` (ca requester **muốn nhận**)
- Một trong hai null → **400** `"Ca của một trong hai bên không còn tồn tại."`
- Requester đã chấm công `partnerShift.WorkDate` → **400** `"Người gửi yêu cầu đã chấm công ca kia rồi."`
- Requester đã có ca cùng khung giờ ngày nhận → **400** `$"Người gửi yêu cầu đã có ca cùng khung giờ ngày {partnerShift.WorkDate:dd/MM/yyyy} rồi, không thể nhận thêm ca nữa."`
- Responder đã có ca cùng khung giờ ngày nhận → **400** `$"Bạn đã có ca cùng khung giờ ngày {myShift.WorkDate:dd/MM/yyyy} rồi, không thể nhận thêm ca nữa."`
- **Swap owner:**
  ```csharp
  (myShift.StaffId, partnerShift.StaffId) = (partnerShift.StaffId, myShift.StaffId);
  ```
- Dọn trùng khung giờ cho cả 2 bên: `RemoveDuplicateSlotRowsForStaffAsync(...)` — giữ dòng có `ScheduleId` nhỏ nhất, xoá các dòng còn lại (cùng `StaffId` + `WorkDate` + `StartTime` + `EndTime`).

**Bước 2 — Leave (`Type != "Swap"`):**
- `myShift` = `WorkSchedules[ScheduleId == req.FromShiftId && StaffId == req.EmployeeId]` → null → **400** `"Ca cần nhờ làm thay không còn tồn tại."`
- Requester đã có ca cùng khung giờ → **400** `$"Người gửi yêu cầu đã có ca cùng khung giờ ngày {myShift.WorkDate:dd/MM/yyyy} rồi, không thể nhận thêm ca."`
- Responder đã có ca cùng khung giờ → **400** `$"Nhân viên được nhờ đã có ca cùng khung giờ ngày {myShift.WorkDate:dd/MM/yyyy} rồi, không thể nhận thêm ca."`
- **Chuyển ca:** `myShift.StaffId = responderStaffId;` + dọn trùng.

**Bước 3:** `req.Status = "Approved"; req.ApprovedByTo = true;` → save → **200** `{ "Status": "success", "Message": "Đã chấp nhận yêu cầu đổi ca." }`

### 7.7 Từ chối — `POST /api/shift-exchange/decline/{requestId}?responderStaffId=`

- Request null (sai `ToStaffId` hoặc không `Pending`) → **400** `"Yêu cầu không tồn tại hoặc không thuộc về bạn."`
- Set `Status = "Rejected"`, `ApprovedByTo = false` → **200** `{ "Status": "success", "Message": "Đã từ chối yêu cầu." }`

### 7.8 Hủy — `POST /api/shift-exchange/cancel/{requestId}?staffId=`

- Request null (`EmployeeId != staffId` hoặc không `Pending`) → **400** `"Yêu cầu không tồn tại hoặc không thể huỷ."`
- Set `Status = "Cancelled"` → **200** `{ "Status": "success", "Message": "Đã huỷ yêu cầu." }`

### 7.9 Status flow của `ShiftRequest`

```
                  ┌──────────────┐
  create ────────▶│   Pending    │  (ToNotified=false, ApprovedByTo=false)
                  └───┬───┬───┬──┘
        accept ───────┘   │   └────── decline
             │             │ cancel
             ▼             ▼          ▼
        ┌─────────┐  ┌───────────┐  ┌───────────┐
        │Approved │  │ Cancelled │  │ Rejected  │
        │ApprovedByTo=true     │         │
        └─────────┘  └───────────┘  └───────────┘
```

---

## 8. Đặt lịch Spa

**Service:** `Services/SpaBookingService.cs`. **Repository:** `Repositories/SpaBookingRepository.cs`.
**Hằng số quan trọng:**
```csharp
private static readonly SemaphoreSlim SchedulingLock = new(1, 1);       // lock toàn cục
private static readonly TimeOnly EveningShiftStart   = new(19, 0, 0);   // ca tối: không nhận đặt
private static readonly TimeOnly LatestBookingTime   = new(22, 0, 0);   // giờ đặt muộn nhất
```
**Bước slot:** `GetSlotStepMinutes() => 30` (phút). **Sức chứa tối đa mỗi khung giờ:** `const int maxCapacity = 2`.

### 8.1 `POST /api/spa-booking/availability`

**Input:** `SpaAvailabilityRequest { Start: DateTime, DurationMin: int, Quantity: int }`

**Trình tự:**
1. `startTime = TimeOnly.FromDateTime(start)`
2. `startTime >= 19:00` → `IsAvailable = false`, `Existing = 0`, `MaxCapacity = 2`, `SuggestedStart = null`, message:
   > `Ca tối (bắt đầu từ 19:00) không nhận đặt dịch vụ. Vui lòng chọn ca sáng hoặc ca chiều.`
3. `startTime >= 22:00` → message:
   > `Giờ bắt đầu ({startTime:HH\:mm}) sau giới hạn đặt lịch (22:00). Vui lòng chọn thời gian sớm hơn.`
4. `durationMin <= 0 || quantity <= 0` → `IsAvailable = true`, `Existing = 0`, message `null`.
5. `existing = CountSpaBookingsInTimeSlotAsync(start, end)`; `canBook = existing + quantity <= 2`.
6. Nếu `canBook` → `IsAvailable = true`, `Existing = existing`, message `null`.
7. Ngược lại → tìm `suggested = FindNearestAvailableStartAsync(...)`:
   - Có suggestion → `Khung giờ đã đủ 2 slot. Gợi ý gần nhất: {suggested:dd/MM/yyyy HH:mm}.`
   - Không → `Khung giờ đã đủ 2 slot. Vui lòng chọn thời gian khác.`

**Tìm slot gần nhất:** bắt đầu từ `AlignToSlot(from, 30).AddMinutes(30)`, tối đa **96** lần dò, mỗi lần `+30 phút`; slot đạt nếu `existing + quantity <= maxCapacity`.

### 8.2 `POST /api/spa-booking/validate-slot`

**Input:** `BookingSlotValidationRequest { AppointmentStart: DateTime, DurationMinutes: int }`

**Trình tự:**
1. `DurationMinutes <= 0` → `IsValid = false`, message:
   > `Thời gian dịch vụ phải lớn hơn 0 phút.`
2. `appointmentStartTime >= 19:00` → message:
   > `Ca tối (bắt đầu từ 19:00) không nhận đặt dịch vụ. Vui lòng chọn ca sáng hoặc ca chiều.`
3. `appointmentStartTime >= 22:00` → message:
   > `Giờ bắt đầu ({appointmentStartTime:HH\:mm}) sau giới hạn đặt lịch (22:00). Vui lòng chọn thời gian sớm hơn.`
4. Load `WorkSchedules` của ngày đó, sort `StartTime`, `WorkScheduleDedup.ByTimeSlot(...)`.
5. **Chỉ giữ ca sáng và chiều:** `shifts.Where(s => s.StartTime < EveningShiftStart)`.
6. Với mỗi ca: appointment nằm trọn trong ca (`start >= shiftStart && end <= shiftEnd`) → thêm vào `AvailableShifts`, label:
   - `shift.EndTime <= 12:00` → `Ca sáng ({start:HH\:mm}–{end:HH\:mm})`
   - ngược lại → `Ca chiều ({start:HH\:mm}–{end:HH\:mm})`
   - không khớp → thêm vào `ConflictingShifts` với label `{start:HH\:mm}–{end:HH\:mm}`
7. Nếu không khớp ca nào và có ca vận hành → dựng message theo 4 nhánh:
   - Kết thúc trước ca bắt đầu:
     `Dịch vụ kết thúc lúc {appointmentEndTime:HH\:mm}, ca làm việc bắt đầu lúc {firstConflict.StartTime:HH\:mm}. Vui lòng đặt lại từ {firstConflict.StartTime:HH\:mm}.`
   - Bắt đầu sau ca kết thúc:
     `Giờ bắt đầu ({appointmentStartTime:HH\:mm}) sau ca làm việc kết thúc ({firstConflict.EndTime:HH\:mm}). Vui lòng chọn ca khác.`
   - Bắt đầu trước ca (overlap đầu):
     `Dịch vụ kết thúc lúc {appointmentEndTime:HH\:mm}, ca làm việc bắt đầu lúc {firstConflict.StartTime:HH\:mm}. Vui lòng đặt lại từ {firstConflict.StartTime:HH\:mm}.`
   - Overlap giữa:
     `Dịch vụ kết thúc lúc {appointmentEndTime:HH\:mm}, ca làm việc kết thúc lúc {firstConflict.EndTime:HH\:mm}. Vui lòng rút ngắn dịch vụ hoặc đặt lại.`

**Output:** `BookingSlotValidationResult { IsValid, AppointmentStart, AppointmentEnd, DurationMinutes, Message, AvailableShifts[], ConflictingShifts[] }`
Controller: `IsValid == false` → **400** (trả nguyên result); ngược lại **200**.

### 8.3 `POST /api/spa-booking/estimate?customerId=`

**Input:** `SpaBookingEstimateRequest { PetIds: List<int>, Items: List<{ServiceId, Quantity}> }`

**Trình tự:**
1. `PetIds` null/rỗng → **ArgumentException** `"Vui lòng chọn pet."`
2. `Items` null/rỗng → **ArgumentException** `"Giỏ dịch vụ trống."`
3. `selectedPets` lọc từ pet của customer → rỗng → **InvalidOperationException** `"Không tìm thấy pet của khách hàng."`
4. `perPetDuration` = tổng `pricing.DurationMin * item.Quantity` (dùng `selectedPets[0].BreedId` cho pricing lookup); nếu `Quantity <= 0` → bỏ qua.
5. Với **từng pet × từng item**: `pricing = GetServicePricingAsync(item.ServiceId, pet.BreedId)` → null → **InvalidOperationException** `"Dịch vụ không hợp lệ."`
6. `totalPrice += pricing.UnitPrice * item.Quantity`
7. `perPetDuration <= 0` → fallback = `slotStep` (30) để tránh duration = 0.
8. `totalDuration = perPetDuration * selectedPets.Count`

**Output:** `SpaBookingEstimateDto { Items[], PerPetDurationMin, TotalDurationMin, PetCount, TotalPrice }`

**Công thức giá theo giống (`GetServicePricingAsync`):** `unitPrice = service.Price * priceAdjustPercent / 100m` (nếu có `BreedPricings` theo `BreedId`), `unitPrice = Math.Max(0m, unitPrice)`. Comment: *"PriceAdjust là %: 80 = giảm 20%, 120 = tăng 20%, 100 = giá gốc"*.

### 8.4 `POST /api/spa-booking?customerId=` — Tạo booking

**Input:** `CreateSpaBookingRequest { PetIds[], Items[], AppointmentStart, Note?, PaymentMethod? }`

**Trình tự (`CreateBookingAsync`):**
1. `PetIds` null/rỗng → **ArgumentException** `"Vui lòng chọn pet."`
2. `EstimateAsync(...)` → dùng kết quả cho `PerPetDurationMin`, `TotalDurationMin`, `TotalPrice`.
3. Chuẩn hóa `AppointmentStart`:
   - `Kind == Unspecified` → `DateTime.SpecifyKind(..., Local)`
   - `Kind == Utc` → `.ToLocalTime()`
4. `appointmentStart <= DateTime.Now` → **InvalidOperationException** `"Thời gian hẹn không được nằm trong quá khứ."`
5. `startTime >= 19:00` → **InvalidOperationException** `"Ca tối (bắt đầu từ 19:00) không nhận đặt dịch vụ. Vui lòng chọn ca sáng hoặc ca chiều."`
6. `startTime >= 22:00` → **InvalidOperationException** `"Giờ bắt đầu ({startTime:HH\:mm}) sau giới hạn đặt lịch (22:00). Vui lòng chọn thời gian sớm hơn."`
7. `selectedPets` rỗng → **InvalidOperationException** `"Không tìm thấy pet của khách hàng."`
8. **Lock:** `await SchedulingLock.WaitAsync(ct)` — semaphore tĩnh toàn cục (chống race 2 request tạo booking cùng lúc).
9. **Trạng thái khởi tạo:**
   ```csharp
   var paymentMethod = (request.PaymentMethod ?? "cash").Trim().ToLowerInvariant();
   var initialStatus = paymentMethod == "payos" ? "Chờ thanh toán PayOS" : "Chưa thanh toán";
   ```
10. **Vòng lặp wave** (`petsInThisWave = Math.Min(2, selectedPets.Count - petIndex)`):
    - **Wave 0 (petIndex == 0):** `scheduledStart = AlignToSlot(nextPreferredStart, 30)`; `existingSpa = CountSpaBookingsInTimeSlotAsync(scheduledStart, firstWaveEnd)`.
      - Nếu `existingSpa + petsInThisWave > 2` → tìm `suggested` rồi **ném** `SlotCapacityException("Khung giờ đã đủ 2 slot.", suggested)`.
      - **Không auto đẩy giờ** ở wave đầu — báo lỗi để khách chọn lại.
    - **Wave > 0:** `scheduledStart = await FindNearestAvailableStartWithPlannedAsync(nextPreferredStart, perPetDuration, petsInThisWave, 2, plannedSlots, ct)` → null → **ném** `SlotCapacityException("Không tìm được khung giờ phù hợp cho tất cả pet. Vui lòng chọn thời gian khác.", null)`.
11. **Với mỗi pet trong wave:**
    - Tạo `Booking { CustomerId, PetId, AppointmentStart = scheduledStart, AppointmentEnd = currentEnd, Status = initialStatus, Note = request.Note?.Trim(), CreatedAt = DateTime.Now }` → `CreateBookingAsync` → lấy `bookingId`, ghi vào `createdBookingIds`.
    - Với mỗi item (`Quantity > 0`): `GetServicePricingAsync(item.ServiceId, pet.BreedId)` → null → **InvalidOperationException** `"Dịch vụ không hợp lệ."`; tạo `BookingService { BookingId, ServiceId, Quantity, UnitPrice = pricing.UnitPrice, DurationMin = pricing.DurationMin, Note = "", CreatedAt = DateTime.Now }` → `AddBookingServiceAsync`.
    - `plannedSlots.Add((scheduledStart, currentEnd))`; cập nhật `latestEnd`.
    - `nextPreferredStart = currentEnd` (wave sau nối tiếp).
12. **Rollback:** `catch` → `await _repo.DeleteBookingsAsync(createdBookingIds, ct)` rồi `throw` (tránh partial success). `finally` → `SchedulingLock.Release()`.
13. **Output:** `SpaBookingDto { BookingId, PetId, PetName, BreedName, SpeciesName, ..., AppointmentStart, AppointmentEnd = finalAppointmentEnd, Status = initialStatus, CreatedAt, Items[], TotalPrice, PaymentMethod }` với `PaymentMethod = paymentMethod == "payos" ? "payos" : "cash"` và `BookingIds = createdBookingIds`.

**Mapping lỗi controller (`POST /api/spa-booking`):**
- `ArgumentException` → **400** `{ "message": ex.Message }`
- `SlotCapacityException` → **400** `{ "message": ex.Message, "suggestedStart": ex.SuggestedStart?.ToString("o") }`
- `InvalidOperationException` → **400** `{ "message": ex.Message, "suggestedStart": TryExtractSuggestedStart(ex.Message) }`
  - `TryExtractSuggestedStart` dùng regex `(\d{2}/\d{2}/\d{4})\s+(\d{2}:\d{2})` → parse `"dd/MM/yyyy HH:mm"` → `.ToString("o")`.

### 8.5 Đếm slot — `CountSpaBookingsInTimeSlotAsync`

```csharp
var scanStart = start.AddHours(-12);
var scanEnd   = end.AddHours(12);
// chỉ lấy booking có dịch vụ loại "spa"
.Where(b => b.BookingServices.Any(bs => bs.Service != null
              && bs.Service.ServiceType != null
              && bs.Service.ServiceType.ToLower() == "spa"))
// tính overlap: b.AppointmentStart < end && effectiveEnd > start
// effectiveEnd = AppointmentEnd > AppointmentStart ? AppointmentEnd : AppointmentStart.AddMinutes(30)
// .Distinct() theo (PetId, Start, End) rồi .Count()
```

**Status được tính là "chiếm slot" (`IsOccupyingSlotStatus`)** — chứa (case-insensitive):
```
đã chấp nhận, đã xác nhận, đang xử lý, hoàn thành, đã thanh toán, chờ thanh toán payos,
accepted, confirmed
```
> **Lưu ý:** `"Chưa thanh toán"` **KHÔNG** chiếm slot → 1 booking chưa thanh toán vẫn có thể bị double-book. Cần xác nhận khi migrate.

### 8.6 Các endpoint spa còn lại

- `GET /api/spa-booking/pets?customerId=` — pet của khách (repo `GetPetsByCustomerIdAsync`).
- `GET /api/spa-booking/services` — dịch vụ `ServiceType == "spa"` và (`Status == null` hoặc `"active"`), sort `ServiceType` → `Name`.
- `GET /api/spa-booking/history?customerId=` — lịch sử của khách.
- `GET /api/spa-booking/history/all` và `GET /api/spa-booking/all-bookings` — toàn bộ.
- `GET /api/spa-booking/{bookingId}/invoice` — null → **404** `{ "message": "Không tìm thấy booking." }`.
- `PATCH /api/spa-booking/{bookingId}/status` — body `{ "status": "..." }`:
  - `status` rỗng → **ArgumentException** `"Trạng thái không hợp lệ."`
  - Không tìm thấy booking → **InvalidOperationException** `"Không tìm thấy booking để cập nhật trạng thái."` → **404**
  - Thành công → **200** `{ "message": "Updated" }`
- `POST /api/spa-booking/review?customerId=`:
  - `Rating < 1 || Rating > 5` → **ArgumentException** `"Rating phải từ 1 đến 5."`
  - `HasCompletedBookingAsync` false → **InvalidOperationException** `"Bạn chưa hoàn thành dịch vụ này."`
  - **Luôn tạo review mới** (không ghi đè) → **200** `{ "reviewId": N }`

### 8.7 Status flow booking spa

```
Tạo booking ──► paymentMethod = "cash"  ──► "Chưa thanh toán"
             └─► paymentMethod = "payos" ──► "Chờ thanh toán PayOS"
                                              │
                       PayOS webhook (PAID)    ▼
                                        "Đã thanh toán"

Các trạng thái vận hành khác (qua PATCH status):
"Đã chấp nhận", "Đã xác nhận", "Đang xử lý", "Hoàn thành", "Đã hủy"
```

> **Rủi ro cần xử lý khi migrate:** trạng thái là **string tự do** (không enum), đổi bằng `PATCH` từ client, và frontend `SpaInvoicePage.jsx` tự gọi `PATCH .../status` khi PayOS redirect về → có thể set `"Đã thanh toán"` **trước khi webhook server-side xử lý** (client cũng có thể set `"Đã hủy"` dù đã thanh toán). Nên chuyển sang enum + chỉ cho webhook cập nhật trạng thái thanh toán.

---

## 9. Danh sách rủi ro / điểm cần quyết định khi migrate

| # | Vấn đề | Vị trí | Mức độ |
|---|---|---|---|
| 1 | `SystemPromptPath` khai báo nhưng không dùng; **không tồn tại file prompt riêng** — prompt hard-code trong `ChatbotService` | `ChatbotOptions.cs` | Cao — cần chọn: giữ inline hay tách file |
| 2 | `DefaultProvider = "groq"` nhưng không có provider Groq; `OllamaProviderService` không được đăng ký DI → **không có fallback chain thật** | `ChatbotOptions.cs`, `Program.cs` | Cao |
| 3 | Chatbot **không lưu lịch sử vào DB** dù bảng `ChatMessages` tồn tại; lịch sử do client gửi lên | `ChatbotService.cs` | Cao |
| 4 | `HandleDatabaseQuery` và `TryHandleProductQueryDirectlyAsync` là **dead code** (được viết nhưng không gọi) | `ChatbotService.cs` | Trung bình |
| 5 | Message lỗi provider Gemini nhắc "Ollama is not available..." | `ChatbotService.CallAiProviderAsync` | Trung bình |
| 6 | API key Gemini, PayOS ClientId/ApiKey/ChecksumKey, SMTP App Password **hard-code trong `appsettings.json`** (repo) | `appsettings.json` | Cao — phải chuyển sang User Secrets / biến môi trường |
| 7 | `_lastError` / `_lastResponse` là field instance của scoped service → race khi concurrent | `PayOSService.cs` | Trung bình |
| 8 | OTP hết hạn **5 phút** nhưng email ghi **10 phút** | `AuthService.cs` vs `EmailService.cs` | Thấp |
| 9 | OTP sinh bằng `System.Random` (không phải CSPRNG) | `AuthService.GenerateOtp` | Trung bình |
| 10 | Đăng nhập staff/doctor/admin **không kiểm tra `Status`** (chỉ customer mới check `active`) | `AuthService.LoginAsync` | Cao |
| 11 | `IsPasswordValid` chấp nhận plaintext fallback | `AuthService.cs` | Trung bình |
| 12 | Upload ảnh chỉ kiểm `Content-Type` (client khai báo), không magic bytes / extension thật | `PetsController.cs` | Cao |
| 13 | Auth dựa trên header `X-User-Id` / `X-User-Role` tự khai, **không có JWT** | `PetsController.ValidateSelfAccess` | Cao |
| 14 | IP allowlist đọc từ header `X-Client-IP` (spoofable) | `ShiftExchangeController` | Cao |
| 15 | `SlotCapacityException` lúc wave đầu → **không auto đẩy giờ** (UX) | `SpaBookingService` | Thấp |
| 16 | `"Chưa thanh toán"` không chiếm slot → có thể double-book | `SpaBookingRepository.IsOccupyingSlotStatus` | Cao |
| 17 | Trạng thái booking là string tự do, client tự `PATCH` sau PayOS redirect → có thể ghi đè trạng thái thanh toán | `SpaBookingService` + `SpaInvoicePage.jsx` | Cao |
| 18 | `Payment` record được insert **trước** khi tạo link PayOS; nếu PayOS lỗi → record orphan `pending` | `PayOSController` | Trung bình |
| 19 | `SendOrderInvoiceEmailAsync` tồn tại nhưng không được gọi (chỉ gửi hoá đơn spa) | `PayOSService` | Thấp |
| 20 | `AuthService.Google.cs` không tồn tại (logic Google nằm chung trong `AuthService.cs`) | — | Thấp — cần tách khi migrate |

---

## 10. Phụ lục — file nguồn đã đọc

**Backend (`Projectojt/BackEnd`)**
- `Services/ChatbotService.cs`, `Services/GeminiProviderService.cs`, `Services/OllamaProviderService.cs`
- `Interfaces/IChatbotService.cs`, `Interfaces/IAiProviderService.cs`
- `Models/ChatbotOptions.cs`, `Models/ChatbotDtos.cs`, `Models/ChatMessage.cs`
- `Controllers/ChatbotController.cs`, `Program.cs`, `appsettings.json`
- `Services/PayOSService.cs`, `Controllers/PayOSController.cs`, `Models/PayOSDtos.cs`, `Models/PayOSOptions.cs`
- `Services/AuthService.cs`, `Controllers/AuthController.cs`, `Models/AuthDtos.cs`
- `Services/EmailService.cs`, `EmailTemplates/OrderInvoice.hbs`, `EmailTemplates/SpaInvoice.hbs`
- `Controllers/PetsController.cs`, `Controllers/NotificationController.cs`
- `Controllers/ShiftExchangeController.cs`, `Models/ShiftExchangeDtos.cs`
- `Services/SpaBookingService.cs`, `Controllers/SpaBookingController.cs`, `Repositories/SpaBookingRepository.cs`
- `Data/ShopPetDatabaseContext.cs`, `Models/Customer.cs`, `Models/Admin.cs`

**Frontend (`Projectojt/frontend`)**
- `src/api/chatbotApi.js`, `src/api/authApi.js`, `src/api/petsApi.js`
- `src/pages/LoginPage.jsx`, `src/pages/SpaCartPage.jsx`, `src/pages/SpaInvoicePage.jsx`, `src/pages/SpaBookingHistoryPage.jsx`, `src/pages/SpaBookingHistoryDetailPage.jsx`
