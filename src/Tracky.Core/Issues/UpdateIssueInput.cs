namespace Tracky.Core.Issues;

public sealed record UpdateIssueInput(
    Guid IssueId,
    string Title,
    string Description,
    string? AssigneeDisplayName,
    IssuePriority Priority,
    DateOnly? DueDate,
    string? ProjectName,
    IReadOnlyList<string> Labels,
    string? MilestoneName = null,
    string? IssueTypeName = null,
    IssueContentFormat ContentFormat = IssueContentFormat.Markdown);
