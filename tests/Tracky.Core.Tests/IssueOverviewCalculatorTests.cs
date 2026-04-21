using Tracky.Core.Issues;

namespace Tracky.Core.Tests;

public sealed class IssueOverviewCalculatorTests
{
    [Fact]
    public void Build_counts_open_closed_and_due_buckets_for_open_items()
    {
        var today = new DateOnly(2026, 4, 21);
        var calculator = new IssueOverviewCalculator();
        var issues = new[]
        {
            CreateIssue(101, IssueWorkflowState.Open, today.AddDays(-2)),
            CreateIssue(102, IssueWorkflowState.Open, today),
            CreateIssue(103, IssueWorkflowState.Open, today.AddDays(5)),
            CreateIssue(104, IssueWorkflowState.Closed, today.AddDays(-3)),
        };

        var metrics = calculator.Build(issues, today);

        Assert.Equal(4, metrics.Total);
        Assert.Equal(3, metrics.Open);
        Assert.Equal(1, metrics.Closed);
        Assert.Equal(1, metrics.Overdue);
        Assert.Equal(1, metrics.DueToday);
        Assert.Equal(1, metrics.Upcoming);
    }

    private static IssueListItem CreateIssue(int number, IssueWorkflowState state, DateOnly dueDate)
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
