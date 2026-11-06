namespace CasinoWeb.Auth;

/// <summary>Thrown while the login ticket is built when the player is not in the guild; the remote-failure handler turns it into the access-denied page.</summary>
public sealed class GuildAccessDeniedException() : Exception("The Discord account is not a member of the guild.");
