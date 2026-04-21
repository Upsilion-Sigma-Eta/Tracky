using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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
                () => viewModel.SelectedIssueDetail is not null,
                "The headless window did not load the selected issue detail.");

            var searchBox = window.FindControl<TextBox>("SearchBox");
            var issueListBox = window.FindControl<ListBox>("IssueListBox");
            var titleBox = window.FindControl<TextBox>("QuickCaptureTitleBox");
            var createButton = window.FindControl<Button>("CreateIssueButton");
            var editTitleBox = window.FindControl<TextBox>("IssueEditTitleBox");
            var closeReasonComboBox = window.FindControl<ComboBox>("CloseReasonComboBox");
            var updateIssueButton = window.FindControl<Button>("UpdateIssueButton");
            var deleteIssueButton = window.FindControl<Button>("DeleteIssueButton");
            var detailStatusText = window.FindControl<TextBlock>("DetailStatusMessageText");
            var detailContent = window.FindControl<ContentControl>("SelectedIssueDetailContent");

            Assert.NotNull(searchBox);
            Assert.NotNull(issueListBox);
            Assert.NotNull(titleBox);
            Assert.NotNull(createButton);
            Assert.NotNull(editTitleBox);
            Assert.NotNull(closeReasonComboBox);
            Assert.NotNull(updateIssueButton);
            Assert.NotNull(deleteIssueButton);
            Assert.NotNull(detailStatusText);
            Assert.NotNull(detailContent);
            Assert.Same(viewModel.VisibleIssues, issueListBox.ItemsSource);
            Assert.Same(viewModel.SelectedIssue, issueListBox.SelectedItem);
            Assert.Same(viewModel.SelectedIssueDetail, detailContent.Content);
            Assert.Equal(viewModel.EditTitle, editTitleBox.Text);
            Assert.Same(viewModel.AvailableCloseReasons, closeReasonComboBox.ItemsSource);
            Assert.Equal(viewModel.SelectedCloseReason, closeReasonComboBox.SelectedItem);
            Assert.Contains("Loaded", detailStatusText.Text);

            searchBox.Text = "closed";
            await TestWaiter.UntilAsync(
                () => viewModel.SearchText == "closed" && viewModel.VisibleIssues.Count == 1,
                "The search TextBox did not update the view-model filter.");

            titleBox.Text = "Create issue through a bound TextBox";
            await TestWaiter.UntilAsync(
                () => viewModel.DraftTitle == "Create issue through a bound TextBox",
                "The quick capture TextBox did not update DraftTitle.");

            Assert.True(createButton.Command?.CanExecute(createButton.CommandParameter));
            Assert.True(updateIssueButton.Command?.CanExecute(updateIssueButton.CommandParameter));
            Assert.True(deleteIssueButton.Command?.CanExecute(deleteIssueButton.CommandParameter));
        }
        finally
        {
            window.Close();
        }
    }
}
