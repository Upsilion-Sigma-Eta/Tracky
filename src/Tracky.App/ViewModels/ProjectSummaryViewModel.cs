using Tracky.Core.Projects;

namespace Tracky.App.ViewModels;

public sealed class ProjectSummaryViewModel(ProjectSummary project) : ViewModelBase
{
    public ProjectSummary Project { get; } = project;

    public Guid Id => Project.Id;

    public string Name => Project.Name;

    public string Description => string.IsNullOrWhiteSpace(Project.Description)
        ? "No project description yet."
        : Project.Description;

    public int TotalIssues => Project.TotalIssues;

    public int OpenIssues => Project.OpenIssues;

    public int ClosedIssues => Project.ClosedIssues;

    public string IssueCountText => Project.TotalIssues == 1
        ? "1 issue"
        : $"{Project.TotalIssues} issues";

    public string ProgressText => $"{Project.OpenIssues} open / {Project.ClosedIssues} closed";

    public string UpdatedText => $"Updated {Project.UpdatedAtUtc.ToLocalTime():MMM dd, HH:mm}";
}
