using CasinoWeb.Auth;
using CasinoWeb.Configuration;
using CasinoWeb.Data;
using CasinoWeb.Endpoints;
using CasinoWeb.Http;
using CasinoWeb.Hubs;
using CasinoWeb.Logging;
using DiscordBot.Services;
using DiscordBot.Services.Fun.Casino;
using DiscordBot.Services.Logging;
using DiscordBot.Settings;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<CasinoHostOptions>()
    .BindConfiguration(CasinoHostOptions.SectionName)
    .Validate(CasinoHostOptions.IsValid, "Casino:PublicOrigin must be an absolute http(s) origin without a path, such as https://casino.example.com")
    .ValidateOnStart();
if (!builder.Environment.IsDevelopment())
{
    // Without it a deploy would silently log everyone out.
    builder.Services.AddOptions<CasinoHostOptions>()
        .Validate(o => !string.IsNullOrWhiteSpace(o.KeysPath), "Casino:KeysPath is required outside Development")
        .ValidateOnStart();
}
builder.Services.AddOptions<DiscordOAuthOptions>()
    .BindConfiguration(DiscordOAuthOptions.SectionName)
    .Validate(o => !string.IsNullOrWhiteSpace(o.ClientId) && !string.IsNullOrWhiteSpace(o.ClientSecret), "Discord:ClientId and Discord:ClientSecret are required")
    .ValidateOnStart();

builder.Services.AddSingleton(sp => BotSettingsLoader.Load(sp.GetRequiredService<IOptions<CasinoHostOptions>>().Value.BotSettingsPath));
builder.Services.AddSingleton<ICasinoDatabase>(sp => new PostgresCasinoDatabase(sp.GetRequiredService<BotSettings>().DbConnectionString));
builder.Services.AddSingleton<ILoggingService, WebLoggingService>();
builder.Services.AddSingleton<CasinoService>();

builder.Services.AddCasinoAuthentication();
builder.Services.AddCasinoHub();
builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();

var app = builder.Build();

// A bad settings file stops the start with its reason, not the first player's request.
app.Services.GetRequiredService<BotSettings>();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler();

// The site is reached through a proxy that terminates TLS, so the request alone cannot tell the public address.
// OAuth redirect URLs and cookies are built from the configured one.
app.Use((context, next) =>
{
    var origin = context.RequestServices.GetRequiredService<IOptions<CasinoHostOptions>>().Value.PublicOriginUri;
    context.Request.Scheme = origin.Scheme;
    context.Request.Host = origin.IsDefaultPort ? new HostString(origin.Host) : new HostString(origin.Host, origin.Port);
    return next(context);
});

app.UseSecurityHeaders();

// The built client (CasinoWeb/wwwroot). In development Vite serves it and proxies the API here.
app.UseDefaultFiles();
// index.html names the hashed asset files of one build: a browser must revalidate it, or after a deploy it could keep
// pointing at files that no longer exist.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        if (context.File.Name == "index.html")
            context.Context.Response.Headers.CacheControl = "no-cache";
    },
});

app.UseHubOriginCheck();
app.UseAuthentication();
app.UseAuthorization();
app.MapAccountEndpoints();
app.MapHub<CasinoHub>(HubSetup.HubPath);

app.Run();

public partial class Program;
