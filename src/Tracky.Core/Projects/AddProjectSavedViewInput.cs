namespace Tracky.Core.Projects;

public sealed record AddProjectSavedViewInput(
    Guid ProjectId,
    string Name,
    ProjectViewMode ViewMode,
    string FilterText,
    string SortByField,
    string GroupByField);
