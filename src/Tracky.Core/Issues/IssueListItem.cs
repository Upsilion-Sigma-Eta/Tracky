namespace Tracky.Core.Issues;

public sealed record IssueListItem(
    Guid Id,
    int Number,
    string Title,
    IssueWorkflowState State,
    IssueStateReason StateReason,
    IssuePriority Priority,
    string? AssigneeDisplayName,
    DateOnly? DueDate,
    DateTimeOffset UpdatedAtUtc,
    string? ProjectName,
    int CommentCount,
    int AttachmentCount,
    IReadOnlyList<string> Labels)
{
    public static readonly IReadOnlyList<string> EmptyLabels = [];
}
