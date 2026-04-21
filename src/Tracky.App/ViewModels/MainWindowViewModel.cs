using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using Tracky.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tracky.Core.Issues;
using Tracky.Core.Services;

namespace Tracky.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly List<IssueCardViewModel> _allIssues = [];
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly SemaphoreSlim _detailLoadGate = new(1, 1);
    private readonly IAttachmentLauncher _attachmentLauncher;
    private readonly IAttachmentPicker _attachmentPicker;
    private readonly ITrackyWorkspaceService _workspaceService;
    private CancellationTokenSource? _detailLoadCts;

    public MainWindowViewModel(
        ITrackyWorkspaceService workspaceService,
        IAttachmentPicker attachmentPicker,
        IAttachmentLauncher attachmentLauncher)
    {
        _workspaceService = workspaceService;
        _attachmentPicker = attachmentPicker;
        _attachmentLauncher = attachmentLauncher;
        DraftPriority = IssuePriority.High;
        DraftProjectName = "Tracky Foundation";
        DraftLabels = "foundation, desktop";
    }

    public ObservableCollection<IssueCardViewModel> VisibleIssues { get; } = [];

    public IReadOnlyList<IssuePriority> AvailablePriorities { get; } = Enum.GetValues<IssuePriority>();

    public bool IsAllScopeActive => ActiveScope == IssueFilterScope.All;

    public bool IsOpenScopeActive => ActiveScope == IssueFilterScope.Open;

    public bool IsClosedScopeActive => ActiveScope == IssueFilterScope.Closed;

    public bool HasSelectedIssue => SelectedIssue is not null;

    public bool HasNoSelectedIssue => !HasSelectedIssue;

    public bool HasSelectedIssueDetail => SelectedIssueDetail is not null;

    [ObservableProperty]
    private string workspaceName = "Tracky";

    [ObservableProperty]
    private string workspaceDescription = "Preparing the local-first Phase 1 workspace.";

    [ObservableProperty]
    private string databasePath = string.Empty;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private IssueFilterScope activeScope = IssueFilterScope.All;

    [ObservableProperty]
    private IssueCardViewModel? selectedIssue;

    [ObservableProperty]
    private IssueDetailViewModel? selectedIssueDetail;

    [ObservableProperty]
    private int totalIssues;

    [ObservableProperty]
    private int openIssues;

    [ObservableProperty]
    private int closedIssues;

    [ObservableProperty]
    private int overdueIssues;

    [ObservableProperty]
    private int dueTodayIssues;

    [ObservableProperty]
    private int upcomingIssues;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isDetailBusy;

    [ObservableProperty]
    private string statusMessage = "Tracky is preparing the default workspace.";

    [ObservableProperty]
    private string detailStatusMessage = "Select an issue to load its timeline, comments, and attachments.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateIssueCommand))]
    private string draftTitle = string.Empty;

    [ObservableProperty]
    private string draftAssignee = "Dabin";

    [ObservableProperty]
    private IssuePriority draftPriority;

    [ObservableProperty]
    private DateTimeOffset? draftDueDate = DateTimeOffset.Now.AddDays(2);

    [ObservableProperty]
    private string draftProjectName = string.Empty;

    [ObservableProperty]
    private string draftLabels = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommentCommand))]
    private string draftCommentAuthor = "Dabin";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommentCommand))]
    private string draftCommentBody = string.Empty;

    public Task InitializeAsync()
    {
        return LoadAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilters();
    }

    partial void OnActiveScopeChanged(IssueFilterScope value)
    {
        OnPropertyChanged(nameof(IsAllScopeActive));
        OnPropertyChanged(nameof(IsOpenScopeActive));
        OnPropertyChanged(nameof(IsClosedScopeActive));
        ApplyFilters();
    }

    partial void OnSelectedIssueChanged(IssueCardViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedIssue));
        OnPropertyChanged(nameof(HasNoSelectedIssue));
        ToggleSelectedIssueStateCommand.NotifyCanExecuteChanged();
        AddCommentCommand.NotifyCanExecuteChanged();
        AttachFileCommand.NotifyCanExecuteChanged();
        StartDetailLoad(value?.Id);
    }

    partial void OnSelectedIssueDetailChanged(IssueDetailViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedIssueDetail));
        OpenAttachmentCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ShowAll()
    {
        ActiveScope = IssueFilterScope.All;
    }

    [RelayCommand]
    private void ShowOpen()
    {
        ActiveScope = IssueFilterScope.Open;
    }

    [RelayCommand]
    private void ShowClosed()
    {
        ActiveScope = IssueFilterScope.Closed;
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(SelectedIssue?.Id, cancellationToken: cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanCreateIssue))]
    private async Task CreateIssueAsync(CancellationToken cancellationToken)
    {
        var createdIssue = await _workspaceService.CreateIssueAsync(
            new CreateIssueInput(
                DraftTitle,
                DraftAssignee,
                DraftPriority,
                DraftDueDate is null ? null : DateOnly.FromDateTime(DraftDueDate.Value.Date),
                DraftProjectName,
                ParseLabels(DraftLabels)),
            cancellationToken);

        DraftTitle = string.Empty;
        DraftLabels = "foundation";
        DraftPriority = IssuePriority.High;
        DraftDueDate = DateTimeOffset.Now.AddDays(2);

        await LoadAsync(
            createdIssue.Id,
            $"Issue #{createdIssue.Number} was added to the local workspace.",
            cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanToggleSelectedIssueState))]
    private async Task ToggleSelectedIssueStateAsync(CancellationToken cancellationToken)
    {
        if (SelectedIssue is null)
        {
            return;
        }

        var nextState = SelectedIssue.IsOpen
            ? IssueWorkflowState.Closed
            : IssueWorkflowState.Open;

        var nextReason = nextState == IssueWorkflowState.Closed
            ? IssueStateReason.Completed
            : IssueStateReason.None;

        var updatedIssue = await _workspaceService.UpdateIssueStateAsync(
            new UpdateIssueStateInput(
                SelectedIssue.Id,
                nextState,
                nextReason),
            cancellationToken);

        await LoadAsync(
            updatedIssue?.Id ?? SelectedIssue.Id,
            updatedIssue is null
                ? "The selected issue could not be updated."
                : $"Issue #{updatedIssue.Number} is now {updatedIssue.State.ToString().ToLowerInvariant()}.",
            cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanAddComment))]
    private async Task AddCommentAsync(CancellationToken cancellationToken)
    {
        if (SelectedIssue is null)
        {
            return;
        }

        var comment = await _workspaceService.AddIssueCommentAsync(
            new AddIssueCommentInput(
                SelectedIssue.Id,
                DraftCommentAuthor,
                DraftCommentBody),
            cancellationToken);

        if (comment is null)
        {
            DetailStatusMessage = "The comment could not be saved because the selected issue was not found.";
            return;
        }

        DraftCommentBody = string.Empty;
        await LoadAsync(
            SelectedIssue.Id,
            "The selected issue was refreshed after saving the new comment.",
            cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanAttachFile))]
    private async Task AttachFileAsync(CancellationToken cancellationToken)
    {
        if (SelectedIssue is null)
        {
            return;
        }

        var path = await _attachmentPicker.PickAttachmentAsync();
        if (string.IsNullOrWhiteSpace(path))
        {
            DetailStatusMessage = "Attachment import was canceled.";
            return;
        }

        var content = await File.ReadAllBytesAsync(path, cancellationToken);
        var attachment = await _workspaceService.AddIssueAttachmentAsync(
            new AddIssueAttachmentInput(
                SelectedIssue.Id,
                Path.GetFileName(path),
                GuessContentType(path),
                content),
            cancellationToken);

        if (attachment is null)
        {
            DetailStatusMessage = "The attachment could not be saved because the selected issue was not found.";
            return;
        }

        await LoadAsync(
            SelectedIssue.Id,
            $"Attachment \"{attachment.FileName}\" was added to the selected issue.",
            cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanOpenAttachment))]
    private async Task OpenAttachmentAsync(IssueAttachmentViewModel? attachment, CancellationToken cancellationToken)
    {
        if (attachment is null)
        {
            return;
        }

        var exportedPath = await _workspaceService.ExportAttachmentToTempFileAsync(attachment.Id, cancellationToken);
        if (string.IsNullOrWhiteSpace(exportedPath))
        {
            DetailStatusMessage = $"Attachment \"{attachment.FileName}\" could not be exported from the local workspace.";
            return;
        }

        await _attachmentLauncher.OpenAsync(exportedPath);
        DetailStatusMessage = $"Attachment \"{attachment.FileName}\" was opened via the system shell.";
    }

    private bool CanCreateIssue()
    {
        return !IsBusy && !string.IsNullOrWhiteSpace(DraftTitle);
    }

    private bool CanToggleSelectedIssueState()
    {
        return !IsBusy && SelectedIssue is not null;
    }

    private bool CanAddComment()
    {
        return !IsBusy
            && !IsDetailBusy
            && SelectedIssue is not null
            && !string.IsNullOrWhiteSpace(DraftCommentAuthor)
            && !string.IsNullOrWhiteSpace(DraftCommentBody);
    }

    private bool CanAttachFile()
    {
        return !IsBusy && !IsDetailBusy && SelectedIssue is not null;
    }

    private bool CanOpenAttachment(IssueAttachmentViewModel? attachment)
    {
        return !IsBusy && !IsDetailBusy && attachment is not null;
    }

    private async Task LoadAsync(
        Guid? preferredIssueId = null,
        string? completionMessage = null,
        CancellationToken cancellationToken = default)
    {
        await _loadGate.WaitAsync(cancellationToken);

        try
        {
            IsBusy = true;
            CreateIssueCommand.NotifyCanExecuteChanged();
            ToggleSelectedIssueStateCommand.NotifyCanExecuteChanged();
            AddCommentCommand.NotifyCanExecuteChanged();
            AttachFileCommand.NotifyCanExecuteChanged();
            OpenAttachmentCommand.NotifyCanExecuteChanged();
            StatusMessage = "Loading Tracky workspace from the local SQLite file.";

            var overview = await _workspaceService.GetOverviewAsync(cancellationToken);
            WorkspaceName = overview.WorkspaceName;
            WorkspaceDescription = overview.Description;
            DatabasePath = overview.DatabasePath;
            TotalIssues = overview.Metrics.Total;
            OpenIssues = overview.Metrics.Open;
            ClosedIssues = overview.Metrics.Closed;
            OverdueIssues = overview.Metrics.Overdue;
            DueTodayIssues = overview.Metrics.DueToday;
            UpcomingIssues = overview.Metrics.Upcoming;

            _allIssues.Clear();
            _allIssues.AddRange(overview.Issues.Select(static issue => new IssueCardViewModel(issue)));

            ApplyFilters(preferredIssueId);
            StatusMessage = completionMessage
                ?? $"{overview.Issues.Count} issues synced from the local workspace.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Workspace refresh was canceled.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Workspace load failed: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
            CreateIssueCommand.NotifyCanExecuteChanged();
            ToggleSelectedIssueStateCommand.NotifyCanExecuteChanged();
            AddCommentCommand.NotifyCanExecuteChanged();
            AttachFileCommand.NotifyCanExecuteChanged();
            OpenAttachmentCommand.NotifyCanExecuteChanged();
            _loadGate.Release();
        }
    }

    private void StartDetailLoad(Guid? issueId)
    {
        _detailLoadCts?.Cancel();
        _detailLoadCts?.Dispose();
        _detailLoadCts = null;

        if (issueId is null)
        {
            SelectedIssueDetail = null;
            DetailStatusMessage = "Select an issue to load its timeline, comments, and attachments.";
            return;
        }

        var cancellationSource = new CancellationTokenSource();
        _detailLoadCts = cancellationSource;
        _ = LoadIssueDetailAsync(issueId.Value, cancellationSource.Token);
    }

    private async Task LoadIssueDetailAsync(Guid issueId, CancellationToken cancellationToken)
    {
        await _detailLoadGate.WaitAsync(CancellationToken.None);

        try
        {
            IsDetailBusy = true;
            AddCommentCommand.NotifyCanExecuteChanged();
            AttachFileCommand.NotifyCanExecuteChanged();
            OpenAttachmentCommand.NotifyCanExecuteChanged();
            DetailStatusMessage = "Loading the selected issue detail from the local workspace.";

            var detail = await _workspaceService.GetIssueDetailAsync(issueId, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            SelectedIssueDetail = detail is null
                ? null
                : new IssueDetailViewModel(detail);

            DetailStatusMessage = detail is null
                ? "The selected issue detail could not be found."
                : $"Loaded {detail.Comments.Count} comments, {detail.Attachments.Count} attachments, and {detail.Activity.Count} activity items.";
        }
        catch (OperationCanceledException)
        {
            // Selection changed while a previous detail query was still running.
        }
        catch (Exception exception)
        {
            DetailStatusMessage = $"Issue detail load failed: {exception.Message}";
        }
        finally
        {
            IsDetailBusy = false;
            AddCommentCommand.NotifyCanExecuteChanged();
            AttachFileCommand.NotifyCanExecuteChanged();
            OpenAttachmentCommand.NotifyCanExecuteChanged();
            _detailLoadGate.Release();
        }
    }

    private void ApplyFilters(Guid? preferredIssueId = null)
    {
        var normalizedSearch = SearchText.Trim();
        var filteredIssues = _allIssues
            .Where(issue => MatchesScope(issue, ActiveScope))
            .Where(issue => MatchesSearch(issue, normalizedSearch))
            .ToList();

        VisibleIssues.Clear();
        foreach (var issue in filteredIssues)
        {
            VisibleIssues.Add(issue);
        }

        SelectedIssue = SelectPreferredIssue(filteredIssues, preferredIssueId);
    }

    private static bool MatchesScope(IssueCardViewModel issue, IssueFilterScope scope) => scope switch
    {
        IssueFilterScope.Open => issue.IsOpen,
        IssueFilterScope.Closed => issue.IsClosed,
        _ => true,
    };

    private static bool MatchesSearch(IssueCardViewModel issue, string normalizedSearch)
    {
        if (string.IsNullOrWhiteSpace(normalizedSearch))
        {
            return true;
        }

        return issue.Title.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)
            || issue.NumberText.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)
            || issue.AssigneeText.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)
            || issue.ProjectText.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)
            || issue.Labels.Any(label => label.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase));
    }

    private IssueCardViewModel? SelectPreferredIssue(
        IReadOnlyList<IssueCardViewModel> filteredIssues,
        Guid? preferredIssueId)
    {
        if (filteredIssues.Count == 0)
        {
            return null;
        }

        if (preferredIssueId is not null)
        {
            return filteredIssues.FirstOrDefault(issue => issue.Id == preferredIssueId.Value)
                ?? filteredIssues[0];
        }

        if (SelectedIssue is not null)
        {
            return filteredIssues.FirstOrDefault(issue => issue.Id == SelectedIssue.Id)
                ?? filteredIssues[0];
        }

        return filteredIssues[0];
    }

    private static IReadOnlyList<string> ParseLabels(string rawLabels)
    {
        return rawLabels
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GuessContentType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".txt" => "text/plain",
            ".md" => "text/markdown",
            ".json" => "application/json",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".pdf" => "application/pdf",
            _ => "application/octet-stream",
        };
    }
}
