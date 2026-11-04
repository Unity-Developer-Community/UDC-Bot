using System.Security.Cryptography;

namespace DiscordBot.Domain;

/// <summary>Randomness source of the slot machine, replaceable by a seeded one in tests.</summary>
public interface ISlotRandom
{
    /// <summary>Returns a uniform integer in [0, maxExclusive).</summary>
    int Next(int maxExclusive);
}

/// <summary>Production randomness: cryptographically secure, since the outcome decides real token balances.</summary>
public sealed class SecureSlotRandom : ISlotRandom
{
    public int Next(int maxExclusive) => RandomNumberGenerator.GetInt32(maxExclusive);
}
