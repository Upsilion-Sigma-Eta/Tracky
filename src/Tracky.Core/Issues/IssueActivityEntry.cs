namespace Tracky.Core.Issues;

public sealed record IssueActivityEntry(
    Guid Id,
    Guid IssueId,
    string EventType,
    string Summary,
    DateTimeOffset CreatedAtUtc);
