using System.IO;
using DiscordBot.Services;
using DiscordBot.Services.Code.Unity;

namespace DiscordBot.Tests.Services;

public class UnityDocDatabaseTests
{
    // Shaped like the real indexes: the page list, then the much larger search data.
    private const string ManualIndex =
        "pages=[[\"2DFeature\",\"2D feature set\"],[\"2Dor3D\",\"2D and 3D projects\"]];\ninfo = [[\"a\",\"b\"]];\nsearchIndex = {\"x\":[1,2,3]};";
    private const string ApiIndex =
        "//\n// Search index for Unity script documentation\n//\nvar pages = \n[[\"AccelerationEvent\", \"AccelerationEvent\"],[\"Animator-speed\", \"Animator.speed\"]];\ninfo = [[\"x\"]];";

    [Fact]
    public async Task ReadUntilAsync_StopsBeforeMarker()
    {
        var text = await UnityDocParser.ReadUntilAsync(new StringReader("abc info = rest"), UnityDocParser.InfoMarker);
        Assert.Equal("abc ", text);
    }

    [Fact]
    public async Task ReadUntilAsync_MarkerAcrossReads_StillFound()
    {
        // A 3-char buffer splits "info =" across reads.
        var text = await UnityDocParser.ReadUntilAsync(new StringReader("0123456info = rest"), UnityDocParser.InfoMarker, bufferSize: 3);
        Assert.Equal("0123456", text);
    }

    [Fact]
    public async Task ReadUntilAsync_NoMarker_ReturnsAll()
    {
        Assert.Equal("no marker here", await UnityDocParser.ReadUntilAsync(new StringReader("no marker here"), UnityDocParser.InfoMarker, bufferSize: 4));
    }

    [Theory]
    [InlineData(ManualIndex, true)]
    [InlineData(ApiIndex, false)]
    public async Task ConvertJsToArray_PrefixGivesSameEntriesAsWholeIndex(string index, bool isManual)
    {
        var prefix = await UnityDocParser.ReadUntilAsync(new StringReader(index), UnityDocParser.InfoMarker, bufferSize: 8);

        Assert.Equal(UnityDocParser.ConvertJsToArray(index, isManual), UnityDocParser.ConvertJsToArray(prefix, isManual));
        Assert.Equal(2, UnityDocParser.ConvertJsToArray(prefix, isManual).Length);
    }

    [Fact]
    public void LastDocDatabaseUpdate_RecordedTimeWins()
    {
        var recorded = new DateTime(2026, 11, 7, 9, 0, 0);
        Assert.Equal(recorded, UpdateService.LastDocDatabaseUpdate(recorded, "/missing/a.json", "/missing/b.json"));
    }

    [Fact]
    public void LastDocDatabaseUpdate_Unset_FallsBackToOlderFileWriteTime()
    {
        var dir = Directory.CreateTempSubdirectory("udc-docs-");
        try
        {
            var manual = Path.Combine(dir.FullName, "unitymanual.json");
            var api = Path.Combine(dir.FullName, "unityapi.json");
            File.WriteAllText(manual, "[]");
            File.WriteAllText(api, "[]");
            var older = DateTime.Now.AddHours(-3);
            File.SetLastWriteTime(api, older);

            Assert.Equal(older, UpdateService.LastDocDatabaseUpdate(default, manual, api));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void LastDocDatabaseUpdate_UnsetAndNoFiles_IsStale()
    {
        Assert.Equal(default, UpdateService.LastDocDatabaseUpdate(default, "/missing/a.json", "/missing/b.json"));
    }
}
