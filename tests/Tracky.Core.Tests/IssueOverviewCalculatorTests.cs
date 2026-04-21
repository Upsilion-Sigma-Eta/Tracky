using Tracky.Core.Issues;

namespace Tracky.Core.Tests;

public sealed class IssueOverviewCalculatorTests
{
    [Fact]
    public void BuildCountsOpenClosedAndDueBucketsForOpenItems()
    {
        var today = new DateOnly(2026, 4, 21);
        var issues = new[]
        {
            CreateIssue(101, IssueWorkflowState.Open, today.AddDays(-2)),
            CreateIssue(102, IssueWorkflowState.Open, today),
            CreateIssue(103, IssueWorkflowState.Open, today.AddDays(5)),
            CreateIssue(104, IssueWorkflowState.Closed, today.AddDays(-3))
        };

        var metrics = IssueOverviewCalculator.Build(issues, today);

        Assert.Equal(4, metrics.Total);
        Assert.Equal(3, metrics.Open);
        Assert.Equal(1, metrics.Closed);
        Assert.Equal(1, metrics.Overdue);
        Assert.Equal(1, metrics.DueToday);
        Assert.Equal(1, metrics.Upcoming);
    }

    [Fact]
    public void BuildReturnsZeroMetricsForAnEmptyIssueSet()
    {
        var metrics = IssueOverviewCalculator.Build([], new DateOnly(2026, 4, 21));

        Assert.Equal(0, metrics.Total);
        Assert.Equal(0, metrics.Open);
        Assert.Equal(0, metrics.Closed);
        Assert.Equal(0, metrics.Overdue);
        Assert.Equal(0, metrics.DueToday);
        Assert.Equal(0, metrics.Upcoming);
    }

    [Fact]
    public void BuildIgnoresClosedAndUndatedIssuesForDueBuckets()
    {
        var today = new DateOnly(2026, 4, 21);
        var issues = new[]
        {
            CreateIssue(101, IssueWorkflowState.Open, null),
            CreateIssue(102, IssueWorkflowState.Closed, today.AddDays(-10)),
            CreateIssue(103, IssueWorkflowState.Closed, today),
            CreateIssue(104, IssueWorkflowState.Open, today.AddDays(1))
        };

        var metrics = IssueOverviewCalculator.Build(issues, today);

        Assert.Equal(4, metrics.Total);
        Assert.Equal(2, metrics.Open);
        Assert.Equal(2, metrics.Closed);
        Assert.Equal(0, metrics.Overdue);
        Assert.Equal(0, metrics.DueToday);
        Assert.Equal(1, metrics.Upcoming);
    }

    private static IssueListItem CreateIssue(int number, IssueWorkflowState state, DateOnly? dueDate)
    {
        return new IssueListItem(
            Guid.NewGuid(),
            number,
            $"Issue {number}",
            state,
            state == IssueWorkflowState.Open ? IssueStateReason.None : IssueStateReason.Completed,
            IssuePriority.High,
            "Dabin",
            dueDate,
            DateTimeOffset.UtcNow,
            "Tracky Foundation",
            0,
            0,
            ["foundation"]);
    }
}
