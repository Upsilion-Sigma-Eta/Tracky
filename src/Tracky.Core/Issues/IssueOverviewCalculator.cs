namespace Tracky.Core.Issues;

public sealed class IssueOverviewCalculator
{
    // Phase 1에서는 All Issues 홈 화면의 요약 카드가 빠르게 반응하는 것이 중요하므로,
    // 저장소와 분리된 순수 계산기로 집계 규칙을 고정해 이후 테스트와 확장을 쉽게 만든다.
    public IssueMetrics Build(IReadOnlyCollection<IssueListItem> issues, DateOnly today)
    {
        var total = issues.Count;
        var open = 0;
        var closed = 0;
        var overdue = 0;
        var dueToday = 0;
        var upcoming = 0;

        foreach (var issue in issues)
        {
            if (issue.State == IssueWorkflowState.Open)
            {
                open++;
            }
            else
            {
                closed++;
            }

            if (issue.DueDate is null || issue.State != IssueWorkflowState.Open)
            {
                continue;
            }

            if (issue.DueDate < today)
            {
                overdue++;
                continue;
            }

            if (issue.DueDate == today)
            {
                dueToday++;
                continue;
            }

            upcoming++;
        }

        return new IssueMetrics(total, open, closed, overdue, dueToday, upcoming);
    }
}
