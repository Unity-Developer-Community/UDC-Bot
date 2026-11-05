namespace CasinoWeb.Auth;

public static class OriginPolicy
{
    /// <summary>True when the <c>Origin</c> header is exactly the site's own origin. A missing header is not accepted here: callers decide whether that is allowed.</summary>
    public static bool IsOwnOrigin(string? origin, Uri publicOrigin) =>
        !string.IsNullOrEmpty(origin)
        && string.Equals(origin.TrimEnd('/'), publicOrigin.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);
}
