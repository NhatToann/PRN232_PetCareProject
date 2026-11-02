using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PetShop.Services;
using System.Security.Claims;

namespace PetShop.Tests;

/// <summary>
/// Unit test cho <see cref="JwtTokenService"/> — Phase 4 (JWT Bearer).
///
/// Hành vi cần bảo vệ:
///  - Token phát hành chứa đủ claim: userId, name, email, role.
///  - Token hợp lệ thì ValidateToken trả true và principal có đúng role.
///  - Token sai chữ ký / hết hạn thì ValidateToken trả false (không ném exception).
///  - Khoá ký quá ngắn hoặc rỗng thì ném InvalidOperationException với thông báo rõ ràng.
/// </summary>
public class JwtTokenServiceTests
{
    private const string ValidKey = "petshop-prn232-unit-test-signing-key-32bytes-min";

    private static JwtTokenService CreateService(string key = ValidKey, int expiresInMinutes = 60)
    {
        var settings = Options.Create(new JwtSettings
        {
            Issuer = "PetShop",
            Audience = "PetShopClients",
            Key = key,
            ExpiresInMinutes = expiresInMinutes
        });

        return new JwtTokenService(settings, NullLogger<JwtTokenService>.Instance);
    }

    [Fact]
    public void CreateToken_TokenChuaDayDuClaim()
    {
        var service = CreateService();

        var token = service.CreateToken(42, "Nguyễn Văn A", "a@petshop.com", "staff");

        Assert.False(string.IsNullOrWhiteSpace(token));
        // Token JWT có 3 phần: header.payload.signature
        Assert.Equal(3, token.Split('.').Length);
    }

    [Fact]
    public void ValidateToken_TokenHopLe_TraVePrincipalDungThongT()
    {
        var service = CreateService();

        var token = service.CreateToken(7, "Lê Thị B", "b@petshop.com", "staff");
        var ok = service.ValidateToken(token, out var principal);

        Assert.True(ok);
        Assert.NotNull(principal);
        Assert.Equal("7", principal!.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal("staff", principal.FindFirst(ClaimTypes.Role)?.Value);
        Assert.Equal("b@petshop.com", principal.FindFirst(ClaimTypes.Email)?.Value);
    }

    [Fact]
    public void ValidateToken_TokenSaiChuKy_TraVeFalse()
    {
        var issuer = CreateService(ValidKey);
        var other = CreateService("mot-khoa-khac-hoan-toan-khac-32bytes");

        var token = issuer.CreateToken(1, "A", "a@x.com", "customer");
        var ok = other.ValidateToken(token, out var principal);

        Assert.False(ok);
        Assert.Null(principal);
    }

    [Fact]
    public void ValidateToken_TokenRong_TraVeFalseKhongNemException()
    {
        var service = CreateService();

        Assert.False(service.ValidateToken(string.Empty, out _));
        Assert.False(service.ValidateToken("   ", out _));
        Assert.False(service.ValidateToken("khong-phai-jwt", out _));
    }

    [Fact]
    public void ValidateToken_TokenHetHan_TraVeFalse()
    {
        // ClockSkew mặc định của service là 1 phút, nên token phải hết hạn
        // lâu hơn thế mới bị từ chối.
        var service = CreateService(ValidKey, expiresInMinutes: 120);

        var token = service.CreateToken(
            1, "A", "a@x.com", "customer", TimeSpan.FromMinutes(-10));

        Assert.False(service.ValidateToken(token, out _));
    }

    [Fact]
    public void CreateToken_KhoaRong_NemInvalidOperationException()
    {
        var service = CreateService(string.Empty);

        var ex = Assert.Throws<InvalidOperationException>(
            () => service.CreateToken(1, "A", "a@x.com", "customer"));

        Assert.Contains("JwtSettings", ex.Message);
    }

    [Fact]
    public void CreateToken_KhoaNganHon32KyTu_NemInvalidOperationException()
    {
        var service = CreateService("short-key");

        var ex = Assert.Throws<InvalidOperationException>(
            () => service.CreateToken(1, "A", "a@x.com", "customer"));

        Assert.Contains("32", ex.Message);
    }

    [Fact]
    public void GetPrincipalFromToken_TokenHopLe_DocDuocClaims()
    {
        var service = CreateService();

        var token = service.CreateToken(99, "Nguyễn Văn C", "c@petshop.com", "doctor");
        var principal = service.GetPrincipalFromToken(token);

        Assert.NotNull(principal);
        Assert.Contains(principal!.Claims, c => c.Value == "c@petshop.com");
    }
}
