using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EduFlow.Application.Branding;
using FluentAssertions;

namespace EduFlow.IntegrationTests;

public sealed class BrandingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public async Task Public_branding_is_tenant_scoped_and_recolors_only_that_institute()
    {
        var client = factory.CreateClient();

        var platform = await client.GetFromJsonAsync<BrandingDto>("/api/public/branding", IntegrationTestHelpers.JsonOptions);
        platform!.IsPlatformDefault.Should().BeTrue();
        platform.InstituteDisplayName.Should().Be("EduFlow");
        platform.PrimaryColor.Should().Be("#1F4E46");

        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var tenantA = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "brand-a", "Starter");
        var tenantB = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "brand-b", "Enterprise");

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

        using var publicA = new HttpRequestMessage(HttpMethod.Get, "/api/public/branding");
        publicA.Headers.Add("X-Tenant", tenantA.Subdomain);
        var brandedA = await (await client.SendAsync(publicA)).Content.ReadFromJsonAsync<BrandingDto>(IntegrationTestHelpers.JsonOptions);
        brandedA!.PrimaryColor.Should().Be("#112233");
        brandedA.InstituteDisplayName.Should().Be("Alpha Institute");
        brandedA.Subdomain.Should().Be(tenantA.Subdomain);

        using var publicB = new HttpRequestMessage(HttpMethod.Get, "/api/public/branding");
        publicB.Headers.Add("X-Tenant", tenantB.Subdomain);
        var brandedB = await (await client.SendAsync(publicB)).Content.ReadFromJsonAsync<BrandingDto>(IntegrationTestHelpers.JsonOptions);
        brandedB!.PrimaryColor.Should().Be("#1F4E46");
        brandedB.InstituteDisplayName.Should().Be(tenantB.Name);
        brandedB.CanRemoveProductBranding.Should().BeTrue();
        brandedA.CanRemoveProductBranding.Should().BeFalse();

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

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(TinyPng);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "mark.png");
        form.Add(new StringContent("logo"), "kind");
        using var upload = new HttpRequestMessage(HttpMethod.Post, "/api/settings/branding/assets") { Content = form };
        upload.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authA.AccessToken);
        upload.Headers.Add("X-Tenant", tenantA.Subdomain);
        var uploaded = await client.SendAsync(upload);
        uploaded.StatusCode.Should().Be(HttpStatusCode.OK);
        var withLogo = (await uploaded.Content.ReadFromJsonAsync<BrandingDto>(IntegrationTestHelpers.JsonOptions))!;
        withLogo.LogoUrl.Should().StartWith("/files/branding/");

        var fileRes = await client.GetAsync(withLogo.LogoUrl);
        fileRes.StatusCode.Should().Be(HttpStatusCode.OK);
        fileRes.Content.Headers.ContentType?.MediaType.Should().Be("image/png");
    }
}
