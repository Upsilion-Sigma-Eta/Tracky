using Tracky.Core.Issues;
using Tracky.Core.Services;
using Tracky.Core.Workspaces;

namespace Tracky.App.Tests.TestDoubles;

public sealed class TestTrackyWorkspaceService : ITrackyWorkspaceService
{
    private readonly Dictionary<Guid, byte[]> _attachmentContentById = [];
    private readonly Dictionary<Guid, List<IssueActivityEntry>> _activityByIssueId = [];
    private readonly Dictionary<Guid, List<IssueAttachment>> _attachmentsByIssueId = [];
    private readonly Dictionary<Guid, List<IssueComment>> _commentsByIssueId = [];
    private readonly Dictionary<Guid, string> _descriptionByIssueId = [];
    private readonly IssueOverviewCalculator _overviewCalculator = new();
    private readonly List<IssueListItem> _issues = [];

    private TestTrackyWorkspaceService()
    {
    }

    public Guid OpenIssueId { get; private init; }

    public Guid ClosedIssueId { get; private init; }

    public IReadOnlyList<IssueListItem> Issues => _issues;

    public static TestTrackyWorkspaceService CreateDefault()
    {
        var now = DateTimeOffset.UtcNow;
        var openIssueId = Guid.NewGuid();
        var closedIssueId = Guid.NewGuid();
        var service = new TestTrackyWorkspaceService
        {
            OpenIssueId = openIssueId,
            ClosedIssueId = closedIssueId,
        };

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

        return service;
    }

    public Task<WorkspaceOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var orderedIssues = _issues
            .OrderByDescending(static issue => issue.UpdatedAtUtc)
            .ThenByDescending(static issue => issue.Number)
            .ToArray();
        var metrics = _overviewCalculator.Build(orderedIssues, DateOnly.FromDateTime(DateTime.Today));

        return Task.FromResult(
            new WorkspaceOverview(
                "Test Workspace",
                "A deterministic workspace used by GUI and view-model tests.",
                "memory://tracky-tests",
                metrics,
                orderedIssues));
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
                GetActivity(issueId)));
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
            input.Labels.ToArray());

        _issues.Add(issue);
        _descriptionByIssueId[issue.Id] = "Created by the quick capture flow.";
        _commentsByIssueId[issue.Id] = [];
        _attachmentsByIssueId[issue.Id] = [];
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

    public Task<IssueListItem?> UpdateIssueStateAsync(UpdateIssueStateInput input, CancellationToken cancellationToken = default)
    {
        var index = _issues.FindIndex(issue => issue.Id == input.IssueId);
        if (index < 0)
        {
            return Task.FromResult<IssueListItem?>(null);
        }

        var reason = input.State == IssueWorkflowState.Open ? IssueStateReason.None : input.Reason;
        var updatedIssue = _issues[index] with
        {
            State = input.State,
            StateReason = reason,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        _issues[index] = updatedIssue;
        AddActivity(input.IssueId, "issue.state.changed", $"State changed to {input.State}.");
        return Task.FromResult<IssueListItem?>(updatedIssue);
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
        _attachmentContentById[attachment.Id] = input.Content.ToArray();
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

    private IReadOnlyList<IssueComment> GetComments(Guid issueId)
    {
        return _commentsByIssueId.TryGetValue(issueId, out var comments)
            ? comments.ToArray()
            : [];
    }

    private IReadOnlyList<IssueAttachment> GetAttachments(Guid issueId)
    {
        return _attachmentsByIssueId.TryGetValue(issueId, out var attachments)
            ? attachments.ToArray()
            : [];
    }

    private IReadOnlyList<IssueActivityEntry> GetActivity(Guid issueId)
    {
        return _activityByIssueId.TryGetValue(issueId, out var activity)
            ? activity.OrderByDescending(static item => item.CreatedAtUtc).ToArray()
            : [];
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
}
