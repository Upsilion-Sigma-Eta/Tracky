namespace Tracky.Core.Projects;

public sealed record ProjectSavedView(
    Guid Id,
    Guid ProjectId,
    string Name,
    ProjectViewMode ViewMode,
    string FilterText,
    string SortByField,
    string GroupByField,
    DateTimeOffset UpdatedAtUtc);
