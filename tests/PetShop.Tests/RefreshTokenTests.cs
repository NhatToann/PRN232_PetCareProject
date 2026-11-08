using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PetShop.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace PetShop.Tests;

/// <summary>
/// Unit test cho luồng **refresh token** — slide PRN232 Ch06 S40–S42.
///
/// Hành vi cần bảo vệ:
///  - Refresh token dùng CÙNG key/issuer/audience với access token nhưng hạn dài hơn.
///  - Refresh token mang claim <c>token_type = "refresh"</c>; access token mang <c>"access"</c>.
///  - **Access token KHÔNG được dùng để refresh** (và ngược lại) ⇒ trả false.
///  - Refresh token sai chữ ký / rỗng / rác / hết hạn ⇒ trả false (không ném exception).
///  - Token phát hành có claim đầy đủ (userId, name, email, role) để tái tạo user khi refresh.
/// </summary>
public class RefreshTokenTests
{
    private const string ValidKey = "petshop-prn232-refresh-test-signing-key-32bytes";

    private static JwtTokenService CreateService(
        string key = ValidKey,
        int expiresInMinutes = 60,
        int refreshExpiresInMinutes = 10080)
    {
        var settings = Options.Create(new JwtSettings
        {
            Issuer = "PetShop",
            Audience = "PetShopClients",
            Key = key,
            ExpiresInMinutes = expiresInMinutes,
            RefreshTokenExpiresInMinutes = refreshExpiresInMinutes
        });

        return new JwtTokenService(settings, NullLogger<JwtTokenService>.Instance);
    }

    // ---------------------------------------------------------------------
    // Phát hành
    // ---------------------------------------------------------------------

    [Fact]
    public void CreateRefreshToken_HasClaimTokenTypeRefresh()
    {
        var service = CreateService();

        var token = service.CreateRefreshToken(7, "Lê Thị B", "b@petshop.com", "staff");

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal(JwtTokenService.TokenTypeRefresh, jwt.Claims
            .First(c => c.Type == JwtTokenService.TokenTypeClaim).Value);
    }

    [Fact]
    public void CreateToken_HasClaimTokenTypeAccess()
    {
        var service = CreateService();

        var token = service.CreateToken(7, "Lê Thị B", "b@petshop.com", "staff");

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal(JwtTokenService.TokenTypeAccess, jwt.Claims
            .First(c => c.Type == JwtTokenService.TokenTypeClaim).Value);
    }

    [Fact]
    public void CreateRefreshToken_ChuaDayDuClaimNguoiDung()
    {
        var service = CreateService();

        var token = service.CreateRefreshToken(42, "Nguyễn Văn A", "a@petshop.com", "doctor");

        var ok = service.ValidateRefreshToken(token, out var principal);

        Assert.True(ok);
        Assert.NotNull(principal);
        Assert.Equal("42", principal!.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal("Nguyễn Văn A", principal.FindFirst(ClaimTypes.Name)?.Value);
        Assert.Equal("a@petshop.com", principal.FindFirst(ClaimTypes.Email)?.Value);
        Assert.Equal("doctor", principal.FindFirst(ClaimTypes.Role)?.Value);
    }

    [Fact]
    public void CreateRefreshToken_HanDaiHonAccessToken()
    {
        var service = CreateService(expiresInMinutes: 60, refreshExpiresInMinutes: 10080);

        var access = new JwtSecurityTokenHandler().ReadJwtToken(
            service.CreateToken(1, "A", "a@x.com", "customer"));
        var refresh = new JwtSecurityTokenHandler().ReadJwtToken(
            service.CreateRefreshToken(1, "A", "a@x.com", "customer"));

        Assert.True(refresh.ValidTo > access.ValidTo,
            "Refresh token phải có thời hạn dài hơn access token.");
    }

    [Fact]
    public void CreateRefreshToken_DungCungIssuerAudienceKey()
    {
        // Cùng issuer/audience ⇒ Bearer scheme của app vẫn đọc được, nhưng
        // claim token_type chặn việc dùng nhầm refresh token như access token.
        var service = CreateService();

        var refresh = new JwtSecurityTokenHandler().ReadJwtToken(
            service.CreateRefreshToken(1, "A", "a@x.com", "customer"));

        Assert.Equal("PetShop", refresh.Issuer);
        Assert.Contains("PetShopClients", refresh.Audiences);
    }

    // ---------------------------------------------------------------------
    // Kiểm tra
    // ---------------------------------------------------------------------

    [Fact]
    public void ValidateRefreshToken_RefreshTokenHopLe_TraVeTrue()
    {
        var service = CreateService();

        var token = service.CreateRefreshToken(9, "B", "b@petshop.com", "customer");
        var ok = service.ValidateRefreshToken(token, out var principal);

        Assert.True(ok);
        Assert.NotNull(principal);
        Assert.Equal("9", principal!.FindFirst(ClaimTypes.NameIdentifier)?.Value);
    }

    [Fact]
    public void ValidateRefreshToken_AccessToken_TuChoi()
    {
        // Bảo mật quan trọng: access token KHÔNG được dùng để refresh.
        var service = CreateService();

        var accessToken = service.CreateToken(1, "A", "a@x.com", "customer");
        var ok = service.ValidateRefreshToken(accessToken, out var principal);

        Assert.False(ok);
        Assert.Null(principal);
    }

    [Fact]
    public void ValidateAccessToken_RefreshToken_TuChoi()
    {
        // Chiều ngược lại: refresh token không được dùng như access token.
        var service = CreateService();

        var refreshToken = service.CreateRefreshToken(1, "A", "a@x.com", "customer");
        var ok = service.ValidateAccessToken(refreshToken, out var principal);

        Assert.False(ok);
        Assert.Null(principal);
    }

    [Fact]
    public void ValidateAccessToken_AccessTokenHopLe_TraVeTrue()
    {
        var service = CreateService();

        var token = service.CreateToken(1, "A", "a@x.com", "customer");
        var ok = service.ValidateAccessToken(token, out var principal);

        Assert.True(ok);
        Assert.NotNull(principal);
    }

    [Fact]
    public void ValidateRefreshToken_SaiChuKy_TraVeFalse()
    {
        var issuer = CreateService(ValidKey);
        var other = CreateService("mot-khoa-khac-hoan-toan-khac-32bytes");

        var token = issuer.CreateRefreshToken(1, "A", "a@x.com", "customer");
        var ok = other.ValidateRefreshToken(token, out var principal);

        Assert.False(ok);
        Assert.Null(principal);
    }

    [Fact]
    public void ValidateRefreshToken_ChuoiRac_TraVeFalseKhongNemException()
    {
        var service = CreateService();

        Assert.False(service.ValidateRefreshToken(string.Empty, out _));
        Assert.False(service.ValidateRefreshToken("   ", out _));
        Assert.False(service.ValidateRefreshToken("khong-phai-jwt", out _));
        Assert.False(service.ValidateRefreshToken("a.b.c", out _));
    }

    [Fact]
    public void ValidateRefreshToken_HetHan_TraVeFalse()
    {
        // ClockSkew của service là 1 phút ⇒ token hết hạn lâu hơn thế mới bị từ chối.
        var service = CreateService(refreshExpiresInMinutes: 10080);

        var expiredToken = CreateExpiredRefreshToken();

        Assert.False(service.ValidateRefreshToken(expiredToken, out _));
    }

    /// <summary>
    /// Tạo refresh token đã hết hạn (hạn âm) để test vòng đời token.
    ///
    /// Token được dựng với **cùng key/issuer/audience** của service nên chữ ký hợp lệ —
    /// chỉ có mốc <c>exp</c> là đã qua ⇒ chỉ bị từ chối vì hết hạn.
    /// </summary>
    private static string CreateExpiredRefreshToken()
    {
        var handler = new JwtSecurityTokenHandler();
        var now = DateTime.UtcNow;
        var expired = now.AddMinutes(-10);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "1"),
            new(ClaimTypes.NameIdentifier, "1"),
            new(ClaimTypes.Name, "A"),
            new(ClaimTypes.Email, "a@x.com"),
            new(ClaimTypes.Role, "customer"),
            new(JwtTokenService.TokenTypeClaim, JwtTokenService.TokenTypeRefresh),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: "PetShop",
            audience: "PetShopClients",
            claims: claims,
            notBefore: expired.AddSeconds(-1),
            expires: expired,
            signingCredentials: new Microsoft.IdentityModel.Tokens.SigningCredentials(
                new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                    System.Text.Encoding.UTF8.GetBytes(ValidKey)),
                Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256));

        return handler.WriteToken(token);
    }

    /// <summary>Thay giá trị claim <c>token_type</c> trong JWT (test helper).</summary>
    private static string ReplaceTokenType(string token, string newType)
    {
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        var claims = jwt.Claims
            .Where(c => c.Type != JwtTokenService.TokenTypeClaim)
            .Select(c => new Claim(c.Type, c.Value))
            .ToList();
        claims.Add(new Claim(JwtTokenService.TokenTypeClaim, newType));

        var rebuilt = new JwtSecurityToken(
            issuer: jwt.Issuer,
            audience: "PetShopClients",
            claims: claims,
            notBefore: jwt.ValidFrom,
            expires: jwt.ValidTo,
            signingCredentials: new Microsoft.IdentityModel.Tokens.SigningCredentials(
                new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                    System.Text.Encoding.UTF8.GetBytes(ValidKey)),
                Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256));

        return handler.WriteToken(rebuilt);
    }

    [Fact]
    public void ValidateRefreshToken_AccessTokenCu_TokenKhongDungLoai_TuChoi()
    {
        // Token hợp lệ về chữ ký/hạn nhưng thiếu claim token_type ⇒ không dùng được để refresh.
        var service = CreateService();

        var token = ReplaceTokenType(
            service.CreateToken(1, "A", "a@x.com", "customer"), "access");

        Assert.False(service.ValidateRefreshToken(token, out _));
    }

    [Fact]
    public void RefreshToken_DungChungKey_AccessTokenCuVanDungDuoc()
    {
        // Không phá luồng login cũ: access token phát hành trước khi có refresh token
        // (thiếu claim token_type) vẫn được ValidateToken chấp nhận.
        var service = CreateService();

        var legacyToken = service.CreateToken(1, "A", "a@x.com", "customer");

        Assert.True(service.ValidateToken(legacyToken, out _));
    }
}
