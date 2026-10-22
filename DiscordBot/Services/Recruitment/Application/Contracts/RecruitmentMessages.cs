namespace DiscordBot.Services.Recruitment;

public enum RecruitmentMessageFormat { Standard, ComponentsV2 }
public sealed record RecruitmentMedia(string FileName, byte[] Bytes, string Description);
public sealed record ThreadRestoration(bool Archived, bool Locked)
{
    public static readonly ThreadRestoration LockedArchive = new(true, true);
}
public sealed record RecruitmentMessage(string Content, string Marker, bool OwnerControls, bool RestoreArchive = false, Embed? Embed = null, string? TextFallback = null, bool AcknowledgementRequired = false, bool ReaderControls = false, RecruitmentMessageFormat Format = RecruitmentMessageFormat.Standard,
    string? RenderIdentity = null, MessageComponent? Components = null, MessageComponent? FallbackComponents = null, RecruitmentMedia? Media = null, bool RestoreLocked = false, ulong? MentionUserId = null)
{
    public RecruitmentMessage WithRestoration(ThreadRestoration target) =>
        this with { RestoreArchive = target.Archived, RestoreLocked = target.Locked };

    public string RenderKey => RenderIdentity ?? Content + "\n" + OwnerControls + "\n" + AcknowledgementRequired + "\n" + ReaderControls + "\n" + EmbedKey(Embed);
    public static string EmbedKey(IEmbed? embed) => embed is null ? "" : Newtonsoft.Json.JsonConvert.SerializeObject(embed.ToEmbedBuilder().Build());
}
public sealed record Delivery(string Id, bool NewlySent, string? MediaAttachmentId = null, string? MediaFileName = null);
