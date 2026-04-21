using System.Collections.ObjectModel;
using Tracky.Core.Issues;

namespace Tracky.Core.Projects;

public sealed record ProjectIssueItem(
    Guid ProjectItemId,
    Guid IssueId,
    int IssueNumber,
    string Title,
    IssueWorkflowState State,
    IssueStateReason StateReason,
    IssuePriority Priority,
    string? AssigneeDisplayName,
    DateOnly? DueDate,
    string BoardColumn,
    int SortOrder,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyDictionary<string, string> CustomFieldValues)
{
    public static IReadOnlyDictionary<string, string> EmptyCustomFieldValues { get; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
}
