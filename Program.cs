using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using PetShop.Interfaces;
using PetShop.Models;
using PetShop.Repositories;
using PetShop.Services;
using PetShop.Middleware;
using Swashbuckle.AspNetCore.SwaggerGen;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>(optional: true, reloadOnChange: true);
}

builder.Services.AddControllersWithViews(options =>
{
    // Formatter CSV tùy chỉnh — client gửi "Accept: text/csv" sẽ nhận bảng CSV.
    options.OutputFormatters.Add(new CsvOutputFormatter());

    // LS-10 / SL-04 — Custom Input Formatter (slide PRN232 Ch03 S17):
    // chuẩn hoá chữ thường trong body JSON thành HOA trước khi model binding.
    // Insert(0) để formatter này được thử TRƯỚC SystemTextJsonInputFormatter mặc định
    // (nếu Add() thì formatter JSON built-in đã đọc body trước và formatter custom không bao giờ chạy).
    options.InputFormatters.Insert(0, new UpperCaseTextInputFormatter());
})
    // Bật XML formatter để hỗ trợ content negotiation: JSON (mặc định) và XML.
    .AddPetShopXmlFormatters()
    // OData (slide PRN232 - Ch04): cho phép client truy vấn động bằng
    // $filter / $select / $orderby / $top / $skip / $expand / $count trên /odata/*.
    .AddOData(options => options
        .AddRouteComponents("PetShop", PetShopEdmModel.Build())
        .Select()
        .Filter()
        .OrderBy()
        .Expand()
        .Count()
        .SkipToken()
        .SetMaxTop(200))
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

// TỐI ƯU RAM + an toàn:
//   DetailedErrors = true ⇒ mỗi exception trong Blazor serialize toàn bộ stack trace +
//   state component vào gửi cho client ⇒ tốn bộ nhớ và lộ chi tiết nội bộ.
//   Chỉ bật ở Development; Production để false (mặc định).
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options =>
    {
        options.DetailedErrors = builder.Environment.IsDevelopment();
    });

// CircuitDiagnosticsHandler — theo dõi vòng đời circuit Blazor (chẩn đoán lỗi
// "Connection disconnected" mà user báo). Chỉ log thêm, KHÔNG đổi hành vi runtime.
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler, PetShop.Services.CircuitDiagnosticsHandler>();

// ---------------------------------------------------------------------------
// TỐI ƯU RAM — giới hạn bộ nhớ theo CIRCUIT (Blazor Server).
//
// Mỗi tab người dùng = 1 circuit = 1 scope DI (giữ UserSessionService,
// CartStateService, RealtimeHubService, DbContext...). Khi người dùng đóng tab
// nhưng mạng chập chờn, ASP.NET Core GIỮ circuit đã ngắt trong RAM để cho
// phép reconnect nhanh.
//
//   .NET 8 dùng cặp property này (thay cho DisconnectedIdleTimeout cũ):
//     - DisconnectedCircuitMaxRetained = 10
//         ⇒ tối đa 10 circuit đã ngắt được giữ lại. Mặc định 20.
//     - DisconnectedCircuitRetentionPeriod = 60s
//         ⇒ circuit đã ngắt bị giải phóng sau 60 giây. Mặc định 3 phút.
//
//   ⇒ Circuit KHÔNG được giữ trong RAM lâu dài ⇒ giảm rõ rệt RSS khi người
//     dùng mở/đóng nhiều tab.
//
//   MaxBufferedUnacknowledgedRenderBatches = 32:
//   giới hạn số batch render đang chờ gửi tới client (mỗi batch giữ render tree trong RAM).
//
//   ⚠️ SỬA BUG (10/11/2026) — KHÔNG được đặt nhỏ hơn mặc định (10).
//   Trước đây đặt = 8 ⇒ khi client chậm (tab nền, mạng chập chờn, máy yếu) hàng
//   đợi render batch đầy ⇒ server CHỦ ĐỘNG ĐÓNG circuit ⇒ client báo:
//     "Error: Connection disconnected with error 'Error: Server returned an error
//      on close: Connection closed with an error.'"
//   ⇒ toàn bộ UI Blazor Server chết, mọi nút bấm không phản hồi, buộc user
//      phải F5/reset trang. Đây chính là triệu chứng "nút giỏ hàng không thèm
//      hoạt động" mà user báo.
//
//   Nay đặt = 32 (cao hơn mặc định 10) ⇒ chịu được burst render lớn (mở drawer,
//   đổi trang, thêm giỏ hàng) mà không bị cắt circuit.
//
//   32 batch × vài chục KB render tree ≈ vài MB RAM/circuit ⇒ vẫn rẻ hơn nhiều
//   so với việc user phải F5 liên tục.
//
// ---------------------------------------------------------------------------
// ⚠️ TỐI ƯU RAM (11/11/2026) — giảm số circuit "đã ngắt" được giữ lại.
//
//   DisconnectedCircuitMaxRetained = 100 (trước) → 20 (mặc định .NET 8):
//     MỖI circuit đã ngắt vẫn GIỮ NGUYÊN scope DI của nó trong RAM:
//     scoped DbContext + ChangeTracker + UserSessionService + CartStateService +
//     RealtimeHubService + render buffer ⇒ vài trăm KB – vài MB mỗi circuit.
//     100 circuit × 3 phút = có thể giữ hàng trăm MB chỉ cho các circuit
//     NGẮT (user đã đóng tab / mạng chập chờn).
//
//     Máy local của user chỉ còn ~1.6 GB RAM trống (SQL Server 1.4 GB +
//     Cursor 700 MB × nhiều tiến trình) ⇒ giữ 100 circuit đã ngắt là nguyên nhân
//     chính khiến máy chậm. 20 circuit là mặc định của .NET 8 và đủ cho việc
//     reconnect nhanh.
//
//   DisconnectedCircuitRetentionPeriod = 3 phút → 1 phút:
//     Circuit đã ngắt được giữ để reconnect nhanh. 1 phút là đủ cho người dùng
//     bấm F5 / chuyển tab (Blazor reconnect thường xong trong vài giây),
//     nhưng giảm 2/3 thời gian giữ circuit chết trong RAM.
//
//   KHÔNG được đặt MaxBufferedUnacknowledgedRenderBatches < 10 (mặc định .NET 8)
//   — xem cảnh báo bug "Connection disconnected" ở trên.
// ---------------------------------------------------------------------------
builder.Services.Configure<Microsoft.AspNetCore.Components.Server.CircuitOptions>(options =>
{
    options.DisconnectedCircuitMaxRetained = 20;
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(1);
    options.MaxBufferedUnacknowledgedRenderBatches = 32;
});

// ---------------------------------------------------------------------------
// GZIP / BROTLI — fix hiệu năng lớn nhất.
//
// VÌ SAO CẦN:
//   App nạp ~380 KB CSS + JS ở MỌI trang. Nếu không nén, mỗi lần tải trang là
//   ~400 KB chữ thô qua WebSocket/TCP ⇒ điều hướng rất lag trên máy local.
//
//   Brotli cho static assets (CSS/JS) + Gzip cho response động.
//   Mức nén mặc định: Brotli Quality 5 (cân bằng CPU / tỉ lệ nén),
//   Gzip Level 6.
//
//   LƯU Ý: KHÔNG nén WebSocket (Blazor Server dùng WebSocket) — ASP.NET Core
//   tự động bỏ qua response của type "text/event-stream" và WebSocket.
// ---------------------------------------------------------------------------
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;

    options.MimeTypes = options.MimeTypes.Concat(new[]
    {
        "text/css",
        "text/plain",
        "text/javascript",
        "text/html",
        "application/javascript",
        "application/json",
        "application/xml",
        "image/svg+xml",
        "font/woff",
        "font/woff2",
    });

    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});

builder.Services.AddHttpContextAccessor();

builder.Services.Configure<PetShop.Services.JwtSettings>(
    builder.Configuration.GetSection(PetShop.Services.JwtSettings.SectionName));
builder.Services.AddSingleton<PetShop.Services.IJwtTokenService, PetShop.Services.JwtTokenService>();

// ---------------------------------------------------------------------------
// Xác thực dual-mode (slide PRN232 - Ch06):
//   - "Session" : header nội bộ do Blazor gắn (X-User-Id / X-User-Role) — dùng cho self-HTTP.
//   - "Bearer"  : JWT Bearer token cho client API bên ngoài.
// Endpoint có [Authorize] sẽ chấp nhận cả hai nguồn.
// ---------------------------------------------------------------------------
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = PetShop.Services.AuthSchemes.Default;
        options.DefaultAuthenticateScheme = PetShop.Services.AuthSchemes.Default;
        // Khi client không hợp lệ, challenge bằng Bearer để trả 401 + header WWW-Authenticate.
        options.DefaultChallengeScheme = PetShop.Services.AuthSchemes.Bearer;
        options.DefaultForbidScheme = PetShop.Services.AuthSchemes.Bearer;
    })
    // Scheme tổng hợp: nếu request có "Authorization: Bearer ..." thì chuyển tiếp sang
    // JwtBearer, ngược lại dùng scheme Session (header nội bộ do Blazor gắn).
    .AddPolicyScheme(PetShop.Services.AuthSchemes.Default, "Session hoặc Bearer", options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            var authorization = context.Request.Headers.Authorization.ToString();
            return authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? PetShop.Services.AuthSchemes.Bearer
                : PetShop.Services.AuthSchemes.Session;
        };
    })
    .AddScheme<AuthenticationSchemeOptions, PetShop.Services.SessionHeaderAuthenticationHandler>(
        PetShop.Services.AuthSchemes.Session, _ => { })
    .AddJwtBearer(PetShop.Services.AuthSchemes.Bearer, options =>
    {
        var jwtSettings = builder.Configuration
            .GetSection(PetShop.Services.JwtSettings.SectionName)
            .Get<PetShop.Services.JwtSettings>() ?? new PetShop.Services.JwtSettings();

        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(jwtSettings.Key ?? string.Empty)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization(options =>
{
    // Policy theo vai trò — dùng cho API (xem AppRoles.cs để biết chuẩn hoá role).
    //
    // Vì sao cần: trước đây các endpoint nhạy cảm (api/checkout, api/orders, api/pets,
    // api/spa-booking/*) KHÔNG có [Authorize] ⇒ ai cũng xem/hủy được đơn người khác
    // (IDOR). Nay gắn [Authorize] + policy ⇒ controller lấy CustomerId từ CLAIM
    // thay vì tin query string.
    options.AddPolicy(AuthPolicies.StaffOnly, policy =>
        policy.RequireAssertion(ctx => AppRoles.IsStaff(ctx.User.FindFirst(ClaimTypes.Role)?.Value)));

    options.AddPolicy(AuthPolicies.StaffOrAdmin, policy =>
        policy.RequireAssertion(ctx => AppRoles.IsInAllowedRoles(
            ctx.User.FindFirst(ClaimTypes.Role)?.Value, AppRoles.Staff, AppRoles.Admin)));

    options.AddPolicy(AuthPolicies.CustomerOnly, policy =>
        policy.RequireAssertion(ctx => AppRoles.IsCustomer(ctx.User.FindFirst(ClaimTypes.Role)?.Value)));
});

// Typed HttpClient trỏ về chính API của ứng dụng (xem Services/SelfHttpClientExtensions.cs).
builder.Services.AddSelfHttpClient<PetShop.Interfaces.IProductCatalogClient, PetShop.Services.ProductCatalogClient>();
// AuthClient cũng tự gắn header qua SessionHeader.Attach (xem Services/AuthClient.cs)
// ⇒ dùng AddSelfHttpClient (KHÔNG dùng AddSelfHttpClientWithAuth) để không phụ
// thuộc SpaAuthHeaderHandler — handler đó KHÔNG nhìn thấy UserSessionService của
// circuit Blazor (scope riêng của HttpClientFactory) ⇒ header không được gắn.
builder.Services.AddSelfHttpClient<PetShop.Interfaces.IAuthClient, PetShop.Services.AuthClient>();
builder.Services.AddSelfHttpClient<PetShop.Interfaces.ICheckoutClient, PetShop.Services.CheckoutClient>();
builder.Services.AddSelfHttpClient<PetShop.Interfaces.IOrderClient, PetShop.Services.OrderClient>();
builder.Services.AddSelfHttpClient<PetShop.Interfaces.IReviewClient, PetShop.Services.ReviewClient>();
builder.Services.AddSelfHttpClient<PetShop.Interfaces.IPaymentClient, PetShop.Services.PaymentClient>();
builder.Services.AddSelfHttpClient<PetShop.Interfaces.IPetServiceClient, PetShop.Services.PetServiceClient>();
builder.Services.AddSelfHttpClient<PetShop.Interfaces.INotificationClient, PetShop.Services.NotificationClient>();

// Hai client dưới đây cần header phiên đăng nhập (X-User-Id / X-Customer-Id / X-User-Role).
//
// VÌ SAO KHÔNG DÙNG SpaAuthHeaderHandler (DelegatingHandler):
//   Handler do IHttpClientFactory tạo nằm trong scope RIÊNG của HttpClient
//   (handler chain được cache ~2 phút theo tên client) ⇒ KHÔNG nhìn thấy
//   UserSessionService scoped của circuit Blazor ⇒ CurrentUser luôn null
//   ⇒ header không được gắn ⇒ endpoint có [Authorize] trả 401 hoặc rơi vào
//   nhánh không xác thực ⇒ dữ liệu sai / "tự đăng nhập tài khoản khác".
//
// ⇒ Mỗi client tự inject UserSessionService (cùng scope với component) và gắn
//   header trực tiếp qua SessionHeader.Attach(...). Xem SessionHeader.cs.
builder.Services.AddSelfHttpClient<PetShop.Interfaces.ISpaBookingClient, PetShop.Services.SpaBookingClient>();
builder.Services.AddSelfHttpClient<PetShop.Interfaces.IPetsClient, PetShop.Services.PetsClient>();
// Q21 — widget chat 1-1 khách ↔ nhân viên: client gọi `api/chat/*`.
// Client tự gắn header phiên đăng nhập (X-User-Id / X-User-Role) từ UserSessionService
// vì endpoint chat lấy danh tính từ claims — xem ChatClient.cs.
builder.Services.AddSelfHttpClient<PetShop.Interfaces.IChatClient, PetShop.Services.ChatClient>();

// Q22 — widget chatbot AI: client gọi `api/chatbot/*`.
// Endpoint chatbot là [AllowAnonymous] ⇒ KHÔNG cần header phiên đăng nhập
// (khách chưa đăng nhập vẫn tư vấn được). customerId trong body quyết định
// có lấy ngữ cảnh cá nhân hay không.
builder.Services.AddSelfHttpClient<PetShop.Interfaces.IChatbotClient, PetShop.Services.ChatbotClient>();

// SpaAuthHeaderHandler: giữ đăng ký để không nổ "No service for type" nếu có
// ai đó dùng lại AddSelfHttpClientWithAuth. Handler này KHÔNG nhìn thấy
// UserSessionService của circuit (scope riêng của HttpClientFactory) ⇒ các
// client quan trọng đã chuyển sang tự gắn header qua SessionHeader.Attach.
builder.Services.AddTransient<PetShop.Services.SpaAuthHeaderHandler>();
// Q21 — 3-layer chat: Repository → Service. Controller/Razor không inject DbContext.
builder.Services.AddScoped<PetShop.Interfaces.IChatRepository, PetShop.Repositories.ChatRepository>();
builder.Services.AddScoped<PetShop.Interfaces.IChatService, PetShop.Services.ChatService>();
builder.Services.AddScoped<PetShop.Services.UserSessionService>();
builder.Services.AddScoped<PetShop.Services.CartStateService>();

// CartDrawerStateService — trạng thái mở/đóng của drawer giỏ hàng DÙNG CHUNG
// cho mọi trang (render 1 lần trong MainLayout).
//
// VÌ SAO: trước đây mỗi trang tự khai báo <CartDrawer> + <Header OnOpenCart=...>.
// Chỉ 4 trang làm vậy ⇒ các trang khác (/services, /about, /boarding, /blog,
// /contact, /account…) bấm nút giỏ hàng KHÔNG có gì xảy ra ⇒ buộc F5.
builder.Services.AddScoped<PetShop.Services.CartDrawerStateService>();

// RealtimeHubService — SCOPED (1 instance cho mỗi circuit Blazor).
//
// VÌ SAO SCOPED:
//   Mỗi circuit = 1 tab người dùng. Service giữ 1 HubConnection SignalR dùng chung
//   cho NotificationBell + ChatWidget ⇒ 1 WebSocket thay vì 2 (tiết kiệm RAM, giảm
//   1 vòng handshake WebSocket mỗi lần mở trang ⇒ bớt lag).
//
// VÌ SAO KHÔNG SINGLETON:
//   userId lấy từ UserSessionService (scoped theo circuit) ⇒ không dùng chung được
//   giữa các circuit (sẽ lẫn thông báo của người dùng này sang người dùng khác).
builder.Services.AddScoped<PetShop.Services.RealtimeHubService>();
builder.Services.AddScoped<PetShop.Services.ChatPopupState>();
builder.Services.AddScoped<IStaffRepository, StaffRepository>();
builder.Services.AddScoped<IStaffService, StaffService>();
builder.Services.AddScoped<IStaffDashboardService, StaffDashboardService>();
builder.Services.AddScoped<IStaffProfileService, StaffProfileService>();
// LS-13: 3-layer cho màn hình quản lý sản phẩm + báo cáo/dashboard nhân viên.
builder.Services.AddScoped<IProductManagementRepository, ProductManagementRepository>();
builder.Services.AddScoped<IProductManagementService, ProductManagementService>();
builder.Services.AddScoped<IStaffReportsRepository, StaffReportsRepository>();
builder.Services.AddScoped<IStaffReportsService, StaffReportsService>();
// Tín hiệu làm mới dữ liệu cho staff workspace (đồng hồ "Làm mới: 30s" gọi thật).
builder.Services.AddScoped<PetShop.Services.StaffRefreshSignal>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "PetShop API", Version = "v1" });
    options.OperationFilter<FormFileOperationFilter>();
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendDev", policy =>
    {
        policy
            .WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var dbConnectionString = builder.Configuration.GetConnectionString("DBDefault")
    ?? builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(dbConnectionString))
{
    throw new InvalidOperationException(
        "Missing connection string 'ConnectionStrings:DBDefault' or 'ConnectionStrings:DefaultConnection'. " +
        "Add it to appsettings.json/appsettings.Development.json, or set environment variable 'ConnectionStrings__DBDefault'.");
}

// ---------------------------------------------------------------------------
// TỐI ƯU RAM — DbContext cho vòng đời circuit Blazor.
//
// VÌ SAO CẦN:
//   Trong Blazor Server, service SCOPED sống suốt vòng đời của circuit
//   (một tab người dùng — có thể vài giờ). Scoped DbContext giữ ChangeTracker
//   của mọi entity đã đọc trong suốt vòng đời đó ⇒ RAM TĂNG DẦN theo thời gian
//   người dùng mở trang (mỗi lần đọc 1 entity là 1 entry bị giữ lại).
//
// CÁCH KHẮC PHỤC:
//   Mặc định QUERY TRACKING = NoTracking cho scoped context ⇒ các truy vấn
//   chỉ-đọc không tạo entry trong ChangeTracker.
//
//   AN TOÀN: các đường GHI đều dùng .Add()/.Update()/.Remove() (EF tự BẬT lại
//   tracking cho entity được attach) hoặc đã được đánh dấu .AsTracking() tường minh.
//   ⇒ hành vi ghi/dữ liệu KHÔNG đổi.
// ---------------------------------------------------------------------------
builder.Services.AddDbContext<PetShop.Data.ShopPetDatabaseContext>(options =>
    options.UseSqlServer(dbConnectionString, sql => sql.CommandTimeout(30))
        // Tắt tracking mặc định ⇒ ChangeTracker không phình theo vòng đời circuit.
        .UseQueryTrackingBehavior(Microsoft.EntityFrameworkCore.QueryTrackingBehavior.NoTracking));

// IDbContextFactory: cho phép mỗi thao tác truy vấn dùng context riêng.
//
// VÌ SAO CẦN:
//   Blazor Server render layout + page trong cùng scoped DI ⇒ các repository
//   dùng chung một DbContext scoped có thể chạy truy vấn SONG SONG ⇒ EF Core
//   ném "A second operation was started on this context instance before a
//   previous operation completed".
//
//   Các thao tác chỉ ĐỌC (báo cáo/dashboard/hồ sơ) nên chuyển sang factory
//   là an toàn và loại bỏ hoàn toàn lỗi concurrency.
//
//   Lifetime = Scoped (không phải Singleton) vì DbContextOptions mặc định là
//   scoped — factory singleton sẽ gây lỗi validation của DI container.
//   Bù lại, mỗi lần gọi CreateDbContextAsync vẫn tạo context RIÊNG ⇒ không
//   còn xung đột concurrency detector.
builder.Services.AddDbContextFactory<PetShop.Data.ShopPetDatabaseContext>(
    options => options.UseSqlServer(dbConnectionString, sql => sql.CommandTimeout(30))
        .UseQueryTrackingBehavior(Microsoft.EntityFrameworkCore.QueryTrackingBehavior.NoTracking),
    ServiceLifetime.Scoped);

// ---------------------------------------------------------------------------
// Session — chỉ dùng cho giỏ hàng server-side (CartController).
//
// TỐI ƯU RAM:
//   IdleTimeout mặc định 8 giờ ⇒ mỗi phiên session giữ dữ liệu trong
//   DistributedMemoryCache (RAM) suốt 8 giờ kể cả khi người dùng đã đóng trình
//   duyệt ⇒ cache phình dần theo số phiên.
//   Giảm xuống 2 giờ: đủ cho luồng mua hàng (thanh toán PayOS có thể mất vài
//   phút), nhưng dữ liệu được giải phóng nhanh hơn nhiều.
// ---------------------------------------------------------------------------
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;

    // ⚠️ TỐI ƯU RAM (11/11/2026): 2 giờ → 30 phút.
    //   Session data nằm trong DistributedMemoryCache (RAM của server) và bị giữ
    //   cho tới khi hết IdleTimeout, KỂ CẢ khi user đã đóng trình duyệt.
    //   30 phút là đủ cho toàn bộ luồng mua hàng (chọn dịch vụ → thanh toán
    //   PayOS → xác nhận), nhưng dữ liệu được giải phóng gấp 4 lần nhanh hơn
    //   ⇒ bớt "phình" RAM khi mở nhiều tab / test nhiều luồng.
    options.IdleTimeout = TimeSpan.FromMinutes(30);
});

builder.Services.AddScoped<PetShop.Interfaces.IProductRepository, PetShop.Repositories.ProductRepository>();
builder.Services.AddScoped<PetShop.Interfaces.IProductService, PetShop.Services.ProductService>();
// ProductNameCache — SINGLETON (dùng chung cho mọi circuit Blazor).
//
// VÌ SAO singleton:
//   Danh sách tên sản phẩm là dữ liệu ĐỌC-ONLY, giống nhau cho mọi người dùng.
//   Nếu để scoped ⇒ mỗi circuit (mỗi tab) giữ 1 bản riêng trong RAM ⇒
//   mở 10 tab là tốn 10 bản. Singleton ⇒ toàn server chỉ giữ 1 bản ⇒ tiết kiệm RAM.
//   Bên trong có SemaphoreSlim để chống "thundering herd" khi nhiều request cùng hết hạn.
builder.Services.AddSingleton<PetShop.Services.ProductNameCache>();
builder.Services.AddScoped<PetShop.Interfaces.IAuthRepository, PetShop.Repositories.AuthRepository>();
builder.Services.AddSingleton<PetShop.Services.GoogleOAuthStateStore>();
builder.Services.AddScoped<PetShop.Services.GoogleOAuthCallbackService>();
builder.Services.AddScoped<PetShop.Interfaces.IAuthService, PetShop.Services.AuthService>();
builder.Services.AddScoped<PetShop.Interfaces.IEmailService, PetShop.Services.EmailService>();
builder.Services.AddScoped<PetShop.Interfaces.IPetServiceRepository, PetShop.Repositories.PetServiceRepository>();
builder.Services.AddScoped<PetShop.Interfaces.IPetServiceService, PetShop.Services.PetServiceService>();
builder.Services.AddScoped<PetShop.Interfaces.ISpaBookingRepository, PetShop.Repositories.SpaBookingRepository>();
builder.Services.AddScoped<PetShop.Interfaces.ISpaBookingService, PetShop.Services.SpaBookingService>();
builder.Services.AddScoped<PetShop.Interfaces.IReviewRepository, PetShop.Repositories.ReviewRepository>();
builder.Services.AddScoped<PetShop.Interfaces.IAttendanceService, PetShop.Services.AttendanceService>();
builder.Services.AddScoped<PetShop.Interfaces.IPayrollService, PetShop.Services.PayrollService>();
builder.Services.AddScoped<PetShop.Interfaces.IBoardingRepository, PetShop.Repositories.BoardingRepository>();
builder.Services.AddScoped<PetShop.Interfaces.IBoardingService, PetShop.Services.BoardingService>();

// ---------------------------------------------------------------------------
// LS-09 — gRPC (slide PRN232 Ch09):
//   - AddGrpc() đăng ký server gRPC + DI cho các service kế thừa PetCatalogBase.
//   - GrpcPetCatalogService là 3-layer: chỉ gọi IProductService, không inject DbContext.
//   - GrpcPetCatalogClient là client demo (Grpc.Net.Client) cho 4 loại RPC.
// ---------------------------------------------------------------------------
builder.Services.AddGrpc();
builder.Services.AddScoped<PetShop.Services.GrpcPetCatalogService>();

// GrpcPetCatalogClient — SINGLETON (trước đây là Scoped).
//
// ⚠️ TỐI ƯU RAM (11/11/2026):
//   `GrpcPetCatalogClient` tạo 1 `GrpcChannel` — tức 1 kết nối HTTP/2 (có
//   buffer đọc/ghi + connection pool riêng) — trong constructor.
//
//   Đăng ký SCOPED ⇒ mỗi circuit Blazor (mỗi tab người dùng) tạo RIÊNG một
//   channel tới chính tiến trình này ⇒ mở 10 tab là 10 kết nối HTTP/2 + 10
//   bộ buffer, trong khi channel được thiết kế để DÙNG LẠI cho nhiều RPC.
//
//   SINGLETON ⇒ toàn server chỉ giữ 1 channel ⇒ tiết kiệm RAM + bớt handshake.
//   An toàn: `GrpcChannel` là thread-safe và được thiết kế cho mục đích này;
//   client này không giữ state theo user (chỉ truyền keyword/categoryId).
builder.Services.AddSingleton<PetShop.Services.GrpcPetCatalogClient>();

// ---------------------------------------------------------------------------
// LS-11 — SignalR (realtime notification + chat):
//   - AddSignalR() đăng ký server SignalR (package server-side đã có sẵn trong
//     Microsoft.NET.Sdk.Web, KHÔNG thêm package riêng).
//   - NotificationHub nhóm theo user ("user:{userId}") — xem Hubs/NotificationHub.cs.
//   - RealtimeNotificationService là tầng service của luồng 3-layer: controller/
//     service gọi qua interface IRealtimeNotificationService, không inject DbContext
//     vào component Razor.
// ---------------------------------------------------------------------------
// TỐI ƯU RAM:
//   MaximumReceiveMessageSize mặc định = 32 KB ⇒ mỗi connection giữ buffer tối đa
//   32 KB. Giảm xuống 8 KB (payload SignalR ở đây chỉ là DTO nhỏ) ⇒ mỗi WebSocket
//   nhỏ hơn, nhiều connection hơn trong cùng giới hạn RAM.
//
//   KeepAliveInterval tăng lên ⇒ ít gói ping hơn ⇒ ít wake-up của thread pool
//   ⇒ giảm CPU (quan trọng với máy local yếu).
builder.Services.AddSignalR(options =>
{
    // ⚠️ SỬA BUG NGHIÊM TRỌNG (11/11/2026) — KHÔNG được siết option này.
    //
    // Gốc rễ của lỗi "Connection disconnected ... Connection closed with an error."
    // là MaximumReceiveMessageSize = 8 KB.
    //
    // VÌ SAO NGUY HIỂM:
    //   AddSignalR(options => ...) đặt option cho TOÀN BỘ hub SignalR trong app.
    //   Circuit Blazor Server CŨNG đi qua đúng hệ thống SignalR này (Blazor Server
    //   dùng hub nội bộ /blazor trên cùng pipeline).
    //
    //   => Blazor Server gửi render batch (render tree) qua chính connection đó.
    //   => Trang có DOM/component lớn (home ~37 KB HTML, staff dashboard bảng báo cáo)
    //      => render batch VƯỢT 8 KB => server CHỦ ĐỘNG CẮT connection với lỗi
    //      "Connection closed with an error."
    //   => Toàn bộ UI Blazor Server chết => mọi nút bấm không phản hồi => buộc F5.
    //
    // => Đây là NGUYÊN NHÂN GỐC, không phải hàng đợi render batch.
    //
    // ĐỔI LẠI: RAM tăng một chút, nhưng UI KHÔNG CHẾT — đổi lại nhiều hơn so với
    // việc user phải F5 liên tục.
    options.MaximumReceiveMessageSize = 64 * 1024;
    // Mặc định 15s — ping đều để phát hiện connection chết sớm.
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);

    // TỐI ƯU RAM — buffer cho mỗi connection SignalR.
    //
    //   StreamBufferCapacity = 4 KB (mặc định 4 KB ở .NET 8, đặt rõ để chắc chắn).
    //   ⇒ mỗi WebSocket giữ buffer nhỏ ⇒ nhiều connection hơn trong cùng RAM.
    //
    //   MaximumParallelInvocationsPerClient = 1 (mặc định 1):
    //   ⇒ một client không thể chạy nhiều hub method song song ⇒ tránh việc
    //   nhiều task cùng giữ state trong RAM.
    //
    //   ClientTimeoutInterval = 60s: nếu client im lặng quá 60s thì server
    //   đóng connection ⇒ giải phóng buffer + scope server-side.
    options.StreamBufferCapacity = 4 * 1024;
    options.MaximumParallelInvocationsPerClient = 1;
    // Mặc định 30s. Nới lên 2 phút cho phép circuit sống lâu hơn khi máy chậm
    // hoặc tab chạy nền (điều hướng SPA làm máy bận, ping có thể trễ).
    options.ClientTimeoutInterval = TimeSpan.FromMinutes(2);
});
builder.Services.AddScoped<PetShop.Interfaces.IRealtimeNotificationService, PetShop.Services.RealtimeNotificationService>();

// ---------------------------------------------------------------------------
// Chatbot AI (slide LS-08 — port từ OJT):
//   - ChatbotOptions: đọc khối "Chatbot" trong appsettings.json.
//   - IAiProviderService: chỉ Gemini (quyết định của chủ dự án, không port Ollama).
//     Dùng AddHttpClient để có timeout lấy từ Chatbot:Providers:gemini:TimeoutSeconds.
//   - IChatbotService: logic intent → context → system prompt → Gemini.
//   - Truy vấn DB của chatbot đi qua IChatbotRepository (3-layer, không truy vấn trực tiếp trong service).
// ---------------------------------------------------------------------------
builder.Services.Configure<PetShop.Models.ChatbotOptions>(
    builder.Configuration.GetSection(PetShop.Models.ChatbotOptions.SectionName));
builder.Services.AddScoped<PetShop.Interfaces.IChatbotRepository, PetShop.Repositories.ChatbotRepository>();
builder.Services.AddHttpClient<PetShop.Interfaces.IAiProviderService, PetShop.Services.GeminiProviderService>();
// Cooldown của nhà cung cấp AI phải DÙNG CHUNG (singleton) vì GeminiProviderService
// là transient (AddHttpClient) ⇒ lưu cờ trong instance sẽ mất sau mỗi request.
// Mục đích: khi Gemini trả 429 (hết quota) thì các lần gọi sau trả lỗi NGAY,
// không đốt thêm quota và không khiến user chờ 30-120s cho một lỗi chắc chắn.
builder.Services.AddSingleton<PetShop.Services.AiProviderCooldown>();
// Cache intent (singleton, TTL 15 phút, tối đa 100 mục) ⇒ câu hỏi lặp lại
// không tốn thêm 1 request Gemini (free tier chỉ 20 request/ngày).
builder.Services.AddSingleton<PetShop.Services.ChatbotIntentCache>();
builder.Services.AddScoped<PetShop.Interfaces.IChatbotService, PetShop.Services.ChatbotService>();

// ---------------------------------------------------------------------------
// Thông tin liên hệ của shop — đọc từ bảng SystemSettings (KHÔNG hardcode).
// Dùng chung cho Footer / TopBar / Contact / Chatbot ⇒ 1 nguồn sự thật duy nhất.
// ---------------------------------------------------------------------------
// ⚠️ TỐI ƯU RAM (11/11/2026): giữ Scoped — KHÔNG đổi sang Singleton.
//
//   Đã thử đổi sang Singleton để mỗi circuit không giữ bản cache riêng, nhưng
//   `ShopContactRepository` phụ thuộc `IDbContextFactory<ShopPetDatabaseContext>`
//   được đăng ký với Lifetime = Scoped (vì `DbContextOptions` mặc định scoped)
//   ⇒ DI container ném: "Cannot consume scoped service from singleton".
//
//   ⇒ Giữ Scoped. Lượng RAM tiết kiệm được từ cache này rất nhỏ (1 dictionary
//     4 phần tử + 1 SemaphoreSlim mỗi circuit) — không đáng để phá cấu trúc DI.
//     Phần RAM lớn đã được xử lý ở GC / CircuitOptions / Session / gRPC channel.
builder.Services.AddScoped<PetShop.Interfaces.IShopContactRepository, PetShop.Repositories.ShopContactRepository>();
builder.Services.AddScoped<PetShop.Services.ShopContactService>();

builder.Services.Configure<PetShop.Models.PayOSOptions>(
    builder.Configuration.GetSection(PetShop.Models.PayOSOptions.SectionName));
builder.Services.AddHttpClient<PetShop.Interfaces.IPayOSService, PetShop.Services.PayOSService>()
    .ConfigureHttpClient((sp, client) =>
    {
        var payosOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PetShop.Models.PayOSOptions>>().Value;
        client.DefaultRequestHeaders.Add("x-client-id", payosOptions.ClientId);
        client.DefaultRequestHeaders.Add("x-api-key", payosOptions.ApiKey);
        client.Timeout = TimeSpan.FromSeconds(30);
    });

// Đối soát tự động các giao dịch PayOS đang "pending".
//
// VÌ SAO CẦN: webhook PayOS gửi về URL mà merchant đăng ký. Ở môi trường local
// URL đó là http://localhost:5286/api/payos/webhook ⇒ PayOS (ngoài internet) KHÔNG
// gọi tới được ⇒ đơn đã trả tiền thật mãi kẹt ở "Chờ thanh toán PayOS".
// Service này hỏi PayOS trực tiếp (server → server) mỗi 60s và áp dụng CHUNG
// đường xử lý của webhook để cập nhật payment + booking + gửi email hoá đơn.
builder.Services.AddHostedService<PetShop.Services.PayOSReconciliationService>();

// ---------------------------------------------------------------------------
// LS-09 — Kestrel: bật HTTP/2 (h2c) để phục vụ gRPC.
//
// gRPC bắt buộc HTTP/2. Với cổng không TLS (h2c), Kestrel chỉ chấp nhận HTTP/2
// prior-knowledge khi endpoint được khai báo Protocols = Http2 — nếu để
// Http1AndHttp2 thì Kestrel trả lỗi "HTTP_1_1_REQUIRED" cho client gRPC.
//
// Vì vậy tách 2 cổng:
//   - 5286 : HTTP/1.1            — phục vụ web (Blazor) và REST API.
//   - 5287 : HTTP/2 thuần (h2c)  — phục vụ gRPC (client demo dùng cổng này).
// Endpoint HTTPS mặc định đã dùng HTTP/2 qua ALPN nên không cần cấu hình thêm.
// ---------------------------------------------------------------------------
builder.WebHost.ConfigureKestrel(options =>
{
    // -----------------------------------------------------------------------
    // TỐI ƯU RAM — giới hạn tài nguyên Kestrel.
    //
    // Mặc định Kestrel cho phép connection không giới hạn và buffer request
    // 30 MB ⇒ vài trình duyệt mở nhiều tab là đủ để đẩy RSS lên vài trăm MB.
    // Trên máy local (16 GB, đang chạy Cursor + trình duyệt), giới hạn lại
    // là biện pháp đơn giản mà hiệu quả nhất.
    // -----------------------------------------------------------------------

    // Tối đa 100 connection đồng thời (mỗi connection ~ vài chục KB buffer).
    options.Limits.MaxConcurrentConnections = 100;

    // Buffer header tối đa 32 KB (mặc định 32 KB — giữ nguyên).
    options.Limits.MaxRequestHeadersTotalSize = 32 * 1024;

    // Body tối đa 10 MB (mặc định 30 MB) — đủ cho upload ảnh sản phẩm.
    options.Limits.MaxRequestBodySize = 10 * 1024 * 1024;

    // Header tối đa 16 KB (mặc định 32 KB).
    options.Limits.MaxRequestHeaderCount = 32;

    options.ListenAnyIP(5286, listen =>
    {
        listen.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1;
    });

    options.ListenAnyIP(5287, listen =>
    {
        listen.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
    });

    // Cổng HTTPS của launch profile "https" (https://localhost:7083).
    //
    // VÌ SAO CẦN:
    // launchSettings.json khai báo profile "https" với applicationUrl
    // "https://localhost:7083;http://localhost:5286", nhưng ConfigureKestrel
    // GHI ĐÈ (override) toàn bộ địa chỉ của launch profile ⇒ cổng 7083 không
    // bao giờ listen ⇒ trình duyệt mở https://localhost:7083 báo
    // ERR_CONNECTION_REFUSED.
    //
    // VÌ SAO KHÔNG DÙNG app.UseHttpsRedirection():
    //   - Cổng HTTP 5286 là cổng chính đã đăng ký Google OAuth
    //     (redirect_uris + javascript_origins = http://localhost:5286).
    //     Redirect ⇒ origin GSI không còn khớp ClientId ⇒ login Google hỏng.
    //   - Self-HTTP client nội bộ sẽ phải đi theo redirect sang cert dev tự ký.
    //   - SignalR/WebSocket bị đổi scheme.
    // ⇒ Cổng 7083 phục vụ TRỰC TIẾP, không redirect hai chiều.
    //
    // Dev cert: tạo & tin cậy bằng  `dotnet dev-certs https --trust`.
    var httpsCert = TryFindLocalhostDevCertificate();
    if (httpsCert is not null)
    {
        options.ListenAnyIP(7083, listen =>
        {
            listen.UseHttps(httpsCert);
            listen.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2;
        });
    }
    else
    {
        Console.WriteLine(
            "[Kestrel] Bỏ qua cổng HTTPS 7083: không tìm thấy dev cert cho CN=localhost. " +
            "Chạy 'dotnet dev-certs https --trust' rồi restart để dùng cổng HTTPS.");
    }
});

// Tìm dev certificate CN=localhost trong store CurrentUser\My (do
// `dotnet dev-certs https --trust` tạo). Trả về null nếu không có.
static System.Security.Cryptography.X509Certificates.X509Certificate2? TryFindLocalhostDevCertificate()
{
    try
    {
        using var store = new System.Security.Cryptography.X509Certificates.X509Store(
            System.Security.Cryptography.X509Certificates.StoreName.My,
            System.Security.Cryptography.X509Certificates.StoreLocation.CurrentUser);
        store.Open(System.Security.Cryptography.X509Certificates.OpenFlags.ReadOnly);

        var now = DateTime.Now;
        var candidates = store.Certificates
            .Where(c => c.HasPrivateKey
                        && c.NotBefore <= now && now <= c.NotAfter
                        && c.Subject.Contains("localhost", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.NotAfter)
            .ToList();

        if (candidates.Count == 0) return null;

        // Cần exportable/ephemeral key cho Linux + Kestrel.
        return new System.Security.Cryptography.X509Certificates.X509Certificate2(
            candidates[0].Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx));
    }
    catch
    {
        return null;
    }
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    // Bảng route OData tĩnh: giúp tra cứu nhanh các URL hợp lệ của /PetShop/*
    // (xem file PRN232_PLAN.md, Phase 3 — OData).
    app.UseODataRouteDebug();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// KHÔNG dùng app.UseHttpsRedirection() ở đây.
//
// Lý do: cổng HTTP 5286 là cổng chính mà Google OAuth đã đăng ký
// (redirect URI + javascript origin = http://localhost:5286) và cũng là cổng
// mà các self-HTTP client nội bộ gọi tới. Nếu bật HttpsRedirection, mọi
// request HTTP sẽ bị 307 sang https://localhost:7083 ⇒
//   - Origin của Google Identity Services không còn khớp ClientId đã đăng ký.
//   - Self-HTTP client phải theo redirect sang cert dev tự ký.
//   - Blazor SignalR/WebSocket bị đổi scheme.
//
// Thay vào đó: cổng 7083 (HTTPS) phục vụ trực tiếp, không redirect hai chiều.
// Người dùng có thể mở http://localhost:5286 hoặc https://localhost:7083.
app.UseResponseCompression();
app.UseStaticFiles();
app.UseCors("FrontendDev");
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Chuẩn hoá mọi lỗi phát sinh thành ProblemDetails (RFC 7807) cho cả API lẫn Blazor.
app.UseMiddleware<ApiExceptionMiddleware>();

app.Use(async (context, next) =>
{
    var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(forwardedFor))
    {
        context.Items["RealClientIp"] = forwardedFor.Split(',')[0].Trim();
    }

    await next();
});

// Rewrite luồng Google OAuth callback (redirect_uri đăng ký = gốc origin).
// Xem Middleware/GoogleOAuthCallbackRewriteMiddleware.cs.
app.Use(async (context, next) =>
{
    var mw = new GoogleOAuthCallbackRewriteMiddleware(
        next, builder.Configuration, context.RequestServices.GetRequiredService<GoogleOAuthCallbackService>());
    await mw.InvokeAsync(context);
});

app.MapControllers();

// LS-09 — đăng ký endpoint gRPC (PetCatalog): /PetCatalog/GetProducts, /PetCatalog/StreamProducts,
// /PetCatalog/UploadProducts, /PetCatalog/SyncProducts.
app.MapGrpcService<PetShop.Services.GrpcPetCatalogService>();

// LS-11 — đăng ký endpoint SignalR (realtime notification + chat): /hubs/notification.
// Hub dùng chung AddAuthentication của app; client Blazor kết nối qua WebSocket
// trên cổng 5286 (HTTP/1.1).
app.MapHub<PetShop.Hubs.NotificationHub>("/hubs/notification");

app.MapRazorComponents<PetShop_PRN222.Components.App>()
    .AddInteractiveServerRenderMode();

app.MapControllerRoute(
    name: "default",
    pattern: "mvc/{controller=ProductsPage}/{action=Index}/{id?}");

app.Run();

/// <summary>
/// Đánh dấu lớp <c>Program</c> của top-level statements là <c>public</c> để
/// test project có thể dùng <c>WebApplicationFactory&lt;Program&gt;</c> —
///
/// <para>
/// Mục đích: chạy <b>integration test HTTP thật</b> cho luồng quên mật khẩu
/// (POST /api/auth/send-email-otp → verify-email-otp → reset-password) mà không
/// cần mở app thật. Test như vậy bắt được lỗi thật (route sai, validation
/// sinh 409, response sai shape) thay vì chỉ assert trên source code.
/// </para>
///
/// <para>
/// Không ảnh hưởng runtime: top-level statements vẫn sinh entry point như cũ.
/// </para>
/// </summary>
public partial class Program
{
}

public sealed class FormFileOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var formFileParams = context.ApiDescription.ActionDescriptor.Parameters
            .OfType<Microsoft.AspNetCore.Mvc.Abstractions.ParameterDescriptor>()
            .Where(p => p.ParameterType == typeof(IFormFile))
            .ToList();

        if (formFileParams.Count == 0)
        {
            foreach (var param in operation.Parameters)
            {
                if (param.Schema is not null && param.Schema.Format == "binary")
                {
                    param.Schema.Type = "string";
                    param.Schema.Format = "binary";
                }
            }

            return;
        }

        operation.RequestBody = new OpenApiRequestBody
        {
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["multipart/form-data"] = new OpenApiMediaType
                {
                    Schema = new OpenApiSchema
                    {
                        Type = "object",
                        Properties = formFileParams.ToDictionary(
                            p => p.Name ?? "file",
                            _ => new OpenApiSchema { Type = "string", Format = "binary" }),
                        Required = new HashSet<string>(formFileParams.Select(p => p.Name ?? "file"))
                    }
                }
            }
        };

        operation.Parameters.Clear();
    }
}
