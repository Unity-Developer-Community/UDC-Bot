using System.Text.Json;
using DiscordBot.Services.Recruitment.Policy;
using DiscordBot.Services.Recruitment.State;
using DiscordBot.Settings.Options;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static DiscordBot.Tests.Recruitment.RecruitmentTestData;

namespace DiscordBot.Tests.Recruitment;

[TestClass]
public sealed class RecruitmentStateStoreTests
{
    [TestMethod]
    public async Task MissingState_RequiresExplicitEnrollment_AndRoundTripsExactIds()
    {
        using var root = new StoreRoot();
        await using (var store = root.Store())
        {
            Assert.IsNull(await store.LoadAsync());
            Assert.IsFalse(store.IsHealthy);
            Assert.IsFalse(File.Exists(store.StatePath));
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateAsync(s => true));
            await store.InitializeAsync(Now);
            await store.UpdateAsync(s => { var post = Post(1545677520513540099); s.Posts[post.ThreadId] = post; return true; });
            using var json = JsonDocument.Parse(File.ReadAllText(store.StatePath));
            Assert.AreEqual(JsonValueKind.String, json.RootElement.GetProperty("GuildId").ValueKind);
            Assert.AreEqual("1545677520513540099", json.RootElement.GetProperty("Posts")
                .GetProperty("1545677520513540099").GetProperty("ThreadId").GetString());
        }
        await using var reopened = root.Store();
        var loaded = await reopened.LoadAsync();
        Assert.AreEqual(1L, loaded!.Revision);
        Assert.AreEqual(1545677520513540099ul, loaded.Posts.Single().Key);
        Assert.IsTrue(reopened.IsHealthy);
    }

    [TestMethod]
    public async Task Transactions_SerializeAcceptanceAcrossForums_AndPreserveOtherGroup()
    {
        using var root = new StoreRoot(); await using var store = root.Store();
        await store.LoadAsync(); await store.InitializeAsync(Now);
        await store.UpdateAsync(s =>
        {
            foreach (var post in new[] { Post(1), Post(2, ForumKind.HobbyRecruiting), Post(3, ForumKind.PaidForHire) })
                s.Posts.Add(post.ThreadId, post);
            return true;
        });
        await Task.WhenAll(new ulong[] { 2, 3, 1 }.Select(id => store.UpdateAsync(s => Policy().TryAccept(s, id))));
        var loaded = (await store.LoadAsync())!;
        Assert.AreEqual(2, loaded.Posts.Values.Count(p => p.AcceptedAtUtc is not null));
        Assert.IsNotNull(loaded.Posts[1].AcceptedAtUtc);
        Assert.IsNull(loaded.Posts[2].AcceptedAtUtc);
        Assert.IsNotNull(loaded.Posts[3].AcceptedAtUtc);
        Assert.AreEqual(4L, loaded.Revision);
    }

    [TestMethod]
    public async Task SnapshotsAndCallbackResults_CannotMutateCommittedState()
    {
        using var root = new StoreRoot(); await using var store = root.Store();
        await store.LoadAsync(); await store.InitializeAsync(Now);
        var held = await store.UpdateAsync(s => { s.Posts[10] = Post(); return s; });
        held.Posts.Clear();
        var snapshot = (await store.LoadAsync())!; snapshot.Posts.Clear();
        Assert.AreEqual(1, (await store.LoadAsync())!.Posts.Count);
    }

    [TestMethod]
    public async Task SecondWriter_IsRejected_ThenCanOpenAfterDisposal()
    {
        using var root = new StoreRoot();
        await using var first = root.Store(); await using var second = root.Store();
        await first.LoadAsync(); await first.InitializeAsync(Now);
        await Assert.ThrowsAsync<IOException>(() => second.LoadAsync());
        Assert.IsFalse(second.IsHealthy);
        await first.DisposeAsync();
        Assert.IsNotNull(await second.LoadAsync());
    }

    [TestMethod]
    [DataRow("{broken")]
    [DataRow("{}")]
    public async Task CorruptOrIncompleteDocuments_ArePreservedAndNeverReset(string contents)
    {
        using var root = new StoreRoot(); await using var store = root.Store();
        Directory.CreateDirectory(Path.GetDirectoryName(store.StatePath)!);
        File.WriteAllText(store.StatePath, contents);
        await Assert.ThrowsAsync<JsonException>(() => store.LoadAsync());
        Assert.AreEqual(contents, File.ReadAllText(store.StatePath));
        Assert.IsFalse(store.IsHealthy);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateAsync(s => true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.InitializeAsync(Now));
        Assert.AreEqual(contents, File.ReadAllText(store.StatePath));
    }

    [TestMethod]
    public async Task UnsupportedVersionAndWrongGuild_AreRejectedWithoutChanges()
    {
        using var root = new StoreRoot(); string statePath;
        await using (var store = root.Store())
        {
            await store.LoadAsync(); await store.InitializeAsync(Now); statePath = store.StatePath;
        }
        var original = File.ReadAllText(statePath);
        foreach (var text in new[] { original.Replace("\"SchemaVersion\": 1", "\"SchemaVersion\": 99"),
                     original.Replace("\"GuildId\": \"1\"", "\"GuildId\": \"2\"") })
        {
            File.WriteAllText(statePath, text);
            await using var reopened = root.Store();
            await Assert.ThrowsAsync<InvalidDataException>(() => reopened.LoadAsync());
            Assert.AreEqual(text, File.ReadAllText(statePath));
        }
    }

    [TestMethod]
    public async Task FailedCommit_PreservesSourceAndStopsFurtherWrites()
    {
        using var root = new StoreRoot(); await using var store = root.Store();
        await store.LoadAsync(); await store.InitializeAsync(Now);
        var original = File.ReadAllText(store.StatePath);
        // A directory at the backup destination exercises a real filesystem failure.
        Directory.CreateDirectory(store.BackupPath);
        await Assert.ThrowsAsync<IOException>(() => store.UpdateAsync(s => { s.Posts[10] = Post(); return true; }));
        Assert.AreEqual(original, File.ReadAllText(store.StatePath));
        Assert.IsFalse(store.IsHealthy);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateAsync(s => true));
        Assert.AreEqual(0, Directory.GetFiles(Path.GetDirectoryName(store.StatePath)!, "*.tmp").Length);
    }

    [TestMethod]
    public async Task ExternalEditWithUnchangedRevision_IsDetectedAndPreserved()
    {
        using var root = new StoreRoot(); await using var store = root.Store();
        await store.LoadAsync(); await store.InitializeAsync(Now);
        await store.UpdateAsync(s => { s.Posts[10] = Post(); return true; });
        var external = File.ReadAllText(store.StatePath).Replace("\"Title\": \"\"", "\"Title\": \"External edit\"");
        File.WriteAllText(store.StatePath, external);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.UpdateAsync(s => true));
        Assert.AreEqual(external, File.ReadAllText(store.StatePath));
        Assert.IsFalse(store.IsHealthy);
    }

    [TestMethod]
    public async Task ExplicitRecovery_ValidatesBackupAndPreservesDamagedSource()
    {
        using var root = new StoreRoot(); string path;
        await using (var store = root.Store())
        {
            await store.LoadAsync(); await store.InitializeAsync(Now);
            await store.UpdateAsync(s => { s.Posts[10] = Post(); return true; });
            await store.UpdateAsync(s => { s.Posts[20] = Post(20); return true; });
            path = store.StatePath;
        }
        File.WriteAllText(path, "broken");
        await using var restored = root.Store();
        await Assert.ThrowsAsync<JsonException>(() => restored.LoadAsync());
        await restored.RestoreBackupAsync();
        Assert.AreEqual(1L, (await restored.LoadAsync())!.Revision);
        Assert.AreEqual(1, (await restored.LoadAsync())!.Posts.Count);
        var preserved = Directory.GetFiles(Path.GetDirectoryName(path)!, "*.replaced-*").Single();
        Assert.AreEqual("broken", File.ReadAllText(preserved));
    }

    [TestMethod]
    public async Task LostPrimaryWithBackup_CannotBeEnrolledAsEmptyHistory()
    {
        using var root = new StoreRoot(); string path;
        await using (var store = root.Store())
        {
            await store.LoadAsync(); await store.InitializeAsync(Now); await store.UpdateAsync(s => true);
            path = store.StatePath;
        }
        File.Delete(path);
        await using var reopened = root.Store();
        Assert.IsNull(await reopened.LoadAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => reopened.InitializeAsync(Now));
        await reopened.RestoreBackupAsync();
        Assert.IsTrue(reopened.IsHealthy);
    }

    [TestMethod]
    public async Task InvalidTransitionAndCancellation_DoNotChangeSource()
    {
        using var root = new StoreRoot(); await using var store = root.Store();
        await store.LoadAsync(); await store.InitializeAsync(Now);
        var original = File.ReadAllText(store.StatePath);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.UpdateAsync(s =>
        {
            var post = Post(); post.Acknowledgement = AcknowledgementStatus.Pending;
            s.Posts[10] = post; return true;
        }));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.UpdateAsync(s => true, cancelled.Token));
        Assert.AreEqual(original, File.ReadAllText(store.StatePath));
        Assert.IsTrue(store.IsHealthy);
    }

    private sealed class StoreRoot : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "udc-recruitment-" + Guid.NewGuid().ToString("N"));
        public StateStore Store() => new(
            Microsoft.Extensions.Options.Options.Create(new StorageOptions { ServerRootPath = _path }),
            Microsoft.Extensions.Options.Options.Create(new DiscordGuildOptions { GuildId = 1 }));
        public void Dispose() { if (Directory.Exists(_path)) Directory.Delete(_path, recursive: true); }
    }
}
