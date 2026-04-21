namespace Tracky.Core.Projects;

public sealed record ProjectBoardColumn(
    string Name,
    int OpenIssueCount,
    IReadOnlyList<ProjectIssueItem> Items);
