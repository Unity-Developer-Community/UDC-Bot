using System.Net;

namespace CasinoWeb.Tests;

public sealed class ClientHostingTests : IDisposable
{
    private readonly CasinoWebFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task The_built_client_is_served_at_the_root_without_a_login()
    {
        var response = await _factory.CreateBrowser().GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(CasinoWebFactory.ClientHtml, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/access-denied", HttpStatusCode.Forbidden)]
    [InlineData("/login-failed", HttpStatusCode.Unauthorized)]
    public async Task The_failure_pages_are_the_client_with_the_failure_status(string path, HttpStatusCode status)
    {
        var response = await _factory.CreateBrowser().GetAsync(path);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(CasinoWebFactory.ClientHtml, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Without_a_built_client_the_failure_pages_are_plain_pages_with_the_message()
    {
        using var factory = new CasinoWebFactory(withClient: false);

        var response = await factory.CreateBrowser().GetAsync("/access-denied");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("Access denied", await response.Content.ReadAsStringAsync());
    }
}
