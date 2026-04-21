using Tracky.Core.Issues;
using Tracky.Core.Projects;

namespace Tracky.App.ViewModels;

public sealed class ProjectIssueItemViewModel(ProjectIssueItem item) : ViewModelBase
{
    private static readonly string[] BoardColumns =
    [
        "To do",
        "In progress",
        "Done",
    ];

    public ProjectIssueItem Item { get; } = item;

    public Guid ProjectItemId => Item.ProjectItemId;

    public Guid IssueId => Item.IssueId;

    public int IssueNumber => Item.IssueNumber;

    public string IssueNumberText => $"#{Item.IssueNumber}";

    public string Title => Item.Title;

    public string BoardColumn => Item.BoardColumn;

    public string AssigneeText => string.IsNullOrWhiteSpace(Item.AssigneeDisplayName)
        ? "Unassigned"
        : Item.AssigneeDisplayName;

    public string StateText => Item.State == IssueWorkflowState.Open
        ? "Open"
        : $"Closed as {FormatStateReason(Item.StateReason)}";

    public string PriorityText => Item.Priority switch
    {
        IssuePriority.Critical => "Critical",
        IssuePriority.High => "High",
        IssuePriority.Medium => "Medium",
        IssuePriority.Low => "Low",
        _ => "No priority",
    };

    public string DueText => Item.DueDate is null
        ? "No due date"
        : $"Due {Item.DueDate:MMM dd}";

    public string UpdatedText => $"Updated {Item.UpdatedAtUtc.ToLocalTime():MMM dd, HH:mm}";

    public bool HasCustomFieldValues => Item.CustomFieldValues.Count > 0;

    public string CustomFieldSummaryText => HasCustomFieldValues
        ? string.Join(", ", Item.CustomFieldValues.Select(static pair => $"{pair.Key}: {pair.Value}"))
        : "No custom field values";

    public bool HasPreviousColumn => GetBoardColumnIndex() > 0;

    public bool HasNextColumn => GetBoardColumnIndex() >= 0 && GetBoardColumnIndex() < BoardColumns.Length - 1;

    public string? PreviousBoardColumn => HasPreviousColumn
        ? BoardColumns[GetBoardColumnIndex() - 1]
        : null;

    public string? NextBoardColumn => HasNextColumn
        ? BoardColumns[GetBoardColumnIndex() + 1]
        : null;

    public string MoveBackwardLabel => PreviousBoardColumn is null
        ? "Move back"
        : $"Move to {PreviousBoardColumn}";

    public string MoveForwardLabel => NextBoardColumn is null
        ? "Move next"
        : $"Move to {NextBoardColumn}";

    public string PriorityBadgeBackground => Item.Priority switch
    {
        IssuePriority.Critical => "#FEF2F2",
        IssuePriority.High => "#FFF7ED",
        IssuePriority.Medium => "#FEFCE8",
        IssuePriority.Low => "#EFF6FF",
        _ => "#F5F5F4",
    };

    public string PriorityBadgeForeground => Item.Priority switch
    {
        IssuePriority.Critical => "#B91C1C",
        IssuePriority.High => "#B45309",
        IssuePriority.Medium => "#A16207",
        IssuePriority.Low => "#1D4ED8",
        _ => "#57534E",
    };

    private int GetBoardColumnIndex()
    {
        return Array.FindIndex(
            BoardColumns,
            column => string.Equals(column, BoardColumn, StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatStateReason(IssueStateReason reason) => reason switch
    {
        IssueStateReason.Completed => "completed",
        IssueStateReason.NotPlanned => "not planned",
        IssueStateReason.Duplicate => "duplicate",
        _ => "unspecified",
    };

    public override string ToString()
    {
        return $"{IssueNumberText} {Title}";
    }
}
