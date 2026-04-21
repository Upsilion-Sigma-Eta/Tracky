using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Tracky.App.Tests.TestDoubles;
using Tracky.App.ViewModels;
using Tracky.App.Views;

namespace Tracky.App.Tests;

public sealed class MainWindowHeadlessTests
{
    [AvaloniaFact]
    public async Task MainWindow_loads_key_controls_and_binds_to_the_view_model()
    {
        var service = TestTrackyWorkspaceService.CreateDefault();
        var viewModel = new MainWindowViewModel(
            service,
            new TestAttachmentPicker(),
            new TestAttachmentLauncher());
        var window = new MainWindow
        {
            DataContext = viewModel,
        };

        try
        {
            window.Show();
            await viewModel.InitializeAsync();
            await TestWaiter.UntilAsync(
                () => viewModel.VisibleIssues.Count > 0,
                "The headless window did not load the issue list.");

            var searchBox = window.FindControl<TextBox>("SearchBox");
            var issueListBox = window.FindControl<ListBox>("IssueListBox");
            var backToIssueListButton = window.FindControl<Button>("BackToIssueListButton");
            var titleBox = window.FindControl<TextBox>("QuickCaptureTitleBox");
            var createButton = window.FindControl<Button>("CreateIssueButton");
            var detailStatusText = window.FindControl<TextBlock>("DetailStatusMessageText");
            var detailContent = window.FindControl<ContentControl>("SelectedIssueDetailContent");

            Assert.NotNull(searchBox);
            Assert.NotNull(issueListBox);
            Assert.NotNull(backToIssueListButton);
            Assert.NotNull(titleBox);
            Assert.NotNull(createButton);
            Assert.NotNull(detailStatusText);
            Assert.NotNull(detailContent);
            Assert.Same(viewModel.VisibleIssues, issueListBox.ItemsSource);
            Assert.Null(issueListBox.SelectedItem);
            Assert.True(viewModel.IsIssueListViewVisible);
            Assert.False(viewModel.IsIssueDetailViewActive);

            titleBox.Text = "Create issue through a bound TextBox";
            await TestWaiter.UntilAsync(
                () => viewModel.DraftTitle == "Create issue through a bound TextBox",
                "The quick capture TextBox did not update DraftTitle.");

            issueListBox.SelectedItem = viewModel.VisibleIssues[0];
            await TestWaiter.UntilAsync(
                () => viewModel.SelectedIssueDetail?.Summary.Id == viewModel.SelectedIssue?.Id,
                "The headless window did not load the selected issue detail after row navigation.");

            Assert.Same(viewModel.SelectedIssue, issueListBox.SelectedItem);
            Assert.Same(viewModel.SelectedIssueDetail, detailContent.Content);

            window.UpdateLayout();

            var editTitleBox = window.FindControl<TextBox>("IssueEditTitleBox");
            var closeReasonComboBox = FindVisual<ComboBox>(window, "ComposerCloseReasonComboBox");
            var updateIssueButton = window.FindControl<Button>("UpdateIssueButton");
            var deleteIssueButton = window.FindControl<Button>("DeleteIssueButton");
            var descriptionEditButton = FindVisual<Button>(window, "DescriptionEditButton");
            var descriptionEditSourceBox = FindVisual<TextBox>(window, "DescriptionEditSourceBox");
            var commentWriteTabButton = FindVisual<Button>(window, "CommentWriteTabButton");
            var commentPreviewTabButton = FindVisual<Button>(window, "CommentPreviewTabButton");
            var commentPreviewContent = FindVisual<ContentControl>(window, "CommentPreviewContent");
            var metadataAssigneesSection = window.FindControl<StackPanel>("MetadataAssigneesSection");
            var metadataLabelsSection = window.FindControl<StackPanel>("MetadataLabelsSection");
            var metadataProjectsSection = window.FindControl<StackPanel>("MetadataProjectsSection");

            Assert.NotNull(editTitleBox);
            Assert.NotNull(closeReasonComboBox);
            Assert.NotNull(updateIssueButton);
            Assert.NotNull(deleteIssueButton);
            Assert.NotNull(descriptionEditButton);
            Assert.NotNull(descriptionEditSourceBox);
            Assert.NotNull(commentWriteTabButton);
            Assert.NotNull(commentPreviewTabButton);
            Assert.NotNull(commentPreviewContent);
            Assert.NotNull(metadataAssigneesSection);
            Assert.NotNull(metadataLabelsSection);
            Assert.NotNull(metadataProjectsSection);
            Assert.Equal(viewModel.EditTitle, editTitleBox.Text);
            Assert.Same(viewModel.AvailableCloseReasons, closeReasonComboBox.ItemsSource);
            Assert.Equal(viewModel.SelectedCloseReason, closeReasonComboBox.SelectedItem);
            Assert.Contains("Loaded", detailStatusText.Text);
            Assert.True(viewModel.IsIssueDetailViewActive);
            Assert.True(createButton.Command?.CanExecute(createButton.CommandParameter));
            Assert.True(updateIssueButton.Command?.CanExecute(updateIssueButton.CommandParameter));
            Assert.True(deleteIssueButton.Command?.CanExecute(deleteIssueButton.CommandParameter));

            searchBox.Text = "closed";
            await TestWaiter.UntilAsync(
                () => viewModel.SearchText == "closed" && viewModel.VisibleIssues.Count == 1,
                "The search TextBox did not update the view-model filter.");
        }
        finally
        {
            window.Close();
        }
    }

    private static T? FindVisual<T>(Control root, string name)
        where T : Control
    {
        return root.GetVisualDescendants()
            .OfType<T>()
            .FirstOrDefault(control => control.Name == name);
    }
}
