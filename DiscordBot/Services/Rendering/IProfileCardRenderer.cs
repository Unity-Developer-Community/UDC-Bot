namespace DiscordBot.Services.Rendering;

public interface IProfileCardRenderer
{
    byte[] Render(ProfileCardRenderRequest request);
}
