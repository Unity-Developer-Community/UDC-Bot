namespace DiscordBot.Services.Recruitment.Application;

public enum StaffOperation { Unknown, Status, Preview, Lookup, Repair, Reconcile, WaiveCooldown, Exempt, Unexempt, ResolveReports, ReviewChanges, ResetAttempts, ResetTimeouts, Reset, Reference, Close, Reopen, ReviewAbsent }
public enum PortfolioOperation { Unknown, List, Add, Remove }
public sealed record StaffCommand(StaffOperation Operation, string Name);
public sealed record PortfolioCommand(PortfolioOperation Operation, string Name);

public static class RecruitmentCommands
{
    public static StaffCommand Staff(string name) => new(name switch
    {
        "status" => StaffOperation.Status,
        "preview" => StaffOperation.Preview,
        "lookup" => StaffOperation.Lookup,
        "repair" => StaffOperation.Repair,
        "reconcile" => StaffOperation.Reconcile,
        "waive-cooldown" => StaffOperation.WaiveCooldown,
        "exempt" => StaffOperation.Exempt,
        "unexempt" => StaffOperation.Unexempt,
        "resolve-reports" => StaffOperation.ResolveReports,
        "review-changes" => StaffOperation.ReviewChanges,
        "reset-attempts" => StaffOperation.ResetAttempts,
        "reset-timeouts" => StaffOperation.ResetTimeouts,
        "reset" => StaffOperation.Reset,
        "reference" => StaffOperation.Reference,
        "close" => StaffOperation.Close,
        "reopen" => StaffOperation.Reopen,
        "review-absent" => StaffOperation.ReviewAbsent,
        _ => StaffOperation.Unknown
    }, name);
    public static PortfolioCommand Portfolio(string name) => new(name switch
    {
        "list" => PortfolioOperation.List, "add" => PortfolioOperation.Add, "remove" => PortfolioOperation.Remove,
        _ => PortfolioOperation.Unknown
    }, name);
}
