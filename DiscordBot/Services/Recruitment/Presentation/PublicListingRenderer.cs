using System.IO;
using static DiscordBot.Utils.DiscordDisplayText;

namespace DiscordBot.Services.Recruitment.Presentation;

public static class PublicListingRenderer
{
    public static RecruitmentMessage Render(PublicListingView view, Func<IReadOnlyList<RecruitmentObservation>, byte[]>? render = null)
    {
        var (checks, alt, needsAck, identity, avatar, marker, visible, foot, _, _) = view;
        string? fileName = null;
        MessageComponent Build(bool image)
        {
            var children = new List<IMessageComponentBuilder> { new TextDisplayBuilder($"## {RecruitmentPresentation.Heading}") };
            if (image) children.Add(new MediaGalleryBuilder().AddItem("attachment://" + fileName, RecruitmentBanner.GalleryAlt(checks)));
            else children.Add(new TextDisplayBuilder(string.Join("\n", checks.Select(c => $"{RecruitmentBanner.Icon(c)} **{c.Label}:** {Literal(c.Value, 60)}"))));
            children.Add(new TextDisplayBuilder($"*{RecruitmentPresentation.Guidance}*"));
            if (foot.Length > 0) children.Add(new TextDisplayBuilder(foot));
            if (image && avatar is not null) children.Add(new SectionBuilder(new ThumbnailBuilder(new UnfurledMediaItemProperties(avatar), "Author avatar"), new[] { new TextDisplayBuilder(identity) }));
            else children.Add(new TextDisplayBuilder(identity + (avatar is null ? "\nAvatar unavailable." : "\nAvatar image omitted; see Profile Image observation.")));
            var row = new ActionRowBuilder().WithButton("Legend", $"recruit:details:{view.ListingId}", ButtonStyle.Secondary)
                .WithButton("Report concern", $"recruit:report:{view.ListingId}", ButtonStyle.Secondary)
                .WithButton("Your listing", $"recruit:owner:{view.ListingId}", ButtonStyle.Secondary);
            if (needsAck) row.WithButton("Acknowledge", $"recruit:ack:{view.ListingId}", ButtonStyle.Primary);
            children.Add(row);
            children.Add(new TextDisplayBuilder($"||{marker}||"));
            return new ComponentBuilderV2(new[] { new ContainerBuilder(children) }).Build();
        }
        RecruitmentMedia? media = null;
        try
        {
            var bytes = (render ?? (c => RecruitmentBanner.Render(c)))(checks);
            fileName = "recruitment-observations-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant() + ".png";
            media = new(fileName, bytes, alt);
        }
        catch (Exception ex) when (ex is ImageMagick.MagickException or IOException or ArgumentException or InvalidOperationException) { /* Text remains complete and operable. */ }
        // Deliberately excludes CDN signatures and SDK-generated component IDs.
        var avatarKey = avatar is null ? "" : new Uri(avatar).GetLeftPart(UriPartial.Path);
        var key = RecruitmentBanner.Identity(RecruitmentBanner.Version + "\n" + view.RegistryVersion + "\n" +
            alt + "\n" + visible + foot + identity + avatarKey + "\n" + needsAck + "\n" + marker + "\n" + fileName);
        var fallback = Build(false);
        return new RecruitmentMessage("", marker, true, AcknowledgementRequired: needsAck, ReaderControls: true,
            Format: RecruitmentMessageFormat.ComponentsV2, RenderIdentity: key, Components: media is null ? fallback : Build(true),
            FallbackComponents: fallback, Media: media);
    }
}
