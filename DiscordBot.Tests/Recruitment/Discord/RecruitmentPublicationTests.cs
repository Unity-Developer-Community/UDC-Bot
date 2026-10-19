using DiscordBot.Services.Recruitment.Infrastructure.Discord;
using System.Net;
using System.Text;
using Discord;
using Discord.Net.Rest;
using Discord.WebSocket;
using DiscordBot.Services.Recruitment;
using DiscordBot.Settings;
using Newtonsoft.Json.Linq;

namespace DiscordBot.Tests.Recruitment;

// The transport's synthetic global rate limit is process-wide, including across SDK clients.
[Collection("Recruitment transport")]
public class RecruitmentPublicationTests
{
    [Theory]
    [InlineData(RecruitmentMessageFormat.Standard, false)]
    [InlineData(RecruitmentMessageFormat.Standard, true)]
    [InlineData(RecruitmentMessageFormat.ComponentsV2, false)]
    [InlineData(RecruitmentMessageFormat.ComponentsV2, true)]
    public async Task SharedRecoveryDistinguishesCoveredAbsenceFromExhaustedHistory(RecruitmentMessageFormat format, bool incomplete)
    {
        var transport = new PublicationTransport { MessageMissing = true, LongHistory = incomplete };
        using var client = await Client(transport);
        var channel = (IMessageChannel)(await client.Rest.GetChannelAsync(100))!;
        transport.Message["author"]!["id"] = "200";
        transport.Message["timestamp"] = DateTimeOffset.UtcNow.ToString("O");
        var result = await OwnedMessageLookup.FindAsync(channel, 999, null, true, DateTimeOffset.UtcNow.AddDays(-1),
            "recruitment:100:public", format, default);
        Assert.Equal(incomplete ? MessageLookupStatus.Incomplete : MessageLookupStatus.Absent, result.Status);
        Assert.Null(result.Message);
        if (incomplete) Assert.Throws<InvalidOperationException>(() => result.RequireCovered());
        else Assert.Null(result.RequireCovered());
        Assert.Empty(transport.MessageWrites);
    }

    [Theory]
    [InlineData("50", false, true, 1)]
    [InlineData("51", false, true, 1)]
    [InlineData("52", false, false, 1)]
    [InlineData("1", true, false, 0)]
    public async Task InstalledSdkSnapshotsStaffBotsAndRoles(string role, bool bot, bool staff, int roleCount)
    {
        var transport = new PublicationTransport { Role = role, OwnerBot = bot };
        using var client = await Client(transport);
        var thread = await new DiscordRecruitmentAdapter(client, Settings()).GetThreadAsync("100", default);
        Assert.Equal(staff, thread!.OwnerIsStaff); Assert.Equal(bot, thread.OwnerIsBot);
        Assert.Equal(roleCount, thread.MemberRoleCount);
    }

    [Theory]
    [InlineData("Server", "Global", "Server")]
    [InlineData(null, "Global", "Global")]
    [InlineData(null, null, "member")]
    public async Task InstalledSdkCapturesIdentityFallbacks(string? nickname, string? globalName, string expected)
    {
        var transport = new PublicationTransport { Nickname = nickname, GlobalName = globalName };
        using var client = await Client(transport);
        var thread = await new DiscordRecruitmentAdapter(client, Settings()).GetThreadAsync("100", default);
        Assert.Equal(expected, thread!.ServerNickname ?? thread.GlobalName ?? thread.Username);
    }

    [Fact]
    public async Task InstalledSdkCreatesAndPinsReferenceWithoutChangingOtherFlagsOrMessages()
    {
        var transport = new PublicationTransport(); using var client = await Client(transport);
        var adapter = new DiscordRecruitmentAdapter(client, Settings());
        const string marker = "recruitment:reference:instance:11";
        var id = await adapter.CreateReferenceAsync("11", "71", marker, default);
        Assert.Equal("100", id);
        Assert.Equal("71", (string?)transport.CreatePayload!["applied_tags"]![0]);
        var content = transport.StarterContent;
        Assert.True(await adapter.SecureReferenceAsync("11", id, true, marker, default));
        Assert.Equal(32770, (int)transport.Thread["flags"]!);
        Assert.False((bool)transport.Thread["thread_metadata"]!["archived"]!);
        Assert.True((bool)transport.Thread["thread_metadata"]!["locked"]!);
        Assert.Equal(content, transport.StarterContent);
        Assert.DoesNotContain(transport.ChannelWrites, w => w["permission_overwrites"] is not null);
        var writes = transport.ChannelWrites.Count;
        Assert.True(await adapter.SecureReferenceAsync("11", id, true, marker, default));
        Assert.Equal(writes, transport.ChannelWrites.Count);
    }

    [Fact]
    public async Task InstalledSdkUpdatesFieldOnlyChangesAndRemovesLegacyBannerOnce()
    {
        var transport = new PublicationTransport(); using var client = await Client(transport);
        var adapter = new DiscordRecruitmentAdapter(client, Settings());
        const string marker = "recruitment:100:public";
        var embed = new EmbedBuilder().WithTitle("Recruitment").WithDescription("Listing active").AddField("Payment", "Rate detected").Build();
        var message = new RecruitmentMessage($"||{marker}||", marker, false, Embed: embed);
        var writes = 0;
        await adapter.UpsertAsync("100", "101", false, DateTimeOffset.UtcNow, message, () => writes++, default);
        Assert.Equal(1, writes);
        var patch = Assert.Single(transport.MessageWrites);
        Assert.Empty((JArray)patch["attachments"]!);
        Assert.Equal("Rate detected", (string?)patch["embeds"]![0]!["fields"]![0]!["value"]);
        await adapter.UpsertAsync("100", "101", false, DateTimeOffset.UtcNow, message, () => writes++, default);
        Assert.Equal(1, writes);
        var corrected = embed.ToEmbedBuilder().WithFields(new EmbedFieldBuilder().WithName("Payment").WithValue("No rate detected")).Build();
        await adapter.UpsertAsync("100", "101", false, DateTimeOffset.UtcNow, message with { Embed = corrected }, () => writes++, default);
        Assert.Equal(2, writes);
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("global-hash", null, true)]
    [InlineData(null, "server-hash", true)]
    public async Task InstalledSdkUsesEffectiveAvatarAndMediaAttachments(string? globalAvatar, string? serverAvatar, bool custom)
    {
        var transport = new PublicationTransport { GlobalAvatar = globalAvatar, ServerAvatar = serverAvatar,
            Attachments = new JArray(new JObject { ["id"] = "800", ["filename"] = "demo.mp4", ["content_type"] = "video/mp4", ["size"] = 100,
                ["url"] = "https://cdn.discordapp.com/demo.mp4", ["proxy_url"] = "https://cdn.discordapp.com/demo.mp4" }) };
        using var client = await Client(transport);
        var thread = await new DiscordRecruitmentAdapter(client, Settings()).GetThreadAsync("100", default);
        Assert.Equal(custom, thread!.HasCustomAvatar);
        Assert.Equal("member", thread.Username); Assert.Equal("Artist", thread.GlobalName); Assert.Equal("Server Artist", thread.ServerNickname);
        Assert.NotNull(thread.AvatarUrl);
        Assert.Equal(serverAvatar ?? globalAvatar, thread.AvatarIdentity);
        Assert.Equal("media sample attached", RecruitmentContent.Portfolio(thread.Starter, thread.Attachments));
    }

    [Fact]
    public async Task InstalledSdkRemovesAcknowledgementButtonWhileKeepingReaderAndOwnerControls()
    {
        var transport = new PublicationTransport(); using var client = await Client(transport);
        var adapter = new DiscordRecruitmentAdapter(client, Settings());
        const string marker = "recruitment:100:public";
        var message = new RecruitmentMessage($"||{marker}||", marker, true, AcknowledgementRequired: true, ReaderControls: true);
        await adapter.UpsertAsync("100", "101", false, DateTimeOffset.UtcNow, message, () => { }, default);
        Assert.Contains("recruit:ack:100", transport.MessageWrites.Last().ToString());
        await adapter.UpsertAsync("100", "101", false, DateTimeOffset.UtcNow, message with { AcknowledgementRequired = false }, () => { }, default);
        var controls = transport.MessageWrites.Last()["components"]!.ToString();
        Assert.DoesNotContain("recruit:ack:", controls); Assert.Contains("recruit:report:100", controls);
        Assert.Contains("recruit:details:100", controls); Assert.Contains("recruit:owner:100", controls);
        Assert.DoesNotContain("recruit:remove:", controls);
        var writes = transport.MessageWrites.Count;
        await adapter.UpsertAsync("100", "101", false, DateTimeOffset.UtcNow, message with { AcknowledgementRequired = false }, () => { }, default);
        Assert.Equal(writes, transport.MessageWrites.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InstalledSdkAuthorNoticeMentionsOnlyAuthorOnCreationAndNeverOnRecoveryOrEdit(bool loseResponse)
    {
        var transport = new PublicationTransport { MessageMissing = true, LoseCreateResponse = loseResponse };
        transport.Message["attachments"] = new JArray(); transport.Message["embeds"] = new JArray();
        using var client = await Client(transport);
        var adapter = new DiscordRecruitmentAdapter(client, Settings());
        const string marker = "recruitment:100:acknowledgement";
        var message = new RecruitmentMessage($"<@200> please acknowledge. <@201> @everyone <@&50>\n||{marker}||",
            marker, false, MentionUserId: 200);
        var error = await Record.ExceptionAsync(() => adapter.UpsertAsync("100", null, false, DateTimeOffset.UtcNow,
            message, () => { }, default));
        if (loseResponse) Assert.IsType<InvalidOperationException>(error); else Assert.Null(error);
        var create = Assert.Single(transport.MessageWrites);
        var mentions = create["allowed_mentions"]!;
        Assert.Empty((JArray)mentions["parse"]!);
        Assert.Equal(new[] { "200" }, ((JArray)mentions["users"]!).Values<string>());
        Assert.False(mentions["roles"]?.Values<string>().Any() ?? false);
        var recovered = await adapter.UpsertAsync("100", null, true, DateTimeOffset.UtcNow, message, () => { }, default);
        Assert.False(recovered.NewlySent); Assert.Single(transport.MessageWrites);
        await adapter.UpsertAsync("100", recovered.Id, false, DateTimeOffset.UtcNow,
            message with { Content = message.Content + "\nUpdated deadline." }, () => { }, default);
        var edit = transport.MessageWrites.Last()["allowed_mentions"]!;
        Assert.Empty((JArray)edit["parse"]!);
        Assert.False(edit["users"]?.Values<string>().Any() ?? false);
    }

    [Fact]
    public async Task DepartedMemberLeavesProfileFactsUnavailableWithoutLosingStarter()
    {
        var transport = new PublicationTransport { MissingMember = true }; using var client = await Client(transport);
        var adapter = new DiscordRecruitmentAdapter(client, Settings());
        var thread = await adapter.GetThreadAsync("100", default);
        Assert.NotNull(thread); Assert.NotNull(thread.Starter);
        Assert.Null(thread.JoinedAt); Assert.Null(thread.HasCustomAvatar); Assert.Null(thread.MemberRoleCount);
        Assert.False(await adapter.IsMemberAsync("1", "200", default));
    }

    private static RecruitmentMessage V2(string text = "Facts", bool ack = true, bool media = true)
    {
        MessageComponent Build(bool image)
        {
            var children = new List<IMessageComponentBuilder> { new TextDisplayBuilder(text), new TextDisplayBuilder("||recruitment:100:public||") };
            if (image) children.Add(new MediaGalleryBuilder().AddItem("attachment://facts-" + text + ".png", "All nine facts"));
            if (ack) children.Add(new ActionRowBuilder().WithButton("Acknowledge", "recruit:ack:100"));
            return new ComponentBuilderV2(new[] { new ContainerBuilder(children) }).Build();
        }
        return new RecruitmentMessage("", "recruitment:100:public", true, Format: RecruitmentMessageFormat.ComponentsV2,
            Components: Build(media), FallbackComponents: Build(false), Media: media ? new("facts-" + text + ".png", new byte[] { 1, 2, 3 }, "All nine facts") : null);
    }

    [Fact]
    public async Task V2InstalledSdkCreatesRetainsAndReplacesMediaWithoutAccumulation()
    {
        var transport = new PublicationTransport { MessageMissing = true }; using var client = await Client(transport);
        var channel = (IMessageChannel)(await client.Rest.GetChannelAsync(100))!;
        var writes = 0;
        var first = await V2MessageWriter.UpsertAsync(channel, 999, null, false, DateTimeOffset.UtcNow, V2(), true, () => writes++, default);
        Assert.True(first.NewlySent); Assert.Equal("901", first.MediaAttachmentId);
        Assert.Equal(1, transport.Uploads);
        var create = Assert.Single(transport.MessageWrites);
        Assert.Equal(32768, (int)create["flags"]!);
        Assert.Null(create["content"]);
        Assert.Null(create["embeds"]);
        Assert.True(create["allowed_mentions"]!["parse"] is null or { Type: JTokenType.Null } || !create["allowed_mentions"]!["parse"]!.Any());
        var same = await V2MessageWriter.UpsertAsync(channel, 999, first.Id, false, DateTimeOffset.UtcNow, V2(), true, () => writes++, default);
        Assert.False(same.NewlySent); Assert.Equal(1, writes); Assert.Equal(1, transport.Uploads);
        await V2MessageWriter.UpsertAsync(channel, 999, first.Id, false, DateTimeOffset.UtcNow, V2(ack: false), true, () => writes++, default);
        Assert.Equal(2, writes); Assert.Equal(1, transport.Uploads);
        var retain = transport.MessageWrites.Last();
        Assert.Equal("901", (string?)retain["attachments"]![0]!["id"]);
        Assert.True(retain["allowed_mentions"]!["parse"] is null or { Type: JTokenType.Null } || !retain["allowed_mentions"]!["parse"]!.Any());
        Assert.DoesNotContain("Acknowledge", retain["components"]!.ToString());
        await V2MessageWriter.UpsertAsync(channel, 999, first.Id, false, DateTimeOffset.UtcNow, V2("Changed"), true, () => writes++, default);
        Assert.Equal(2, transport.Uploads); Assert.Single((JArray)transport.Message["attachments"]!);
        Assert.Equal("902", (string?)transport.Message["attachments"]![0]!["id"]);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task V2OptionalMediaUnavailableKeepsTextAndControls(bool mediaAvailable, bool renderSucceeded)
    {
        var transport = new PublicationTransport { MessageMissing = true }; using var client = await Client(transport);
        var channel = (IMessageChannel)(await client.Rest.GetChannelAsync(100))!;
        await V2MessageWriter.UpsertAsync(channel, 999, null, false, DateTimeOffset.UtcNow, V2(media: renderSucceeded), mediaAvailable, () => { }, default);
        Assert.Equal(0, transport.Uploads);
        var create = Assert.Single(transport.MessageWrites);
        Assert.Equal(32768, (int)create["flags"]!);
        Assert.Contains("Acknowledge", create["components"]!.ToString()); Assert.Contains("Facts", create["components"]!.ToString());
        Assert.DoesNotContain("attachment://", create.ToString());
    }

    [Fact]
    public async Task V2RecoveryUsesExactOwnedMarkerAndHoldsOnIncompleteScan()
    {
        var transport = new PublicationTransport { MessageMissing = true }; using var client = await Client(transport);
        var channel = (IMessageChannel)(await client.Rest.GetChannelAsync(100))!;
        transport.LoseCreateResponse = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => V2MessageWriter.UpsertAsync(channel, 999, null, false, DateTimeOffset.UtcNow, V2(media: false), true, () => { }, default));
        // Restart/lost create response: no stored ID, recover from component marker.
        var recovered = await V2MessageWriter.UpsertAsync(channel, 999, null, true, DateTimeOffset.UtcNow, V2(media: false), true, () => { }, default);
        Assert.Equal("101", recovered.Id); Assert.False(recovered.NewlySent); Assert.Single(transport.MessageWrites);
        transport.Message["author"]!["id"] = "200";
        await Assert.ThrowsAsync<InvalidOperationException>(() => V2MessageWriter.UpsertAsync(channel, 999, "101", false,
            DateTimeOffset.UtcNow, V2(media: false), true, () => { }, default));
        transport.LongHistory = true; transport.Message["timestamp"] = DateTimeOffset.UtcNow.ToString("O");
        await Assert.ThrowsAsync<InvalidOperationException>(() => V2MessageWriter.UpsertAsync(channel, 999, null, true,
            DateTimeOffset.UtcNow.AddDays(-1), V2(media: false), true, () => { }, default));
        Assert.Single(transport.MessageWrites);
    }

    [Fact]
    public async Task V2LostEditResponseRestoresArchiveAndRetainsOneUploadAfterRestart()
    {
        var transport = new PublicationTransport { MessageMissing = true }; using var client = await Client(transport);
        var channel = (IMessageChannel)(await client.Rest.GetChannelAsync(100))!;
        await V2MessageWriter.UpsertAsync(channel, 999, null, false, DateTimeOffset.UtcNow, V2(), true, () => { }, default);
        transport.Thread["thread_metadata"]!["archived"] = true; transport.Thread["thread_metadata"]!["locked"] = true;
        channel = (IMessageChannel)(await client.Rest.GetChannelAsync(100))!;
        transport.LoseEditResponse = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => V2MessageWriter.UpsertAsync(channel, 999, "101", false,
            DateTimeOffset.UtcNow, V2("Changed") with { RestoreArchive = true, RestoreLocked = true }, true, () => { }, default));
        channel = (IMessageChannel)(await client.Rest.GetChannelAsync(100))!;
        var recovered = await V2MessageWriter.UpsertAsync(channel, 999, "101", true, DateTimeOffset.UtcNow,
            V2("Changed") with { RestoreArchive = true, RestoreLocked = true }, true, () => { }, default);
        Assert.False(recovered.NewlySent); Assert.Equal(2, transport.Uploads); Assert.Equal(2, transport.MessageWrites.Count);
        Assert.True((bool)transport.Thread["thread_metadata"]!["archived"]!); Assert.True((bool)transport.Thread["thread_metadata"]!["locked"]!);
    }

    [Fact]
    public async Task V2RejectedMediaWriteFallsBackAndRestoresArchivedLock()
    {
        var transport = new PublicationTransport { MessageMissing = true }; using var client = await Client(transport);
        var channel = (IMessageChannel)(await client.Rest.GetChannelAsync(100))!;
        await V2MessageWriter.UpsertAsync(channel, 999, null, false, DateTimeOffset.UtcNow, V2(), true, () => { }, default);
        transport.Thread["thread_metadata"]!["archived"] = true; transport.Thread["thread_metadata"]!["locked"] = true;
        transport.RejectUpload = true;
        channel = (IMessageChannel)(await client.Rest.GetChannelAsync(100))!;
        var delivery = await V2MessageWriter.UpsertAsync(channel, 999, "101", false, DateTimeOffset.UtcNow,
            V2("Changed") with { RestoreArchive = true, RestoreLocked = true }, true, () => { }, default);
        Assert.Null(delivery.MediaAttachmentId); Assert.False(delivery.NewlySent);
        Assert.True((bool)transport.Thread["thread_metadata"]!["archived"]!); Assert.True((bool)transport.Thread["thread_metadata"]!["locked"]!);
        Assert.Empty((JArray)transport.Message["attachments"]!);
        Assert.Contains("Acknowledge", transport.Message["components"]!.ToString()); Assert.DoesNotContain("attachment://", transport.Message["components"]!.ToString());
    }

    [Fact]
    public async Task V2OwnershipRequiresExactRecursiveMarkerBotChannelAndFlag()
    {
        var transport = new PublicationTransport { MessageMissing = true }; using var client = await Client(transport);
        var adapter = new DiscordRecruitmentAdapter(client, Settings());
        var channel = (IMessageChannel)(await client.Rest.GetChannelAsync(100))!;
        await V2MessageWriter.UpsertAsync(channel, 999, null, false, DateTimeOffset.UtcNow, V2(media: false), true, () => { }, default);
        var owned = (IUserMessage)(await channel.GetMessageAsync(101))!;
        Assert.True(OwnedMessageLookup.OwnsMessage(owned, 999, 100, "recruitment:100:public", RecruitmentMessageFormat.ComponentsV2));
        Assert.False(OwnedMessageLookup.OwnsMessage(owned, 200, 100, "recruitment:100:public", RecruitmentMessageFormat.ComponentsV2));
        Assert.False(OwnedMessageLookup.OwnsMessage(owned, 999, 200, "recruitment:100:public", RecruitmentMessageFormat.ComponentsV2));
        Assert.False(OwnedMessageLookup.OwnsMessage(owned, 999, 100, "recruitment:100:pub", RecruitmentMessageFormat.ComponentsV2));
        transport.Message["flags"] = 0;
        Assert.False(OwnedMessageLookup.OwnsMessage((IUserMessage)(await channel.GetMessageAsync(101))!, 999, 100, "recruitment:100:public", RecruitmentMessageFormat.ComponentsV2));
        transport.Message["flags"] = 32768;
        await adapter.DeleteAdvisoryAsync("100", "101", default, "recruitment:100:public");
        Assert.Equal(1, transport.Deletes);
    }

    [Fact]
    public async Task V2CancellationDoesNotWriteAndUnscopedSdkMessagesStayUntouched()
    {
        var transport = new PublicationTransport { MessageMissing = true }; using var client = await Client(transport);
        var channel = (IMessageChannel)(await client.Rest.GetChannelAsync(100))!;
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => V2MessageWriter.UpsertAsync(channel, 999, null, false,
            DateTimeOffset.UtcNow, V2(), true, () => { }, cancellation.Token));
        Assert.Empty(transport.MessageWrites);
        await channel.SendMessageAsync("Ordinary content");
        Assert.Equal("Ordinary content", (string?)transport.MessageWrites.Single()["content"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task V2RecoversComponentOnlyAttachmentIdentityThroughInstalledSdk(bool cdnUrl)
    {
        var transport = new PublicationTransport { MessageMissing = true, ComponentOnlyMedia = true, CdnMediaUrl = cdnUrl };
        using var client = await Client(transport);
        var channel = (IMessageChannel)(await client.Rest.GetChannelAsync(100))!;
        var delivered = await V2MessageWriter.UpsertAsync(channel, 999, null, false, DateTimeOffset.UtcNow, V2(), true, () => { }, default);
        Assert.Equal("901", delivered.MediaAttachmentId);
        using var restarted = await Client(transport);
        channel = (IMessageChannel)(await restarted.Rest.GetChannelAsync(100))!;
        var recovered = await V2MessageWriter.UpsertAsync(channel, 999, null, true, DateTimeOffset.UtcNow, V2(), true, () => { }, default);
        Assert.Equal("901", recovered.MediaAttachmentId); Assert.Single(transport.MessageWrites); Assert.Equal(1, transport.Uploads);
        await V2MessageWriter.UpsertAsync(channel, 999, recovered.Id, false, DateTimeOffset.UtcNow, V2(ack: false), true, () => { }, default);
        Assert.Equal(1, transport.Uploads); Assert.Equal("901", (string?)transport.MessageWrites.Last()["attachments"]![0]!["id"]);
    }

    [Fact]
    public async Task V2DeniedMediaPayloadOmitsAvatarThumbnailToo()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post();
        post.Facts.AvatarUrl = "https://cdn.discordapp.com/avatars/200/avatar.png"; post.Facts.ServerNickname = "Safe name";
        var message = RecruitmentPresentation.PublicV2(rig.State, post, rig.Settings.Recruitment, rig.Clock.Now);
        var transport = new PublicationTransport { MessageMissing = true }; using var client = await Client(transport);
        var channel = (IMessageChannel)(await client.Rest.GetChannelAsync(100))!;
        await V2MessageWriter.UpsertAsync(channel, 999, null, false, DateTimeOffset.UtcNow, message, false, () => { }, default);
        var payload = Assert.Single(transport.MessageWrites);
        Assert.DoesNotContain("cdn.discordapp.com", payload.ToString()); Assert.DoesNotContain("attachment://", payload.ToString());
        Assert.DoesNotContain(payload["components"]!.SelectTokens("$..type"), t => (int)t is 11 or 12);
        Assert.Contains("Safe name", payload.ToString()); Assert.Contains("Acknowledge", payload.ToString());
    }

    [Fact]
    public async Task V2AuthorNameOnlyEditRetainsUploadedImageAndCannotPing()
    {
        using var rig = new RecruitmentRig(); await rig.Start(); var post = await rig.Post();
        post.Facts.ServerNickname = "Original name";
        var message = RecruitmentPresentation.PublicV2(rig.State, post, rig.Settings.Recruitment, rig.Clock.Now);
        var transport = new PublicationTransport { MessageMissing = true }; using var client = await Client(transport);
        var channel = (IMessageChannel)(await client.Rest.GetChannelAsync(100))!;
        var sent = await V2MessageWriter.UpsertAsync(channel, 999, null, false, DateTimeOffset.UtcNow, message, true, () => { }, default);
        post.Facts.ServerNickname = "**<@123> New name**";
        var renamed = RecruitmentPresentation.PublicV2(rig.State, post, rig.Settings.Recruitment, rig.Clock.Now);
        await V2MessageWriter.UpsertAsync(channel, 999, sent.Id, false, DateTimeOffset.UtcNow, renamed, true, () => { }, default);
        Assert.Equal(1, transport.Uploads); Assert.Equal(2, transport.MessageWrites.Count);
        Assert.DoesNotContain("<@123>", transport.MessageWrites.Last().ToString()); Assert.Contains("New name", transport.MessageWrites.Last().ToString());
        Assert.Equal("901", (string?)transport.MessageWrites.Last()["attachments"]![0]!["id"]);
    }

    [Fact]
    public async Task V2RepairsInterruptedLockRestorationWithoutMessageRewrite()
    {
        var transport = new PublicationTransport { MessageMissing = true }; using var client = await Client(transport);
        var channel = (IMessageChannel)(await client.Rest.GetChannelAsync(100))!;
        await V2MessageWriter.UpsertAsync(channel, 999, null, false, DateTimeOffset.UtcNow, V2(media: false), true, () => { }, default);
        transport.Thread["thread_metadata"]!["archived"] = true; transport.Thread["thread_metadata"]!["locked"] = false;
        channel = (IMessageChannel)(await client.Rest.GetChannelAsync(100))!;
        var saves = 0;
        await V2MessageWriter.UpsertAsync(channel, 999, "101", true, DateTimeOffset.UtcNow,
            V2(media: false) with { RestoreArchive = true, RestoreLocked = true }, true, () => saves++, default);
        Assert.Equal(1, saves); Assert.Single(transport.MessageWrites); Assert.True((bool)transport.Thread["thread_metadata"]!["locked"]!);
    }

    [Fact]
    public async Task V2ConfirmedMissingMessageIsRecreatedWithUsableControls()
    {
        var transport = new PublicationTransport { MessageMissing = true }; using var client = await Client(transport);
        var channel = (IMessageChannel)(await client.Rest.GetChannelAsync(100))!;
        var sent = await V2MessageWriter.UpsertAsync(channel, 999, "101", false, DateTimeOffset.UtcNow, V2(media: false), true, () => { }, default);
        Assert.True(sent.NewlySent); Assert.Single(transport.MessageWrites); Assert.Contains("Acknowledge", transport.Message.ToString());
    }

    [Fact]
    public async Task InstalledSdkPinCorrectionIsScopedToExactMessageAndLeavesOrdinaryPinsAlone()
    {
        var transport = new PublicationTransport { MessageMissing = true }; using var client = await Client(transport);
        var channel = (IThreadChannel)(await client.Rest.GetChannelAsync(100))!;
        await V2MessageWriter.UpsertAsync(channel, 999, "101", false, DateTimeOffset.UtcNow, V2(media: false), true, () => { }, default);
        Assert.True(await DiscordRecruitmentAdapter.PinOwnedAsync(channel, 999, 101, true, false, false, (_, _) => { }, default));
        Assert.Equal(new[] { "channels/100/messages/pins/101" }, transport.PinRoutes);
        var message = (IUserMessage)(await channel.GetMessageAsync(101))!;
        await message.PinAsync();
        Assert.Equal("channels/100/pins/101", transport.PinRoutes.Last());
        using (RecruitmentMessageTransport.Pin(100, 101))
        {
            Assert.Equal("channels/100/pins/102", RecruitmentMessageTransport.PinEndpoint("PUT", "channels/100/pins/102"));
            Assert.Equal("channels/200/pins/101", RecruitmentMessageTransport.PinEndpoint("PUT", "channels/200/pins/101"));
            Assert.Equal("channels/100/pins/101", RecruitmentMessageTransport.PinEndpoint("DELETE", "channels/100/pins/101"));
        }
        Assert.Equal(32768, (int)transport.Thread["flags"]!);
        Assert.Equal(0, transport.Deletes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task OptionalPinWithoutPermissionDoesNotWrite(bool? allowed)
    {
        var transport = new PublicationTransport { MessageMissing = true }; using var client = await Client(transport);
        var channel = (IThreadChannel)(await client.Rest.GetChannelAsync(100))!;
        await V2MessageWriter.UpsertAsync(channel, 999, "101", false, DateTimeOffset.UtcNow, V2(media: false), true, () => { }, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => DiscordRecruitmentAdapter.PinOwnedAsync(channel, 999, 101, allowed, false, false, (_, _) => { }, default));
        Assert.Empty(transport.PinRoutes); Assert.Empty(transport.ChannelWrites);
    }

    [Fact]
    public async Task LostPinResponseRestoresArchivedClosedThreadWhenMessageAlreadyPinned()
    {
        var transport = new PublicationTransport { MessageMissing = true }; using var client = await Client(transport);
        var channel = (IThreadChannel)(await client.Rest.GetChannelAsync(100))!;
        await V2MessageWriter.UpsertAsync(channel, 999, "101", false, DateTimeOffset.UtcNow, V2(media: false), true, () => { }, default);
        transport.Thread["thread_metadata"]!["archived"] = true; transport.Thread["thread_metadata"]!["locked"] = true;
        channel = (IThreadChannel)(await client.Rest.GetChannelAsync(100))!;
        transport.LosePinResponse = true; transport.FailRestoreOnce = true;
        var receipt = new MessagePinReceipt { MessageId = "101" };
        void SaveIntent(bool archive, bool locked) { receipt.RestoreArchive = archive; receipt.RestoreLocked = locked; }
        await Assert.ThrowsAsync<TimeoutException>(() => DiscordRecruitmentAdapter.PinOwnedAsync(channel, 999, 101, true, false, false, SaveIntent, default));
        Assert.True(receipt.RestoreArchive); Assert.True(receipt.RestoreLocked);
        Assert.False((bool)transport.Thread["thread_metadata"]!["archived"]!);
        Assert.True((bool)transport.Message["pinned"]!);
        channel = (IThreadChannel)(await client.Rest.GetChannelAsync(100))!;
        Assert.True(await DiscordRecruitmentAdapter.PinOwnedAsync(channel, 999, 101, false, receipt.RestoreArchive, receipt.RestoreLocked, SaveIntent, default));
        Assert.Single(transport.PinRoutes);
        Assert.True((bool)transport.Thread["thread_metadata"]!["archived"]!);
        Assert.True((bool)transport.Thread["thread_metadata"]!["locked"]!);
        Assert.Equal(32768, (int)transport.Thread["flags"]!);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(429)]
    public async Task InstalledSdkArchivedPinFailureRestoresThreadAndKeepsFailureCosmetic(int status)
    {
        var transport = new PublicationTransport { MessageMissing = true }; using var client = await Client(transport);
        var channel = (IThreadChannel)(await client.Rest.GetChannelAsync(100))!;
        await V2MessageWriter.UpsertAsync(channel, 999, "101", false, DateTimeOffset.UtcNow, V2(media: false), true, () => { }, default);
        transport.Thread["thread_metadata"]!["archived"] = true; transport.Thread["thread_metadata"]!["locked"] = true;
        channel = (IThreadChannel)(await client.Rest.GetChannelAsync(100))!;
        transport.PinStatus = status;
        var receipt = new MessagePinReceipt { MessageId = "101" };
        var error = await Record.ExceptionAsync(() => DiscordRecruitmentAdapter.PinOwnedAsync(channel, 999, 101, true, false, false,
            (archive, locked) => { receipt.RestoreArchive = archive; receipt.RestoreLocked = locked; }, default));
        Assert.NotNull(error);
        // A 429 also defers restoration in the same channel. The saved intent retries once
        // that server deadline passes, without requiring Pin Messages permission.
        if (status == 429)
        {
            var deferred = Assert.IsType<RecruitmentDeferredException>(error);
            Assert.True(receipt.RestoreArchive);
            await Task.Delay(deferred.RetryAt - DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(100));
            channel = (IThreadChannel)(await client.Rest.GetChannelAsync(100))!;
            transport.PinStatus = 0;
            await Assert.ThrowsAsync<InvalidOperationException>(() => DiscordRecruitmentAdapter.PinOwnedAsync(channel, 999, 101, false,
                receipt.RestoreArchive, receipt.RestoreLocked, (archive, locked) => { receipt.RestoreArchive = archive; receipt.RestoreLocked = locked; }, default));
        }
        else Assert.IsType<Discord.Net.HttpException>(error);
        Assert.True((bool)transport.Thread["thread_metadata"]!["archived"]!);
        Assert.True((bool)transport.Thread["thread_metadata"]!["locked"]!);
        Assert.False(receipt.RestoreArchive); Assert.Single(transport.PinRoutes);
        var fault = RecruitmentFaultClassifier.From(error!, DateTimeOffset.UtcNow);
        Assert.Equal(status == 429, fault.RetryAt.HasValue);
        Assert.Equal(0, transport.Deletes);
    }

    [Fact]
    public async Task PinRefusesCopiedMarkerOrWrongOwner()
    {
        var transport = new PublicationTransport { MessageMissing = true }; using var client = await Client(transport);
        var channel = (IThreadChannel)(await client.Rest.GetChannelAsync(100))!;
        await V2MessageWriter.UpsertAsync(channel, 999, "101", false, DateTimeOffset.UtcNow, V2(media: false), true, () => { }, default);
        transport.Message["author"]!["id"] = "200";
        await Assert.ThrowsAsync<InvalidOperationException>(() => DiscordRecruitmentAdapter.PinOwnedAsync(channel, 999, 101, true, false, false, (_, _) => { }, default));
        Assert.Empty(transport.PinRoutes);
    }

    private static BotSettings Settings() => new() { GuildId = 1,
        Recruitment = new RecruitmentSettings { Forums = new RecruitmentForums { PaidRecruitingId = 11, PaidForHireId = 12, HobbyRecruitingId = 13, HobbyForHireId = 14 } },
        Roles = new() { Moderator = 50 } };

    private static async Task<DiscordSocketClient> Client(PublicationTransport transport)
    {
        var client = new DiscordSocketClient(new DiscordSocketConfig { RestClientProvider = DiscordCompatibilityTransport.Wrap(_ => transport) });
        await client.LoginAsync(TokenType.Bot, "offline-placeholder-token", validateToken: false);
        // READY normally seeds the socket REST wrapper's identity. Seed only that SDK cache from an
        // offline REST login so these payload tests never open a gateway or replace production I/O.
        using var rest = new Discord.Rest.DiscordRestClient(new Discord.Rest.DiscordRestConfig { RestClientProvider = _ => transport });
        await rest.LoginAsync(TokenType.Bot, "offline-placeholder-token", validateToken: false);
        typeof(Discord.Rest.DiscordRestClient).GetProperty("CurrentUser", System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)!.SetValue(client.Rest, rest.CurrentUser);
        return client;
    }

    private sealed class PublicationTransport : IRestClient
    {
        public string? Nickname { get; init; } = "Server Artist";
        public string? GlobalName { get; init; } = "Artist";
        public string Role { get; init; } = "52";
        public bool OwnerBot { get; init; }
        public bool MissingMember { get; init; }
        public string? GlobalAvatar { get; init; }
        public string? ServerAvatar { get; init; }
        public JArray Attachments { get; init; } = new();
        public string StarterContent { get; private set; } = "Example listing";
        public JObject? CreatePayload { get; private set; }
        public List<JObject> ChannelWrites { get; } = new();
        public List<JObject> MessageWrites { get; } = new();
        public JObject Thread { get; } = JObject.Parse("""{"id":"100","type":11,"guild_id":"1","parent_id":"11","owner_id":"200","name":"Example","flags":32768,"applied_tags":[],"thread_metadata":{"archived":false,"locked":false,"auto_archive_duration":10080,"archive_timestamp":"2026-10-14T12:00:00Z"}}""");
        public bool MessageMissing { get; set; }
        public bool LongHistory { get; set; }
        public bool LoseEditResponse { get; set; }
        public bool LoseCreateResponse { get; set; }
        public bool RejectUpload { get; set; }
        public bool ComponentOnlyMedia { get; set; }
        public bool CdnMediaUrl { get; set; }
        public bool LosePinResponse { get; set; }
        public bool FailRestoreOnce { get; set; }
        public int PinStatus { get; set; }
        public List<string> PinRoutes { get; } = new();
        public int Deletes { get; private set; }
        public int Uploads { get; private set; }
        public JObject Message => _message;
        private readonly JObject _message = JObject.Parse("""{"id":"101","channel_id":"100","type":0,"content":"||recruitment:100:public||","author":{"id":"999","username":"offline","discriminator":"0","bot":true},"timestamp":"2026-10-14T12:00:00Z","embeds":[{"type":"rich","title":"Recruitment","description":"Listing active","fields":[{"name":"Payment","value":"Unknown","inline":false}]}],"attachments":[{"id":"900","filename":"recruitment.png","size":10,"url":"https://cdn.discordapp.com/example.png","proxy_url":"https://cdn.discordapp.com/example.png"}],"components":[]}""");
        public void Dispose() { }
        public void SetHeader(string key, string value) { }
        public void SetCancelToken(CancellationToken token) { }
        public Task<RestResponse> SendAsync(string method, string endpoint, CancellationToken cancelToken, bool headerOnly, string reason, IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers) => Respond(method, endpoint, null);
        public Task<RestResponse> SendAsync(string method, string endpoint, string json, CancellationToken cancelToken, bool headerOnly, string reason, IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers) => Respond(method, endpoint, JObject.Parse(json));
        public Task<RestResponse> SendAsync(string method, string endpoint, IReadOnlyDictionary<string, object> multipart, CancellationToken cancelToken, bool headerOnly, string reason, IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers) {
            if (RejectUpload) return Task.FromResult(new RestResponse(HttpStatusCode.Forbidden, new Dictionary<string, string>(),
                new MemoryStream(Encoding.UTF8.GetBytes("{\"code\":50013,\"message\":\"Missing Permissions\"}"))));
            Uploads++;
            var patch = JObject.Parse((string)multipart["payload_json"]);
            return Respond(method, endpoint, patch);
        }
        private JObject MessageResponse()
        {
            var response = (JObject)_message.DeepClone();
            if (!ComponentOnlyMedia) return response;
            foreach (var media in response["components"]!.SelectTokens("$..media").OfType<JObject>())
            {
                var fileName = ((string?)media["url"])?.Replace("attachment://", "");
                var attachment = ((JArray)_message["attachments"]!).FirstOrDefault(a => (string?)a["filename"] == fileName);
                if (attachment is null) continue;
                media["attachment_id"] = attachment["id"]!.DeepClone();
                if (CdnMediaUrl) media["url"] = "https://cdn.discordapp.com/attachments/100/" + (string?)attachment["id"] + "/" + fileName + "?ex=123";
                media["proxy_url"] = "https://media.discordapp.net/attachments/100/" + (string?)attachment["id"] + "/" + fileName;
                media["width"] = 1200; media["height"] = 840; media["content_type"] = "image/png";
            }
            response["attachments"] = new JArray();
            return response;
        }
        private void ApplyMessage(JObject patch)
        {
            MessageWrites.Add((JObject)patch.DeepClone());
            foreach (var property in patch.Properties()) _message[property.Name] = property.Value.DeepClone();
            if (_message["content"] is null or { Type: JTokenType.Null }) _message["content"] = "";
            if (_message["embeds"] is null or { Type: JTokenType.Null }) _message["embeds"] = new JArray();
            foreach (var property in _message.Properties().Where(p => p.Value.Type == JTokenType.Null).ToArray()) property.Remove();
            foreach (var attachment in (JArray)_message["attachments"]!)
            {
                if ((string?)attachment["id"] == "0") attachment["id"] = (900 + Uploads).ToString();
                attachment["size"] = 3; attachment["url"] = "https://cdn.discordapp.com/" + (string?)attachment["filename"];
                attachment["proxy_url"] = attachment["url"]!.DeepClone();
            }
        }
        private Task<RestResponse> Respond(string method, string endpoint, JObject? patch)
        {
            string body;
            if (endpoint.StartsWith("guilds/1/members/200") && MissingMember)
                return Task.FromResult(new RestResponse(HttpStatusCode.NotFound, new Dictionary<string, string>(), new MemoryStream(Encoding.UTF8.GetBytes("{\"code\":10007,\"message\":\"Unknown Member\"}"))));
            if (endpoint == "users/@me") body = """{"id":"999","username":"offline","discriminator":"0","bot":true}""";
            else if (endpoint == "oauth2/applications/@me") body = """{"id":"999","name":"offline","description":"","bot_public":true,"bot_require_code_grant":false,"verify_key":"00"}""";
            else if (endpoint.StartsWith("guilds/1/members/200")) body = new JObject {
                ["user"] = new JObject { ["id"] = "200", ["username"] = "member", ["discriminator"] = "0", ["bot"] = OwnerBot, ["avatar"] = GlobalAvatar, ["global_name"] = GlobalName },
                ["nick"] = Nickname, ["avatar"] = ServerAvatar, ["roles"] = new JArray(Role), ["joined_at"] = "2020-01-01T00:00:00Z" }.ToString();
            else if (endpoint.StartsWith("guilds/1")) body = """{"id":"1","name":"Offline","owner_id":"900","roles":[{"id":"1","name":"@everyone","permissions":"0"},{"id":"50","name":"Moderator","permissions":"0"},{"id":"51","name":"Admin","permissions":"8"},{"id":"52","name":"Artist","permissions":"0"}],"emojis":[],"features":[],"stickers":[],"afk_timeout":300,"preferred_locale":"en-US"}""";
            else if (endpoint == "channels/11") body = """{"id":"11","type":15,"guild_id":"1","name":"forum","flags":16,"available_tags":[{"id":"71","name":"Closed","moderated":false}]}""";
            else if (endpoint == "channels/11/threads" && method == "POST")
            {
                CreatePayload = patch!; StarterContent = (string)patch!["message"]!["content"]!;
                Thread["owner_id"] = "999"; Thread["applied_tags"] = patch["applied_tags"]!.DeepClone(); body = Thread.ToString();
            }
            else if (endpoint == "channels/100")
            {
                if (method == "PATCH")
                {
                    if (FailRestoreOnce && (bool?)patch?["archived"] == true)
                    { FailRestoreOnce = false; throw new TimeoutException("Restore response unavailable"); }
                    ChannelWrites.Add(patch!);
                    foreach (var property in patch!.Properties())
                        if (property.Name is "locked" or "archived") Thread["thread_metadata"]![property.Name] = property.Value.DeepClone();
                        else Thread[property.Name] = property.Value.DeepClone();
                    // Discord clears the forum pin when archiving; do not allow an impossible fixture state.
                    if ((bool)Thread["thread_metadata"]!["archived"]!) Thread["flags"] = (int)Thread["flags"]! & ~2;
                }
                body = Thread.ToString();
            }
            else if (endpoint == "channels/100/messages/100") body = new JObject { ["id"] = "100", ["channel_id"] = "100", ["type"] = 0,
                ["content"] = StarterContent, ["timestamp"] = "2026-10-14T12:00:00Z", ["author"] = new JObject { ["id"] = Thread["owner_id"]!.ToString(), ["username"] = "owner", ["discriminator"] = "0", ["bot"] = (string?)Thread["owner_id"] == "999" || OwnerBot }, ["embeds"] = new JArray(), ["attachments"] = Attachments }.ToString();
            else if (endpoint.StartsWith("channels/100/messages?"))
                body = new JArray(LongHistory ? Enumerable.Range(0, 100).Select(_ => _message.DeepClone()) : MessageMissing ? Array.Empty<JToken>() : new[] { MessageResponse() }).ToString();
            else if (endpoint == "channels/100/messages" && method == "POST")
            {
                MessageMissing = false; _message["content"] = ""; _message["embeds"] = new JArray(); _message["attachments"] = new JArray();
                ApplyMessage(patch!);
                if (LoseCreateResponse) { LoseCreateResponse = false; throw new InvalidOperationException("Lost create response"); }
                body = MessageResponse().ToString();
            }
            else if (method == "PUT" && endpoint is "channels/100/messages/pins/101" or "channels/100/pins/101")
            {
                PinRoutes.Add(endpoint);
                if (PinStatus != 0)
                {
                    var payload = PinStatus == 429 ? "{\"retry_after\":0.01,\"global\":false}" : "{\"code\":30003,\"message\":\"Pin write rejected\"}";
                    return Task.FromResult(new RestResponse((HttpStatusCode)PinStatus, new Dictionary<string, string>(), new MemoryStream(Encoding.UTF8.GetBytes(payload))));
                }
                _message["pinned"] = true;
                if (LosePinResponse) { LosePinResponse = false; throw new TimeoutException("Pin response lost"); }
                return Task.FromResult(new RestResponse(HttpStatusCode.NoContent, new Dictionary<string, string>(), new MemoryStream()));
            }
            else if (endpoint == "channels/100/messages/101")
            {
                if (method == "DELETE") { Deletes++; return Task.FromResult(new RestResponse(HttpStatusCode.NoContent, new Dictionary<string, string>(), new MemoryStream())); }
                if (MessageMissing) return Task.FromResult(new RestResponse(HttpStatusCode.NotFound, new Dictionary<string, string>(), new MemoryStream(Encoding.UTF8.GetBytes("{\"code\":10008,\"message\":\"Unknown Message\"}"))));
                if (method == "PATCH")
                {
                    ApplyMessage(patch!);
                    if (LoseEditResponse) { LoseEditResponse = false; throw new InvalidOperationException("Lost edit response"); }
                }
                body = MessageResponse().ToString();
            }
            else throw new InvalidOperationException($"Unexpected offline route: {method} {endpoint}");
            return Task.FromResult(new RestResponse(HttpStatusCode.OK, new Dictionary<string, string>(), new MemoryStream(Encoding.UTF8.GetBytes(body))));
        }
    }
}
