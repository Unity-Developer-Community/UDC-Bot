using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DiscordBot.Tests.Architecture;

[TestClass]
public sealed class ConfigurationBoundaryTests
{
    [TestMethod]
    public void Modules_DoNotReferenceLegacyOrSecretConfigurationTypes()
    {
        var repositoryRoot = GetRepositoryRoot();
        var moduleRoot = Path.Combine(repositoryRoot, "DiscordBot", "Modules");
        var forbidden = new[]
        {
            "BotSettings",
            "DiscordConnectionOptions",
            "DatabaseOptions",
            "WeatherOptions",
            "AirportOptions"
        };

        var violations = Directory.EnumerateFiles(moduleRoot, "*.cs", SearchOption.AllDirectories)
            .SelectMany(path => forbidden
                .Where(name => File.ReadAllText(path).Contains(name, StringComparison.Ordinal))
                .Select(name => $"{Path.GetRelativePath(repositoryRoot, path)} references {name}"))
            .ToArray();

        Assert.IsEmpty(violations, string.Join(Environment.NewLine, violations));
    }

    [TestMethod]
    public void Services_DoNotReferenceLegacyBotSettings()
    {
        var repositoryRoot = GetRepositoryRoot();
        var serviceRoot = Path.Combine(repositoryRoot, "DiscordBot", "Services");
        var violations = Directory.EnumerateFiles(serviceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("BotSettings", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(repositoryRoot, path))
            .ToArray();

        Assert.IsEmpty(violations, string.Join(Environment.NewLine, violations));
    }

    private static string GetRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "DiscordBot.sln")))
            current = current.Parent;

        return current?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
