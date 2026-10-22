using DiscordBot.Services.Recruitment.Domain.Policies;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Modules.Recruitment;
using DiscordBot.Services.Recruitment;
using DiscordBot.Settings;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace DiscordBot.Tests.Recruitment;

public class RecruitmentPortfolioTests
{
    [Theory]
    [InlineData("github.com", "github.com")]
    [InlineData("  ARTIST.CO.UK  ", "artist.co.uk")]
    [InlineData("itch.io", "itch.io")]
    [InlineData("creator.itch.io", "creator.itch.io")]
    [InlineData("bar.foo.ck", "bar.foo.ck")]
    [InlineData("www.ck", "www.ck")]
    [InlineData("city.kawasaki.jp", "city.kawasaki.jp")]
    [InlineData("bücher.de", "xn--bcher-kva.de")]
    public void HostnamesNormalizeBelowActualIcannSuffixes(string input, string expected)
    {
        Assert.True(RecruitmentPortfolioDomains.TryNormalize(input, out var result)); Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("com")]
    [InlineData("co.uk")]
    [InlineData("com.au")]
    [InlineData("k12.ak.us")]
    [InlineData("foo.ck")]
    [InlineData("foo.kawasaki.jp")]
    [InlineData("https://github.com")]
    [InlineData("github.com/foo")]
    [InlineData("github.com?foo=1")]
    [InlineData("user@github.com")]
    [InlineData("github.com:443")]
    [InlineData("*.github.com")]
    [InlineData("127.0.0.1")]
    [InlineData("[::1]")]
    [InlineData("localhost")]
    [InlineData("artist.local")]
    [InlineData("artist.invalid")]
    [InlineData("a..com")]
    [InlineData("-a.com")]
    [InlineData("a_.com")]
    public void RejectsUrlsAndRegistryBoundaries(string input) => Assert.False(RecruitmentPortfolioDomains.TryNormalize(input, out _));

    [Theory]
    [InlineData("200", "1")]
    [InlineData("900", "1")]
    [InlineData("901", "2")]
    public async Task EngineRequiresCurrentConfiguredGuildAdministrator(string actor, string guild)
    {
        using var rig = new RecruitmentRig(); await rig.Start();
        var before = rig.State.PortfolioSites.Version;
        Assert.Contains("Administrator", await rig.Engine.PortfolioSitesAsync(guild, actor, "add", "example.org", "Reason", default));
        Assert.Contains("Administrator", await rig.Engine.PortfolioSitesAsync(guild, actor, "list", "", "", default));
        Assert.Equal(before, rig.State.PortfolioSites.Version); Assert.Empty(rig.State.PortfolioSites.Audit);
        rig.Discord.Administrators.Remove("901");
        Assert.Contains("Administrator", await rig.Engine.PortfolioSitesAsync("1", "901", "add", "example.org", "Reason", default));
    }

    [Fact]
    public async Task RegistryIsAuditedIdempotentPersistentAndDoesNotWriteSettings()
    {
        using var rig = new RecruitmentRig(); await rig.Start();
        var settingsPath = Path.Combine(rig.Root, "Settings.json"); File.WriteAllText(settingsPath, "settings sentinel");
        var original = rig.State.PortfolioSites.Version;
        Assert.Contains("reason", await Sites(rig, "add", "example.org", "  "));
        Assert.Contains("DNS hostname", await Sites(rig, "add", "https://example.org"));
        Assert.Contains("added", await Sites(rig, "add", "Example.ORG"));
        Assert.Contains("already configured", await Sites(rig, "add", "example.org"));
        Assert.Equal(original + 1, rig.State.PortfolioSites.Version);
        var audit = Assert.Single(rig.State.PortfolioSites.Audit); Assert.Equal("901", audit.Actor); Assert.Contains("add example.org", audit.Reason);
        Assert.Contains("not endorsement", await Sites(rig, "list"));
        rig.Discord.MissingMembers.Add("901");
        Assert.Contains("Administrator", await Sites(rig, "remove", "example.org")); rig.Discord.MissingMembers.Clear();
        await rig.Restart(); Assert.Contains("example.org", rig.State.PortfolioSites.Hosts);
        Assert.Contains("removed", await Sites(rig, "remove", "github.com"));
        await rig.Restart(); Assert.DoesNotContain("github.com", rig.State.PortfolioSites.Hosts);
        foreach (var host in rig.State.PortfolioSites.Hosts.ToArray()) await Sites(rig, "remove", host);
        await rig.Restart(); Assert.Empty(rig.State.PortfolioSites.Hosts); Assert.Contains("None configured", await Sites(rig, "list"));
        Assert.Equal("settings sentinel", File.ReadAllText(settingsPath));
    }

    [Fact]
    public async Task AbsentRegistryStartsFromSeedsOnceAndMalformedRegistryStopsLoading()
    {
        using var rig = new RecruitmentRig(); await rig.Start();
        var path = Path.Combine(rig.Root, "recruitment/recruitment-state.json");
        rig.Engine.Dispose();
        var json = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path)); json.Remove("PortfolioSites"); File.WriteAllText(path, json.ToString());
        await rig.Restart();
        Assert.Equal(RecruitmentContent.SeedPortfolioHosts.Order(), rig.State.PortfolioSites.Hosts.Order());
        Assert.NotNull(Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path))["PortfolioSites"]);
        await Sites(rig, "remove", "github.com"); await rig.Restart(); Assert.DoesNotContain("github.com", rig.State.PortfolioSites.Hosts);
        rig.Engine.Dispose(); json = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path));
        json["PortfolioSites"]!["Hosts"] = new Newtonsoft.Json.Linq.JArray("co.uk"); File.WriteAllText(path, json.ToString());
        await rig.Restart(); Assert.Contains("registry is invalid", rig.Engine.Status); Assert.Null(rig.Engine.State);
    }

    [Fact]
    public async Task HostAndAuditLimitsAreBounded()
    {
        using var rig = new RecruitmentRig(); await rig.Start();
        for (var i = rig.State.PortfolioSites.Hosts.Count; i < 100; i++) Assert.Contains("added", await Sites(rig, "add", $"site{i}.org"));
        Assert.Contains("limit is 100", await Sites(rig, "add", "overflow.org"));
        for (var i = 0; i < 10; i++) { await Sites(rig, "remove", "github.com"); await Sites(rig, "add", "github.com"); }
        Assert.Equal(100, rig.State.PortfolioSites.Hosts.Count); Assert.Equal(100, rig.State.PortfolioSites.Audit.Count);
        await rig.Restart(); Assert.Equal(100, rig.State.PortfolioSites.Audit.Count);
    }

    [Theory]
    [InlineData("https://github.com", false)]
    [InlineData("https://www.artstation.com/", false)]
    [InlineData("https://itch.io/", false)]
    [InlineData("https://itch.io/explore", false)]
    [InlineData("https://github.com/issues", false)]
    [InlineData("https://github.com/settings/profile", false)]
    [InlineData("https://youtube.com/feed/subscriptions", false)]
    [InlineData("https://artist.itch.io", true)]
    [InlineData("https://artist.artstation.com/", true)]
    [InlineData("https://github.com/Artist/Work", true)]
    [InlineData("https://gitlab.com/Artist/Work", true)]
    [InlineData("https://behance.net/gallery/123/work", true)]
    [InlineData("https://dribbble.com/artist", true)]
    [InlineData("https://vimeo.com/123456", true)]
    [InlineData("https://deviantart.com/artist", true)]
    [InlineData("https://youtube.com/watch?v=AbCdEfGhI_1", true)]
    [InlineData("https://youtube.com/watch?v=AbCdEfGhI_2", true)]
    [InlineData("https://youtube.com/watch", false)]
    [InlineData("https://github.com.evil.org/artist", false)]
    [InlineData("https://evilgithub.com/artist", false)]
    public void SpecificSupportedPlatformsRequireActualProfileOrWorkIdentity(string url, bool green)
    {
        var tile = Tile(url);
        Assert.Equal(green ? ObservationSeverity.Green : ObservationSeverity.Yellow, tile.Severity);
    }

    [Theory]
    [InlineData("https://github.com/contact")]
    [InlineData("https://discord.gg/project")]
    [InlineData("https://discord.com/invite/project")]
    [InlineData("https://t.me/artist")]
    [InlineData("https://example.org/join")]
    public void ContactOnlyAndInvitesAreAbsent(string url)
    {
        Assert.Empty(RecruitmentContent.ReadPortfolio(url).Candidates); Assert.Empty(RecruitmentContent.ContactLinks(url));
        Assert.Equal(ObservationSeverity.Red, Tile(url).Severity);
    }

    [Fact]
    public void RemovedEmptyAndSubdomainPoliciesAreRespectedWithoutClearingNegation()
    {
        Assert.Equal(ObservationSeverity.Yellow, Tile("https://github.com/artist", new HashSet<string>()).Severity);
        var hosts = new HashSet<string> { "portfolio.example.org" };
        Assert.Equal(ObservationSeverity.Green, Tile("https://creator.portfolio.example.org/work", hosts).Severity);
        Assert.Equal(ObservationSeverity.Yellow, Tile("https://evilportfolio.example.org/work", hosts).Severity);
        Assert.Equal(ObservationSeverity.Red, Tile("No portfolio available: https://github.com/artist").Severity);
        Assert.Equal(ObservationSeverity.Yellow, Tile("https://personal.example.org/").Severity);
    }

    [Fact]
    public void CanonicalIdentityPreservesVideoIdsAndPathCaseAndScrubsUnrecognizedQueries()
    {
        var links = RecruitmentContent.ContactLinks("https://YouTube.com:443/watch?v=AbCdEfGhI_1&utm_source=one#time https://youtu.be/AbCdEfGhI_1?si=tracking https://youtube.com/watch?v=AbCdEfGhI_2 https://example.org/Work https://example.org/work");
        Assert.Equal(new[] { "https://youtube.com/watch?v=AbCdEfGhI_1", "https://youtube.com/watch?v=AbCdEfGhI_2", "https://example.org/Work", "https://example.org/work" }, links);
        Assert.NotEqual(Assert.Single(RecruitmentContent.ContactLinks("https://youtube.com/watch?v=AbCdEfGhI_1")),
            Assert.Single(RecruitmentContent.ContactLinks("https://youtube.com:444/watch?v=AbCdEfGhI_1")));
        var unknown = Assert.Single(RecruitmentContent.ReadPortfolio("https://example.org/view?id=project&token=secret#part").Candidates);
        Assert.Equal("https://example.org/view", unknown.Url); Assert.Null(unknown.ReuseUrl);
        Assert.Empty(RecruitmentContent.ContactLinks("https://example.org/view?id=one https://example.org/view?id=two https://youtube.com/watch"));
        Assert.Equal(new[] { "https://example.org/Work" }, RecruitmentContent.ContactLinks("https://EXAMPLE.org:443/Work?utm_medium=x#part"));
    }

    [Fact]
    public void CandidatesRemainBoundedAndDoNotTruncateIntoFalseIdentities()
    {
        var content = string.Join(' ', Enumerable.Range(0, 20).Select(i => $"https://example.org/work/{i}"));
        Assert.Equal(8, RecruitmentContent.ReadPortfolio(content).Candidates.Count); Assert.Equal(8, RecruitmentContent.ContactLinks(content).Count);
        Assert.Empty(RecruitmentContent.ContactLinks("https://example.org/" + new string('a', 310)));
        Assert.Empty(RecruitmentContent.ContactLinks(new string('a', 8000) + " https://example.org/hidden"));
        Assert.False(RecruitmentContent.ReadPortfolio(null).Available);
    }

    [Fact]
    public async Task RegistryChangesReclassifyInBoundedBatchesWithoutMaterialChangesOrInventory()
    {
        using var rig = new RecruitmentRig(); await rig.Start();
        for (var i = 0; i < 24; i++)
        {
            var id = (100 + i).ToString(); await rig.Post(id, (200 + i).ToString(), "12"); await rig.Ack(id, (200 + i).ToString());
            rig.Discord.Threads[id] = rig.Discord.Threads[id] with { Starter = "Portfolio: https://example.org/works" };
            await rig.Engine.FactsChangedAsync(id, default);
        }
        var inventories = rig.Discord.Inventories; var reads = rig.Discord.ThreadReads;
        var material = rig.State.Listings.ToDictionary(p => p.Key, p => p.Value.MaterialChanges);
        var versions = rig.State.Listings.ToDictionary(p => p.Key, p => p.Value.LatestStarter);
        var notices = rig.State.Listings.ToDictionary(p => p.Key, p => p.Value.Attention.Count);
        await Sites(rig, "add", "example.org");
        Assert.Equal(reads, rig.Discord.ThreadReads);
        await rig.Engine.RunDueAsync(default);
        Assert.Equal(20, rig.Discord.ThreadReads - reads);
        Assert.Equal(20, rig.State.Listings.Values.Count(p => p.PortfolioRegistryVersion == rig.State.PortfolioSites.Version));
        await rig.Engine.RunDueAsync(default);
        Assert.All(rig.State.Listings.Values, p => {
            Assert.Equal(rig.State.PortfolioSites.Version, p.PortfolioRegistryVersion);
            Assert.Equal(ObservationSeverity.Green, p.Facts.Checks.Single(c => c.Id == "portfolio").Severity);
            Assert.Equal(material[p.Id], p.MaterialChanges); Assert.Equal(versions[p.Id], p.LatestStarter); Assert.Equal(notices[p.Id], p.Attention.Count);
        });
        Assert.Equal(inventories, rig.Discord.Inventories); Assert.Empty(rig.Discord.Actions);
        await Sites(rig, "remove", "example.org"); await rig.Restart();
        while (rig.State.Listings.Values.Any(p => p.PortfolioRegistryVersion != rig.State.PortfolioSites.Version)) await rig.Engine.RunDueAsync(default);
        Assert.All(rig.State.Listings.Values, p => Assert.Equal(ObservationSeverity.Yellow, p.Facts.Checks.Single(c => c.Id == "portfolio").Severity));
    }

    [Fact]
    public async Task ReuseHintsAreBoundedPrivateDifferentAuthorAndRetainedOnlyWithListingEvidence()
    {
        using var rig = new RecruitmentRig(); await rig.Start();
        for (var i = 0; i < 8; i++)
        {
            var id = (100 + i).ToString(); await rig.Post(id, (200 + i).ToString());
            rig.Discord.Threads[id] = rig.Discord.Threads[id] with { Starter = "Portfolio https://youtube.com/watch?v=AbCdEfGhI_1" };
            await rig.Engine.FactsChangedAsync(id, default);
        }
        var lookup = await rig.Staff("lookup"); var hint = lookup[lookup.IndexOf("Shared starter", StringComparison.Ordinal)..];
        Assert.Equal(5, hint.Split("(author").Length - 1); Assert.Contains("shared projects can be legitimate", hint);
        Assert.DoesNotContain("Shared starter", await rig.Engine.DetailsAsync("1", "100", "200", default));
        var message = rig.Discord.Messages["100:recruitment:100:public"].Message;
        foreach (var components in new[] { message.Components, message.FallbackComponents })
        {
            Assert.NotNull(components);
            Assert.DoesNotContain("Shared starter", Newtonsoft.Json.JsonConvert.SerializeObject(components));
        }
        foreach (var post in rig.State.Listings.Values.Where(p => p.Id != "100"))
        { post.Status = ListingStatus.Deleted; post.UpdatedAt = rig.Clock.Now.AddYears(-2); rig.Discord.Threads.Remove(post.Id); }
        RecruitmentRetention.Prune(rig.State, rig.Clock.Now, 12, 24, 30);
        Assert.DoesNotContain("Shared starter", await rig.Staff("lookup"));
        await rig.Post("108", "200");
        rig.Discord.Threads["108"] = rig.Discord.Threads["108"] with { Starter = "Portfolio https://youtube.com/watch?v=AbCdEfGhI_1" };
        await rig.Engine.FactsChangedAsync("108", default);
        Assert.DoesNotContain("Shared starter", await rig.Staff("lookup"));
    }

    [Fact]
    public async Task QueryAndCaseDistinctUrlsNeverMatchButTrackingAndRetainedVersionsCanMatch()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); await rig.Post(); await rig.Post("101", "201");
        async Task Set(string id, string url) { rig.Discord.Threads[id] = rig.Discord.Threads[id] with { Starter = "Portfolio " + url }; await rig.Engine.FactsChangedAsync(id, default); }
        await Set("100", "https://youtube.com/watch?v=AbCdEfGhI_1"); await Set("101", "https://youtube.com/watch?v=AbCdEfGhI_2");
        Assert.DoesNotContain("Shared starter", await rig.Staff("lookup"));
        await Set("100", "https://example.org/Work"); await Set("101", "https://example.org/work");
        Assert.DoesNotContain("Shared starter", await rig.Staff("lookup"));
        await Set("100", "https://example.org/view?id=1&token=one"); await Set("101", "https://example.org/view?id=2&token=two");
        Assert.DoesNotContain("Shared starter", await rig.Staff("lookup"));
        await Set("100", "https://example.org/Work?utm_source=one"); await Set("101", "https://example.org/Work#part");
        Assert.Contains("Shared starter", await rig.Staff("lookup"));
        await rig.Ack(); await Set("100", "https://example.org/another");
        rig.Discord.Threads["100"] = rig.Discord.Threads["100"] with { Starter = null }; await rig.Engine.FactsChangedAsync("100", default);
        Assert.Contains("Shared starter", await rig.Staff("lookup"));
        await rig.Restart(); Assert.Contains("Shared starter", await rig.Staff("lookup"));
    }

    [Fact]
    public async Task CaseOnlyUrlEditCreatesDistinctVersionAndMaterialLinkChange()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); await rig.Post();
        rig.Discord.Threads["100"] = rig.Discord.Threads["100"] with { Starter = "Portfolio https://example.org/Work" };
        await rig.Engine.FactsChangedAsync("100", default); await rig.Ack();
        rig.Discord.Threads["100"] = rig.Discord.Threads["100"] with { Starter = "Portfolio https://example.org/work" };
        await rig.Engine.FactsChangedAsync("100", default);
        Assert.Equal(1, rig.State.Listings["100"].MaterialChanges);
        Assert.Equal("https://example.org/work", Assert.Single(rig.State.Listings["100"].LatestStarter!.Links));
    }

    [Fact]
    public async Task RegisteredAdministratorPreconditionDoesNotRestrictOtherCommands()
    {
        using var client = new DiscordSocketClient(); using var interactions = new InteractionService(client);
        using var provider = new ServiceCollection().AddSingleton(new RecruitmentService(client, new BotSettings())).BuildServiceProvider();
        var module = await interactions.AddModuleAsync<RecruitmentModule>(provider);
        var command = module.SlashCommands.Single(c => c.Name == "portfolio-sites");
        var requirement = Assert.Single(command.Preconditions.OfType<RequireUserPermissionAttribute>());
        foreach (var permission in new[] { GuildPermission.SendMessages, GuildPermission.ManageMessages, GuildPermission.Administrator })
        {
            var user = Proxy<IGuildUser>(m => m.Name == "get_GuildPermissions" ? new GuildPermissions((ulong)permission) : null);
            var guild = Proxy<IGuild>(m => m.Name == "get_Id" ? 1UL : null);
            var context = Proxy<IInteractionContext>(m => m.Name switch { "get_User" => user, "get_Guild" => guild, _ => null });
            var result = await requirement.CheckRequirementsAsync(context, command, provider);
            Assert.Equal(permission == GuildPermission.Administrator, result.IsSuccess);
        }
        Assert.Empty(module.Preconditions); Assert.Null(module.DefaultMemberPermissions);
        Assert.Empty(module.SlashCommands.Single(c => c.Name == "eligibility").Preconditions);
        Assert.Empty(module.SlashCommands.Single(c => c.Name == "status").Preconditions);
    }

    private static Task<string> Sites(RecruitmentRig rig, string operation, string domain = "", string reason = "Test change") =>
        rig.Engine.PortfolioSitesAsync("1", "901", operation, domain, reason, default);
    private static RecruitmentObservation Tile(string content, IReadOnlySet<string>? hosts = null) => RecruitmentSignals.Evaluate(
        new ThreadSnapshot("100", "12", "200", "Listing", DateTimeOffset.UtcNow, false, false, false, false, Array.Empty<string>(), content, null, null),
        "paid-for-hire", hosts).Single(c => c.Id == "portfolio");
    private static T Proxy<T>(Func<MethodInfo, object?> implementation) where T : class
    {
        var proxy = DispatchProxy.Create<T, InterfaceProxy>(); ((InterfaceProxy)(object)proxy).Implementation = implementation; return proxy;
    }
    public class InterfaceProxy : DispatchProxy
    {
        public Func<MethodInfo, object?> Implementation { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Implementation(targetMethod!);
    }
}
