namespace DiscordBot.Domain;

/// <summary>The one row of the shared slots jackpot.</summary>
public class CasinoJackpot
{
    public int Id { get; set; }

    /// <summary>The pot in hundredths of a token, see <see cref="SlotSettlementMath.HundredthsPerToken"/>.</summary>
    public long Amount { get; set; }

    public DateTime UpdatedAt { get; set; }
}

public static class JackpotProps
{
    public const string TableName = "casino_jackpot";

    /// <summary>The only row of the table.</summary>
    public const string RowId = "1";

    public const string Id = nameof(CasinoJackpot.Id);
    public const string Amount = nameof(CasinoJackpot.Amount);
    public const string UpdatedAt = nameof(CasinoJackpot.UpdatedAt);

    /// <summary>
    /// Runs at every start. Idempotent, and independent of the casino tables, which are only created when
    /// <c>casino_users</c> is missing.
    /// </summary>
    public static readonly string[] EnsureStatements =
    [
        $"CREATE TABLE IF NOT EXISTS {TableName} (" +
        $"{Id} integer PRIMARY KEY, " +
        $"{Amount} bigint NOT NULL, " +
        $"{UpdatedAt} timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP)",

        $"INSERT INTO {TableName} ({Id}, {Amount}, {UpdatedAt}) " +
        $"VALUES ({RowId}, {SlotSettlementMath.JackpotFloorHundredths}, CURRENT_TIMESTAMP) " +
        $"ON CONFLICT ({Id}) DO NOTHING",
    ];
}
