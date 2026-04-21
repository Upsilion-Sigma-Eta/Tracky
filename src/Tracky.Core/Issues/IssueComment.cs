namespace Tracky.Core.Issues;

public sealed record IssueComment(
    Guid Id,
    Guid IssueId,
    string AuthorDisplayName,
    string Body,
    DateTimeOffset CreatedAtUtc);
