namespace DiscordBot.Services.Recruitment.Domain.Policies;

internal sealed record ForumSetupPlan(string Topic, string Code, List<TagDefinition> Definitions, List<LiveTag> Tags)
{
    public static ForumSetupPlan Prepare(string purpose, ForumReceipt receipt, ForumSnapshot live, bool repair,
        string renderedTopic, string rotationCode, List<TagDefinition> tags)
    {
        var topic = receipt.PendingTopic ?? renderedTopic;
        var code = receipt.PendingCode ?? rotationCode;
        var definitions = receipt.PendingTags ?? tags;
        if (!repair && live.Topic != receipt.ConfirmedTopic && live.Topic != receipt.PendingTopic)
            throw new InvalidOperationException($"Guidelines changed outside the bot in {purpose}; preview and repair setup.");
        if (!repair && receipt.ConfirmedTopic.Length > 0 && receipt.PendingTopic is null && !live.RequireTag)
            throw new InvalidOperationException($"Require Tag was disabled outside the bot in {purpose}; preview and repair setup.");
        if (!repair && receipt.ConfirmedTopic.Length == 0 && receipt.PendingTopic is null && live.Tags.Count > 0)
            throw new InvalidOperationException($"Unexpected existing tags in {purpose}; preview and repair setup.");
        if (receipt.Tags.Keys.Except(definitions.Select(d => d.Key)).Any())
            throw new InvalidOperationException($"Removing managed tags in {purpose} needs manual review; existing relationships must be preserved.");
        var desired = live.Tags.ToList();
        foreach (var definition in definitions)
        {
            receipt.Tags.TryGetValue(definition.Key, out var saved);
            var existing = saved is null ? null : live.Tags.SingleOrDefault(t => t.Id == saved.Id);
            if (saved is not null && existing is null && !repair && !receipt.PendingRepair)
                throw new InvalidOperationException($"A managed tag is missing in {purpose}; preview and repair setup to recreate it.");
            if (!repair && existing is not null && existing.Moderated != saved!.Moderated)
                throw new InvalidOperationException($"Managed tag permissions changed outside the bot in {purpose}.");
            if (!repair && existing is not null && existing.Name != saved!.Name && existing.Name != definition.Name)
                throw new InvalidOperationException($"A managed tag was renamed outside the bot in {purpose}.");
            var matches = live.Tags.Where(t => t.Name.Equals(definition.Name, StringComparison.OrdinalIgnoreCase) && t.Id != existing?.Id).ToArray();
            if (existing is null && matches.Length == 1 && (repair || receipt.PendingTags is not null)) existing = matches[0];
            else if (matches.Length > 0) throw new InvalidOperationException($"Conflicting tag names in {purpose}.");
            if (existing is not null) desired.Remove(existing);
            desired.Add(new LiveTag(existing?.Id ?? "", definition.Name, false));
        }
        if (desired.Count > 20) throw new InvalidOperationException($"Tag capacity exceeded in {purpose}; unrelated tags were preserved.");
        return new(topic, code, definitions, desired);
    }
}
