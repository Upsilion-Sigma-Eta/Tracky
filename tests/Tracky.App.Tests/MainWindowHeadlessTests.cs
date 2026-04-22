using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Tracky.App.Controls;
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

    [AvaloniaFact]
    public async Task MainWindow_uses_github_like_single_column_shell_without_left_panel()
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
            viewModel.ShowProjectsCommand.Execute(null);
            await TestWaiter.UntilAsync(
                () => viewModel.SelectedProject is not null,
                "The GitHub-like shell did not load a selected repository.");

            window.UpdateLayout();

            var rootGrid = Assert.IsType<Grid>(window.Content);
            Assert.Single(rootGrid.ColumnDefinitions);
            Assert.Null(FindTextBlockByText(window, "TRACKY"));
            Assert.NotNull(FindVisual<TextBlock>(window, "RepositoryOwnerNameText"));
            Assert.NotNull(FindVisual<Button>(window, "RepositoryIssuesTabButton"));
            Assert.NotNull(FindVisual<Button>(window, "RepositoryProjectsTabButton"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void IssueHtmlPreview_uses_text_fallback_inside_scroll_viewers()
    {
        var originalDisableWebViewValue = Environment.GetEnvironmentVariable("TRACKY_DISABLE_WEBVIEW");
        Environment.SetEnvironmentVariable("TRACKY_DISABLE_WEBVIEW", null);

        var preview = new IssueHtmlPreview
        {
            HtmlContent = "<p>Preview body</p>",
            FallbackText = "Preview body",
            PreviewHeight = 180,
        };

        var window = new Window
        {
            Width = 400,
            Height = 300,
            Content = new ScrollViewer
            {
                Content = preview,
            },
        };

        try
        {
            window.Show();
            window.UpdateLayout();

            Assert.IsType<TextBlock>(preview.Content);
        }
        finally
        {
            window.Close();
            Environment.SetEnvironmentVariable("TRACKY_DISABLE_WEBVIEW", originalDisableWebViewValue);
        }
    }

    [AvaloniaFact]
    public async Task Repository_dashboard_exposes_github_like_repository_tabs()
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
            viewModel.ShowProjectsCommand.Execute(null);
            await TestWaiter.UntilAsync(
                () => viewModel.SelectedProject is not null,
                "The repository dashboard did not select an issue repository.");

            window.UpdateLayout();

            var repositoryListBox = window.FindControl<ListBox>("RepositoryListBox");
            var repositoryIssuesTabButton = FindVisual<Button>(window, "RepositoryIssuesTabButton");
            var repositoryMilestonesTabButton = FindVisual<Button>(window, "RepositoryMilestonesTabButton");
            var repositoryDiscussionsTabButton = FindVisual<Button>(window, "RepositoryDiscussionsTabButton");
            var repositoryProjectsTabButton = FindVisual<Button>(window, "RepositoryProjectsTabButton");
            var repositorySecurityTabButton = FindVisual<Button>(window, "RepositorySecurityTabButton");
            var repositoryIssuesListBox = FindVisual<ListBox>(window, "RepositoryIssuesListBox");
            var repositoryOwnerNameText = FindVisual<TextBlock>(window, "RepositoryOwnerNameText");
            var repositoryNameText = FindVisual<TextBlock>(window, "RepositoryNameText");
            var repositoryVisibilityBadge = FindVisual<Border>(window, "RepositoryVisibilityBadge");
            var repositoryWatchButton = FindVisual<Button>(window, "RepositoryWatchButton");
            var repositoryForkButton = FindVisual<Button>(window, "RepositoryForkButton");
            var repositoryStarButton = FindVisual<Button>(window, "RepositoryStarButton");
            var repositoryIssueSearchBox = FindVisual<TextBox>(window, "RepositoryIssueSearchBox");
            var repositoryLabelsButton = FindVisual<Button>(window, "RepositoryLabelsButton");
            var repositoryMilestonesButton = FindVisual<Button>(window, "RepositoryMilestonesButton");
            var repositoryNewIssueButton = FindVisual<Button>(window, "RepositoryNewIssueButton");
            var repositoryOpenIssuesText = FindVisual<TextBlock>(window, "RepositoryOpenIssuesText");
            var repositoryClosedIssuesText = FindVisual<TextBlock>(window, "RepositoryClosedIssuesText");

            Assert.NotNull(repositoryListBox);
            Assert.NotNull(repositoryIssuesTabButton);
            Assert.Null(repositoryMilestonesTabButton);
            Assert.NotNull(repositoryDiscussionsTabButton);
            Assert.NotNull(repositoryProjectsTabButton);
            Assert.NotNull(repositorySecurityTabButton);
            Assert.NotNull(repositoryIssuesListBox);
            Assert.NotNull(repositoryOwnerNameText);
            Assert.NotNull(repositoryNameText);
            Assert.NotNull(repositoryVisibilityBadge);
            Assert.Null(repositoryWatchButton);
            Assert.Null(repositoryForkButton);
            Assert.Null(repositoryStarButton);
            Assert.NotNull(repositoryIssueSearchBox);
            Assert.NotNull(repositoryLabelsButton);
            Assert.NotNull(repositoryMilestonesButton);
            Assert.NotNull(repositoryNewIssueButton);
            Assert.NotNull(repositoryOpenIssuesText);
            Assert.NotNull(repositoryClosedIssuesText);
            Assert.Same(viewModel.Projects, repositoryListBox.ItemsSource);
            Assert.Same(viewModel.RepositoryIssues, repositoryIssuesListBox.ItemsSource);

            repositoryMilestonesButton.Command?.Execute(repositoryMilestonesButton.CommandParameter);
            window.UpdateLayout();
            var repositoryMilestonesPanel = FindVisual<ItemsControl>(window, "RepositoryMilestonesPanel");
            var repositoryMilestonesOpenText = FindVisual<TextBlock>(window, "RepositoryMilestonesOpenText");
            var repositoryMilestonesClosedText = FindVisual<TextBlock>(window, "RepositoryMilestonesClosedText");
            var repositoryMilestonesSortButton = FindVisual<Button>(window, "RepositoryMilestonesSortButton");
            Assert.NotNull(repositoryMilestonesPanel);
            Assert.NotNull(repositoryMilestonesOpenText);
            Assert.NotNull(repositoryMilestonesClosedText);
            Assert.NotNull(repositoryMilestonesSortButton);

            repositoryProjectsTabButton.Command?.Execute(repositoryProjectsTabButton.CommandParameter);
            window.UpdateLayout();
            var repositoryProjectsContent = FindVisual<ContentControl>(window, "RepositoryProjectsContent");
            var repositoryProjectsSearchBox = FindVisual<TextBox>(window, "RepositoryProjectsSearchBox");
            var repositoryProjectsResultsText = FindVisual<TextBlock>(window, "RepositoryProjectsResultsText");
            Assert.NotNull(repositoryProjectsContent);
            Assert.NotNull(repositoryProjectsSearchBox);
            Assert.NotNull(repositoryProjectsResultsText);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Repository_screen_goes_directly_from_github_header_to_repository_content()
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
            viewModel.ShowProjectsCommand.Execute(null);
            await TestWaiter.UntilAsync(
                () => viewModel.SelectedProject is not null,
                "The repository screen did not select an issue repository.");

            window.UpdateLayout();

            Assert.NotNull(window.FindControl<ListBox>("RepositoryListBox"));
            Assert.NotNull(FindVisual<TextBox>(window, "RepositoryIssueSearchBox"));
            Assert.Null(FindTextBlockByText(window, "Repository Dashboard"));
            Assert.Null(FindTextBlockByText(window, "Selected Repository"));
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

    private static TextBlock? FindTextBlockByText(Control root, string text)
    {
        return root.GetVisualDescendants()
            .OfType<TextBlock>()
            .FirstOrDefault(control => string.Equals(control.Text, text, StringComparison.Ordinal));
    }
}
