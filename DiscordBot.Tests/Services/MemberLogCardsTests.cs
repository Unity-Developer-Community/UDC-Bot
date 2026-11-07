using Discord;
using DiscordBot.Services.Server;
using static DiscordBot.Services.Server.MemberLogCards;

namespace DiscordBot.Tests.Services;

public class MemberLogCardsTests
{
    private static readonly CardUser User = new(123456789012345678, "Display (aka username)", "https://cdn.discordapp.com/embed/avatars/0.png");
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private static string Text(MessageComponent card) => AuditLogCardsTests.Text(card);

    private static JoinedMember Joined(TimeSpan accountAge) => new(User, Now - accountAge, Now);

    [Fact]
    public void JoinedCard_OldAccount_ShowsIdentityAndCreatedWithoutWarning()
    {
        var text = Text(JoinedCard(Joined(TimeSpan.FromDays(400))));

        Assert.Contains("📥 Member joined", text);
        Assert.Contains($"<@{User.Id}>", text);
        Assert.Contains($"Account created {AuditLogCards.Timestamp(Now - TimeSpan.FromDays(400), 'R')}", text);
        Assert.Contains($"Joined {AuditLogCards.Timestamp(Now)}", text);
        Assert.DoesNotContain("⚠️", text);
        Assert.DoesNotContain("Flags:", text);
        Assert.DoesNotContain("Joined via", text);
    }

    [Fact]
    public void JoinedCard_AccountUnderSevenDays_FlaggedAsNew()
    {
        var text = Text(JoinedCard(Joined(NewAccountAge - TimeSpan.FromSeconds(1))));
        Assert.Contains("⚠️ Account created", text);
        Assert.Contains("(new account)", text);
    }

    [Fact]
    public void JoinedCard_AccountExactlySevenDays_NotNew()
    {
        Assert.DoesNotContain("⚠️", Text(JoinedCard(Joined(NewAccountAge))));
    }

    [Fact]
    public void JoinedCard_TrueFlags_ShownOnOneLine()
    {
        var member = Joined(TimeSpan.FromDays(400)) with
        {
            IsBot = true, DefaultAvatar = true, Rejoined = true, PendingScreening = true, QuarantinedUsername = true,
        };
        Assert.Contains("Flags: Bot · Default avatar · Rejoined · Pending screening · Quarantined username", Text(JoinedCard(member)));
    }

    [Fact]
    public void JoinedCard_SomeFlags_OnlyTrueOnesShown()
    {
        var text = Text(JoinedCard(Joined(TimeSpan.FromDays(400)) with { Rejoined = true }));
        Assert.Contains("Flags: Rejoined", text);
        Assert.DoesNotContain("Bot", text);
        Assert.DoesNotContain("Default avatar", text);
    }

    [Fact]
    public void JoinedCard_WithJoinSource_ShowsLine()
    {
        var line = JoinSourceLine(JoinSourceType.InviteCode, "abc123", 42);
        Assert.Contains("Joined via invite `abc123` from <@42>", Text(JoinedCard(Joined(TimeSpan.FromDays(400)), line)));
    }

    [Theory]
    [InlineData(JoinSourceType.InviteCode, "abc", 42ul, "Joined via invite `abc` from <@42>")]
    [InlineData(JoinSourceType.InviteCode, "abc", null, "Joined via invite `abc`")]
    [InlineData(JoinSourceType.InviteCode, null, 42ul, "Joined via an invite from <@42>")]
    [InlineData(JoinSourceType.InviteCode, null, null, "Joined via an invite")]
    [InlineData(JoinSourceType.VanityUrl, "unity", null, "Joined via vanity URL `unity`")]
    [InlineData(JoinSourceType.VanityUrl, null, null, "Joined via vanity URL")]
    [InlineData(JoinSourceType.ServerDiscovery, null, null, "Joined via Server Discovery")]
    [InlineData(JoinSourceType.StudentHub, null, null, "Joined via Student Hub")]
    [InlineData(JoinSourceType.BotInvite, null, null, "Added by bot invite")]
    [InlineData(JoinSourceType.BotInvite, null, 42ul, "Added by bot invite from <@42>")]
    [InlineData(JoinSourceType.Integration, null, null, "Added by an integration")]
    [InlineData(JoinSourceType.ManualVerification, null, null, "Joined via manual verification")]
    [InlineData(JoinSourceType.Unknown, "abc", null, "Joined via invite `abc`")]
    [InlineData(JoinSourceType.Unknown, null, null, null)]
    [InlineData(JoinSourceType.Unknown, "  ", 0ul, null)]
    [InlineData((JoinSourceType)99, null, null, null)]
    public void JoinSourceLine_MapsEverySource(JoinSourceType type, string? code, ulong? inviter, string? expected)
    {
        Assert.Equal(expected, JoinSourceLine(type, code, inviter));
    }

    [Fact]
    public void JoinSourceLine_BacktickInCode_CannotBreakFormatting()
    {
        Assert.Equal("Joined via invite `a'b`", JoinSourceLine(JoinSourceType.InviteCode, "a`b", null));
    }

    [Fact]
    public void LeftCard_PlainLeave_ShowsStayAndRoles()
    {
        var member = new LeftMember(User, Now - TimeSpan.FromHours(76), ["Moderator", "Unity User"]);
        var text = Text(LeftCard(member, Now));

        Assert.Contains("📤 Member left", text);
        Assert.Contains($"Joined {AuditLogCards.Timestamp(Now - TimeSpan.FromHours(76), 'R')} · stayed 3d 4h", text);
        Assert.Contains("Roles: Moderator, Unity User", text);
        Assert.Contains($"Left {AuditLogCards.Timestamp(Now)}", text);
        Assert.DoesNotContain("Kicked", text);
        Assert.DoesNotContain("Banned", text);
        Assert.DoesNotContain("Reason", text);
    }

    [Fact]
    public void LeftCard_UnknownJoinAndNoRoles_LinesOmitted()
    {
        var text = Text(LeftCard(new LeftMember(User, null, []), Now));
        Assert.DoesNotContain("stayed", text);
        Assert.DoesNotContain("Roles:", text);
    }

    [Fact]
    public void LeftCard_Kicked_ShowsModeratorAndReason()
    {
        var member = new LeftMember(User, null, [], LeaveKind.Kicked, 55, "Mod*Name", "spam *links*");
        var text = Text(LeftCard(member, Now));

        Assert.Contains("👢 Kicked by <@55> Mod\\*Name", text);
        Assert.Contains("Reason: spam \\*links\\*", text);
    }

    [Fact]
    public void LeftCard_BannedWithoutReasonOrModerator_NoReasonLine()
    {
        var text = Text(LeftCard(new LeftMember(User, null, [], LeaveKind.Banned), Now));
        Assert.Contains("🔨 Banned by an unknown moderator", text);
        Assert.DoesNotContain("Reason", text);
    }

    [Fact]
    public void LeftCard_LongReasonAndManyRoles_Capped()
    {
        var roles = Enumerable.Range(0, 300).Select(i => $"Role number {i}").ToList();
        var member = new LeftMember(User, null, roles, LeaveKind.Banned, 55, "Mod", new string('r', 2000));
        var card = LeftCard(member, Now);

        Assert.True(Text(card).Length < AuditCard.DiscordTextLimit);
        Assert.Contains("Reason: rrr", Text(card));
        Assert.DoesNotContain(new string('r', 700), Text(card));
        Assert.DoesNotContain("Role number 299", Text(card));
    }

    [Fact]
    public void LeftCard_RoleNamesSanitized()
    {
        Assert.Contains("Roles: \\_under\\_", Text(LeftCard(new LeftMember(User, null, ["_under_"]), Now)));
    }

    [Theory]
    [InlineData(NameKind.Nickname, "Nickname changed")]
    [InlineData(NameKind.DisplayName, "Display name changed")]
    [InlineData(NameKind.Username, "Username changed")]
    public void NameChangedCard_TitlePerKind(NameKind kind, string title)
    {
        var text = Text(NameChangedCard(User, kind, "old", "new", Now));
        Assert.Contains(title, text);
        Assert.Contains("**Before:** old", text);
        Assert.Contains("**After:** new", text);
        Assert.Contains($"Changed {AuditLogCards.Timestamp(Now)}", text);
    }

    [Fact]
    public void NameChangedCard_ClearedOrAddedNickname_ShowsNone()
    {
        Assert.Contains("**After:** *(none)*", Text(NameChangedCard(User, NameKind.Nickname, "old", null, Now)));
        Assert.Contains("**Before:** *(none)*", Text(NameChangedCard(User, NameKind.Nickname, "", "new", Now)));
    }

    [Fact]
    public void NameChangedCard_NamesSanitized()
    {
        var text = Text(NameChangedCard(User, NameKind.Nickname, "**bold**", "a_b_", Now));
        Assert.Contains("**Before:** \\*\\*bold\\*\\*", text);
        Assert.Contains("**After:** a\\_b\\_", text);
    }

    [Fact]
    public void NameChangedCard_SeveralChangesOfOneKind_ShowsHistoryAndRange()
    {
        var text = Text(NameChangedCard(User, [new NameChange(NameKind.Username, ["a", "b", "a"])], Now, Now.AddMinutes(3)));
        Assert.Contains("Username changed", text);
        Assert.Contains("**Username:** a → b → a", text);
        Assert.DoesNotContain("**Before:**", text);
        Assert.Contains($"Changed {AuditLogCards.Timestamp(Now)} – {AuditLogCards.Timestamp(Now.AddMinutes(3), 'T')} · 2 changes", text);
    }

    [Fact]
    public void NameChangedCard_SeveralKinds_OneLinePerField()
    {
        var text = Text(NameChangedCard(User,
            [new NameChange(NameKind.DisplayName, ["D1", null]), new NameChange(NameKind.Username, ["u_1", "u2"])],
            Now, Now.AddMinutes(2)));
        Assert.Contains("Name changed", text);
        Assert.Contains("**Display name:** D1 → *(none)*", text);
        Assert.Contains("**Username:** u\\_1 → u2", text);
        Assert.Contains("· 2 changes", text);
    }

    [Fact]
    public void NameChangedCard_OmittedValues_MarkedWithEllipsis()
    {
        var text = Text(NameChangedCard(User, [new NameChange(NameKind.Username, ["a", "e", "f"], Omitted: 3)], Now, Now.AddMinutes(1)));
        Assert.Contains("**Username:** a → … → e → f", text);
        Assert.Contains("· 5 changes", text);
    }

    [Theory]
    [InlineData(0, 0, 0, 40, "40s")]
    [InlineData(0, 0, 12, 5, "12m")]
    [InlineData(0, 5, 0, 0, "5h")]
    [InlineData(0, 5, 12, 0, "5h 12m")]
    [InlineData(1, 0, 0, 0, "1d")]
    [InlineData(3, 4, 30, 0, "3d 4h")]
    [InlineData(365, 0, 0, 0, "1y")]
    [InlineData(744, 0, 0, 0, "2y 14d")]
    public void StayDuration_Formats(int days, int hours, int minutes, int seconds, string expected)
    {
        Assert.Equal(expected, StayDuration(new TimeSpan(days, hours, minutes, seconds)));
    }

    [Fact]
    public void StayDuration_Negative_IsZero()
    {
        Assert.Equal("0s", StayDuration(TimeSpan.FromMinutes(-5)));
    }
}
