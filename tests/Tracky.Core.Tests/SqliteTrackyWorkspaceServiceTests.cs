using Microsoft.Data.Sqlite;
using Tracky.Core.Issues;
using Tracky.Core.Projects;
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
                new TrackyWorkspacePathProvider(testRoot));

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

            var metadataUpdate = await service.UpdateIssueAsync(
                new UpdateIssueInput(
                    createdIssue.Id,
                    "Add workspace switching shell and selector metadata",
                    "The selector should keep issue metadata editable from the Phase 1 detail panel.",
                    "Tracky Maintainer",
                    IssuePriority.Critical,
                    new DateOnly(2026, 4, 24),
                    "Tracky Shell",
                    ["foundation", "metadata", "desktop"]));

            Assert.NotNull(metadataUpdate);
            Assert.Equal("Add workspace switching shell and selector metadata", metadataUpdate.Title);
            Assert.Equal("Tracky Maintainer", metadataUpdate.AssigneeDisplayName);
            Assert.Equal(IssuePriority.Critical, metadataUpdate.Priority);
            Assert.Equal(new DateOnly(2026, 4, 24), metadataUpdate.DueDate);
            Assert.Equal("Tracky Shell", metadataUpdate.ProjectName);
            Assert.Contains("metadata", metadataUpdate.Labels);

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
            Assert.Equal(IssueWorkflowState.Closed, updatedIssue.State);
            Assert.Equal(IssueStateReason.Completed, updatedIssue.StateReason);

            var detail = await service.GetIssueDetailAsync(createdIssue.Id);
            Assert.NotNull(detail);
            Assert.Equal("The selector should keep issue metadata editable from the Phase 1 detail panel.", detail.Description);
            Assert.Single(detail.Comments);
            Assert.Single(detail.Attachments);
            Assert.Contains(detail.Activity, item => item.EventType == "issue.updated");
            Assert.Contains(detail.Activity, item => item.EventType == "issue.comment.added");
            Assert.Contains(detail.Activity, item => item.EventType == "issue.attachment.added");

            var exportedAttachmentPath = await service.ExportAttachmentToTempFileAsync(detail.Attachments[0].Id);
            Assert.False(string.IsNullOrWhiteSpace(exportedAttachmentPath));
            Assert.True(File.Exists(exportedAttachmentPath));

            Assert.True(await service.DeleteIssueAsync(createdIssue.Id));
            Assert.Null(await service.GetIssueDetailAsync(createdIssue.Id));
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
                new TrackyWorkspacePathProvider(testRoot));

            var overview = await service.GetOverviewAsync();
            var seededIssueWithAttachment = overview.Issues.First(issue => issue.AttachmentCount > 0);
            var detail = await service.GetIssueDetailAsync(seededIssueWithAttachment.Id);

            Assert.NotNull(detail);
            Assert.False(string.IsNullOrWhiteSpace(detail.Description));
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
    public async Task Project_management_core_persists_board_moves_custom_fields_and_saved_views()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "Tracky.Tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var service = new SqliteTrackyWorkspaceService(
                new TrackyWorkspacePathProvider(testRoot));

            var createdIssue = await service.CreateIssueAsync(
                new CreateIssueInput(
                    "Build the Phase 2 project board",
                    "Dabin",
                    IssuePriority.Critical,
                    new DateOnly(2026, 4, 28),
                    "Tracky Phase 2",
                    ["phase2", "project"]));
            var projects = await service.GetProjectsAsync();
            var phase2Project = Assert.Single(projects, project => project.Name == "Tracky Phase 2");

            var detail = await service.GetProjectDetailAsync(phase2Project.Id);
            Assert.NotNull(detail);
            Assert.Contains(detail.TableItems, item => item.IssueId == createdIssue.Id);
            Assert.Contains(detail.BoardColumns, column => column.Name == "In progress");
            Assert.NotEmpty(detail.CustomFields);
            Assert.NotEmpty(detail.SavedViews);

            var projectItem = detail.TableItems.Single(item => item.IssueId == createdIssue.Id);
            var movedItem = await service.MoveProjectItemAsync(
                new MoveProjectItemInput(projectItem.ProjectItemId, "Done"));
            Assert.NotNull(movedItem);
            Assert.Equal("Done", movedItem.BoardColumn);

            var field = await service.AddProjectCustomFieldAsync(
                new AddProjectCustomFieldInput(
                    phase2Project.Id,
                    "Impact",
                    ProjectCustomFieldType.SingleSelect,
                    "Low, Medium, High"));
            var savedView = await service.AddProjectSavedViewAsync(
                new AddProjectSavedViewInput(
                    phase2Project.Id,
                    "High impact board",
                    ProjectViewMode.Board,
                    "impact:high",
                    "Priority",
                    "Status"));

            Assert.NotNull(field);
            Assert.NotNull(savedView);
            Assert.Equal("Priority", savedView.SortByField);

            var itemWithFieldValue = await service.UpdateProjectCustomFieldValueAsync(
                new UpdateProjectCustomFieldValueInput(
                    projectItem.ProjectItemId,
                    field.Id,
                    "High"));
            Assert.NotNull(itemWithFieldValue);
            Assert.True(itemWithFieldValue.CustomFieldValues.TryGetValue("Impact", out var impactValue));
            Assert.Equal("High", impactValue);

            var updatedDetail = await service.GetProjectDetailAsync(phase2Project.Id);
            Assert.NotNull(updatedDetail);
            Assert.Contains(updatedDetail.TableItems, item => item.ProjectItemId == projectItem.ProjectItemId && item.BoardColumn == "Done");
            Assert.Contains(
                updatedDetail.TableItems,
                item => item.CustomFieldValues.TryGetValue("Impact", out var value) && value == "High");
            Assert.Contains(updatedDetail.CustomFields, item => item.Name == "Impact");
            Assert.Contains(updatedDetail.SavedViews, item => item.Name == "High impact board" && item.SortByField == "Priority");
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
    public async Task State_reason_transitions_round_trip_close_reasons_and_reopen_clears_reason()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "Tracky.Tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var service = new SqliteTrackyWorkspaceService(
                new TrackyWorkspacePathProvider(testRoot));

            var issue = await service.CreateIssueAsync(
                new CreateIssueInput(
                    "Exercise every Phase 1 close reason",
                    "Dabin",
                    IssuePriority.High,
                    null,
                    "Tracky State Model",
                    ["state"]));

            foreach (var closeReason in new[]
            {
                IssueStateReason.Completed,
                IssueStateReason.NotPlanned,
                IssueStateReason.Duplicate,
                IssueStateReason.None,
            })
            {
                var expectedReason = closeReason == IssueStateReason.None
                    ? IssueStateReason.Completed
                    : closeReason;

                var closedIssue = await service.UpdateIssueStateAsync(
                    new UpdateIssueStateInput(issue.Id, IssueWorkflowState.Closed, closeReason));

                Assert.NotNull(closedIssue);
                Assert.Equal(IssueWorkflowState.Closed, closedIssue.State);
                Assert.Equal(expectedReason, closedIssue.StateReason);

                var reopenedIssue = await service.UpdateIssueStateAsync(
                    new UpdateIssueStateInput(issue.Id, IssueWorkflowState.Open, IssueStateReason.Duplicate));

                Assert.NotNull(reopenedIssue);
                Assert.Equal(IssueWorkflowState.Open, reopenedIssue.State);
                Assert.Equal(IssueStateReason.None, reopenedIssue.StateReason);
            }
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
    public async Task Update_issue_trims_optional_metadata_and_deduplicates_labels()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "Tracky.Tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var service = new SqliteTrackyWorkspaceService(
                new TrackyWorkspacePathProvider(testRoot));

            var issue = await service.CreateIssueAsync(
                new CreateIssueInput(
                    "Normalize issue metadata",
                    "Dabin",
                    IssuePriority.Medium,
                    new DateOnly(2026, 4, 25),
                    "Tracky Metadata",
                    ["foundation"]));

            var updatedIssue = await service.UpdateIssueAsync(
                new UpdateIssueInput(
                    issue.Id,
                    "  Normalized issue metadata  ",
                    "  Body should be trimmed.  ",
                    "   ",
                    IssuePriority.None,
                    null,
                    "   ",
                    ["ux", " UX ", "", "bug", "bug"]));

            Assert.NotNull(updatedIssue);
            Assert.Equal("Normalized issue metadata", updatedIssue.Title);
            Assert.Null(updatedIssue.AssigneeDisplayName);
            Assert.Null(updatedIssue.DueDate);
            Assert.Null(updatedIssue.ProjectName);
            Assert.Equal(IssuePriority.None, updatedIssue.Priority);
            Assert.Equal(2, updatedIssue.Labels.Count);
            Assert.Contains("ux", updatedIssue.Labels);
            Assert.Contains("bug", updatedIssue.Labels);

            var detail = await service.GetIssueDetailAsync(issue.Id);
            Assert.NotNull(detail);
            Assert.Equal("Body should be trimmed.", detail.Description);
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
    public async Task Attachment_export_sanitizes_windows_unsafe_file_names_and_defaults_content_type()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "Tracky.Tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var service = new SqliteTrackyWorkspaceService(
                new TrackyWorkspacePathProvider(testRoot));

            var issue = await service.CreateIssueAsync(
                new CreateIssueInput(
                    "Attach a Windows unsafe file name",
                    "Dabin",
                    IssuePriority.Low,
                    null,
                    "Tracky Attachments",
                    ["attachment"]));
            var content = "Attachment content should survive the temp export."u8.ToArray();

            var attachment = await service.AddIssueAttachmentAsync(
                new AddIssueAttachmentInput(
                    issue.Id,
                    "phase1:notes?.txt",
                    "",
                    content));

            Assert.NotNull(attachment);
            Assert.Equal("application/octet-stream", attachment.ContentType);

            var exportedPath = await service.ExportAttachmentToTempFileAsync(attachment.Id);
            Assert.False(string.IsNullOrWhiteSpace(exportedPath));

            var exportedFileName = Path.GetFileName(exportedPath);
            Assert.DoesNotContain(':', exportedFileName);
            Assert.DoesNotContain('?', exportedFileName);
            Assert.Equal(content, await File.ReadAllBytesAsync(exportedPath));
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
    public async Task Invalid_required_inputs_throw_without_mutating_the_workspace()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "Tracky.Tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var service = new SqliteTrackyWorkspaceService(
                new TrackyWorkspacePathProvider(testRoot));
            var baselineOverview = await service.GetOverviewAsync();
            var issue = await service.CreateIssueAsync(
                new CreateIssueInput(
                    "Validate required inputs",
                    "Dabin",
                    IssuePriority.Medium,
                    null,
                    "Tracky Validation",
                    ["validation"]));

            await Assert.ThrowsAsync<ArgumentException>(
                () => service.CreateIssueAsync(
                    new CreateIssueInput("   ", "Dabin", IssuePriority.Low, null, null, [])));
            await Assert.ThrowsAsync<ArgumentException>(
                () => service.UpdateIssueAsync(
                    new UpdateIssueInput(issue.Id, "   ", "body", "Dabin", IssuePriority.Low, null, null, [])));
            await Assert.ThrowsAsync<ArgumentException>(
                () => service.AddIssueCommentAsync(
                    new AddIssueCommentInput(issue.Id, "Dabin", "   ")));
            await Assert.ThrowsAsync<ArgumentException>(
                () => service.AddIssueCommentAsync(
                    new AddIssueCommentInput(issue.Id, "   ", "Body")));
            await Assert.ThrowsAsync<ArgumentException>(
                () => service.AddIssueAttachmentAsync(
                    new AddIssueAttachmentInput(issue.Id, "   ", "text/plain", [])));
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => service.AddIssueAttachmentAsync(
                    new AddIssueAttachmentInput(issue.Id, "file.txt", "text/plain", null!)));

            var overviewAfterInvalidInputs = await service.GetOverviewAsync();
            Assert.Equal(baselineOverview.Issues.Count + 1, overviewAfterInvalidInputs.Issues.Count);

            var detail = await service.GetIssueDetailAsync(issue.Id);
            Assert.NotNull(detail);
            Assert.Empty(detail.Comments);
            Assert.Empty(detail.Attachments);
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
    public async Task Delete_issue_cascades_comments_attachments_labels_and_export_content()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "Tracky.Tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var service = new SqliteTrackyWorkspaceService(
                new TrackyWorkspacePathProvider(testRoot));
            var issue = await service.CreateIssueAsync(
                new CreateIssueInput(
                    "Delete should cascade child data",
                    "Dabin",
                    IssuePriority.High,
                    null,
                    "Tracky Delete",
                    ["delete", "cascade"]));
            var comment = await service.AddIssueCommentAsync(
                new AddIssueCommentInput(issue.Id, "Dabin", "This comment should be removed with the issue."));
            var attachment = await service.AddIssueAttachmentAsync(
                new AddIssueAttachmentInput(issue.Id, "cascade.txt", "text/plain", "cascade"u8.ToArray()));

            Assert.NotNull(comment);
            Assert.NotNull(attachment);
            Assert.True(await service.DeleteIssueAsync(issue.Id));

            var overview = await service.GetOverviewAsync();
            Assert.DoesNotContain(overview.Issues, item => item.Id == issue.Id);
            Assert.Null(await service.GetIssueDetailAsync(issue.Id));
            Assert.Null(await service.ExportAttachmentToTempFileAsync(attachment.Id));
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
                new TrackyWorkspacePathProvider(testRoot));

            var missingIssueId = Guid.NewGuid();
            var missingDetail = await service.GetIssueDetailAsync(missingIssueId);
            var missingComment = await service.AddIssueCommentAsync(
                new AddIssueCommentInput(
                    missingIssueId,
                    "Dabin",
                    "This comment should not be saved."));
            var missingUpdate = await service.UpdateIssueAsync(
                new UpdateIssueInput(
                    missingIssueId,
                    "Missing issue",
                    "This update should not be saved.",
                    "Dabin",
                    IssuePriority.Low,
                    null,
                    null,
                    ["missing"]));
            var missingAttachment = await service.AddIssueAttachmentAsync(
                new AddIssueAttachmentInput(
                    missingIssueId,
                    "missing.txt",
                    "text/plain",
                    "missing"u8.ToArray()));
            var missingAttachmentPath = await service.ExportAttachmentToTempFileAsync(Guid.NewGuid());

            Assert.Null(missingDetail);
            Assert.Null(missingComment);
            Assert.Null(missingUpdate);
            Assert.Null(missingAttachment);
            Assert.Null(missingAttachmentPath);
            Assert.False(await service.DeleteIssueAsync(missingIssueId));
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
