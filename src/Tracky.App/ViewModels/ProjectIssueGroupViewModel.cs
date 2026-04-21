using Tracky.Core.Projects;

namespace Tracky.App.ViewModels;

public sealed class ProjectIssueGroupViewModel(string name, IReadOnlyList<ProjectIssueItem> items) : ViewModelBase
{
    public string Name { get; } = name;

    public IReadOnlyList<ProjectIssueItemViewModel> Items { get; } =
        [.. items.Select(static item => new ProjectIssueItemViewModel(item))];

    public string CountText => Items.Count == 1
        ? "1 item"
        : $"{Items.Count} items";
}
