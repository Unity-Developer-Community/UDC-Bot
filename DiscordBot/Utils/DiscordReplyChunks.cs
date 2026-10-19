namespace DiscordBot.Utils;

public static class DiscordReplyChunks
{
    public static IEnumerable<string> Split(string text, int limit = 1900)
    {
        if (limit < 2) throw new ArgumentOutOfRangeException(nameof(limit));
        for (var offset = 0; offset < text.Length;)
        {
            var length = Math.Min(limit, text.Length - offset);
            // Keep a supplementary Unicode character intact while preserving every input character.
            if (offset + length < text.Length && char.IsHighSurrogate(text[offset + length - 1]) && char.IsLowSurrogate(text[offset + length])) length--;
            yield return text.Substring(offset, length);
            offset += length;
        }
    }
}
