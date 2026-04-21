namespace Tracky.Core.Issues;

public sealed record IssueAttachment(
    Guid Id,
    Guid IssueId,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset CreatedAtUtc);
