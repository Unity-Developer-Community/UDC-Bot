using System.Net;
using CasinoWeb.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace CasinoWeb.Tests;

public sealed class SecurityHeadersTests : IDisposable
{
    private readonly CasinoWebFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Theory]
    [InlineData("/")]
    [InlineData("/api/me")]
    [InlineData("/access-denied")]
    public async Task Every_response_carries_the_security_headers_over_https(string path)
    {
        var response = await _factory.CreateBrowser().GetAsync(path);

        Assert.Equal("nosniff", Single(response, "X-Content-Type-Options"));
        Assert.Equal("max-age=31536000", Single(response, "Strict-Transport-Security"));
        var csp = Single(response, "Content-Security-Policy");
        Assert.Contains("default-src 'self'", csp);
        Assert.Contains("script-src 'self'", csp);
        Assert.Contains("connect-src 'self' wss://casino.test", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.DoesNotContain("unsafe-", csp);
    }

    [Fact]
    public async Task A_refused_request_is_covered_too()
    {
        var response = await _factory.CreateBrowser().GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.Contains("Content-Security-Policy"));
    }

    [Fact]
    public async Task An_error_page_keeps_the_headers_the_exception_handler_would_clear()
    {
        using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(
            services => services.AddTransient<IStartupFilter, ThrowingEndpointFilter>()));

        var response = await factory.CreateClient().GetAsync("/boom");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("nosniff", Single(response, "X-Content-Type-Options"));
        Assert.Contains("default-src 'self'", Single(response, "Content-Security-Policy"));
    }

    [Fact]
    public async Task The_client_page_is_revalidated_but_its_hashed_files_are_not()
    {
        var response = await _factory.CreateBrowser().GetAsync("/");

        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public void A_plain_http_origin_gets_ws_and_the_port_in_the_policy()
    {
        var csp = SecurityHeaders.ContentSecurityPolicy(new Uri("http://localhost:5080"));

        Assert.Contains("connect-src 'self' ws://localhost:5080", csp);
    }

    /// <summary>Ends the pipeline with an endpoint that fails, behind everything the app registered.</summary>
    private sealed class ThrowingEndpointFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Run(_ => throw new InvalidOperationException("boom"));
        };
    }

    private static string Single(HttpResponseMessage response, string name) => Assert.Single(response.Headers.GetValues(name));
}
