namespace Tracky.Core.Issues;

public sealed record IssueRelation(
    Guid Id,
    Guid SourceIssueId,
    Guid TargetIssueId,
    int TargetIssueNumber,
    string TargetIssueTitle,
    IssueRelationType RelationType,
    DateTimeOffset CreatedAtUtc);
