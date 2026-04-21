using Tracky.App.Tests.TestDoubles;
using Tracky.App.ViewModels;
using Tracky.Core.Exports;
using Tracky.Core.Issues;
using Tracky.Core.Preferences;
using Tracky.Core.Projects;

namespace Tracky.App.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task Initialize_filters_and_loads_the_selected_issue_detail()
    {
        var viewModel = CreateViewModel(out _, out _);

        await viewModel.InitializeAsync();
        await TestWaiter.UntilAsync(
            () => viewModel.SelectedIssueDetail is not null,
            "The selected issue detail was not loaded after initialization.");

        Assert.Equal(2, viewModel.TotalIssues);
        Assert.Equal(2, viewModel.VisibleIssues.Count);
        Assert.NotNull(viewModel.SelectedIssue);
        Assert.NotNull(viewModel.SelectedIssueDetail);
        Assert.Equal(viewModel.SelectedIssue!.Id, viewModel.SelectedIssueDetail!.Summary.Id);

        viewModel.SearchText = "closed";
        Assert.Single(viewModel.VisibleIssues);
        Assert.Equal("Closed issue should remain filterable", viewModel.VisibleIssues[0].Title);

        viewModel.ShowOpenCommand.Execute(null);
        Assert.Empty(viewModel.VisibleIssues);

        viewModel.SearchText = string.Empty;
        viewModel.ShowClosedCommand.Execute(null);
        Assert.Single(viewModel.VisibleIssues);
        Assert.True(viewModel.VisibleIssues[0].IsClosed);
    }

    [Fact]
    public async Task Search_matches_number_assignee_project_and_label_and_clears_selection_when_empty()
    {
        var viewModel = CreateViewModel(out _, out _);

        await viewModel.InitializeAsync();
        await TestWaiter.UntilAsync(
            () => viewModel.SelectedIssueDetail is not null,
            "The selected issue detail was not loaded before search edge cases ran.");

        viewModel.SearchText = "#201";
        Assert.Single(viewModel.VisibleIssues);
        Assert.Equal(201, viewModel.VisibleIssues[0].Number);

        viewModel.SearchText = "Dabin";
        Assert.Equal(2, viewModel.VisibleIssues.Count);

        viewModel.SearchText = "Tracky Foundation";
        Assert.Single(viewModel.VisibleIssues);
        Assert.Equal("Prepare GUI test coverage for quick capture", viewModel.VisibleIssues[0].Title);

        viewModel.SearchText = "desktop";
        Assert.Single(viewModel.VisibleIssues);
        Assert.Contains("desktop", viewModel.VisibleIssues[0].Labels);

        viewModel.SearchText = "no matching issue";
        Assert.Empty(viewModel.VisibleIssues);
        Assert.Null(viewModel.SelectedIssue);
        Assert.Null(viewModel.SelectedIssueDetail);
    }

    [Fact]
    public async Task Phase3_advanced_search_matches_state_label_reason_due_and_negated_operators()
    {
        var viewModel = CreateViewModel(out _, out _);

        await viewModel.InitializeAsync();
        await TestWaiter.UntilAsync(
            () => viewModel.SelectedIssueDetail is not null,
            "The selected issue detail was not loaded before Phase 3 search ran.");

        viewModel.SearchText = "is:open label:desktop due:today";
        Assert.Single(viewModel.VisibleIssues);
        Assert.Equal("Prepare GUI test coverage for quick capture", viewModel.VisibleIssues[0].Title);

        viewModel.SearchText = "is:closed reason:completed";
        Assert.Single(viewModel.VisibleIssues);
        Assert.True(viewModel.VisibleIssues[0].IsClosed);

        viewModel.SearchText = "-label:desktop assignee:me";
        Assert.Single(viewModel.VisibleIssues);
        Assert.DoesNotContain("desktop", viewModel.VisibleIssues[0].Labels);
    }

    [Fact]
    public async Task Phase3_metadata_relations_saved_searches_and_preferences_flow_through_commands()
    {
        var viewModel = CreateViewModel(out _, out _);

        await viewModel.InitializeAsync();
        await TestWaiter.UntilAsync(
            () => viewModel.SelectedIssueDetail is not null,
            "The selected issue detail was not loaded before Phase 3 command coverage ran.");

        viewModel.DraftTitle = "Phase 3 relation target";
        viewModel.DraftProjectName = "Tracky Phase 3";
        viewModel.DraftLabels = "phase3, relation";
        viewModel.DraftMilestoneName = "Phase 3 Validation";
        viewModel.DraftIssueTypeName = "Task";
        await viewModel.CreateIssueCommand.ExecuteAsync();
        await TestWaiter.UntilAsync(
            () => viewModel.SelectedIssue?.Title == "Phase 3 relation target",
            "The relation target issue was not created and selected.");
        var targetIssueNumber = viewModel.SelectedIssue!.Number;

        viewModel.DraftTitle = "Phase 3 metadata command flow";
        viewModel.DraftProjectName = "Tracky Phase 3";
        viewModel.DraftLabels = "phase3, metadata";
        viewModel.DraftMilestoneName = "Phase 3 Validation";
        viewModel.DraftIssueTypeName = "Bug";
        await viewModel.CreateIssueCommand.ExecuteAsync();
        await TestWaiter.UntilAsync(
            () => viewModel.SelectedIssueDetail?.Summary.Title == "Phase 3 metadata command flow",
            "The metadata issue was not selected with detail loaded.");

        Assert.Equal("Phase 3 Validation", viewModel.SelectedIssue!.MilestoneText);
        Assert.Equal("Bug", viewModel.SelectedIssue.IssueTypeText);

        viewModel.SearchText = "milestone:Validation type:Bug";
        Assert.Single(viewModel.VisibleIssues);
        Assert.Equal("Phase 3 metadata command flow", viewModel.VisibleIssues[0].Title);

        viewModel.DraftRelationTargetIssueNumber = targetIssueNumber;
        viewModel.DraftRelationType = IssueRelationType.BlockedBy;
        await viewModel.AddIssueRelationCommand.ExecuteAsync();
        await TestWaiter.UntilAsync(
            () => viewModel.SelectedIssueDetail?.Relations.Any(relation => relation.Relation.TargetIssueNumber == targetIssueNumber) == true,
            "The issue relation was not added and reloaded into the selected detail.");
        Assert.Contains("Relation to", viewModel.DetailStatusMessage, StringComparison.OrdinalIgnoreCase);

        viewModel.DraftSavedIssueSearchName = "Phase 3 validation bugs";
        await viewModel.SaveIssueSearchCommand.ExecuteAsync();
        await TestWaiter.UntilAsync(
            () => viewModel.SavedIssueSearches.Any(search => search.QueryText == "milestone:Validation type:Bug"),
            "The saved issue search was not reloaded after saving.");

        viewModel.SearchText = string.Empty;
        var savedSearch = viewModel.SavedIssueSearches.Single(search => search.QueryText == "milestone:Validation type:Bug");
        await viewModel.ApplySavedIssueSearchCommand.ExecuteAsync(savedSearch);
        Assert.Equal("milestone:Validation type:Bug", viewModel.SearchText);
        Assert.Single(viewModel.VisibleIssues);

        viewModel.SelectedThemePreference = AppThemePreference.Dark;
        viewModel.CompactDensityPreference = false;
        viewModel.ShortcutProfilePreference = "Vim";
        await viewModel.SavePreferencesCommand.ExecuteAsync();

        Assert.Equal(AppThemePreference.Dark, viewModel.SelectedThemePreference);
        Assert.False(viewModel.CompactDensityPreference);
        Assert.Equal("Vim", viewModel.ShortcutProfilePreference);
        Assert.Contains("Preferences saved", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Phase3_reminder_commands_schedule_and_dismiss_the_selected_issue_reminder()
    {
        var viewModel = CreateViewModel(out _, out _);

        await viewModel.InitializeAsync();
        await TestWaiter.UntilAsync(
            () => viewModel.SelectedIssueDetail is not null,
            "The selected issue detail was not loaded before reminder commands ran.");

        viewModel.DraftReminderTitle = "Review reminder command flow";
        viewModel.DraftReminderNote = "This reminder is scheduled through the ViewModel command.";
        viewModel.DraftReminderAt = DateTimeOffset.Now.AddHours(4);

        await viewModel.ScheduleReminderCommand.ExecuteAsync();
        await TestWaiter.UntilAsync(
            () => viewModel.SelectedIssueDetail?.Reminders.Any(reminder => reminder.Title == "Review reminder command flow") == true,
            "The scheduled reminder did not appear on the selected issue detail.");

        var reminder = viewModel.SelectedIssueDetail!.Reminders.Single(item => item.Title == "Review reminder command flow");
        await viewModel.DismissReminderCommand.ExecuteAsync(reminder);
        await TestWaiter.UntilAsync(
            () => viewModel.SelectedIssueDetail?.Reminders.Any(item => item.Id == reminder.Id && item.IsDismissed) == true,
            "The reminder was not dismissed on the selected issue detail.");

        Assert.Contains("dismissed", viewModel.DetailStatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Phase3_export_commands_write_selection_and_store_presets()
    {
        var viewModel = CreateViewModel(out _, out _);

        await viewModel.InitializeAsync();
        await TestWaiter.UntilAsync(
            () => viewModel.SelectedIssueDetail is not null,
            "The selected issue detail was not loaded before export commands ran.");

        viewModel.SearchText = "is:open";
        viewModel.DraftExportScope = ExportSelectionScope.CurrentFilter;
        viewModel.DraftExportFormat = ExportFormat.Markdown;
        viewModel.DraftExportBodyFormat = ExportBodyFormat.Markdown;
        viewModel.DraftExportIncludeComments = true;
        viewModel.DraftExportIncludeActivity = true;

        await viewModel.ExportSelectionCommand.ExecuteAsync();
        Assert.True(File.Exists(viewModel.LastExportPath));
        Assert.Contains("Exported", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);

        viewModel.ExportPresetName = "Open issue markdown";
        await viewModel.SaveExportPresetCommand.ExecuteAsync();
        await TestWaiter.UntilAsync(
            () => viewModel.ExportPresets.Any(preset => preset.Name == "Open issue markdown"),
            "The export preset was not reloaded after saving.");
    }

    [Fact]
    public async Task Quick_capture_creates_and_selects_a_new_issue_with_detail_loaded()
    {
        var viewModel = CreateViewModel(out _, out _);
        await viewModel.InitializeAsync();

        viewModel.DraftTitle = "Write a broader GUI regression suite";
        viewModel.DraftAssignee = "Dabin";
        viewModel.DraftPriority = IssuePriority.Critical;
        viewModel.DraftProjectName = "Tracky Tests";
        viewModel.DraftLabels = "tests, gui";

        await viewModel.CreateIssueCommand.ExecuteAsync();
        await TestWaiter.UntilAsync(
            () => viewModel.SelectedIssueDetail?.Summary.Title == "Write a broader GUI regression suite",
            "The quick-captured issue was not selected with its detail loaded.");

        Assert.Equal(3, viewModel.TotalIssues);
        Assert.Equal("Write a broader GUI regression suite", viewModel.SelectedIssue!.Title);
        Assert.Equal(IssuePriority.Critical, viewModel.SelectedIssue.Issue.Priority);
        Assert.Contains("Issue #", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Project_screen_loads_phase2_views_and_updates_project_metadata()
    {
        var viewModel = CreateViewModel(out _, out _);

        await viewModel.InitializeAsync();
        viewModel.DraftProjectName = "Tracky Foundation";
        viewModel.DraftTitle = "Critical project sorting check";
        viewModel.DraftPriority = IssuePriority.Critical;
        viewModel.DraftDueDate = DateTimeOffset.Now.AddDays(1);
        await viewModel.CreateIssueCommand.ExecuteAsync();
        viewModel.DraftTitle = "Low project sorting check";
        viewModel.DraftPriority = IssuePriority.Low;
        viewModel.DraftDueDate = DateTimeOffset.Now.AddDays(5);
        await viewModel.CreateIssueCommand.ExecuteAsync();

        viewModel.ShowProjectsCommand.Execute(null);
        await TestWaiter.UntilAsync(
            () => viewModel.SelectedProject is not null && viewModel.ProjectBoardColumns.Count > 0,
            "The Projects screen did not load a selected project with board columns.");

        Assert.True(viewModel.IsProjectsViewActive);
        Assert.False(viewModel.IsIssuesViewActive);
        Assert.NotEmpty(viewModel.Projects);
        Assert.NotEmpty(viewModel.ProjectTableItems);
        Assert.NotEmpty(viewModel.ProjectCustomFields);
        Assert.NotEmpty(viewModel.ProjectSavedViews);

        viewModel.ProjectSortText = "Priority";
        viewModel.ProjectGroupByText = "Priority";
        Assert.Equal(IssuePriority.Critical, viewModel.ProjectTableItems.First().Item.Priority);
        Assert.Equal("Critical", viewModel.ProjectTableGroups.First().Name);
        Assert.Contains(viewModel.ProjectTableGroups, group => group.Name == "Low");

        var movableItem = viewModel.ProjectBoardColumns
            .SelectMany(column => column.Items)
            .First(item => item.HasNextColumn);
        await viewModel.MoveProjectItemForwardCommand.ExecuteAsync(movableItem);
        await TestWaiter.UntilAsync(
            () => viewModel.ProjectBoardColumns
                .Any(column => column.Name == movableItem.NextBoardColumn
                    && column.Items.Any(item => item.ProjectItemId == movableItem.ProjectItemId)),
            "The project board item was not moved to the next Phase 2 column.");

        viewModel.DraftCustomFieldName = "Impact";
        viewModel.DraftCustomFieldType = ProjectCustomFieldType.SingleSelect;
        viewModel.DraftCustomFieldOptions = "Low, Medium, High";
        await viewModel.AddProjectCustomFieldCommand.ExecuteAsync();
        await TestWaiter.UntilAsync(
            () => viewModel.ProjectCustomFields.Any(field => field.Name == "Impact"),
            "The custom field command did not refresh the selected project detail.");

        var impactField = viewModel.ProjectCustomFields.Single(field => field.Name == "Impact");
        var fieldItem = viewModel.ProjectTableItems.First();
        viewModel.SelectedProjectItemForFields = fieldItem;
        viewModel.SelectedProjectCustomField = impactField;
        viewModel.DraftCustomFieldValue = "High";
        await viewModel.SaveProjectCustomFieldValueCommand.ExecuteAsync();
        await TestWaiter.UntilAsync(
            () => viewModel.ProjectTableItems.Any(
                item => item.ProjectItemId == fieldItem.ProjectItemId
                    && item.Item.CustomFieldValues.TryGetValue("Impact", out var value)
                    && value == "High"),
            "The custom field value command did not refresh the selected project item.");

        viewModel.DraftSavedViewName = "High impact board";
        viewModel.DraftSavedViewMode = ProjectViewMode.Board;
        viewModel.DraftSavedViewFilter = "impact:high";
        viewModel.DraftSavedViewSort = "Priority";
        viewModel.DraftSavedViewGroup = "Status";
        await viewModel.AddProjectSavedViewCommand.ExecuteAsync();
        await TestWaiter.UntilAsync(
            () => viewModel.ProjectSavedViews.Any(view => view.Name == "High impact board"),
            "The saved view command did not refresh the selected project detail.");

        var highImpactView = viewModel.ProjectSavedViews.Single(view => view.Name == "High impact board");
        await viewModel.ApplyProjectSavedViewCommand.ExecuteAsync(highImpactView);

        Assert.True(viewModel.IsProjectBoardViewActive);
        Assert.Equal("impact:high", viewModel.ProjectFilterText);
        Assert.Equal("Priority", viewModel.ProjectSortText);
        Assert.Equal("Status", viewModel.ProjectGroupByText);
        Assert.All(viewModel.ProjectTableGroups, group => Assert.False(string.IsNullOrWhiteSpace(group.Name)));
        Assert.All(
            viewModel.ProjectTableItems,
            item => Assert.True(item.Item.CustomFieldValues.TryGetValue("Impact", out var value) && value == "High"));
    }

    [Fact]
    public async Task Create_project_selects_the_new_project_shell()
    {
        var viewModel = CreateViewModel(out _, out _);
        await viewModel.InitializeAsync();

        viewModel.NewProjectName = "Tracky Reports";
        viewModel.NewProjectDescription = "Report planning for later phases.";
        await viewModel.CreateProjectCommand.ExecuteAsync();
        await TestWaiter.UntilAsync(
            () => viewModel.SelectedProject?.Name == "Tracky Reports",
            "The newly created project was not selected after project creation.");

        Assert.True(viewModel.IsProjectsViewActive);
        Assert.Contains(viewModel.Projects, project => project.Name == "Tracky Reports");
        Assert.Equal(0, viewModel.SelectedProject!.TotalIssues);
    }

    [Fact]
    public async Task Commands_reject_blank_required_fields_and_cancelled_attachment_imports()
    {
        var viewModel = CreateViewModel(out var picker, out _);

        await viewModel.InitializeAsync();
        await TestWaiter.UntilAsync(
            () => viewModel.SelectedIssueDetail is not null,
            "The selected issue detail was not loaded before command edge cases ran.");

        viewModel.DraftTitle = "   ";
        Assert.False(viewModel.CreateIssueCommand.CanExecute(null));

        viewModel.EditTitle = "   ";
        Assert.False(viewModel.UpdateSelectedIssueCommand.CanExecute(null));

        viewModel.DraftCommentBody = "   ";
        Assert.False(viewModel.AddCommentCommand.CanExecute(null));

        viewModel.DraftCommentBody = "Valid body";
        viewModel.DraftCommentAuthor = "   ";
        Assert.False(viewModel.AddCommentCommand.CanExecute(null));

        var attachmentCount = viewModel.SelectedIssueDetail!.Attachments.Count;
        await viewModel.AttachFileCommand.ExecuteAsync();

        Assert.Equal(1, picker.PickCount);
        Assert.Equal(attachmentCount, viewModel.SelectedIssueDetail!.Attachments.Count);
        Assert.Contains("canceled", viewModel.DetailStatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Detail_commands_add_comment_attach_file_and_open_exported_attachment()
    {
        var temporaryAttachmentPath = Path.Combine(Path.GetTempPath(), $"tracky-input-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(temporaryAttachmentPath, "Attachment content from GUI command test.");

        try
        {
            var viewModel = CreateViewModel(out var picker, out var launcher);
            picker.NextPath = temporaryAttachmentPath;

            await viewModel.InitializeAsync();
            await TestWaiter.UntilAsync(
                () => viewModel.SelectedIssueDetail is not null,
                "The selected issue detail was not loaded before detail commands ran.");

            viewModel.DraftCommentBody = "The GUI command should append this comment to the selected issue.";
            await viewModel.AddCommentCommand.ExecuteAsync();
            await TestWaiter.UntilAsync(
                () => viewModel.SelectedIssueDetail?.Comments.Count == 2,
                "The comment command did not refresh the selected issue detail.");

            await viewModel.AttachFileCommand.ExecuteAsync();
            await TestWaiter.UntilAsync(
                () => viewModel.SelectedIssueDetail?.Attachments.Count == 2,
                "The attach-file command did not refresh the selected issue detail.");

            var attachment = viewModel.SelectedIssueDetail!.Attachments[0];
            await viewModel.OpenAttachmentCommand.ExecuteAsync(attachment);

            Assert.Equal(1, picker.PickCount);
            Assert.Single(launcher.OpenedPaths);
            Assert.True(File.Exists(launcher.OpenedPaths[0]));
            Assert.Contains("opened via the system shell", viewModel.DetailStatusMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(temporaryAttachmentPath))
            {
                File.Delete(temporaryAttachmentPath);
            }
        }
    }

    [Fact]
    public async Task Edit_commands_update_selected_issue_metadata_and_delete_it()
    {
        var viewModel = CreateViewModel(out _, out _);

        await viewModel.InitializeAsync();
        await TestWaiter.UntilAsync(
            () => viewModel.SelectedIssueDetail is not null,
            "The selected issue detail was not loaded before edit commands ran.");

        var selectedIssueId = viewModel.SelectedIssue!.Id;
        viewModel.EditTitle = "Refine the Phase 1 issue editor";
        viewModel.EditDescription = "The selected issue editor should persist body and metadata changes.";
        viewModel.EditAssignee = "Tracky Maintainer";
        viewModel.EditPriority = IssuePriority.Critical;
        viewModel.EditProjectName = "Tracky CRUD";
        viewModel.EditLabels = "foundation, crud";

        await viewModel.UpdateSelectedIssueCommand.ExecuteAsync();
        await TestWaiter.UntilAsync(
            () => viewModel.SelectedIssueDetail?.Summary.Title == "Refine the Phase 1 issue editor",
            "The selected issue did not reload after metadata was updated.");

        Assert.Equal(selectedIssueId, viewModel.SelectedIssue!.Id);
        Assert.Equal("Tracky Maintainer", viewModel.SelectedIssue.AssigneeText);
        Assert.Equal(IssuePriority.Critical, viewModel.SelectedIssue.Issue.Priority);
        Assert.Equal("Tracky CRUD", viewModel.SelectedIssue.ProjectText);
        Assert.Contains("crud", viewModel.SelectedIssue.Labels);
        Assert.Equal("The selected issue editor should persist body and metadata changes.", viewModel.SelectedIssueDetail!.Detail.Description);
        Assert.Contains("saved", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);

        await viewModel.DeleteSelectedIssueCommand.ExecuteAsync();
        await TestWaiter.UntilAsync(
            () => viewModel.TotalIssues == 1 && viewModel.VisibleIssues.All(issue => issue.Id != selectedIssueId),
            "The selected issue was not removed from the All Issues list.");

        Assert.DoesNotContain(viewModel.VisibleIssues, issue => issue.Id == selectedIssueId);
        Assert.Contains("deleted", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Toggle_selected_issue_state_closes_and_reopens_the_same_issue()
    {
        var viewModel = CreateViewModel(out _, out _);

        await viewModel.InitializeAsync();
        await TestWaiter.UntilAsync(
            () => viewModel.SelectedIssue is not null && viewModel.SelectedIssue.IsOpen,
            "The default selected issue was not loaded as open.");

        var selectedIssueId = viewModel.SelectedIssue!.Id;
        viewModel.SelectedCloseReason = IssueStateReason.Duplicate;

        await viewModel.ToggleSelectedIssueStateCommand.ExecuteAsync();
        await TestWaiter.UntilAsync(
            () => viewModel.SelectedIssue?.Id == selectedIssueId
                && viewModel.SelectedIssue.IsClosed
                && viewModel.SelectedIssue.Issue.StateReason == IssueStateReason.Duplicate,
            "The selected issue was not closed by the state command.");

        Assert.False(viewModel.CanChooseCloseReason);
        Assert.Contains("duplicate", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);

        await viewModel.ToggleSelectedIssueStateCommand.ExecuteAsync();
        await TestWaiter.UntilAsync(
            () => viewModel.SelectedIssue?.Id == selectedIssueId
                && viewModel.SelectedIssue.IsOpen
                && viewModel.SelectedIssue.Issue.StateReason == IssueStateReason.None,
            "The selected issue was not reopened by the state command.");

        await TestWaiter.UntilAsync(
            () => viewModel.SelectedIssueDetail?.Summary.Id == selectedIssueId
                && viewModel.SelectedCloseReason == IssueStateReason.Completed,
            "The close reason draft was not reset after reopening the issue.");
        Assert.True(viewModel.CanChooseCloseReason);
    }

    private static MainWindowViewModel CreateViewModel(
        out TestAttachmentPicker picker,
        out TestAttachmentLauncher launcher)
    {
        var service = TestTrackyWorkspaceService.CreateDefault();
        picker = new TestAttachmentPicker();
        launcher = new TestAttachmentLauncher();
        return new MainWindowViewModel(service, picker, launcher);
    }
}
