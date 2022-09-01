namespace DiscordBot.Skin;

public struct RectangleD
{
    public double UpperLeftX;
    public double UpperLeftY;
    public double LowerRightX;
    public double LowerRightY;

    public RectangleD(double upperLeftX, double upperLeftY, double lowerRightX, double lowerRightY)
    {
        UpperLeftX = upperLeftX;
        UpperLeftY = upperLeftY;
        LowerRightX = lowerRightX;
        LowerRightY = lowerRightY;
    }
}