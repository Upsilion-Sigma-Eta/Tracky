using Tracky.Core.Issues;
using Tracky.Core.Exports;
using Tracky.Core.Preferences;
using Tracky.Core.Reminders;
using Tracky.Core.Search;

namespace Tracky.Core.Workspaces;

public sealed record WorkspaceOverview(
    string WorkspaceName,
    string Description,
    string DatabasePath,
    IssueMetrics Metrics,
    IReadOnlyList<IssueListItem> Issues,
    IReadOnlyList<IssueReminder> Reminders,
    IReadOnlyList<ExportPreset> ExportPresets,
    IReadOnlyList<SavedIssueSearch> SavedIssueSearches,
    IReadOnlyList<MilestoneSummary> Milestones,
    IReadOnlyList<IssueTypeDefinition> IssueTypes,
    WorkspacePreferences Preferences);
