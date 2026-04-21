using Tracky.Core.Issues;

namespace Tracky.Core.Workspaces;

public sealed record WorkspaceOverview(
    string WorkspaceName,
    string Description,
    string DatabasePath,
    IssueMetrics Metrics,
    IReadOnlyList<IssueListItem> Issues);
