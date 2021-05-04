using System;

public static class ConsoleLogger
{
    public static void Log(string message, Severity severity = Severity.Info)
    {
        ConsoleColor restoreColour = Console.ForegroundColor;

        SetConsoleColour(severity);
        Console.WriteLine($"{DateTime.Now} {severity}\n{message}");

        Console.ForegroundColor = restoreColour;
    }


    private static void SetConsoleColour(Severity severity)
    {
        switch (severity)
        {
            case Severity.Info:
                Console.ForegroundColor = ConsoleColor.Gray;
                break;
            case Severity.Warning:
                Console.ForegroundColor = ConsoleColor.Yellow;
                break;
            case Severity.Error:
                Console.ForegroundColor = ConsoleColor.Red;
                break;
            case Severity.Fail:
                Console.ForegroundColor = ConsoleColor.DarkRed;
                break;
            case Severity.Pass:
                Console.ForegroundColor = ConsoleColor.Green;
                break;
        }
    }
}

public enum Severity
{
    Info = 0,
    Warning = 1,
    Error = 2,
    Fail = 3,
    Pass = 4
}