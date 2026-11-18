using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EduFlow.Application.Branding;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using SkiaSharp;

namespace EduFlow.IntegrationTests;

public sealed class BrandingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly string[] ExpectedPublicKeys =
    [
        "accentColor",
        "displayName",
        "faviconUrl",
        "logoUrl",
        "primaryColor",
        "tagline"
    ];

    private static byte[] CreatePngBytes(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.Teal);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 90);
        return encoded.ToArray();
    }

    [Fact]
    public async Task Public_branding_supports_code_lookup_hides_unknown_codes_leaks_no_extra_fields_and_invalidates_cache()
    {
        var client = factory.CreateClient();

        // 1. Empty code and omitted code return platform brand with strictly 6 fields
        var platformRes = await client.GetAsync("/api/public/branding?code=");
        platformRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var platformJson = await platformRes.Content.ReadAsStringAsync();
        using (var doc = JsonDocument.Parse(platformJson))
        {
            doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n)
                .Should().Equal(ExpectedPublicKeys);
        }

        var platform = JsonSerializer.Deserialize<PublicBrandingDto>(platformJson, IntegrationTestHelpers.JsonOptions)!;
        platform.DisplayName.Should().Be("EduFlow");
        platform.PrimaryColor.Should().Be("#1F4E46");

        // 2. Unknown code returns identical platform brand without revealing existence
        var unknownRes = await client.GetAsync("/api/public/branding?code=unknown-institute-xyz");
        unknownRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var unknown = await unknownRes.Content.ReadFromJsonAsync<PublicBrandingDto>(IntegrationTestHelpers.JsonOptions);
        unknown.Should().BeEquivalentTo(platform);

        // 3. Create two tenants and verify valid code lookup + authenticated /api/branding/current
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var tenantA = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "brand-a", "Starter");
        var tenantB = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "brand-b", "Enterprise");

        // Warm cache for brand-a before updating
        var warmA = await client.GetFromJsonAsync<PublicBrandingDto>($"/api/public/branding?code={tenantA.Subdomain}", IntegrationTestHelpers.JsonOptions);
        warmA!.DisplayName.Should().Be(tenantA.Name);

        var authA = await IntegrationTestHelpers.LoginTenantUserAsync(
            client, tenantA.Subdomain, $"admin@{tenantA.Subdomain}.lk", "AdminUser@123!");

        using var updateA = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put,
            "/api/settings/branding",
            authA.AccessToken,
            tenantA.Subdomain,
            new UpdateBrandingRequest(
                "#112233",
                "#C45C26",
                null,
                null,
                null,
                "Alpha Institute",
                "Quiet classrooms",
                "Inter",
                "light",
                false));
        var updateRes = await client.SendAsync(updateA);
        updateRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Cache must be invalidated immediately so ?code=brand-a reflects the update
        var brandedARes = await client.GetAsync($"/api/public/branding?code={tenantA.Subdomain}");
        brandedARes.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var doc = JsonDocument.Parse(await brandedARes.Content.ReadAsStringAsync()))
        {
            doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n)
                .Should().Equal(ExpectedPublicKeys);
        }

        var brandedA = await client.GetFromJsonAsync<PublicBrandingDto>($"/api/public/branding?code={tenantA.Subdomain}", IntegrationTestHelpers.JsonOptions);
        brandedA!.PrimaryColor.Should().Be("#112233");
        brandedA.DisplayName.Should().Be("Alpha Institute");
        brandedA.Tagline.Should().Be("Quiet classrooms");

        // Header-based lookup still works when ?code is omitted
        using var publicB = new HttpRequestMessage(HttpMethod.Get, "/api/public/branding");
        publicB.Headers.Add("X-Tenant", tenantB.Subdomain);
        var brandedB = await (await client.SendAsync(publicB)).Content.ReadFromJsonAsync<PublicBrandingDto>(IntegrationTestHelpers.JsonOptions);
        brandedB!.PrimaryColor.Should().Be("#1F4E46");
        brandedB.DisplayName.Should().Be(tenantB.Name);

        // Authenticated /api/branding/current returns full BrandingDto per role/tenant
        using var currentA = IntegrationTestHelpers.CreateTenantRequest(HttpMethod.Get, "/api/branding/current", authA.AccessToken, tenantA.Subdomain);
        var currentADto = await (await client.SendAsync(currentA)).Content.ReadFromJsonAsync<BrandingDto>(IntegrationTestHelpers.JsonOptions);
        currentADto!.InstituteDisplayName.Should().Be("Alpha Institute");
        currentADto.CanRemoveProductBranding.Should().BeFalse();

        using var currentSuper = new HttpRequestMessage(HttpMethod.Get, "/api/branding/current");
        currentSuper.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdmin.AccessToken);
        var currentSuperDto = await (await client.SendAsync(currentSuper)).Content.ReadFromJsonAsync<BrandingDto>(IntegrationTestHelpers.JsonOptions);
        currentSuperDto!.IsPlatformDefault.Should().BeTrue();

        var authB = await IntegrationTestHelpers.LoginTenantUserAsync(
            client, tenantB.Subdomain, $"admin@{tenantB.Subdomain}.lk", "AdminUser@123!");
        using var currentB = IntegrationTestHelpers.CreateTenantRequest(HttpMethod.Get, "/api/branding/current", authB.AccessToken, tenantB.Subdomain);
        var currentBDto = await (await client.SendAsync(currentB)).Content.ReadFromJsonAsync<BrandingDto>(IntegrationTestHelpers.JsonOptions);
        currentBDto!.CanRemoveProductBranding.Should().BeTrue();

        using var removeOnStarter = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put,
            "/api/settings/branding",
            authA.AccessToken,
            tenantA.Subdomain,
            new UpdateBrandingRequest("#112233", "#C45C26", null, null, null, "Alpha Institute", null, "Inter", "light", true));
        var denied = await client.SendAsync(removeOnStarter);
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var deniedDoc = JsonDocument.Parse(await denied.Content.ReadAsStringAsync());
        deniedDoc.RootElement.GetProperty("code").GetString().Should().Be("FEATURE_NOT_IN_PLAN");

        // 4. Oversized PNG logo (800x400) & favicon (256x256) are resized server-side
        using var logoForm = new MultipartFormDataContent();
        var logoFile = new ByteArrayContent(CreatePngBytes(800, 400));
        logoFile.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        logoForm.Add(logoFile, "file", "mark.png");
        logoForm.Add(new StringContent("logo"), "kind");
        using var uploadLogo = new HttpRequestMessage(HttpMethod.Post, "/api/settings/branding/assets") { Content = logoForm };
        uploadLogo.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authA.AccessToken);
        uploadLogo.Headers.Add("X-Tenant", tenantA.Subdomain);
        var uploadedLogo = await client.SendAsync(uploadLogo);
        uploadedLogo.StatusCode.Should().Be(HttpStatusCode.OK);
        var withLogo = (await uploadedLogo.Content.ReadFromJsonAsync<BrandingDto>(IntegrationTestHelpers.JsonOptions))!;
        withLogo.LogoUrl.Should().StartWith("/files/branding/");

        var logoBytes = await client.GetByteArrayAsync(withLogo.LogoUrl);
        using var decodedLogo = SKBitmap.Decode(logoBytes);
        decodedLogo.Width.Should().BeLessOrEqualTo(512);
        decodedLogo.Height.Should().BeLessOrEqualTo(512);

        using var favForm = new MultipartFormDataContent();
        var favFile = new ByteArrayContent(CreatePngBytes(256, 256));
        favFile.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        favForm.Add(favFile, "file", "fav.png");
        favForm.Add(new StringContent("favicon"), "kind");
        using var uploadFav = new HttpRequestMessage(HttpMethod.Post, "/api/settings/branding/assets") { Content = favForm };
        uploadFav.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authA.AccessToken);
        uploadFav.Headers.Add("X-Tenant", tenantA.Subdomain);
        var uploadedFav = await client.SendAsync(uploadFav);
        uploadedFav.StatusCode.Should().Be(HttpStatusCode.OK);
        var withFav = (await uploadedFav.Content.ReadFromJsonAsync<BrandingDto>(IntegrationTestHelpers.JsonOptions))!;
        var favBytes = await client.GetByteArrayAsync(withFav.FaviconUrl);
        using var decodedFav = SKBitmap.Decode(favBytes);
        decodedFav.Width.Should().Be(64);
        decodedFav.Height.Should().Be(64);

        // 5. Valid SVG upload succeeds (and unsafe SVG is rejected) on Super Admin platform branding too
        using var svgForm = new MultipartFormDataContent();
        var svgContent = new ByteArrayContent(Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\"><rect width=\"24\" height=\"24\" fill=\"#0F766E\"/></svg>"));
        svgContent.Headers.ContentType = new MediaTypeHeaderValue("image/svg+xml");
        svgForm.Add(svgContent, "file", "platform-logo.svg");
        svgForm.Add(new StringContent("logo"), "kind");
        using var uploadPlatformSvg = new HttpRequestMessage(HttpMethod.Post, "/api/super-admin/branding/assets") { Content = svgForm };
        uploadPlatformSvg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdmin.AccessToken);
        var platformSvgRes = await client.SendAsync(uploadPlatformSvg);
        platformSvgRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var platformWithSvg = (await platformSvgRes.Content.ReadFromJsonAsync<BrandingDto>(IntegrationTestHelpers.JsonOptions))!;
        platformWithSvg.LogoUrl.Should().EndWith(".svg");

        using var badSvgForm = new MultipartFormDataContent();
        var badSvg = new ByteArrayContent(Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"));
        badSvg.Headers.ContentType = new MediaTypeHeaderValue("image/svg+xml");
        badSvgForm.Add(badSvg, "file", "evil.svg");
        badSvgForm.Add(new StringContent("logo"), "kind");
        using var uploadBadSvg = new HttpRequestMessage(HttpMethod.Post, "/api/settings/branding/assets") { Content = badSvgForm };
        uploadBadSvg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authA.AccessToken);
        uploadBadSvg.Headers.Add("X-Tenant", tenantA.Subdomain);
        var badSvgRes = await client.SendAsync(uploadBadSvg);
        badSvgRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Public_branding_enforces_rate_limit()
    {
        await using var limitedFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:PublicPermitLimit", "3");
            builder.UseSetting("RateLimiting:WindowSeconds", "60");
        });

        using var client = limitedFactory.CreateClient();
        for (var i = 0; i < 3; i++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "/api/public/branding?code=");
            req.Headers.Add("X-Forwarded-For", "203.0.113.55");
            var res = await client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using var blockedReq = new HttpRequestMessage(HttpMethod.Get, "/api/public/branding?code=");
        blockedReq.Headers.Add("X-Forwarded-For", "203.0.113.55");
        var blocked = await client.SendAsync(blockedReq);
        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        var body = await blocked.Content.ReadAsStringAsync();
        body.Should().Contain("RATE_LIMIT_EXCEEDED");
    }
}
