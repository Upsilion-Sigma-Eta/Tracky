namespace Tracky.Core.Issues;

public sealed record UpdateIssueStateInput(
    Guid IssueId,
    IssueWorkflowState State,
    IssueStateReason Reason);
