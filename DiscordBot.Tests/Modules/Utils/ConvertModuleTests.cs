using Discord;
using Discord.Commands;
using DiscordBot.Modules.Utils;
using DiscordBot.Services.Utils;

namespace DiscordBot.Tests.Modules.Utils;

/// <summary>
/// Discord.Net parses every overload of a command and keeps the highest scoring one, so which argument
/// order <c>!curr</c> accepts is decided by the overload set in <see cref="ConvertModule"/> rather than by
/// module code. These tests pin the dispatched overload by inspecting the parsed argument values.
/// </summary>
public class ConvertModuleTests
{
    [Fact]
    public async Task AmountFirst_KeepsAmountInFirstParameter()
    {
        Assert.Equal(new object[] { 100d, "eur", "usd" }, await ParseArgumentsAsync("curr 100 eur usd"));
    }

    [Fact]
    public async Task AmountInMiddle_KeepsAmountInMiddleParameter()
    {
        Assert.Equal(new object[] { "eur", 100d, "usd" }, await ParseArgumentsAsync("curr eur 100 usd"));
    }

    [Fact]
    public async Task AmountLast_KeepsAmountInLastParameter()
    {
        Assert.Equal(new object[] { "eur", "usd", 100d }, await ParseArgumentsAsync("curr eur usd 100"));
    }

    [Fact]
    public async Task AmountOmitted_ParsesCurrenciesOnly()
    {
        Assert.Equal(new object[] { "eur", "usd" }, await ParseArgumentsAsync("curr eur usd"));
    }

    /// <summary><c>!curr 100 eur</c> also parses as two currencies, so the amount-first overload must win.</summary>
    [Fact]
    public async Task NumericFirstWithoutTarget_UsesAmountFirstOverload()
    {
        Assert.Equal(new object[] { 100d, "eur", "usd" }, await ParseArgumentsAsync("curr 100 eur"));
    }

    /// <summary>
    /// Every overload parses three numbers equally, so the winner is decided by priority alone: this pins
    /// the amount-first overload above both alternative orders.
    /// </summary>
    [Fact]
    public async Task AmbiguousThreeArgInput_PrefersAmountFirstOverload()
    {
        Assert.Equal(new object[] { 100d, "200", "300" }, await ParseArgumentsAsync("curr 100 200 300"));
    }

    private static async Task<object[]> ParseArgumentsAsync(string input)
    {
        using var service = new CommandService(new CommandServiceConfig { CaseSensitiveCommands = false });
        await service.AddModuleAsync<ConvertModule>(new StubServiceProvider());

        var search = service.Search(input);
        Assert.True(search.IsSuccess, $"'{input}' matched no command.");

        var result = await service.ValidateAndGetBestMatch(search, new StubCommandContext(), new StubServiceProvider());
        var parseResult = Assert.IsType<ParseResult>(Assert.IsType<MatchResult>(result).Pipeline);
        Assert.True(parseResult.IsSuccess, $"'{input}' did not parse: {parseResult.ErrorReason}");

        return parseResult.ArgValues.Select(values => values.Values.First().Value).ToArray();
    }

    /// <summary>Registering a module instantiates it, so the injected service has to be resolvable.</summary>
    private sealed class StubServiceProvider : IServiceProvider
    {
        // Currency codes and amounts are parsed by the default type readers, which never use the service.
        private readonly CurrencyService _currencyService = new(null!);

        public object? GetService(Type serviceType) =>
            serviceType == typeof(CurrencyService) ? _currencyService : null;
    }

    /// <summary>Currency codes and amounts are parsed by the default type readers, which never touch the context.</summary>
    private sealed class StubCommandContext : ICommandContext
    {
        public IDiscordClient Client => null!;
        public IGuild Guild => null!;
        public IMessageChannel Channel => null!;
        public IUser User => null!;
        public IUserMessage Message => null!;
    }
}
