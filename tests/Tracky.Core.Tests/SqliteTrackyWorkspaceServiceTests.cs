using Microsoft.Data.Sqlite;
using Tracky.Core.Issues;
using Tracky.Infrastructure.Persistence;

namespace Tracky.Core.Tests;

public sealed class SqliteTrackyWorkspaceServiceTests
{
    [Fact]
    public async Task Create_and_update_issue_round_trips_through_the_local_workspace()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "Tracky.Tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var service = new SqliteTrackyWorkspaceService(
                new TrackyWorkspacePathProvider(testRoot),
                new IssueOverviewCalculator());

            var seededOverview = await service.GetOverviewAsync();
            Assert.NotEmpty(seededOverview.Issues);
            Assert.True(File.Exists(seededOverview.DatabasePath));

            var createdIssue = await service.CreateIssueAsync(
                new CreateIssueInput(
                    "Add workspace switching shell",
                    "Dabin",
                    IssuePriority.High,
                    new DateOnly(2026, 4, 23),
                    "Tracky Foundation",
                    ["foundation", "desktop"]));

            var overviewAfterCreate = await service.GetOverviewAsync();
            Assert.Contains(overviewAfterCreate.Issues, issue => issue.Id == createdIssue.Id);

            var comment = await service.AddIssueCommentAsync(
                new AddIssueCommentInput(
                    createdIssue.Id,
                    "Dabin",
                    "We should keep workspace switching visible from the top navigation."));

            Assert.NotNull(comment);

            var attachment = await service.AddIssueAttachmentAsync(
                new AddIssueAttachmentInput(
                    createdIssue.Id,
                    "workspace-switch-plan.txt",
                    "text/plain",
                    "Switch workspace from the global header."u8.ToArray()));

            Assert.NotNull(attachment);

            var updatedIssue = await service.UpdateIssueStateAsync(
                new UpdateIssueStateInput(
                    createdIssue.Id,
                    IssueWorkflowState.Closed,
                    IssueStateReason.Completed));

            Assert.NotNull(updatedIssue);
            Assert.Equal(IssueWorkflowState.Closed, updatedIssue!.State);
            Assert.Equal(IssueStateReason.Completed, updatedIssue.StateReason);

            var detail = await service.GetIssueDetailAsync(createdIssue.Id);
            Assert.NotNull(detail);
            Assert.Single(detail!.Comments);
            Assert.Single(detail.Attachments);
            Assert.Contains(detail.Activity, item => item.EventType == "issue.comment.added");
            Assert.Contains(detail.Activity, item => item.EventType == "issue.attachment.added");

            var exportedAttachmentPath = await service.ExportAttachmentToTempFileAsync(detail.Attachments[0].Id);
            Assert.False(string.IsNullOrWhiteSpace(exportedAttachmentPath));
            Assert.True(File.Exists(exportedAttachmentPath));
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Seeded_workspace_includes_issue_details_comments_attachments_and_activity()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "Tracky.Tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var service = new SqliteTrackyWorkspaceService(
                new TrackyWorkspacePathProvider(testRoot),
                new IssueOverviewCalculator());

            var overview = await service.GetOverviewAsync();
            var seededIssueWithAttachment = overview.Issues.First(issue => issue.AttachmentCount > 0);
            var detail = await service.GetIssueDetailAsync(seededIssueWithAttachment.Id);

            Assert.NotNull(detail);
            Assert.False(string.IsNullOrWhiteSpace(detail!.Description));
            Assert.NotEmpty(detail.Activity);
            Assert.Equal(seededIssueWithAttachment.AttachmentCount, detail.Attachments.Count);
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Missing_issue_and_attachment_operations_return_null_without_creating_side_effects()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "Tracky.Tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var service = new SqliteTrackyWorkspaceService(
                new TrackyWorkspacePathProvider(testRoot),
                new IssueOverviewCalculator());

            var missingIssueId = Guid.NewGuid();
            var missingDetail = await service.GetIssueDetailAsync(missingIssueId);
            var missingComment = await service.AddIssueCommentAsync(
                new AddIssueCommentInput(
                    missingIssueId,
                    "Dabin",
                    "This comment should not be saved."));
            var missingAttachment = await service.AddIssueAttachmentAsync(
                new AddIssueAttachmentInput(
                    missingIssueId,
                    "missing.txt",
                    "text/plain",
                    "missing"u8.ToArray()));
            var missingAttachmentPath = await service.ExportAttachmentToTempFileAsync(Guid.NewGuid());

            Assert.Null(missingDetail);
            Assert.Null(missingComment);
            Assert.Null(missingAttachment);
            Assert.Null(missingAttachmentPath);
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }
}
