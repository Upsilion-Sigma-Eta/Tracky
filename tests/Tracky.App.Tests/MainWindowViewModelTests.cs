using CommunityToolkit.Mvvm.Input;
using Tracky.App.Tests.TestDoubles;
using Tracky.App.ViewModels;
using Tracky.Core.Issues;

namespace Tracky.App.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task Initialize_filters_and_loads_the_selected_issue_detail()
    {
        var viewModel = CreateViewModel(out _, out _, out _);

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
    public async Task Quick_capture_creates_and_selects_a_new_issue_with_detail_loaded()
    {
        var viewModel = CreateViewModel(out _, out _, out _);
        await viewModel.InitializeAsync();

        viewModel.DraftTitle = "Write a broader GUI regression suite";
        viewModel.DraftAssignee = "Dabin";
        viewModel.DraftPriority = IssuePriority.Critical;
        viewModel.DraftProjectName = "Tracky Tests";
        viewModel.DraftLabels = "tests, gui";

        await ((IAsyncRelayCommand)viewModel.CreateIssueCommand).ExecuteAsync(null);
        await TestWaiter.UntilAsync(
            () => viewModel.SelectedIssueDetail?.Summary.Title == "Write a broader GUI regression suite",
            "The quick-captured issue was not selected with its detail loaded.");

        Assert.Equal(3, viewModel.TotalIssues);
        Assert.Equal("Write a broader GUI regression suite", viewModel.SelectedIssue!.Title);
        Assert.Equal(IssuePriority.Critical, viewModel.SelectedIssue.Issue.Priority);
        Assert.Contains("Issue #", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Detail_commands_add_comment_attach_file_and_open_exported_attachment()
    {
        var temporaryAttachmentPath = Path.Combine(Path.GetTempPath(), $"tracky-input-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(temporaryAttachmentPath, "Attachment content from GUI command test.");

        try
        {
            var viewModel = CreateViewModel(out _, out var picker, out var launcher);
            picker.NextPath = temporaryAttachmentPath;

            await viewModel.InitializeAsync();
            await TestWaiter.UntilAsync(
                () => viewModel.SelectedIssueDetail is not null,
                "The selected issue detail was not loaded before detail commands ran.");

            viewModel.DraftCommentBody = "The GUI command should append this comment to the selected issue.";
            await ((IAsyncRelayCommand)viewModel.AddCommentCommand).ExecuteAsync(null);
            await TestWaiter.UntilAsync(
                () => viewModel.SelectedIssueDetail?.Comments.Count == 2,
                "The comment command did not refresh the selected issue detail.");

            await ((IAsyncRelayCommand)viewModel.AttachFileCommand).ExecuteAsync(null);
            await TestWaiter.UntilAsync(
                () => viewModel.SelectedIssueDetail?.Attachments.Count == 2,
                "The attach-file command did not refresh the selected issue detail.");

            var attachment = viewModel.SelectedIssueDetail!.Attachments[0];
            await ((IAsyncRelayCommand<IssueAttachmentViewModel?>)viewModel.OpenAttachmentCommand).ExecuteAsync(attachment);

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
    public async Task Toggle_selected_issue_state_closes_and_reopens_the_same_issue()
    {
        var viewModel = CreateViewModel(out _, out _, out _);

        await viewModel.InitializeAsync();
        await TestWaiter.UntilAsync(
            () => viewModel.SelectedIssue is not null && viewModel.SelectedIssue.IsOpen,
            "The default selected issue was not loaded as open.");

        var selectedIssueId = viewModel.SelectedIssue!.Id;

        await ((IAsyncRelayCommand)viewModel.ToggleSelectedIssueStateCommand).ExecuteAsync(null);
        await TestWaiter.UntilAsync(
            () => viewModel.SelectedIssue?.Id == selectedIssueId && viewModel.SelectedIssue.IsClosed,
            "The selected issue was not closed by the state command.");

        await ((IAsyncRelayCommand)viewModel.ToggleSelectedIssueStateCommand).ExecuteAsync(null);
        await TestWaiter.UntilAsync(
            () => viewModel.SelectedIssue?.Id == selectedIssueId && viewModel.SelectedIssue.IsOpen,
            "The selected issue was not reopened by the state command.");
    }

    private static MainWindowViewModel CreateViewModel(
        out TestTrackyWorkspaceService service,
        out TestAttachmentPicker picker,
        out TestAttachmentLauncher launcher)
    {
        service = TestTrackyWorkspaceService.CreateDefault();
        picker = new TestAttachmentPicker();
        launcher = new TestAttachmentLauncher();
        return new MainWindowViewModel(service, picker, launcher);
    }
}
