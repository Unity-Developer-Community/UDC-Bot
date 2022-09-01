namespace DiscordBot.Data;

public class Rating
{
    public object Count { get; set; }
    public int Average { get; set; }
}

public class Kategory
{
    public string Slug { get; set; }
    public string Name { get; set; }
    public string Id { get; set; }
}

public class Category
{
    public string TreeId { get; set; }
    public string LabelEnglish { get; set; }
    public string Label { get; set; }
    public string Id { get; set; }
    public string Multiple { get; set; }
}

public class Publisher
{
    public string LabelEnglish { get; set; }
    public string Url { get; set; }
    public string Slug { get; set; }
    public string Label { get; set; }
    public string Id { get; set; }
    public string SupportEmail { get; set; }
    public object SupportUrl { get; set; }
}

public class Link
{
    public string Type { get; set; }
    public string Id { get; set; }
}

public class List
{
    public string Slug { get; set; }
    public string SlugV2 { get; set; }
    public string Name { get; set; }
    public object Overlay { get; set; }
}

public class Flags
{
}

public class Image
{
    public string Link { get; set; }
    public string Width { get; set; }
    public string Name { get; set; }
    public string Type { get; set; }
    public string Height { get; set; }
    public string Thumb { get; set; }
}

public class Keyimage
{
    public string Small { get; set; }
    public string Big { get; set; }
    public object SmallLegacy { get; set; }
    public object Facebook { get; set; }
    public object BigLegacy { get; set; }
    public string Icon { get; set; }
    public string Icon75 { get; set; }
    public string Icon25 { get; set; }
}

public class Daily
{
    public string Icon { get; set; }
    public Rating Rating { get; set; }
    public int Remaining { get; set; }
    public Kategory Kategory { get; set; }
    public string PackageVersionId { get; set; }
    public string Slug { get; set; }
    public Category Category { get; set; }
    public string Hotness { get; set; }
    public string Id { get; set; }
    public Publisher Publisher { get; set; }
    public List<object> List { get; set; }
    public Link Link { get; set; }
    public Flags Flags { get; set; }
    public Keyimage Keyimage { get; set; }
    public string Description { get; set; }
    public string TitleEnglish { get; set; }
    public string Title { get; set; }
}

public class Content
{
    public string Pubdate { get; set; }
    public string MinUnityVersion { get; set; }
    public Rating Rating { get; set; }
    public Kategory Kategory { get; set; }
    public List<string> UnityVersions { get; set; }
    public string Url { get; set; }
    public string PackageVersionId { get; set; }
    public string Slug { get; set; }
    public Category Category { get; set; }
    public string Id { get; set; }
    public Publisher Publisher { get; set; }
    public string Sizetext { get; set; }
    public List<object> List { get; set; }
    public Link Link { get; set; }
    public List<Image> Images { get; set; }
    public Flags Flags { get; set; }
    public string Version { get; set; }
    public string FirstPublishedAt { get; set; }
    public Keyimage Keyimage { get; set; }
    public int License { get; set; }
    public string Description { get; set; }
    public List<object> Upgrades { get; set; }
    public string Publishnotes { get; set; }
    public string Title { get; set; }
    public string ShortUrl { get; set; }
    public List<object> Upgradables { get; set; }
}

public class DailyObject
{
    public string Banner { get; set; }
    public string Feed { get; set; }
    public string Status { get; set; }
    public int DaysLeft { get; set; }
    public int Total { get; set; }
    public Daily Daily { get; set; }
    public int Remaining { get; set; }
    public string Badge { get; set; }
    public string Title { get; set; }
    public bool Countdown { get; set; }
    public List<object> Results { get; set; }
}

public class PackageObject
{
    public Content Content { get; set; }
}

public class PriceObject
{
    public string Vat { get; set; }
    public string PriceExvat { get; set; }
    public string Price { get; set; }
    public bool IsFree { get; set; }
}

public class Result
{
    public string Category { get; set; }
    public string Title { get; set; }
    public string Publisher { get; set; }
}

public class PackageHeadObject
{
    public Result Result { get; set; }
}