namespace Tracky.Core.Issues;

public sealed record MilestoneSummary(
    Guid Id,
    string Name,
    DateOnly? DueDate,
    int OpenIssues,
    int ClosedIssues);
