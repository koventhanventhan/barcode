using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EduFlow.Domain;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;

namespace EduFlow.IntegrationTests;

public sealed class HealthAndInfoTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Health_returns_healthy()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    [Fact]
    public async Task Info_returns_product_name()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/info");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ProductInfo>();
        body.Should().NotBeNull();
        body!.Product.Should().Be(ProductName.Value);
    }

    [Fact]
    public async Task Unhandled_exception_returns_problem_details()
    {
        await using var throwingFactory = new ApiFactory().WithWebHostBuilder(builder =>
            builder.UseSetting("Diagnostics:ThrowOnGet", "true"));
        var client = throwingFactory.CreateClient();

        var response = await client.GetAsync("/api/diagnostics/throw");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        root.GetProperty("status").GetInt32().Should().Be(500);
        root.GetProperty("title").GetString().Should().Be("An unexpected error occurred.");
        root.GetProperty("type").GetString().Should().Be("https://datatracker.ietf.org/doc/html/rfc7807");
        root.GetProperty("detail").GetString().Should().Be("diagnostic failure");
    }

    private sealed record ProductInfo(string Product);
}
