using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using PetShop.Interfaces;
using PetShop.Models;
using PetShop.Repositories;
using PetShop.Services;
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

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options => options.DetailedErrors = true);

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

builder.Services.AddAuthorization();

// Typed HttpClient trỏ về chính API của ứng dụng (xem Services/SelfHttpClientExtensions.cs).
builder.Services.AddSelfHttpClient<PetShop.Interfaces.IProductCatalogClient, PetShop.Services.ProductCatalogClient>();
builder.Services.AddSelfHttpClient<PetShop.Interfaces.IAuthClient, PetShop.Services.AuthClient>();
builder.Services.AddSelfHttpClient<PetShop.Interfaces.ICheckoutClient, PetShop.Services.CheckoutClient>();
builder.Services.AddSelfHttpClient<PetShop.Interfaces.IOrderClient, PetShop.Services.OrderClient>();
builder.Services.AddSelfHttpClient<PetShop.Interfaces.IReviewClient, PetShop.Services.ReviewClient>();
builder.Services.AddSelfHttpClient<PetShop.Interfaces.IPaymentClient, PetShop.Services.PaymentClient>();
builder.Services.AddSelfHttpClient<PetShop.Interfaces.IPetServiceClient, PetShop.Services.PetServiceClient>();

// Hai client dưới đây cần header phiên đăng nhập (X-User-Id / X-Customer-Id / X-User-Role).
builder.Services.AddTransient<PetShop.Services.SpaAuthHeaderHandler>();
builder.Services.AddSelfHttpClientWithAuth<PetShop.Interfaces.ISpaBookingClient, PetShop.Services.SpaBookingClient>();
builder.Services.AddSelfHttpClientWithAuth<PetShop.Interfaces.IPetsClient, PetShop.Services.PetsClient>();
builder.Services.AddScoped<PetShop.Services.UserSessionService>();
builder.Services.AddScoped<PetShop.Services.CartStateService>();
builder.Services.AddScoped<IStaffRepository, StaffRepository>();
builder.Services.AddScoped<IStaffService, StaffService>();
builder.Services.AddScoped<IStaffDashboardService, StaffDashboardService>();
builder.Services.AddScoped<IStaffProfileService, StaffProfileService>();

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

builder.Services.AddDbContext<PetShop.Data.ShopPetDatabaseContext>(options =>
    options.UseSqlServer(dbConnectionString));

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.IdleTimeout = TimeSpan.FromHours(8);
});

builder.Services.AddScoped<PetShop.Interfaces.IProductRepository, PetShop.Repositories.ProductRepository>();
builder.Services.AddScoped<PetShop.Interfaces.IProductService, PetShop.Services.ProductService>();
builder.Services.AddScoped<PetShop.Interfaces.IAuthRepository, PetShop.Repositories.AuthRepository>();
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

app.UseHttpsRedirection();
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

app.MapControllers();

app.MapRazorComponents<PetShop_PRN222.Components.App>()
    .AddInteractiveServerRenderMode();

app.MapControllerRoute(
    name: "default",
    pattern: "mvc/{controller=ProductsPage}/{action=Index}/{id?}");

app.Run();

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
