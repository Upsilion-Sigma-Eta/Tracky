namespace Tracky.Core.Projects;

public sealed record ProjectDetail(
    ProjectSummary Summary,
    IReadOnlyList<ProjectBoardColumn> BoardColumns,
    IReadOnlyList<ProjectIssueItem> TableItems,
    IReadOnlyList<ProjectIssueItem> CalendarItems,
    IReadOnlyList<ProjectIssueItem> TimelineItems,
    IReadOnlyList<ProjectCustomField> CustomFields,
    IReadOnlyList<ProjectSavedView> SavedViews);
