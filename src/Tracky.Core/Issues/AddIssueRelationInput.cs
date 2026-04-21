namespace Tracky.Core.Issues;

public sealed record AddIssueRelationInput(
    Guid SourceIssueId,
    Guid TargetIssueId,
    IssueRelationType RelationType);
