namespace Tracky.App.ViewModels;

public sealed class RepositoryMilestoneViewModel(
    string name,
    DateOnly? dueDate,
    int openIssues,
    int closedIssues) : ViewModelBase
{
    public string Name { get; } = name;

    public DateOnly? DueDate { get; } = dueDate;

    public int OpenIssues { get; } = openIssues;

    public int ClosedIssues { get; } = closedIssues;

    public int TotalIssues => OpenIssues + ClosedIssues;

    public bool HasDueDate => DueDate is not null;

    public string DueText => DueDate is null
        ? "No due date"
        : $"Due {DueDate:MMM dd, yyyy}";

    public string IssueCountText => TotalIssues == 1
        ? "1 issue"
        : $"{TotalIssues} issues";

    public string ProgressText => $"{OpenIssues} open / {ClosedIssues} closed";

    public double CompletionPercent => TotalIssues == 0
        ? 0
        : ClosedIssues * 100d / TotalIssues;

    public string CompletionText => $"{CompletionPercent:0}% complete";
}
