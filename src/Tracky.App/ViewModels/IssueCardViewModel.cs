using System.Globalization;
using Tracky.Core.Issues;

namespace Tracky.App.ViewModels;

public sealed class IssueCardViewModel(IssueListItem issue) : ViewModelBase
{
    public IssueListItem Issue { get; } = issue;

    public Guid Id => Issue.Id;

    public int Number => Issue.Number;

    public string NumberText => $"#{Issue.Number}";

    public string Title => Issue.Title;

    public bool IsOpen => Issue.State == IssueWorkflowState.Open;

    public bool IsClosed => !IsOpen;

    public string StateText => IsOpen ? "Open" : "Closed";

    // GitHub Issues 아이콘에 대응: 열린 이슈는 점이 찍힌 원(●), 닫힌 이슈는 상태 사유에 따라 체크(✓)나 빗금(⊘)으로 표시한다.
    public string StateIconText => IsOpen
        ? "\u25CF"
        : Issue.StateReason == IssueStateReason.NotPlanned
            ? "\u2298"
            : "\u2713";

    public string StateIconBackground => IsOpen ? "#1A7F37" : "#8250DF";

    public string StateIconForeground => IsOpen ? "#FFFFFF" : "#FFFFFF";

    public string StateDetailText => IsClosed && Issue.StateReason != IssueStateReason.None
        ? $"Closed as {FormatStateReason(Issue.StateReason)}"
        : "Active work";

    public string StateBadgeBackground => IsOpen ? "#1A7F37" : "#8250DF";

    public string StateBadgeForeground => IsOpen ? "#FFFFFF" : "#FFFFFF";

    public string PriorityText => Issue.Priority switch
    {
        IssuePriority.Critical => "Critical",
        IssuePriority.High => "High",
        IssuePriority.Medium => "Medium",
        IssuePriority.Low => "Low",
        _ => "No priority",
    };

    public string PriorityBadgeBackground => Issue.Priority switch
    {
        IssuePriority.Critical => "#FEF2F2",
        IssuePriority.High => "#FFF7ED",
        IssuePriority.Medium => "#FEFCE8",
        IssuePriority.Low => "#EFF6FF",
        _ => "#F5F5F4",
    };

    public string PriorityBadgeForeground => Issue.Priority switch
    {
        IssuePriority.Critical => "#B91C1C",
        IssuePriority.High => "#B45309",
        IssuePriority.Medium => "#A16207",
        IssuePriority.Low => "#1D4ED8",
        _ => "#57534E",
    };

    public bool HasAssignee => !string.IsNullOrWhiteSpace(Issue.AssigneeDisplayName);

    public string AssigneeText => HasAssignee
        ? Issue.AssigneeDisplayName!
        : "Unassigned";

    public bool HasProject => !string.IsNullOrWhiteSpace(Issue.ProjectName);

    public string ProjectText => HasProject
        ? Issue.ProjectName!
        : "Workspace inbox";

    public bool HasMilestone => !string.IsNullOrWhiteSpace(Issue.MilestoneName);

    public string MilestoneText => HasMilestone
        ? Issue.MilestoneName!
        : "No milestone";

    public bool HasIssueType => !string.IsNullOrWhiteSpace(Issue.IssueTypeName);

    public string IssueTypeText => HasIssueType
        ? Issue.IssueTypeName!
        : "Task";

    public IReadOnlyList<string> Labels => Issue.Labels;

    public IReadOnlyList<LabelChipViewModel> LabelChips { get; } = [.. issue.Labels.Select(static label => new LabelChipViewModel(label))];

    public bool HasLabels => Labels.Count > 0;

    public bool HasComments => Issue.CommentCount > 0;

    public string CommentsText => Issue.CommentCount == 1
        ? "1 comment"
        : $"{Issue.CommentCount} comments";

    public string CommentBadgeText => Issue.CommentCount.ToString(CultureInfo.InvariantCulture);

    public bool HasAttachments => Issue.AttachmentCount > 0;

    public string AttachmentsText => Issue.AttachmentCount == 1
        ? "1 attachment"
        : $"{Issue.AttachmentCount} attachments";

    public bool HasDueDate => Issue.DueDate is not null;

    public bool IsOverdue => HasDueDate
        && IsOpen
        && Issue.DueDate < DateOnly.FromDateTime(DateTime.Today);

    public bool IsDueToday => HasDueDate
        && IsOpen
        && Issue.DueDate == DateOnly.FromDateTime(DateTime.Today);

    public string DueText
    {
        get
        {
            if (!HasDueDate)
            {
                return "No due date";
            }

            if (IsOverdue)
            {
                return $"Overdue since {Issue.DueDate:MMM dd}";
            }

            if (IsDueToday)
            {
                return $"Due today ({Issue.DueDate:MMM dd})";
            }

            return $"Due {Issue.DueDate:MMM dd}";
        }
    }

    public string DueBadgeBackground => IsOverdue
        ? "#FEF2F2"
        : IsDueToday
            ? "#FFF7ED"
            : "#ECFDF5";

    public string DueBadgeForeground => IsOverdue
        ? "#B91C1C"
        : IsDueToday
            ? "#B45309"
            : "#047857";

    public string UpdatedText => $"Updated {Issue.UpdatedAtUtc.ToLocalTime():MMM dd, HH:mm}";

    public string GitHubListMetaText => IsOpen
        ? $"{NumberText} updated {Issue.UpdatedAtUtc.ToLocalTime():MMM dd, HH:mm} by {AssigneeText}"
        : $"{NumberText} closed {Issue.UpdatedAtUtc.ToLocalTime():MMM dd, HH:mm} by {AssigneeText}";

    public string GitHubListSecondaryText => $"{ProjectText} / {MilestoneText} / {IssueTypeText}";

    public string ActionLabel => IsOpen
        ? "Close issue"
        : "Reopen issue";

    public string CardBorderBrush => IsOverdue
        ? "#FCA5A5"
        : IsDueToday
            ? "#FDBA74"
            : "#E7E5E4";

    private static string FormatStateReason(IssueStateReason reason) => reason switch
    {
        IssueStateReason.Completed => "completed",
        IssueStateReason.NotPlanned => "not planned",
        IssueStateReason.Duplicate => "duplicate",
        _ => "unspecified",
    };
}
