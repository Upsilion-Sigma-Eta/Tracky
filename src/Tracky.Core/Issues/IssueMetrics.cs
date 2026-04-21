namespace Tracky.Core.Issues;

public sealed record IssueMetrics(
    int Total,
    int Open,
    int Closed,
    int Overdue,
    int DueToday,
    int Upcoming);
