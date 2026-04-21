using Tracky.Core.Projects;

namespace Tracky.App.ViewModels;

public sealed class ProjectBoardColumnViewModel(ProjectBoardColumn column) : ViewModelBase
{
    public ProjectBoardColumn Column { get; } = column;

    public string Name => Column.Name;

    public int OpenIssueCount => Column.OpenIssueCount;

    public string CountText => Column.Items.Count == 1
        ? "1 card"
        : $"{Column.Items.Count} cards";

    public IReadOnlyList<ProjectIssueItemViewModel> Items { get; } =
        [.. column.Items.Select(static item => new ProjectIssueItemViewModel(item))];
}
