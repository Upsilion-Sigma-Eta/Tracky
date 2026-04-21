using System.Text;
using Tracky.Core.Exports;
using Tracky.Core.Issues;
using Tracky.Core.Preferences;
using Tracky.Core.Projects;
using Tracky.Core.Reminders;
using Tracky.Core.Search;
using Tracky.Core.Services;
using Tracky.Core.Workspaces;

namespace Tracky.App.Tests.TestDoubles;

public sealed class TestTrackyWorkspaceService : ITrackyWorkspaceService
{
    private readonly Dictionary<Guid, byte[]> _attachmentContentById = [];
    private readonly Dictionary<Guid, List<IssueActivityEntry>> _activityByIssueId = [];
    private readonly Dictionary<Guid, List<IssueAttachment>> _attachmentsByIssueId = [];
    private readonly Dictionary<Guid, List<ProjectCustomField>> _customFieldsByProjectId = [];
    private readonly Dictionary<Guid, Dictionary<string, string>> _customFieldValuesByProjectItemId = [];
    private readonly Dictionary<Guid, List<IssueComment>> _commentsByIssueId = [];
    private readonly Dictionary<Guid, string> _descriptionByIssueId = [];
    private readonly Dictionary<Guid, List<IssueRelation>> _relationsByIssueId = [];
    private readonly Dictionary<Guid, IssueReminder> _remindersById = [];
    private readonly Dictionary<Guid, string> _projectDescriptionById = [];
    private readonly Dictionary<Guid, (Guid ProjectId, Guid IssueId)> _projectItemLocationById = [];
    private readonly Dictionary<(Guid ProjectId, Guid IssueId), Guid> _projectItemIdByIssue = [];
    private readonly Dictionary<Guid, string> _projectNameById = [];
    private readonly Dictionary<string, Guid> _projectIdByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, List<ProjectSavedView>> _savedViewsByProjectId = [];
    private readonly List<ExportPreset> _exportPresets = [];
    private readonly List<SavedIssueSearch> _savedIssueSearches = [];
    private WorkspacePreferences _preferences = WorkspacePreferences.Default;
    private readonly Dictionary<Guid, string> _boardColumnByProjectItemId = [];
    private readonly List<IssueListItem> _issues = [];

    private TestTrackyWorkspaceService()
    {
    }

    public static TestTrackyWorkspaceService CreateDefault()
    {
        var now = DateTimeOffset.UtcNow;
        var openIssueId = Guid.NewGuid();
        var closedIssueId = Guid.NewGuid();
        var service = new TestTrackyWorkspaceService();

        service._issues.Add(
            new IssueListItem(
                openIssueId,
                201,
                "Prepare GUI test coverage for quick capture",
                IssueWorkflowState.Open,
                IssueStateReason.None,
                IssuePriority.High,
                "Dabin",
                DateOnly.FromDateTime(DateTime.Today),
                now.AddMinutes(-10),
                "Tracky Foundation",
                1,
                1,
                ["foundation", "desktop"]));

        service._issues.Add(
            new IssueListItem(
                closedIssueId,
                202,
                "Closed issue should remain filterable",
                IssueWorkflowState.Closed,
                IssueStateReason.Completed,
                IssuePriority.Medium,
                "Dabin",
                DateOnly.FromDateTime(DateTime.Today).AddDays(-3),
                now.AddMinutes(-30),
                "Tracky Tests",
                0,
                0,
                ["tests"]));

        service.EnsureProject("Tracky Foundation", "Default project used by Phase 2 board tests.");
        service.EnsureProject("Tracky Tests", "Closed issue project used by filtering tests.");

        service._descriptionByIssueId[openIssueId] = "Open issue description for the right-side detail panel.";
        service._descriptionByIssueId[closedIssueId] = "Closed issue description for filter verification.";

        service._commentsByIssueId[openIssueId] =
        [
            new IssueComment(
                Guid.NewGuid(),
                openIssueId,
                "Dabin",
                "Initial comment visible in the GUI detail panel.",
                now.AddMinutes(-8)),
        ];
        service._commentsByIssueId[closedIssueId] = [];

        var attachmentId = Guid.NewGuid();
        service._attachmentsByIssueId[openIssueId] =
        [
            new IssueAttachment(
                attachmentId,
                openIssueId,
                "gui-test-plan.txt",
                "text/plain",
                19,
                now.AddMinutes(-7)),
        ];
        service._attachmentContentById[attachmentId] = "headless gui testing"u8.ToArray();
        service._attachmentsByIssueId[closedIssueId] = [];

        service._activityByIssueId[openIssueId] =
        [
            new IssueActivityEntry(
                Guid.NewGuid(),
                openIssueId,
                "issue.seeded",
                "Seeded issue for GUI test coverage.",
                now.AddMinutes(-9)),
        ];
        service._activityByIssueId[closedIssueId] = [];
        service._relationsByIssueId[openIssueId] = [];
        service._relationsByIssueId[closedIssueId] = [];

        service._exportPresets.Add(
            new ExportPreset(
                Guid.NewGuid(),
                "Filtered Markdown handoff",
                ExportSelectionScope.CurrentFilter,
                ExportFormat.Markdown,
                ExportBodyFormat.Markdown,
                IncludeComments: true,
                IncludeActivity: true,
                IncludeAttachments: false,
                IncludeClosedIssues: false,
                now));
        service._savedIssueSearches.Add(
            new SavedIssueSearch(
                Guid.NewGuid(),
                "Open desktop work",
                "is:open label:desktop",
                IsPinned: true,
                now));

        return service;
    }

    public Task<WorkspaceOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var orderedIssues = _issues
            .OrderByDescending(static issue => issue.UpdatedAtUtc)
            .ThenByDescending(static issue => issue.Number)
            .ToArray();
        var metrics = IssueOverviewCalculator.Build(orderedIssues, DateOnly.FromDateTime(DateTime.Today));

        return Task.FromResult(
            new WorkspaceOverview(
                "Test Workspace",
                "A deterministic workspace used by GUI and view-model tests.",
                "memory://tracky-tests",
                metrics,
                orderedIssues,
                [.. _remindersById.Values.Where(static reminder => !reminder.IsDismissed).OrderBy(static reminder => reminder.RemindAtUtc)],
                [.. _exportPresets],
                [.. _savedIssueSearches],
                [],
                [],
                _preferences));
    }

    public Task<IssueDetail?> GetIssueDetailAsync(Guid issueId, CancellationToken cancellationToken = default)
    {
        var issue = _issues.FirstOrDefault(item => item.Id == issueId);
        if (issue is null)
        {
            return Task.FromResult<IssueDetail?>(null);
        }

        return Task.FromResult<IssueDetail?>(
            new IssueDetail(
                issue,
                _descriptionByIssueId.GetValueOrDefault(issueId, string.Empty),
                GetComments(issueId),
                GetAttachments(issueId),
                GetActivity(issueId),
                GetReminders(issueId),
                GetRelations(issueId)));
    }

    public Task<IssueListItem> CreateIssueAsync(CreateIssueInput input, CancellationToken cancellationToken = default)
    {
        var nextIssueNumber = _issues.Count == 0 ? 201 : _issues.Max(static issue => issue.Number) + 1;
        var issue = new IssueListItem(
            Guid.NewGuid(),
            nextIssueNumber,
            input.Title.Trim(),
            IssueWorkflowState.Open,
            IssueStateReason.None,
            input.Priority,
            input.AssigneeDisplayName,
            input.DueDate,
            DateTimeOffset.UtcNow,
            input.ProjectName,
            0,
            0,
            [.. input.Labels],
            input.MilestoneName,
            input.IssueTypeName);

        _issues.Add(issue);
        if (!string.IsNullOrWhiteSpace(issue.ProjectName))
        {
            EnsureProject(issue.ProjectName, $"Issues grouped under {issue.ProjectName}.");
        }

        _descriptionByIssueId[issue.Id] = "Created by the quick capture flow.";
        _commentsByIssueId[issue.Id] = [];
        _attachmentsByIssueId[issue.Id] = [];
        _relationsByIssueId[issue.Id] = [];
        _activityByIssueId[issue.Id] =
        [
            new IssueActivityEntry(
                Guid.NewGuid(),
                issue.Id,
                "issue.created",
                $"Issue #{issue.Number} was created.",
                issue.UpdatedAtUtc),
        ];

        return Task.FromResult(issue);
    }

    public Task<IssueListItem?> UpdateIssueAsync(UpdateIssueInput input, CancellationToken cancellationToken = default)
    {
        var index = _issues.FindIndex(issue => issue.Id == input.IssueId);
        if (index < 0)
        {
            return Task.FromResult<IssueListItem?>(null);
        }

        var existingIssue = _issues[index];
        var updatedIssue = existingIssue with
        {
            Title = input.Title.Trim(),
            Priority = input.Priority,
            AssigneeDisplayName = string.IsNullOrWhiteSpace(input.AssigneeDisplayName)
                ? null
                : input.AssigneeDisplayName.Trim(),
            DueDate = input.DueDate,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            ProjectName = string.IsNullOrWhiteSpace(input.ProjectName)
                ? null
                : input.ProjectName.Trim(),
            Labels = [.. input.Labels],
            MilestoneName = string.IsNullOrWhiteSpace(input.MilestoneName)
                ? null
                : input.MilestoneName.Trim(),
            IssueTypeName = string.IsNullOrWhiteSpace(input.IssueTypeName)
                ? null
                : input.IssueTypeName.Trim(),
        };

        // 테스트 더블도 프로덕션 서비스처럼 본문/라벨/메타데이터를 함께 갱신해야
        // ViewModel 테스트가 실제 Phase 1 편집 흐름과 같은 계약을 검증한다.
        _issues[index] = updatedIssue;
        if (!string.IsNullOrWhiteSpace(updatedIssue.ProjectName))
        {
            EnsureProject(updatedIssue.ProjectName, $"Issues grouped under {updatedIssue.ProjectName}.");
        }

        _descriptionByIssueId[input.IssueId] = input.Description.Trim();
        AddActivity(input.IssueId, "issue.updated", "Issue title, body, and metadata were updated.");
        return Task.FromResult<IssueListItem?>(updatedIssue);
    }

    public Task<IssueListItem?> UpdateIssueStateAsync(UpdateIssueStateInput input, CancellationToken cancellationToken = default)
    {
        var index = _issues.FindIndex(issue => issue.Id == input.IssueId);
        if (index < 0)
        {
            return Task.FromResult<IssueListItem?>(null);
        }

        var reason = NormalizeStateReason(input.State, input.Reason);
        var updatedIssue = _issues[index] with
        {
            State = input.State,
            StateReason = reason,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        _issues[index] = updatedIssue;
        foreach (var projectItemId in _projectItemLocationById
            .Where(pair => pair.Value.IssueId == input.IssueId)
            .Select(static pair => pair.Key))
        {
            _boardColumnByProjectItemId[projectItemId] = input.State == IssueWorkflowState.Closed
                ? "Done"
                : "To do";
        }

        AddActivity(input.IssueId, "issue.state.changed", BuildStateSummary(input.State, reason));
        return Task.FromResult<IssueListItem?>(updatedIssue);
    }

    public Task<bool> DeleteIssueAsync(Guid issueId, CancellationToken cancellationToken = default)
    {
        var removed = _issues.RemoveAll(issue => issue.Id == issueId) > 0;
        if (!removed)
        {
            return Task.FromResult(false);
        }

        if (_attachmentsByIssueId.TryGetValue(issueId, out var attachments))
        {
            foreach (var attachment in attachments)
            {
                _attachmentContentById.Remove(attachment.Id);
            }
        }

        _descriptionByIssueId.Remove(issueId);
        _commentsByIssueId.Remove(issueId);
        _attachmentsByIssueId.Remove(issueId);
        _activityByIssueId.Remove(issueId);
        _relationsByIssueId.Remove(issueId);
        foreach (var relations in _relationsByIssueId.Values)
        {
            // 삭제된 이슈를 대상으로 삼던 관계도 테스트 더블 안에서 같이 제거해
            // 실제 SQLite FK/cascade 동작과 같은 고아 데이터 없는 상태를 검증한다.
            relations.RemoveAll(relation => relation.TargetIssueId == issueId);
        }

        foreach (var projectItem in _projectItemLocationById
            .Where(pair => pair.Value.IssueId == issueId)
            .ToArray())
        {
            _projectItemLocationById.Remove(projectItem.Key);
            _boardColumnByProjectItemId.Remove(projectItem.Key);
            _customFieldValuesByProjectItemId.Remove(projectItem.Key);
            _projectItemIdByIssue.Remove((projectItem.Value.ProjectId, issueId));
        }

        return Task.FromResult(true);
    }

    public Task<IssueComment?> AddIssueCommentAsync(AddIssueCommentInput input, CancellationToken cancellationToken = default)
    {
        var issueIndex = _issues.FindIndex(issue => issue.Id == input.IssueId);
        if (issueIndex < 0)
        {
            return Task.FromResult<IssueComment?>(null);
        }

        var comment = new IssueComment(
            Guid.NewGuid(),
            input.IssueId,
            input.AuthorDisplayName.Trim(),
            input.Body.Trim(),
            DateTimeOffset.UtcNow);
        _commentsByIssueId[input.IssueId].Add(comment);
        _issues[issueIndex] = _issues[issueIndex] with
        {
            CommentCount = _commentsByIssueId[input.IssueId].Count,
            UpdatedAtUtc = comment.CreatedAtUtc,
        };
        AddActivity(input.IssueId, "issue.comment.added", $"{comment.AuthorDisplayName} added a comment.");
        return Task.FromResult<IssueComment?>(comment);
    }

    public Task<IssueAttachment?> AddIssueAttachmentAsync(AddIssueAttachmentInput input, CancellationToken cancellationToken = default)
    {
        var issueIndex = _issues.FindIndex(issue => issue.Id == input.IssueId);
        if (issueIndex < 0)
        {
            return Task.FromResult<IssueAttachment?>(null);
        }

        var attachment = new IssueAttachment(
            Guid.NewGuid(),
            input.IssueId,
            input.FileName,
            input.ContentType,
            input.Content.LongLength,
            DateTimeOffset.UtcNow);
        _attachmentsByIssueId[input.IssueId].Add(attachment);
        _attachmentContentById[attachment.Id] = [.. input.Content];
        _issues[issueIndex] = _issues[issueIndex] with
        {
            AttachmentCount = _attachmentsByIssueId[input.IssueId].Count,
            UpdatedAtUtc = attachment.CreatedAtUtc,
        };
        AddActivity(input.IssueId, "issue.attachment.added", $"Attachment \"{attachment.FileName}\" was added.");
        return Task.FromResult<IssueAttachment?>(attachment);
    }

    public async Task<string?> ExportAttachmentToTempFileAsync(Guid attachmentId, CancellationToken cancellationToken = default)
    {
        if (!_attachmentContentById.TryGetValue(attachmentId, out var content))
        {
            return null;
        }

        var path = Path.Combine(Path.GetTempPath(), $"tracky-test-{attachmentId:N}.bin");
        await File.WriteAllBytesAsync(path, content, cancellationToken);
        return path;
    }

    public Task<IssueRelation?> AddIssueRelationAsync(
        AddIssueRelationInput input,
        CancellationToken cancellationToken = default)
    {
        var source = _issues.FirstOrDefault(issue => issue.Id == input.SourceIssueId);
        var target = _issues.FirstOrDefault(issue => issue.Id == input.TargetIssueId);
        if (source is null || target is null || source.Id == target.Id)
        {
            return Task.FromResult<IssueRelation?>(null);
        }

        var relation = new IssueRelation(
            Guid.NewGuid(),
            source.Id,
            target.Id,
            target.Number,
            target.Title,
            input.RelationType,
            DateTimeOffset.UtcNow);
        if (!_relationsByIssueId.TryGetValue(source.Id, out var relations))
        {
            relations = [];
            _relationsByIssueId[source.Id] = relations;
        }

        relations.Add(relation);
        AddActivity(source.Id, "issue.relation.added", $"Relation to #{target.Number} was added.");
        return Task.FromResult<IssueRelation?>(relation);
    }

    public Task<IssueReminder?> ScheduleIssueReminderAsync(
        ScheduleIssueReminderInput input,
        CancellationToken cancellationToken = default)
    {
        var issue = _issues.FirstOrDefault(item => item.Id == input.IssueId);
        if (issue is null)
        {
            return Task.FromResult<IssueReminder?>(null);
        }

        var reminder = new IssueReminder(
            Guid.NewGuid(),
            input.IssueId,
            string.IsNullOrWhiteSpace(input.Title) ? $"Follow up on #{issue.Number}" : input.Title.Trim(),
            input.Note.Trim(),
            input.RemindAtUtc,
            DateTimeOffset.UtcNow,
            null);
        _remindersById[reminder.Id] = reminder;
        AddActivity(input.IssueId, "issue.reminder.scheduled", $"Reminder scheduled for {reminder.RemindAtUtc:MMM dd, HH:mm}.");
        return Task.FromResult<IssueReminder?>(reminder);
    }

    public Task<IssueReminder?> DismissReminderAsync(
        DismissReminderInput input,
        CancellationToken cancellationToken = default)
    {
        if (!_remindersById.TryGetValue(input.ReminderId, out var reminder))
        {
            return Task.FromResult<IssueReminder?>(null);
        }

        var dismissed = reminder with { DismissedAtUtc = DateTimeOffset.UtcNow };
        _remindersById[input.ReminderId] = dismissed;
        if (dismissed.IssueId is Guid issueId)
        {
            AddActivity(issueId, "issue.reminder.dismissed", $"Reminder \"{dismissed.Title}\" was dismissed.");
        }

        return Task.FromResult<IssueReminder?>(dismissed);
    }

    public async Task<ExportResult> ExportSelectionAsync(
        ExportOptions options,
        CancellationToken cancellationToken = default)
    {
        var issues = ResolveExportIssues(options);
        var exportDirectory = Path.Combine(Path.GetTempPath(), "Tracky.Tests", $"export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(exportDirectory);

        var outputPath = Path.Combine(exportDirectory, options.Format == ExportFormat.Html ? "tracky-export.html" : "tracky-export.md");
        var builder = new StringBuilder();
        foreach (var issue in issues)
        {
            builder.Append("# #")
                .Append(issue.Number)
                .Append(' ')
                .AppendLine(issue.Title);
        }

        await File.WriteAllTextAsync(outputPath, builder.ToString(), cancellationToken);
        return new ExportResult(outputPath, issues.Count, 0, options.Format);
    }

    public Task<ExportPreset?> AddExportPresetAsync(
        AddExportPresetInput input,
        CancellationToken cancellationToken = default)
    {
        _exportPresets.RemoveAll(preset => string.Equals(preset.Name, input.Name.Trim(), StringComparison.OrdinalIgnoreCase));
        var preset = new ExportPreset(
            Guid.NewGuid(),
            input.Name.Trim(),
            input.Scope,
            input.Format,
            input.BodyFormat,
            input.IncludeComments,
            input.IncludeActivity,
            input.IncludeAttachments,
            input.IncludeClosedIssues,
            DateTimeOffset.UtcNow);
        _exportPresets.Add(preset);
        return Task.FromResult<ExportPreset?>(preset);
    }

    public Task<SavedIssueSearch?> AddSavedIssueSearchAsync(
        AddSavedIssueSearchInput input,
        CancellationToken cancellationToken = default)
    {
        _savedIssueSearches.RemoveAll(search => string.Equals(search.Name, input.Name.Trim(), StringComparison.OrdinalIgnoreCase));
        var savedSearch = new SavedIssueSearch(
            Guid.NewGuid(),
            input.Name.Trim(),
            input.QueryText.Trim(),
            input.IsPinned,
            DateTimeOffset.UtcNow);
        _savedIssueSearches.Add(savedSearch);
        return Task.FromResult<SavedIssueSearch?>(savedSearch);
    }

    public Task<WorkspacePreferences> UpdateWorkspacePreferencesAsync(
        UpdateWorkspacePreferencesInput input,
        CancellationToken cancellationToken = default)
    {
        _preferences = new WorkspacePreferences(
            input.Theme,
            input.CompactDensity,
            string.IsNullOrWhiteSpace(input.ShortcutProfile) ? "Default" : input.ShortcutProfile.Trim(),
            DateTimeOffset.UtcNow);
        return Task.FromResult(_preferences);
    }

    public Task<IReadOnlyList<ProjectSummary>> GetProjectsAsync(CancellationToken cancellationToken = default)
    {
        EnsureProjectsFromIssues();
        return Task.FromResult<IReadOnlyList<ProjectSummary>>(
            [.. _projectNameById.Keys
                .Select(BuildProjectSummary)
                .OrderBy(static project => project.Name)]);
    }

    public Task<ProjectDetail?> GetProjectDetailAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        EnsureProjectsFromIssues();
        if (!_projectNameById.TryGetValue(projectId, out var projectName))
        {
            return Task.FromResult<ProjectDetail?>(null);
        }

        EnsureProjectMetadata(projectId);
        var summary = BuildProjectSummary(projectId);
        var items = _issues
            .Where(issue => string.Equals(issue.ProjectName, projectName, StringComparison.OrdinalIgnoreCase))
            .Select(issue => BuildProjectIssueItem(projectId, issue))
            .OrderBy(static item => item.SortOrder)
            .ThenBy(static item => item.IssueNumber)
            .ToArray();
        var boardColumns = new[]
        {
            BuildProjectBoardColumn("To do", items),
            BuildProjectBoardColumn("In progress", items),
            BuildProjectBoardColumn("Done", items),
        };
        var detail = new ProjectDetail(
            summary,
            boardColumns,
            [.. items.OrderBy(static item => item.IssueNumber)],
            [.. items.Where(static item => item.DueDate is not null).OrderBy(static item => item.DueDate)],
            [.. items.OrderBy(static item => item.DueDate ?? DateOnly.MaxValue)],
            [.. _customFieldsByProjectId[projectId]],
            [.. _savedViewsByProjectId[projectId]]);

        return Task.FromResult<ProjectDetail?>(detail);
    }

    public Task<ProjectSummary> CreateProjectAsync(
        CreateProjectInput input,
        CancellationToken cancellationToken = default)
    {
        var projectId = EnsureProject(input.Name.Trim(), input.Description.Trim());
        return Task.FromResult(BuildProjectSummary(projectId));
    }

    public Task<ProjectIssueItem?> MoveProjectItemAsync(
        MoveProjectItemInput input,
        CancellationToken cancellationToken = default)
    {
        if (!_projectItemLocationById.TryGetValue(input.ProjectItemId, out var location))
        {
            return Task.FromResult<ProjectIssueItem?>(null);
        }

        var issue = _issues.FirstOrDefault(item => item.Id == location.IssueId);
        if (issue is null)
        {
            return Task.FromResult<ProjectIssueItem?>(null);
        }

        _boardColumnByProjectItemId[input.ProjectItemId] = input.BoardColumn.Trim();
        return Task.FromResult<ProjectIssueItem?>(BuildProjectIssueItem(location.ProjectId, issue));
    }

    public Task<ProjectCustomField?> AddProjectCustomFieldAsync(
        AddProjectCustomFieldInput input,
        CancellationToken cancellationToken = default)
    {
        if (!_projectNameById.ContainsKey(input.ProjectId))
        {
            return Task.FromResult<ProjectCustomField?>(null);
        }

        EnsureProjectMetadata(input.ProjectId);
        var fields = _customFieldsByProjectId[input.ProjectId];
        fields.RemoveAll(field => string.Equals(field.Name, input.Name.Trim(), StringComparison.OrdinalIgnoreCase));

        var field = new ProjectCustomField(
            Guid.NewGuid(),
            input.ProjectId,
            input.Name.Trim(),
            input.FieldType,
            input.OptionsText.Trim());
        fields.Add(field);
        return Task.FromResult<ProjectCustomField?>(field);
    }

    public Task<ProjectIssueItem?> UpdateProjectCustomFieldValueAsync(
        UpdateProjectCustomFieldValueInput input,
        CancellationToken cancellationToken = default)
    {
        if (!_projectItemLocationById.TryGetValue(input.ProjectItemId, out var location)
            || !_customFieldsByProjectId.TryGetValue(location.ProjectId, out var fields))
        {
            return Task.FromResult<ProjectIssueItem?>(null);
        }

        var field = fields.FirstOrDefault(item => item.Id == input.CustomFieldId);
        var issue = _issues.FirstOrDefault(item => item.Id == location.IssueId);
        if (field is null || issue is null)
        {
            return Task.FromResult<ProjectIssueItem?>(null);
        }

        if (!_customFieldValuesByProjectItemId.TryGetValue(input.ProjectItemId, out var values))
        {
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _customFieldValuesByProjectItemId[input.ProjectItemId] = values;
        }

        values[field.Name] = input.ValueText.Trim();
        return Task.FromResult<ProjectIssueItem?>(BuildProjectIssueItem(location.ProjectId, issue));
    }

    public Task<ProjectSavedView?> AddProjectSavedViewAsync(
        AddProjectSavedViewInput input,
        CancellationToken cancellationToken = default)
    {
        if (!_projectNameById.ContainsKey(input.ProjectId))
        {
            return Task.FromResult<ProjectSavedView?>(null);
        }

        EnsureProjectMetadata(input.ProjectId);
        var savedViews = _savedViewsByProjectId[input.ProjectId];
        savedViews.RemoveAll(view => string.Equals(view.Name, input.Name.Trim(), StringComparison.OrdinalIgnoreCase));

        var savedView = new ProjectSavedView(
            Guid.NewGuid(),
            input.ProjectId,
            input.Name.Trim(),
            input.ViewMode,
            input.FilterText.Trim(),
            input.SortByField.Trim(),
            input.GroupByField.Trim(),
            DateTimeOffset.UtcNow);
        savedViews.Add(savedView);
        return Task.FromResult<ProjectSavedView?>(savedView);
    }

    private Guid EnsureProject(string name, string description)
    {
        if (_projectIdByName.TryGetValue(name, out var existingProjectId))
        {
            return existingProjectId;
        }

        var projectId = Guid.NewGuid();
        _projectIdByName[name] = projectId;
        _projectNameById[projectId] = name;
        _projectDescriptionById[projectId] = description;
        EnsureProjectMetadata(projectId);
        return projectId;
    }

    private void EnsureProjectsFromIssues()
    {
        foreach (var projectName in _issues
            .Select(static issue => issue.ProjectName)
            .Where(static projectName => !string.IsNullOrWhiteSpace(projectName))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            EnsureProject(projectName!, $"Issues grouped under {projectName}.");
        }
    }

    private void EnsureProjectMetadata(Guid projectId)
    {
        if (!_customFieldsByProjectId.ContainsKey(projectId))
        {
            _customFieldsByProjectId[projectId] =
            [
                new ProjectCustomField(
                    Guid.NewGuid(),
                    projectId,
                    "Status",
                    ProjectCustomFieldType.SingleSelect,
                    "To do, In progress, Done"),
                new ProjectCustomField(
                    Guid.NewGuid(),
                    projectId,
                    "Target date",
                    ProjectCustomFieldType.Date,
                    string.Empty),
            ];
        }

        if (!_savedViewsByProjectId.ContainsKey(projectId))
        {
            _savedViewsByProjectId[projectId] =
            [
                new ProjectSavedView(
                    Guid.NewGuid(),
                    projectId,
                    "Board",
                    ProjectViewMode.Board,
                    "is:open",
                    "Board position",
                    "Status",
                    DateTimeOffset.UtcNow),
                new ProjectSavedView(
                    Guid.NewGuid(),
                    projectId,
                    "Calendar",
                    ProjectViewMode.Calendar,
                    "has:due-date",
                    "Due date",
                    "Due date",
                    DateTimeOffset.UtcNow),
            ];
        }
    }

    private ProjectSummary BuildProjectSummary(Guid projectId)
    {
        var projectName = _projectNameById[projectId];
        var issues = _issues
            .Where(issue => string.Equals(issue.ProjectName, projectName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return new ProjectSummary(
            projectId,
            projectName,
            _projectDescriptionById.GetValueOrDefault(projectId, string.Empty),
            issues.Length,
            issues.Count(static issue => issue.State == IssueWorkflowState.Open),
            issues.Count(static issue => issue.State == IssueWorkflowState.Closed),
            issues.Length == 0
                ? DateTimeOffset.UtcNow
                : issues.Max(static issue => issue.UpdatedAtUtc));
    }

    private ProjectIssueItem BuildProjectIssueItem(Guid projectId, IssueListItem issue)
    {
        var projectItemId = EnsureProjectItem(projectId, issue.Id);
        return new ProjectIssueItem(
            projectItemId,
            issue.Id,
            issue.Number,
            issue.Title,
            issue.State,
            issue.StateReason,
            issue.Priority,
            issue.AssigneeDisplayName,
            issue.DueDate,
            _boardColumnByProjectItemId[projectItemId],
            issue.Number,
            issue.UpdatedAtUtc,
            _customFieldValuesByProjectItemId.TryGetValue(projectItemId, out var values)
                ? new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase)
                : ProjectIssueItem.EmptyCustomFieldValues);
    }

    private Guid EnsureProjectItem(Guid projectId, Guid issueId)
    {
        var key = (projectId, issueId);
        if (_projectItemIdByIssue.TryGetValue(key, out var existingProjectItemId))
        {
            return existingProjectItemId;
        }

        var issue = _issues.First(item => item.Id == issueId);
        var projectItemId = Guid.NewGuid();
        _projectItemIdByIssue[key] = projectItemId;
        _projectItemLocationById[projectItemId] = key;
        _boardColumnByProjectItemId[projectItemId] = GetDefaultBoardColumn(issue);
        return projectItemId;
    }

    private static ProjectBoardColumn BuildProjectBoardColumn(
        string columnName,
        IReadOnlyList<ProjectIssueItem> items)
    {
        var columnItems = items
            .Where(item => string.Equals(item.BoardColumn, columnName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static item => item.SortOrder)
            .ToArray();

        return new ProjectBoardColumn(
            columnName,
            columnItems.Count(static item => item.State == IssueWorkflowState.Open),
            columnItems);
    }

    private static string GetDefaultBoardColumn(IssueListItem issue)
    {
        if (issue.State == IssueWorkflowState.Closed)
        {
            return "Done";
        }

        return issue.Priority is IssuePriority.Critical or IssuePriority.High
            ? "In progress"
            : "To do";
    }

    private IssueComment[] GetComments(Guid issueId)
    {
        return _commentsByIssueId.TryGetValue(issueId, out var comments)
            ? [.. comments]
            : [];
    }

    private IssueAttachment[] GetAttachments(Guid issueId)
    {
        return _attachmentsByIssueId.TryGetValue(issueId, out var attachments)
            ? [.. attachments]
            : [];
    }

    private IssueActivityEntry[] GetActivity(Guid issueId)
    {
        return _activityByIssueId.TryGetValue(issueId, out var activity)
            ? [.. activity.OrderByDescending(static item => item.CreatedAtUtc)]
            : [];
    }

    private IssueReminder[] GetReminders(Guid issueId)
    {
        return [.. _remindersById.Values
            .Where(reminder => reminder.IssueId == issueId)
            .OrderBy(static reminder => reminder.RemindAtUtc)];
    }

    private IssueRelation[] GetRelations(Guid issueId)
    {
        return _relationsByIssueId.TryGetValue(issueId, out var relations)
            ? [.. relations.OrderByDescending(static relation => relation.CreatedAtUtc)]
            : [];
    }

    private IReadOnlyList<IssueListItem> ResolveExportIssues(ExportOptions options)
    {
        IEnumerable<IssueListItem> selectedIssues = options.Scope switch
        {
            ExportSelectionScope.CurrentIssue when options.IssueId is Guid issueId =>
                _issues.Where(issue => issue.Id == issueId),
            ExportSelectionScope.CurrentFilter =>
                _issues.Where(issue => options.IssueIds.Contains(issue.Id)),
            ExportSelectionScope.Project when options.ProjectId is Guid projectId && _projectNameById.TryGetValue(projectId, out var projectName) =>
                _issues.Where(issue => string.Equals(issue.ProjectName, projectName, StringComparison.OrdinalIgnoreCase)),
            _ => _issues,
        };

        if (!options.IncludeClosedIssues)
        {
            selectedIssues = selectedIssues.Where(static issue => issue.State == IssueWorkflowState.Open);
        }

        return [.. selectedIssues.OrderBy(static issue => issue.Number)];
    }

    private void AddActivity(Guid issueId, string eventType, string summary)
    {
        _activityByIssueId[issueId].Add(
            new IssueActivityEntry(
                Guid.NewGuid(),
                issueId,
                eventType,
                summary,
                DateTimeOffset.UtcNow));
    }

    private static IssueStateReason NormalizeStateReason(IssueWorkflowState state, IssueStateReason reason)
    {
        if (state == IssueWorkflowState.Open)
        {
            return IssueStateReason.None;
        }

        return reason == IssueStateReason.None
            ? IssueStateReason.Completed
            : reason;
    }

    private static string BuildStateSummary(IssueWorkflowState state, IssueStateReason reason)
    {
        return state == IssueWorkflowState.Open
            ? "Issue was reopened for active work."
            : $"Issue was closed as {reason}.";
    }
}
