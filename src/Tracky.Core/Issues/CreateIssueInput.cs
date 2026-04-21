namespace Tracky.Core.Issues;

public sealed record CreateIssueInput(
    string Title,
    string? AssigneeDisplayName,
    IssuePriority Priority,
    DateOnly? DueDate,
    string? ProjectName,
    IReadOnlyList<string> Labels,
    string? MilestoneName = null,
    string? IssueTypeName = null);
