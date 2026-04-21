namespace Tracky.Core.Projects;

public sealed record ProjectSummary(
    Guid Id,
    string Name,
    string Description,
    int TotalIssues,
    int OpenIssues,
    int ClosedIssues,
    DateTimeOffset UpdatedAtUtc);
