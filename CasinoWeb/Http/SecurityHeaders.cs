using CasinoWeb.Configuration;
using Microsoft.Extensions.Options;

namespace CasinoWeb.Http;

public static class SecurityHeaders
{
    /// <summary>
    /// The client is one bundle of scripts and styles served from the site itself: Phaser draws on a canvas and the DOM is
    /// built with <c>textContent</c>, so nothing needs <c>unsafe-inline</c> or <c>unsafe-eval</c>. Avatars come from Discord's
    /// CDN. The WebSocket origin is spelled out because older Safari versions do not let <c>'self'</c> cover <c>ws:</c>/<c>wss:</c>.
    /// </summary>
    public static string ContentSecurityPolicy(Uri publicOrigin)
    {
        var webSocketScheme = publicOrigin.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";
        var webSocketOrigin = $"{webSocketScheme}://{publicOrigin.Authority}";
        return string.Join("; ",
            "default-src 'self'",
            "script-src 'self'",
            "style-src 'self'",
            "img-src 'self' data: blob: https://cdn.discordapp.com",
            $"connect-src 'self' {webSocketOrigin}",
            "object-src 'none'",
            "base-uri 'none'",
            "form-action 'self'",
            "frame-ancestors 'none'");
    }

    /// <summary>
    /// Adds the headers to every response. HSTS only when the public origin is HTTPS: it must never be sent for a local http address.
    /// They are set when the response starts, because the exception handler clears the headers set before it wrote an error page.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) => app.Use((context, next) =>
    {
        var origin = context.RequestServices.GetRequiredService<IOptions<CasinoHostOptions>>().Value.PublicOriginUri;
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.ContentSecurityPolicy = ContentSecurityPolicy(origin);
            headers.XContentTypeOptions = "nosniff";
            if (origin.Scheme == Uri.UriSchemeHttps)
                headers.StrictTransportSecurity = "max-age=31536000";
            return Task.CompletedTask;
        });
        return next(context);
    });
}
