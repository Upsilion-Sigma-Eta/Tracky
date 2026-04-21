using Tracky.Core.Projects;

namespace Tracky.App.ViewModels;

public sealed class ProjectSavedViewViewModel(ProjectSavedView savedView) : ViewModelBase
{
    public ProjectSavedView SavedView { get; } = savedView;

    public string Name => SavedView.Name;

    public string ViewModeText => SavedView.ViewMode switch
    {
        ProjectViewMode.Table => "Table",
        ProjectViewMode.Calendar => "Calendar",
        ProjectViewMode.Timeline => "Timeline",
        _ => "Board",
    };

    public string FilterText => string.IsNullOrWhiteSpace(SavedView.FilterText)
        ? "No filter"
        : SavedView.FilterText;

    public string SortText => string.IsNullOrWhiteSpace(SavedView.SortByField)
        ? "Default sort"
        : $"Sorted by {SavedView.SortByField}";

    public string GroupByText => string.IsNullOrWhiteSpace(SavedView.GroupByField)
        || SavedView.GroupByField.Equals("None", StringComparison.OrdinalIgnoreCase)
        ? "No grouping"
        : $"Grouped by {SavedView.GroupByField}";

    public override string ToString()
    {
        return Name;
    }
}
