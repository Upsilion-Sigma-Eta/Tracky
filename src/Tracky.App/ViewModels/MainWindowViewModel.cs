using System.Collections.ObjectModel;
using Tracky.App.Services;
using Tracky.Core.Issues;
using Tracky.Core.Services;

namespace Tracky.App.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly List<IssueCardViewModel> _allIssues = [];
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly SemaphoreSlim _detailLoadGate = new(1, 1);
    private readonly IAttachmentLauncher _attachmentLauncher;
    private readonly IAttachmentPicker _attachmentPicker;
    private readonly ITrackyWorkspaceService _workspaceService;
    private CancellationTokenSource? _detailLoadCts;
    private bool _isDisposed;

    public MainWindowViewModel(
        ITrackyWorkspaceService workspaceService,
        IAttachmentPicker attachmentPicker,
        IAttachmentLauncher attachmentLauncher)
    {
        _workspaceService = workspaceService;
        _attachmentPicker = attachmentPicker;
        _attachmentLauncher = attachmentLauncher;

        ShowAllCommand = new RelayCommand(ShowAll);
        ShowOpenCommand = new RelayCommand(ShowOpen);
        ShowClosedCommand = new RelayCommand(ShowClosed);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        CreateIssueCommand = new AsyncRelayCommand(CreateIssueAsync, CanCreateIssue);
        ToggleSelectedIssueStateCommand = new AsyncRelayCommand(ToggleSelectedIssueStateAsync, CanToggleSelectedIssueState);
        UpdateSelectedIssueCommand = new AsyncRelayCommand(UpdateSelectedIssueAsync, CanUpdateSelectedIssue);
        DeleteSelectedIssueCommand = new AsyncRelayCommand(DeleteSelectedIssueAsync, CanDeleteSelectedIssue);
        AddCommentCommand = new AsyncRelayCommand(AddCommentAsync, CanAddComment);
        AttachFileCommand = new AsyncRelayCommand(AttachFileAsync, CanAttachFile);
        OpenAttachmentCommand = new AsyncRelayCommand<IssueAttachmentViewModel?>(OpenAttachmentAsync, CanOpenAttachment);

        DraftPriority = IssuePriority.High;
        DraftProjectName = "Tracky Foundation";
        DraftLabels = "foundation, desktop";
    }

    public RelayCommand ShowAllCommand { get; }

    public RelayCommand ShowOpenCommand { get; }

    public RelayCommand ShowClosedCommand { get; }

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand CreateIssueCommand { get; }

    public AsyncRelayCommand ToggleSelectedIssueStateCommand { get; }

    public AsyncRelayCommand UpdateSelectedIssueCommand { get; }

    public AsyncRelayCommand DeleteSelectedIssueCommand { get; }

    public AsyncRelayCommand AddCommentCommand { get; }

    public AsyncRelayCommand AttachFileCommand { get; }

    public AsyncRelayCommand<IssueAttachmentViewModel?> OpenAttachmentCommand { get; }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        // ViewModel 수명이 끝날 때 진행 중인 상세 조회를 중단하고, 내부 동기화 리소스를 함께 해제한다.
        _detailLoadCts?.Cancel();
        _detailLoadCts?.Dispose();
        _loadGate.Dispose();
        _detailLoadGate.Dispose();
    }

    public ObservableCollection<IssueCardViewModel> VisibleIssues { get; } = [];

    public IReadOnlyList<IssuePriority> AvailablePriorities { get; } = Enum.GetValues<IssuePriority>();

    public IReadOnlyList<IssueStateReason> AvailableCloseReasons { get; } =
    [
        IssueStateReason.Completed,
        IssueStateReason.NotPlanned,
        IssueStateReason.Duplicate,
    ];

    public bool IsAllScopeActive => ActiveScope == IssueFilterScope.All;

    public bool IsOpenScopeActive => ActiveScope == IssueFilterScope.Open;

    public bool IsClosedScopeActive => ActiveScope == IssueFilterScope.Closed;

    public bool HasSelectedIssue => SelectedIssue is not null;

    public bool HasNoSelectedIssue => !HasSelectedIssue;

    public bool HasSelectedIssueDetail => SelectedIssueDetail is not null;

    public bool CanChooseCloseReason => SelectedIssue?.IsOpen == true;

    private string _workspaceName = "Tracky";

    private string _workspaceDescription = "Preparing the local-first Phase 1 workspace.";

    private string _databasePath = string.Empty;

    private string _searchText = string.Empty;

    private IssueFilterScope _activeScope = IssueFilterScope.All;

    private IssueCardViewModel? _selectedIssue;

    private IssueDetailViewModel? _selectedIssueDetail;

    private int _totalIssues;

    private int _openIssues;

    private int _closedIssues;

    private int _overdueIssues;

    private int _dueTodayIssues;

    private int _upcomingIssues;

    private bool _isBusy;

    private bool _isDetailBusy;

    private string _statusMessage = "Tracky is preparing the default workspace.";

    private string _detailStatusMessage = "Select an issue to load its timeline, comments, and attachments.";

    private string _draftTitle = string.Empty;

    private string _draftAssignee = "Dabin";

    private IssuePriority _draftPriority;

    private DateTimeOffset? _draftDueDate = DateTimeOffset.Now.AddDays(2);

    private string _draftProjectName = string.Empty;

    private string _draftLabels = string.Empty;

    private string _draftCommentAuthor = "Dabin";

    private string _draftCommentBody = string.Empty;

    private string _editTitle = string.Empty;

    private string _editDescription = string.Empty;

    private string _editAssignee = string.Empty;

    private IssuePriority _editPriority;

    private DateTimeOffset? _editDueDate;

    private string _editProjectName = string.Empty;

    private string _editLabels = string.Empty;

    private IssueStateReason _selectedCloseReason = IssueStateReason.Completed;

    public string WorkspaceName
    {
        get => _workspaceName;
        private set => SetProperty(ref _workspaceName, value);
    }

    public string WorkspaceDescription
    {
        get => _workspaceDescription;
        private set => SetProperty(ref _workspaceDescription, value);
    }

    public string DatabasePath
    {
        get => _databasePath;
        private set => SetProperty(ref _databasePath, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilters();
            }
        }
    }

    public IssueFilterScope ActiveScope
    {
        get => _activeScope;
        private set
        {
            if (!SetProperty(ref _activeScope, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsAllScopeActive));
            OnPropertyChanged(nameof(IsOpenScopeActive));
            OnPropertyChanged(nameof(IsClosedScopeActive));
            ApplyFilters();
        }
    }

    public IssueCardViewModel? SelectedIssue
    {
        get => _selectedIssue;
        set
        {
            if (!SetProperty(ref _selectedIssue, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasSelectedIssue));
            OnPropertyChanged(nameof(HasNoSelectedIssue));
            OnPropertyChanged(nameof(CanChooseCloseReason));
            ToggleSelectedIssueStateCommand.NotifyCanExecuteChanged();
            UpdateSelectedIssueCommand.NotifyCanExecuteChanged();
            DeleteSelectedIssueCommand.NotifyCanExecuteChanged();
            AddCommentCommand.NotifyCanExecuteChanged();
            AttachFileCommand.NotifyCanExecuteChanged();
            StartDetailLoad(value?.Id);
        }
    }

    public IssueDetailViewModel? SelectedIssueDetail
    {
        get => _selectedIssueDetail;
        private set
        {
            if (!SetProperty(ref _selectedIssueDetail, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasSelectedIssueDetail));
            HydrateEditDraft(value);
            UpdateSelectedIssueCommand.NotifyCanExecuteChanged();
            OpenAttachmentCommand.NotifyCanExecuteChanged();
        }
    }

    public int TotalIssues
    {
        get => _totalIssues;
        private set => SetProperty(ref _totalIssues, value);
    }

    public int OpenIssues
    {
        get => _openIssues;
        private set => SetProperty(ref _openIssues, value);
    }

    public int ClosedIssues
    {
        get => _closedIssues;
        private set => SetProperty(ref _closedIssues, value);
    }

    public int OverdueIssues
    {
        get => _overdueIssues;
        private set => SetProperty(ref _overdueIssues, value);
    }

    public int DueTodayIssues
    {
        get => _dueTodayIssues;
        private set => SetProperty(ref _dueTodayIssues, value);
    }

    public int UpcomingIssues
    {
        get => _upcomingIssues;
        private set => SetProperty(ref _upcomingIssues, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public bool IsDetailBusy
    {
        get => _isDetailBusy;
        private set => SetProperty(ref _isDetailBusy, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string DetailStatusMessage
    {
        get => _detailStatusMessage;
        private set => SetProperty(ref _detailStatusMessage, value);
    }

    public string DraftTitle
    {
        get => _draftTitle;
        set
        {
            if (SetProperty(ref _draftTitle, value))
            {
                CreateIssueCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string DraftAssignee
    {
        get => _draftAssignee;
        set => SetProperty(ref _draftAssignee, value);
    }

    public IssuePriority DraftPriority
    {
        get => _draftPriority;
        set => SetProperty(ref _draftPriority, value);
    }

    public DateTimeOffset? DraftDueDate
    {
        get => _draftDueDate;
        set => SetProperty(ref _draftDueDate, value);
    }

    public string DraftProjectName
    {
        get => _draftProjectName;
        set => SetProperty(ref _draftProjectName, value);
    }

    public string DraftLabels
    {
        get => _draftLabels;
        set => SetProperty(ref _draftLabels, value);
    }

    public string DraftCommentAuthor
    {
        get => _draftCommentAuthor;
        set
        {
            if (SetProperty(ref _draftCommentAuthor, value))
            {
                AddCommentCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string DraftCommentBody
    {
        get => _draftCommentBody;
        set
        {
            if (SetProperty(ref _draftCommentBody, value))
            {
                AddCommentCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string EditTitle
    {
        get => _editTitle;
        set
        {
            if (SetProperty(ref _editTitle, value))
            {
                UpdateSelectedIssueCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string EditDescription
    {
        get => _editDescription;
        set => SetProperty(ref _editDescription, value);
    }

    public string EditAssignee
    {
        get => _editAssignee;
        set => SetProperty(ref _editAssignee, value);
    }

    public IssuePriority EditPriority
    {
        get => _editPriority;
        set => SetProperty(ref _editPriority, value);
    }

    public DateTimeOffset? EditDueDate
    {
        get => _editDueDate;
        set => SetProperty(ref _editDueDate, value);
    }

    public string EditProjectName
    {
        get => _editProjectName;
        set => SetProperty(ref _editProjectName, value);
    }

    public string EditLabels
    {
        get => _editLabels;
        set => SetProperty(ref _editLabels, value);
    }

    public IssueStateReason SelectedCloseReason
    {
        get => _selectedCloseReason;
        set => SetProperty(ref _selectedCloseReason, value);
    }

    public Task InitializeAsync()
    {
        return LoadAsync();
    }

    private void ShowAll()
    {
        ActiveScope = IssueFilterScope.All;
    }

    private void ShowOpen()
    {
        ActiveScope = IssueFilterScope.Open;
    }

    private void ShowClosed()
    {
        ActiveScope = IssueFilterScope.Closed;
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(SelectedIssue?.Id, cancellationToken: cancellationToken);
    }

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

    private async Task ToggleSelectedIssueStateAsync(CancellationToken cancellationToken)
    {
        if (SelectedIssue is null)
        {
            return;
        }

        var issueId = SelectedIssue.Id;
        var nextState = SelectedIssue.IsOpen
            ? IssueWorkflowState.Closed
            : IssueWorkflowState.Open;

        var nextReason = nextState == IssueWorkflowState.Closed
            ? NormalizeCloseReason(SelectedCloseReason)
            : IssueStateReason.None;

        var updatedIssue = await _workspaceService.UpdateIssueStateAsync(
            new UpdateIssueStateInput(
                issueId,
                nextState,
                nextReason),
            cancellationToken);

        await LoadAsync(
            updatedIssue?.Id ?? issueId,
            updatedIssue is null
                ? "The selected issue could not be updated."
                : BuildStateUpdateStatus(updatedIssue),
            cancellationToken);
    }

    private async Task UpdateSelectedIssueAsync(CancellationToken cancellationToken)
    {
        if (SelectedIssue is null)
        {
            return;
        }

        var issueId = SelectedIssue.Id;
        var updatedIssue = await _workspaceService.UpdateIssueAsync(
            new UpdateIssueInput(
                issueId,
                EditTitle,
                EditDescription,
                EditAssignee,
                EditPriority,
                EditDueDate is null ? null : DateOnly.FromDateTime(EditDueDate.Value.Date),
                EditProjectName,
                ParseLabels(EditLabels)),
            cancellationToken);

        await LoadAsync(
            updatedIssue?.Id ?? issueId,
            updatedIssue is null
                ? "The selected issue could not be saved because it was not found."
                : $"Issue #{updatedIssue.Number} was saved with updated Phase 1 metadata.",
            cancellationToken);
    }

    private async Task DeleteSelectedIssueAsync(CancellationToken cancellationToken)
    {
        if (SelectedIssue is null)
        {
            return;
        }

        var issueId = SelectedIssue.Id;
        var deletedIssueNumber = SelectedIssue.Number;
        var deleted = await _workspaceService.DeleteIssueAsync(issueId, cancellationToken);
        await LoadAsync(
            completionMessage: deleted
                ? $"Issue #{deletedIssueNumber} was deleted from the local workspace."
                : "The selected issue could not be deleted because it was not found.",
            cancellationToken: cancellationToken);
    }

    private async Task AddCommentAsync(CancellationToken cancellationToken)
    {
        if (SelectedIssue is null)
        {
            return;
        }

        var issueId = SelectedIssue.Id;
        var comment = await _workspaceService.AddIssueCommentAsync(
            new AddIssueCommentInput(
                issueId,
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
            issueId,
            "The selected issue was refreshed after saving the new comment.",
            cancellationToken);
    }

    private async Task AttachFileAsync(CancellationToken cancellationToken)
    {
        if (SelectedIssue is null)
        {
            return;
        }

        var issueId = SelectedIssue.Id;
        var path = await _attachmentPicker.PickAttachmentAsync();
        if (string.IsNullOrWhiteSpace(path))
        {
            DetailStatusMessage = "Attachment import was canceled.";
            return;
        }

        var content = await File.ReadAllBytesAsync(path, cancellationToken);
        var attachment = await _workspaceService.AddIssueAttachmentAsync(
            new AddIssueAttachmentInput(
                issueId,
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
            issueId,
            $"Attachment \"{attachment.FileName}\" was added to the selected issue.",
            cancellationToken);
    }

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

    private bool CanUpdateSelectedIssue()
    {
        return !IsBusy
            && !IsDetailBusy
            && SelectedIssue is not null
            && SelectedIssueDetail is not null
            && !string.IsNullOrWhiteSpace(EditTitle);
    }

    private bool CanDeleteSelectedIssue()
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
            UpdateSelectedIssueCommand.NotifyCanExecuteChanged();
            DeleteSelectedIssueCommand.NotifyCanExecuteChanged();
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
            UpdateSelectedIssueCommand.NotifyCanExecuteChanged();
            DeleteSelectedIssueCommand.NotifyCanExecuteChanged();
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

        SelectedIssueDetail = null;
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
            UpdateSelectedIssueCommand.NotifyCanExecuteChanged();
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
            UpdateSelectedIssueCommand.NotifyCanExecuteChanged();
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

    private void HydrateEditDraft(IssueDetailViewModel? detail)
    {
        // 선택한 상세 이슈가 바뀔 때 편집 초안을 즉시 재구성해야,
        // 저장 버튼이 이전 이슈의 메타데이터를 실수로 다시 쓰지 않는다.
        if (detail is null)
        {
            EditTitle = string.Empty;
            EditDescription = string.Empty;
            EditAssignee = string.Empty;
            EditPriority = IssuePriority.None;
            EditDueDate = null;
            EditProjectName = string.Empty;
            EditLabels = string.Empty;
            SelectedCloseReason = IssueStateReason.Completed;
            return;
        }

        var summary = detail.Summary.Issue;
        EditTitle = summary.Title;
        EditDescription = detail.Detail.Description;
        EditAssignee = summary.AssigneeDisplayName ?? string.Empty;
        EditPriority = summary.Priority;
        EditDueDate = summary.DueDate is null
            ? null
            : new DateTimeOffset(summary.DueDate.Value.ToDateTime(TimeOnly.MinValue));
        EditProjectName = summary.ProjectName ?? string.Empty;
        EditLabels = string.Join(", ", summary.Labels);
        SelectedCloseReason = summary.StateReason == IssueStateReason.None
            ? IssueStateReason.Completed
            : summary.StateReason;
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
        List<IssueCardViewModel> filteredIssues,
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

    private static string[] ParseLabels(string rawLabels)
    {
        return [.. rawLabels
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
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

    private static IssueStateReason NormalizeCloseReason(IssueStateReason reason)
    {
        return reason == IssueStateReason.None
            ? IssueStateReason.Completed
            : reason;
    }

    private static string BuildStateUpdateStatus(IssueListItem issue)
    {
        if (issue.State == IssueWorkflowState.Open)
        {
            return $"Issue #{issue.Number} was reopened.";
        }

        return $"Issue #{issue.Number} was closed as {FormatStateReason(issue.StateReason)}.";
    }

    private static string FormatStateReason(IssueStateReason reason) => reason switch
    {
        IssueStateReason.Completed => "completed",
        IssueStateReason.NotPlanned => "not planned",
        IssueStateReason.Duplicate => "duplicate",
        _ => "completed",
    };
}
