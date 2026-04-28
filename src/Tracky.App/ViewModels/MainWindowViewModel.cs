using System.Collections.ObjectModel;
using System.Globalization;
using Tracky.App.Services;
using Tracky.Core.Exports;
using Tracky.Core.Issues;
using Tracky.Core.Preferences;
using Tracky.Core.Projects;
using Tracky.Core.Reminders;
using Tracky.Core.Search;
using Tracky.Core.Services;

namespace Tracky.App.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private enum RepositoryDetailTab
    {
        Issues,
        Milestones,
        Projects,
    }

    private const string DefaultProjectSortField = "Board position";
    private const string NoProjectGroupingField = "None";
    private const string DefaultIssueSortOption = "Newest updated";
    private const string AnyIssueAssigneeFilter = "Assignee";
    private const string AnyIssueLabelFilter = "Labels";
    private const string AnyIssueProjectFilter = "Projects";
    private const string AnyIssueMilestoneFilter = "Milestones";
    private const string AnyIssueTypeFilter = "Types";
    private const string UnassignedIssueFilter = "Unassigned";
    private const string NoProjectIssueFilter = "No project";
    private const string NoMilestoneIssueFilter = "No milestone";
    private const string EmptyCommentPreviewText = "Nothing to preview yet.";

    private static readonly string[] BaseProjectSortFields =
    [
        DefaultProjectSortField,
        "Issue number",
        "Title",
        "Priority",
        "Assignee",
        "Due date",
        "Updated",
        "Status",
    ];

    private static readonly string[] BaseProjectGroupFields =
    [
        NoProjectGroupingField,
        "Status",
        "State",
        "Priority",
        "Assignee",
        "Due date",
    ];

    private static readonly string[] IssueSortOptions =
    [
        DefaultIssueSortOption,
        "Oldest updated",
        "Most commented",
        "Least commented",
        "Highest priority",
        "Due date",
    ];

    private readonly List<IssueCardViewModel> _allIssues = [];
    private readonly List<ProjectSummaryViewModel> _allProjects = [];
    private readonly List<MilestoneSummary> _allMilestones = [];
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly SemaphoreSlim _detailLoadGate = new(1, 1);
    private readonly SemaphoreSlim _projectLoadGate = new(1, 1);
    private readonly IAttachmentLauncher _attachmentLauncher;
    private readonly IAttachmentPicker _attachmentPicker;
    private readonly ITrackyWorkspaceService _workspaceService;
    private CancellationTokenSource? _detailLoadCts;
    private CancellationTokenSource? _projectLoadCts;
    private bool _isDisposed;

    public MainWindowViewModel(
        ITrackyWorkspaceService workspaceService,
        IAttachmentPicker attachmentPicker,
        IAttachmentLauncher attachmentLauncher)
    {
        _workspaceService = workspaceService;
        _attachmentPicker = attachmentPicker;
        _attachmentLauncher = attachmentLauncher;

        ShowIssuesCommand = new RelayCommand(ShowIssues);
        ShowProjectsCommand = new RelayCommand(ShowProjects);
        ShowAllCommand = new RelayCommand(ShowAll);
        ShowOpenCommand = new RelayCommand(ShowOpen);
        ShowClosedCommand = new RelayCommand(ShowClosed);
        PrepareNewIssueCommand = new RelayCommand(PrepareNewIssue);
        BackToIssueListCommand = new RelayCommand(BackToIssueList);
        StartDescriptionEditCommand = new RelayCommand(StartDescriptionEdit);
        CancelDescriptionEditCommand = new RelayCommand(CancelDescriptionEdit);
        ShowCommentWriteCommand = new RelayCommand(ShowCommentWrite);
        ShowCommentPreviewCommand = new RelayCommand(ShowCommentPreview);
        ShowProjectBoardCommand = new RelayCommand(() => SetProjectViewMode(ProjectViewMode.Board));
        ShowProjectTableCommand = new RelayCommand(() => SetProjectViewMode(ProjectViewMode.Table));
        ShowProjectCalendarCommand = new RelayCommand(() => SetProjectViewMode(ProjectViewMode.Calendar));
        ShowProjectTimelineCommand = new RelayCommand(() => SetProjectViewMode(ProjectViewMode.Timeline));
        ShowRepositoryIssuesCommand = new RelayCommand(() => SetRepositoryDetailTab(RepositoryDetailTab.Issues));
        ShowRepositoryMilestonesCommand = new RelayCommand(() => SetRepositoryDetailTab(RepositoryDetailTab.Milestones));
        ShowRepositoryProjectsCommand = new RelayCommand(() => SetRepositoryDetailTab(RepositoryDetailTab.Projects));
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        CreateIssueCommand = new AsyncRelayCommand(CreateIssueAsync, CanCreateIssue);
        CreateProjectCommand = new AsyncRelayCommand(CreateProjectAsync, CanCreateProject);
        ToggleSelectedIssueStateCommand = new AsyncRelayCommand(ToggleSelectedIssueStateAsync, CanToggleSelectedIssueState);
        UpdateSelectedIssueCommand = new AsyncRelayCommand(UpdateSelectedIssueAsync, CanUpdateSelectedIssue);
        DeleteSelectedIssueCommand = new AsyncRelayCommand(DeleteSelectedIssueAsync, CanDeleteSelectedIssue);
        AddCommentCommand = new AsyncRelayCommand(AddCommentAsync, CanAddComment);
        AttachFileCommand = new AsyncRelayCommand(AttachFileAsync, CanAttachFile);
        OpenAttachmentCommand = new AsyncRelayCommand<IssueAttachmentViewModel?>(OpenAttachmentAsync, CanOpenAttachment);
        AddIssueRelationCommand = new AsyncRelayCommand(AddIssueRelationAsync, CanAddIssueRelation);
        ScheduleReminderCommand = new AsyncRelayCommand(ScheduleReminderAsync, CanScheduleReminder);
        DismissReminderCommand = new AsyncRelayCommand<IssueReminderViewModel?>(DismissReminderAsync, CanDismissReminder);
        SaveIssueSearchCommand = new AsyncRelayCommand(SaveIssueSearchAsync, CanSaveIssueSearch);
        ApplySavedIssueSearchCommand = new AsyncRelayCommand<SavedIssueSearchViewModel?>(
            ApplySavedIssueSearchAsync,
            CanApplySavedIssueSearch);
        SavePreferencesCommand = new AsyncRelayCommand(SavePreferencesAsync);
        ExportSelectionCommand = new AsyncRelayCommand(ExportSelectionAsync, CanExportSelection);
        SaveExportPresetCommand = new AsyncRelayCommand(SaveExportPresetAsync, CanSaveExportPreset);
        ApplyExportPresetCommand = new AsyncRelayCommand<ExportPresetViewModel?>(
            ApplyExportPresetAsync,
            CanApplyExportPreset);
        MoveProjectItemForwardCommand = new AsyncRelayCommand<ProjectIssueItemViewModel?>(
            MoveProjectItemForwardAsync,
            CanMoveProjectItemForward);
        MoveProjectItemBackwardCommand = new AsyncRelayCommand<ProjectIssueItemViewModel?>(
            MoveProjectItemBackwardAsync,
            CanMoveProjectItemBackward);
        AddProjectCustomFieldCommand = new AsyncRelayCommand(AddProjectCustomFieldAsync, CanAddProjectCustomField);
        AddProjectSavedViewCommand = new AsyncRelayCommand(AddProjectSavedViewAsync, CanAddProjectSavedView);
        ApplyProjectSavedViewCommand = new AsyncRelayCommand<ProjectSavedViewViewModel?>(
            ApplyProjectSavedViewAsync,
            CanApplyProjectSavedView);
        SaveProjectCustomFieldValueCommand = new AsyncRelayCommand(
            SaveProjectCustomFieldValueAsync,
            CanSaveProjectCustomFieldValue);

        DraftPriority = IssuePriority.High;
        DraftProjectName = "Tracky Foundation";
        DraftLabels = "foundation, desktop";
        DraftMilestoneName = "MVP Readiness";
        DraftIssueTypeName = "Task";
        NewProjectName = "Tracky Phase 2";
        NewProjectDescription = "Project board, table, calendar, timeline, custom fields, and saved views.";
        DraftCustomFieldType = ProjectCustomFieldType.Text;
        DraftSavedViewMode = ProjectViewMode.Board;
        DraftSavedViewSort = DefaultProjectSortField;
        DraftReminderAt = DateTimeOffset.Now.AddHours(2);
        DraftExportScope = ExportSelectionScope.CurrentFilter;
        DraftExportFormat = ExportFormat.Markdown;
        DraftExportBodyFormat = ExportBodyFormat.Markdown;
        DraftExportIncludeComments = true;
        DraftExportIncludeActivity = true;
        DraftExportIncludeAttachments = false;
        DraftExportIncludeClosedIssues = false;
        DraftRelationType = IssueRelationType.Related;
        DraftSavedIssueSearchPinned = true;
        DraftIssueContentFormat = IssueContentFormat.Markdown;
        DraftCommentFormat = IssueContentFormat.Markdown;
        EditDescriptionFormat = IssueContentFormat.Markdown;
        SelectedThemePreference = AppThemePreference.WhiteBlue;
        CompactDensityPreference = true;
        ShortcutProfilePreference = "Default";

        RefreshIssueFilterOptions();
        RefreshProjectArrangementOptions([]);
    }

    public RelayCommand ShowIssuesCommand { get; }

    public RelayCommand ShowProjectsCommand { get; }

    public RelayCommand ShowAllCommand { get; }

    public RelayCommand ShowOpenCommand { get; }

    public RelayCommand ShowClosedCommand { get; }

    public RelayCommand PrepareNewIssueCommand { get; }

    public RelayCommand BackToIssueListCommand { get; }

    public RelayCommand StartDescriptionEditCommand { get; }

    public RelayCommand CancelDescriptionEditCommand { get; }

    public RelayCommand ShowCommentWriteCommand { get; }

    public RelayCommand ShowCommentPreviewCommand { get; }

    public RelayCommand ShowProjectBoardCommand { get; }

    public RelayCommand ShowProjectTableCommand { get; }

    public RelayCommand ShowProjectCalendarCommand { get; }

    public RelayCommand ShowProjectTimelineCommand { get; }

    public RelayCommand ShowRepositoryIssuesCommand { get; }

    public RelayCommand ShowRepositoryMilestonesCommand { get; }

    public RelayCommand ShowRepositoryProjectsCommand { get; }

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand CreateIssueCommand { get; }

    public AsyncRelayCommand CreateProjectCommand { get; }

    public AsyncRelayCommand ToggleSelectedIssueStateCommand { get; }

    public AsyncRelayCommand UpdateSelectedIssueCommand { get; }

    public AsyncRelayCommand DeleteSelectedIssueCommand { get; }

    public AsyncRelayCommand AddCommentCommand { get; }

    public AsyncRelayCommand AttachFileCommand { get; }

    public AsyncRelayCommand<IssueAttachmentViewModel?> OpenAttachmentCommand { get; }

    public AsyncRelayCommand AddIssueRelationCommand { get; }

    public AsyncRelayCommand ScheduleReminderCommand { get; }

    public AsyncRelayCommand<IssueReminderViewModel?> DismissReminderCommand { get; }

    public AsyncRelayCommand SaveIssueSearchCommand { get; }

    public AsyncRelayCommand<SavedIssueSearchViewModel?> ApplySavedIssueSearchCommand { get; }

    public AsyncRelayCommand SavePreferencesCommand { get; }

    public AsyncRelayCommand ExportSelectionCommand { get; }

    public AsyncRelayCommand SaveExportPresetCommand { get; }

    public AsyncRelayCommand<ExportPresetViewModel?> ApplyExportPresetCommand { get; }

    public AsyncRelayCommand<ProjectIssueItemViewModel?> MoveProjectItemForwardCommand { get; }

    public AsyncRelayCommand<ProjectIssueItemViewModel?> MoveProjectItemBackwardCommand { get; }

    public AsyncRelayCommand AddProjectCustomFieldCommand { get; }

    public AsyncRelayCommand AddProjectSavedViewCommand { get; }

    public AsyncRelayCommand<ProjectSavedViewViewModel?> ApplyProjectSavedViewCommand { get; }

    public AsyncRelayCommand SaveProjectCustomFieldValueCommand { get; }

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
        _projectLoadCts?.Cancel();
        _projectLoadCts?.Dispose();
        _loadGate.Dispose();
        _detailLoadGate.Dispose();
        _projectLoadGate.Dispose();
    }

    public ObservableCollection<IssueCardViewModel> VisibleIssues { get; } = [];

    public ObservableCollection<ProjectSummaryViewModel> Projects { get; } = [];

    public ObservableCollection<IssueCardViewModel> RepositoryIssues { get; } = [];

    public ObservableCollection<RepositoryMilestoneViewModel> RepositoryMilestones { get; } = [];

    public ObservableCollection<ProjectBoardColumnViewModel> ProjectBoardColumns { get; } = [];

    public ObservableCollection<ProjectIssueItemViewModel> ProjectTableItems { get; } = [];

    public ObservableCollection<ProjectIssueGroupViewModel> ProjectTableGroups { get; } = [];

    public ObservableCollection<ProjectIssueItemViewModel> ProjectCalendarItems { get; } = [];

    public ObservableCollection<ProjectIssueGroupViewModel> ProjectCalendarGroups { get; } = [];

    public ObservableCollection<ProjectIssueItemViewModel> ProjectTimelineItems { get; } = [];

    public ObservableCollection<ProjectIssueGroupViewModel> ProjectTimelineGroups { get; } = [];

    public ObservableCollection<ProjectCustomFieldViewModel> ProjectCustomFields { get; } = [];

    public ObservableCollection<ProjectSavedViewViewModel> ProjectSavedViews { get; } = [];

    public ObservableCollection<IssueReminderViewModel> WorkspaceReminders { get; } = [];

    public ObservableCollection<ExportPresetViewModel> ExportPresets { get; } = [];

    public ObservableCollection<SavedIssueSearchViewModel> SavedIssueSearches { get; } = [];

    public ObservableCollection<string> AvailableIssueAssigneeFilters { get; } = [];

    public ObservableCollection<string> AvailableIssueLabelFilters { get; } = [];

    public ObservableCollection<string> AvailableIssueProjectFilters { get; } = [];

    public ObservableCollection<string> AvailableIssueMilestoneFilters { get; } = [];

    public ObservableCollection<string> AvailableIssueTypeFilters { get; } = [];

    public IReadOnlyList<string> AvailableIssueSortOptions { get; } = IssueSortOptions;

    public IReadOnlyList<IssuePriority> AvailablePriorities { get; } = Enum.GetValues<IssuePriority>();

    public IReadOnlyList<IssueContentFormat> AvailableIssueContentFormats { get; } = Enum.GetValues<IssueContentFormat>();

    public IReadOnlyList<IssueRelationType> AvailableIssueRelationTypes { get; } = Enum.GetValues<IssueRelationType>();

    public IReadOnlyList<AppThemePreference> AvailableThemePreferences { get; } = Enum.GetValues<AppThemePreference>();

    public IReadOnlyList<ExportSelectionScope> AvailableExportScopes { get; } = Enum.GetValues<ExportSelectionScope>();

    public IReadOnlyList<ExportFormat> AvailableExportFormats { get; } = Enum.GetValues<ExportFormat>();

    public IReadOnlyList<ExportBodyFormat> AvailableExportBodyFormats { get; } = Enum.GetValues<ExportBodyFormat>();

    public IReadOnlyList<ProjectCustomFieldType> AvailableProjectCustomFieldTypes { get; } =
        Enum.GetValues<ProjectCustomFieldType>();

    public IReadOnlyList<ProjectViewMode> AvailableProjectViewModes { get; } = Enum.GetValues<ProjectViewMode>();

    public ObservableCollection<string> AvailableProjectSortFields { get; } = [];

    public ObservableCollection<string> AvailableProjectGroupFields { get; } = [];

    public IReadOnlyList<IssueStateReason> AvailableCloseReasons { get; } =
    [
        IssueStateReason.Completed,
        IssueStateReason.NotPlanned,
        IssueStateReason.Duplicate,
    ];

    public bool IsAllScopeActive => ActiveScope == IssueFilterScope.All;

    public bool IsOpenScopeActive => ActiveScope == IssueFilterScope.Open;

    public bool IsClosedScopeActive => ActiveScope == IssueFilterScope.Closed;

    public bool IsIssuesViewActive => !IsProjectsViewActive;

    public bool IsIssueListViewVisible => IsIssuesViewActive && !IsIssueDetailViewActive;

    public bool IsIssueDetailViewActive => IsIssuesViewActive && _isIssueDetailPageActive && HasSelectedIssue;

    public bool IsQuickCaptureVisible => IsIssueListViewVisible;

    public bool IsIssueEditorVisible => IsIssueDetailViewActive && HasSelectedIssueDetail;

    public bool IsDescriptionEditMode => _isDescriptionEditMode;

    public bool IsDescriptionReadMode => !IsDescriptionEditMode;

    public bool IsCommentPreviewMode => _isCommentPreviewMode;

    public bool IsCommentWriteMode => !IsCommentPreviewMode;

    public string DraftCommentPreviewHtmlDocument => IssueHtmlDocumentRenderer.RenderDocument(
        DraftCommentBody,
        DraftCommentFormat,
        EmptyCommentPreviewText);

    public string DraftCommentPreviewFallbackText => string.Join(
        Environment.NewLine,
        IssueContentRenderer
            .Render(DraftCommentBody, DraftCommentFormat, EmptyCommentPreviewText)
            .Select(static block => block.DisplayText));

    public double DraftCommentPreviewHeight => IssueHtmlDocumentRenderer.EstimatePreviewHeight(DraftCommentBody);

    public bool IsIssueDetailPlaceholderVisible => IsIssueListViewVisible && HasNoSelectedIssue;

    public bool IsSelectedIssueDetailVisible => IsIssueDetailViewActive && HasSelectedIssueDetail;

    public bool IsProjectBoardViewActive => SelectedProjectViewMode == ProjectViewMode.Board;

    public bool IsProjectTableViewActive => SelectedProjectViewMode == ProjectViewMode.Table;

    public bool IsProjectCalendarViewActive => SelectedProjectViewMode == ProjectViewMode.Calendar;

    public bool IsProjectTimelineViewActive => SelectedProjectViewMode == ProjectViewMode.Timeline;

    public bool IsRepositoryDashboardViewActive => IsProjectsViewActive;

    public bool IsRepositoryIssuesTabActive => _selectedRepositoryDetailTab == RepositoryDetailTab.Issues;

    public bool IsRepositoryMilestonesTabActive => _selectedRepositoryDetailTab == RepositoryDetailTab.Milestones;

    public bool IsRepositoryProjectsTabActive => _selectedRepositoryDetailTab == RepositoryDetailTab.Projects;

    public bool IsRepositoryProjectBoardViewVisible => IsRepositoryProjectsTabActive && IsProjectBoardViewActive;

    public bool IsRepositoryProjectTableViewVisible => IsRepositoryProjectsTabActive && IsProjectTableViewActive;

    public bool IsRepositoryProjectCalendarViewVisible => IsRepositoryProjectsTabActive && IsProjectCalendarViewActive;

    public bool IsRepositoryProjectTimelineViewVisible => IsRepositoryProjectsTabActive && IsProjectTimelineViewActive;

    public bool HasSelectedIssue => SelectedIssue is not null;

    public bool HasNoSelectedIssue => !HasSelectedIssue;

    public bool HasSelectedIssueDetail => SelectedIssueDetail is not null;

    public bool CanChooseCloseReason => SelectedIssue?.IsOpen == true;

    public bool HasProjects => Projects.Count > 0;

    public bool HasNoProjects => !HasProjects;

    public bool HasSelectedProject => SelectedProject is not null;

    public bool HasNoSelectedProject => !HasSelectedProject;

    public bool HasProjectDetail => ProjectBoardColumns.Count > 0 || ProjectTableItems.Count > 0;

    public bool HasRepositoryIssues => RepositoryIssues.Count > 0;

    public bool HasNoRepositoryIssues => !HasRepositoryIssues;

    public bool HasRepositoryMilestones => RepositoryMilestones.Count > 0;

    public bool HasNoRepositoryMilestones => !HasRepositoryMilestones;

    public string RepositoryIssueCountText => RepositoryIssues.Count == 1
        ? "1 issue"
        : $"{RepositoryIssues.Count} issues";

    public string RepositoryMilestoneCountText => RepositoryMilestones.Count == 1
        ? "1 milestone"
        : $"{RepositoryMilestones.Count} milestones";

    public string RepositoryProjectViewsText => ProjectSavedViews.Count == 1
        ? "1 saved project view"
        : $"{ProjectSavedViews.Count} saved project views";

    public string RepositoryOpenIssuesText => $"{RepositoryIssues.Count(static issue => issue.IsOpen)} Open";

    public string RepositoryClosedIssuesText => $"{RepositoryIssues.Count(static issue => issue.IsClosed)} Closed";

    public string RepositoryProjectsResultsText => $"{ProjectSavedViews.Count} open and 0 closed projects found.";

    public string SelectedRepositoryOwnerName => WorkspaceName;

    public string SelectedRepositoryName => SelectedProject?.Name ?? "Select a repository";

    public string SelectedRepositoryFullName => SelectedProject is null
        ? $"{WorkspaceName} / Select a repository"
        : $"{WorkspaceName} / {SelectedProject.Name}";

    public string SelectedRepositoryDescription => SelectedProject?.Description
        ?? "Select a repository from the dashboard to inspect its issues, milestones, and project views.";

    public bool HasSelectedProjectItemForFields => SelectedProjectItemForFields is not null;

    public bool HasSelectedProjectCustomField => SelectedProjectCustomField is not null;

    public bool HasWorkspaceReminders => WorkspaceReminders.Count > 0;

    public bool HasNoWorkspaceReminders => !HasWorkspaceReminders;

    public bool HasExportPresets => ExportPresets.Count > 0;

    public bool HasNoExportPresets => !HasExportPresets;

    public bool HasSavedIssueSearches => SavedIssueSearches.Count > 0;

    public bool HasNoSavedIssueSearches => !HasSavedIssueSearches;

    private string _workspaceName = "Tracky";

    private string _workspaceDescription = "Preparing the local-first Phase 1 workspace.";

    private string _databasePath = string.Empty;

    private string _searchText = string.Empty;

    private string _selectedIssueAssigneeFilter = AnyIssueAssigneeFilter;

    private string _selectedIssueLabelFilter = AnyIssueLabelFilter;

    private string _selectedIssueProjectFilter = AnyIssueProjectFilter;

    private string _selectedIssueMilestoneFilter = AnyIssueMilestoneFilter;

    private string _selectedIssueTypeFilter = AnyIssueTypeFilter;

    private string _selectedIssueSortOption = DefaultIssueSortOption;

    private string _issueListSummaryText = "0 issues";

    private bool _isProjectsViewActive;

    private bool _isIssueDetailPageActive;

    private bool _isDescriptionEditMode;

    private bool _isCommentPreviewMode;

    private IssueFilterScope _activeScope = IssueFilterScope.All;

    private ProjectViewMode _selectedProjectViewMode = ProjectViewMode.Board;

    private RepositoryDetailTab _selectedRepositoryDetailTab = RepositoryDetailTab.Issues;

    private IssueCardViewModel? _selectedIssue;

    private IssueDetailViewModel? _selectedIssueDetail;

    private ProjectSummaryViewModel? _selectedProject;

    private ProjectIssueItemViewModel? _selectedProjectItemForFields;

    private ProjectCustomFieldViewModel? _selectedProjectCustomField;

    private ProjectDetail? _selectedProjectDetail;

    private int _totalIssues;

    private int _openIssues;

    private int _closedIssues;

    private int _overdueIssues;

    private int _dueTodayIssues;

    private int _upcomingIssues;

    private bool _isBusy;

    private bool _isDetailBusy;

    private bool _isProjectBusy;

    private string _statusMessage = "Tracky is preparing the default workspace.";

    private string _detailStatusMessage = "Select an issue to load its timeline, comments, and attachments.";

    private string _projectStatusMessage = "Projects are being prepared from issue metadata.";

    private string _projectFilterText = string.Empty;

    private string _repositoryIssueSearchText = string.Empty;

    private string _projectSortText = DefaultProjectSortField;

    private string _projectGroupByText = NoProjectGroupingField;

    private string _draftCustomFieldValue = string.Empty;

    private string _draftTitle = string.Empty;

    private string _draftDescription = string.Empty;

    private IssueContentFormat _draftIssueContentFormat;

    private string _draftAssignee = "Dabin";

    private IssuePriority _draftPriority;

    private DateTimeOffset? _draftDueDate = DateTimeOffset.Now.AddDays(2);

    private string _draftProjectName = string.Empty;

    private string _draftLabels = string.Empty;

    private string _draftMilestoneName = string.Empty;

    private string _draftIssueTypeName = string.Empty;

    private string _draftCommentAuthor = "Dabin";

    private string _draftCommentBody = string.Empty;

    private IssueContentFormat _draftCommentFormat;

    private string _editTitle = string.Empty;

    private string _editDescription = string.Empty;

    private IssueContentFormat _editDescriptionFormat;

    private string _editAssignee = string.Empty;

    private IssuePriority _editPriority;

    private DateTimeOffset? _editDueDate;

    private string _editProjectName = string.Empty;

    private string _editLabels = string.Empty;

    private string _editMilestoneName = string.Empty;

    private string _editIssueTypeName = string.Empty;

    private IssueStateReason _selectedCloseReason = IssueStateReason.Completed;

    private string _newProjectName = string.Empty;

    private string _newProjectDescription = string.Empty;

    private string _draftCustomFieldName = string.Empty;

    private ProjectCustomFieldType _draftCustomFieldType;

    private string _draftCustomFieldOptions = string.Empty;

    private string _draftSavedViewName = string.Empty;

    private ProjectViewMode _draftSavedViewMode;

    private string _draftSavedViewFilter = string.Empty;

    private string _draftSavedViewSort = DefaultProjectSortField;

    private string _draftSavedViewGroup = NoProjectGroupingField;

    private string _draftReminderTitle = string.Empty;

    private string _draftReminderNote = string.Empty;

    private DateTimeOffset? _draftReminderAt;

    private string _exportPresetName = string.Empty;

    private ExportSelectionScope _draftExportScope;

    private ExportFormat _draftExportFormat;

    private ExportBodyFormat _draftExportBodyFormat;

    private bool _draftExportIncludeComments;

    private bool _draftExportIncludeActivity;

    private bool _draftExportIncludeAttachments;

    private bool _draftExportIncludeClosedIssues;

    private string _lastExportPath = string.Empty;

    private string _draftSavedIssueSearchName = string.Empty;

    private bool _draftSavedIssueSearchPinned;

    private int _draftRelationTargetIssueNumber;

    private IssueRelationType _draftRelationType;

    private AppThemePreference _selectedThemePreference;

    private bool _compactDensityPreference;

    private string _shortcutProfilePreference = string.Empty;

    public string WorkspaceName
    {
        get => _workspaceName;
        private set
        {
            if (SetProperty(ref _workspaceName, value))
            {
                OnPropertyChanged(nameof(SelectedRepositoryOwnerName));
                OnPropertyChanged(nameof(SelectedRepositoryFullName));
            }
        }
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
                SaveIssueSearchCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string RepositoryIssueSearchText
    {
        get => _repositoryIssueSearchText;
        set
        {
            if (SetProperty(ref _repositoryIssueSearchText, value))
            {
                RefreshSelectedRepositoryContent();
            }
        }
    }

    public string SelectedIssueAssigneeFilter
    {
        get => _selectedIssueAssigneeFilter;
        set
        {
            if (SetProperty(ref _selectedIssueAssigneeFilter, value))
            {
                ApplyFilters();
            }
        }
    }

    public string SelectedIssueLabelFilter
    {
        get => _selectedIssueLabelFilter;
        set
        {
            if (SetProperty(ref _selectedIssueLabelFilter, value))
            {
                ApplyFilters();
            }
        }
    }

    public string SelectedIssueProjectFilter
    {
        get => _selectedIssueProjectFilter;
        set
        {
            if (SetProperty(ref _selectedIssueProjectFilter, value))
            {
                ApplyFilters();
            }
        }
    }

    public string SelectedIssueMilestoneFilter
    {
        get => _selectedIssueMilestoneFilter;
        set
        {
            if (SetProperty(ref _selectedIssueMilestoneFilter, value))
            {
                ApplyFilters();
            }
        }
    }

    public string SelectedIssueTypeFilter
    {
        get => _selectedIssueTypeFilter;
        set
        {
            if (SetProperty(ref _selectedIssueTypeFilter, value))
            {
                ApplyFilters();
            }
        }
    }

    public string SelectedIssueSortOption
    {
        get => _selectedIssueSortOption;
        set
        {
            if (SetProperty(ref _selectedIssueSortOption, value))
            {
                ApplyFilters();
            }
        }
    }

    public string IssueListSummaryText
    {
        get => _issueListSummaryText;
        private set => SetProperty(ref _issueListSummaryText, value);
    }

    public bool IsProjectsViewActive
    {
        get => _isProjectsViewActive;
        private set
        {
            if (!SetProperty(ref _isProjectsViewActive, value))
            {
                return;
            }

            OnIssueNavigationPropertiesChanged();
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

    public ProjectViewMode SelectedProjectViewMode
    {
        get => _selectedProjectViewMode;
        private set
        {
            if (!SetProperty(ref _selectedProjectViewMode, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsProjectBoardViewActive));
            OnPropertyChanged(nameof(IsProjectTableViewActive));
            OnPropertyChanged(nameof(IsProjectCalendarViewActive));
            OnPropertyChanged(nameof(IsProjectTimelineViewActive));
            OnPropertyChanged(nameof(IsRepositoryProjectBoardViewVisible));
            OnPropertyChanged(nameof(IsRepositoryProjectTableViewVisible));
            OnPropertyChanged(nameof(IsRepositoryProjectCalendarViewVisible));
            OnPropertyChanged(nameof(IsRepositoryProjectTimelineViewVisible));
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
            SetIssueDetailPageActive(value is not null && IsIssuesViewActive);
            ToggleSelectedIssueStateCommand.NotifyCanExecuteChanged();
            UpdateSelectedIssueCommand.NotifyCanExecuteChanged();
            DeleteSelectedIssueCommand.NotifyCanExecuteChanged();
            AddCommentCommand.NotifyCanExecuteChanged();
            AttachFileCommand.NotifyCanExecuteChanged();
            AddIssueRelationCommand.NotifyCanExecuteChanged();
            ScheduleReminderCommand.NotifyCanExecuteChanged();
            ExportSelectionCommand.NotifyCanExecuteChanged();
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
            OnIssueNavigationPropertiesChanged();
            HydrateEditDraft(value);
            SetDescriptionEditMode(false);
            UpdateSelectedIssueCommand.NotifyCanExecuteChanged();
            OpenAttachmentCommand.NotifyCanExecuteChanged();
            DismissReminderCommand.NotifyCanExecuteChanged();
        }
    }

    public ProjectSummaryViewModel? SelectedProject
    {
        get => _selectedProject;
        set
        {
            if (!SetProperty(ref _selectedProject, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasSelectedProject));
            OnPropertyChanged(nameof(HasNoSelectedProject));
            OnPropertyChanged(nameof(SelectedRepositoryName));
            OnPropertyChanged(nameof(SelectedRepositoryFullName));
            OnPropertyChanged(nameof(SelectedRepositoryDescription));
            RefreshSelectedRepositoryContent();
            CreateProjectCommand.NotifyCanExecuteChanged();
            AddProjectCustomFieldCommand.NotifyCanExecuteChanged();
            AddProjectSavedViewCommand.NotifyCanExecuteChanged();
            ExportSelectionCommand.NotifyCanExecuteChanged();
            StartProjectDetailLoad(value?.Id);
        }
    }

    public ProjectIssueItemViewModel? SelectedProjectItemForFields
    {
        get => _selectedProjectItemForFields;
        set
        {
            if (!SetProperty(ref _selectedProjectItemForFields, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasSelectedProjectItemForFields));
            HydrateCustomFieldValueDraft();
            SaveProjectCustomFieldValueCommand.NotifyCanExecuteChanged();
        }
    }

    public ProjectCustomFieldViewModel? SelectedProjectCustomField
    {
        get => _selectedProjectCustomField;
        set
        {
            if (!SetProperty(ref _selectedProjectCustomField, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasSelectedProjectCustomField));
            HydrateCustomFieldValueDraft();
            SaveProjectCustomFieldValueCommand.NotifyCanExecuteChanged();
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

    public bool IsProjectBusy
    {
        get => _isProjectBusy;
        private set => SetProperty(ref _isProjectBusy, value);
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

    public string ProjectStatusMessage
    {
        get => _projectStatusMessage;
        private set => SetProperty(ref _projectStatusMessage, value);
    }

    public string ProjectFilterText
    {
        get => _projectFilterText;
        set
        {
            if (!SetProperty(ref _projectFilterText, value))
            {
                return;
            }

            if (_selectedProjectDetail is not null)
            {
                ApplyProjectDetail(_selectedProjectDetail);
            }
        }
    }

    public string ProjectSortText
    {
        get => _projectSortText;
        set
        {
            if (!SetProperty(ref _projectSortText, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ProjectArrangementText));
            if (_selectedProjectDetail is not null)
            {
                ApplyProjectDetail(_selectedProjectDetail);
            }
        }
    }

    public string ProjectGroupByText
    {
        get => _projectGroupByText;
        set
        {
            if (!SetProperty(ref _projectGroupByText, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ProjectArrangementText));
            if (_selectedProjectDetail is not null)
            {
                ApplyProjectDetail(_selectedProjectDetail);
            }
        }
    }

    public string ProjectArrangementText => IsProjectGroupingEnabled(ProjectGroupByText)
        ? $"Sorted by {ProjectSortText}; grouped by {ProjectGroupByText}"
        : $"Sorted by {ProjectSortText}; no grouping";

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

    public string DraftDescription
    {
        get => _draftDescription;
        set => SetProperty(ref _draftDescription, value);
    }

    public IssueContentFormat DraftIssueContentFormat
    {
        get => _draftIssueContentFormat;
        set => SetProperty(ref _draftIssueContentFormat, value);
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

    public string DraftMilestoneName
    {
        get => _draftMilestoneName;
        set => SetProperty(ref _draftMilestoneName, value);
    }

    public string DraftIssueTypeName
    {
        get => _draftIssueTypeName;
        set => SetProperty(ref _draftIssueTypeName, value);
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
                OnDraftCommentPreviewChanged();
            }
        }
    }

    public IssueContentFormat DraftCommentFormat
    {
        get => _draftCommentFormat;
        set
        {
            if (SetProperty(ref _draftCommentFormat, value))
            {
                OnDraftCommentPreviewChanged();
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

    public IssueContentFormat EditDescriptionFormat
    {
        get => _editDescriptionFormat;
        set => SetProperty(ref _editDescriptionFormat, value);
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

    public string EditMilestoneName
    {
        get => _editMilestoneName;
        set => SetProperty(ref _editMilestoneName, value);
    }

    public string EditIssueTypeName
    {
        get => _editIssueTypeName;
        set => SetProperty(ref _editIssueTypeName, value);
    }

    public IssueStateReason SelectedCloseReason
    {
        get => _selectedCloseReason;
        set => SetProperty(ref _selectedCloseReason, value);
    }

    public string NewProjectName
    {
        get => _newProjectName;
        set
        {
            if (SetProperty(ref _newProjectName, value))
            {
                CreateProjectCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string NewProjectDescription
    {
        get => _newProjectDescription;
        set => SetProperty(ref _newProjectDescription, value);
    }

    public string DraftCustomFieldName
    {
        get => _draftCustomFieldName;
        set
        {
            if (SetProperty(ref _draftCustomFieldName, value))
            {
                AddProjectCustomFieldCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public ProjectCustomFieldType DraftCustomFieldType
    {
        get => _draftCustomFieldType;
        set => SetProperty(ref _draftCustomFieldType, value);
    }

    public string DraftCustomFieldOptions
    {
        get => _draftCustomFieldOptions;
        set => SetProperty(ref _draftCustomFieldOptions, value);
    }

    public string DraftCustomFieldValue
    {
        get => _draftCustomFieldValue;
        set
        {
            if (SetProperty(ref _draftCustomFieldValue, value))
            {
                SaveProjectCustomFieldValueCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string DraftSavedViewName
    {
        get => _draftSavedViewName;
        set
        {
            if (SetProperty(ref _draftSavedViewName, value))
            {
                AddProjectSavedViewCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public ProjectViewMode DraftSavedViewMode
    {
        get => _draftSavedViewMode;
        set => SetProperty(ref _draftSavedViewMode, value);
    }

    public string DraftSavedViewFilter
    {
        get => _draftSavedViewFilter;
        set => SetProperty(ref _draftSavedViewFilter, value);
    }

    public string DraftSavedViewSort
    {
        get => _draftSavedViewSort;
        set => SetProperty(ref _draftSavedViewSort, value);
    }

    public string DraftSavedViewGroup
    {
        get => _draftSavedViewGroup;
        set => SetProperty(ref _draftSavedViewGroup, value);
    }

    public string DraftReminderTitle
    {
        get => _draftReminderTitle;
        set
        {
            if (SetProperty(ref _draftReminderTitle, value))
            {
                ScheduleReminderCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string DraftReminderNote
    {
        get => _draftReminderNote;
        set => SetProperty(ref _draftReminderNote, value);
    }

    public DateTimeOffset? DraftReminderAt
    {
        get => _draftReminderAt;
        set
        {
            if (SetProperty(ref _draftReminderAt, value))
            {
                ScheduleReminderCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ExportPresetName
    {
        get => _exportPresetName;
        set
        {
            if (SetProperty(ref _exportPresetName, value))
            {
                SaveExportPresetCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public ExportSelectionScope DraftExportScope
    {
        get => _draftExportScope;
        set
        {
            if (!SetProperty(ref _draftExportScope, value))
            {
                return;
            }

            ExportSelectionCommand.NotifyCanExecuteChanged();
            SaveExportPresetCommand.NotifyCanExecuteChanged();
        }
    }

    public ExportFormat DraftExportFormat
    {
        get => _draftExportFormat;
        set
        {
            if (!SetProperty(ref _draftExportFormat, value))
            {
                return;
            }

            ExportSelectionCommand.NotifyCanExecuteChanged();
            SaveExportPresetCommand.NotifyCanExecuteChanged();
        }
    }

    public ExportBodyFormat DraftExportBodyFormat
    {
        get => _draftExportBodyFormat;
        set
        {
            if (SetProperty(ref _draftExportBodyFormat, value))
            {
                SaveExportPresetCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool DraftExportIncludeComments
    {
        get => _draftExportIncludeComments;
        set => SetProperty(ref _draftExportIncludeComments, value);
    }

    public bool DraftExportIncludeActivity
    {
        get => _draftExportIncludeActivity;
        set => SetProperty(ref _draftExportIncludeActivity, value);
    }

    public bool DraftExportIncludeAttachments
    {
        get => _draftExportIncludeAttachments;
        set => SetProperty(ref _draftExportIncludeAttachments, value);
    }

    public bool DraftExportIncludeClosedIssues
    {
        get => _draftExportIncludeClosedIssues;
        set => SetProperty(ref _draftExportIncludeClosedIssues, value);
    }

    public string LastExportPath
    {
        get => _lastExportPath;
        private set => SetProperty(ref _lastExportPath, value);
    }

    public string DraftSavedIssueSearchName
    {
        get => _draftSavedIssueSearchName;
        set
        {
            if (SetProperty(ref _draftSavedIssueSearchName, value))
            {
                SaveIssueSearchCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool DraftSavedIssueSearchPinned
    {
        get => _draftSavedIssueSearchPinned;
        set => SetProperty(ref _draftSavedIssueSearchPinned, value);
    }

    public int DraftRelationTargetIssueNumber
    {
        get => _draftRelationTargetIssueNumber;
        set
        {
            if (SetProperty(ref _draftRelationTargetIssueNumber, value))
            {
                AddIssueRelationCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public IssueRelationType DraftRelationType
    {
        get => _draftRelationType;
        set => SetProperty(ref _draftRelationType, value);
    }

    public AppThemePreference SelectedThemePreference
    {
        get => _selectedThemePreference;
        set => SetProperty(ref _selectedThemePreference, value);
    }

    public bool CompactDensityPreference
    {
        get => _compactDensityPreference;
        set => SetProperty(ref _compactDensityPreference, value);
    }

    public string ShortcutProfilePreference
    {
        get => _shortcutProfilePreference;
        set => SetProperty(ref _shortcutProfilePreference, value);
    }

    public Task InitializeAsync()
    {
        return LoadAsync();
    }

    private void ShowIssues()
    {
        BackToIssueList();
    }

    private void ShowProjects()
    {
        // Projects is a first-class repository tab in the GitHub-style shell,
        // so the command must activate both the project screen and the repository Projects tab.
        IsProjectsViewActive = true;
        SetRepositoryDetailTab(RepositoryDetailTab.Projects);
    }

    private void SetProjectViewMode(ProjectViewMode viewMode)
    {
        SelectedProjectViewMode = viewMode;
        IsProjectsViewActive = true;
        SetRepositoryDetailTab(RepositoryDetailTab.Projects);
    }

    private void SetRepositoryDetailTab(RepositoryDetailTab tab)
    {
        if (_selectedRepositoryDetailTab == tab)
        {
            return;
        }

        _selectedRepositoryDetailTab = tab;
        OnRepositoryTabPropertiesChanged();
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

    private void PrepareNewIssue()
    {
        IsProjectsViewActive = false;
        SelectedIssue = null;
        SetIssueDetailPageActive(false);
        SetDescriptionEditMode(false);
        ShowCommentWrite();
        DraftTitle = string.Empty;
        DraftDescription = string.Empty;
        DraftIssueContentFormat = IssueContentFormat.Markdown;
        DetailStatusMessage = "New issue panel is ready. Fill the title and metadata, then create it in the local workspace.";
    }

    private void BackToIssueList()
    {
        IsProjectsViewActive = false;
        SelectedIssue = null;
        SetIssueDetailPageActive(false);
        SetDescriptionEditMode(false);
        ShowCommentWrite();
    }

    private void StartDescriptionEdit()
    {
        if (SelectedIssueDetail is null)
        {
            return;
        }

        // GitHub issue body처럼 읽기 카드의 액션에서 원문 편집으로 들어가도록,
        // 현재 상세 데이터를 편집 초안에 다시 채운 뒤 body card만 edit 상태로 전환한다.
        HydrateEditDraft(SelectedIssueDetail);
        SetDescriptionEditMode(true);
    }

    private void CancelDescriptionEdit()
    {
        // 취소 시에는 사용자가 타이핑한 원문 변경을 버리고 선택 이슈의 저장된 원문으로 되돌린다.
        HydrateEditDraft(SelectedIssueDetail);
        SetDescriptionEditMode(false);
    }

    private void ShowCommentWrite()
    {
        SetCommentPreviewMode(false);
    }

    private void ShowCommentPreview()
    {
        SetCommentPreviewMode(true);
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
                ParseLabels(DraftLabels),
                DraftMilestoneName,
                DraftIssueTypeName,
                DraftDescription,
                DraftIssueContentFormat),
            cancellationToken);

        DraftTitle = string.Empty;
        DraftDescription = string.Empty;
        DraftLabels = "foundation";
        DraftMilestoneName = "MVP Readiness";
        DraftIssueTypeName = "Task";
        DraftIssueContentFormat = IssueContentFormat.Markdown;
        DraftPriority = IssuePriority.High;
        DraftDueDate = DateTimeOffset.Now.AddDays(2);

        await LoadAsync(
            createdIssue.Id,
            $"Issue #{createdIssue.Number} was added to the local workspace.",
            cancellationToken: cancellationToken);
    }

    private async Task CreateProjectAsync(CancellationToken cancellationToken)
    {
        var createdProject = await _workspaceService.CreateProjectAsync(
            new CreateProjectInput(
                NewProjectName,
                NewProjectDescription),
            cancellationToken);

        NewProjectName = string.Empty;
        NewProjectDescription = string.Empty;
        IsProjectsViewActive = true;

        await LoadAsync(
            completionMessage: $"Project \"{createdProject.Name}\" is ready for Phase 2 planning.",
            preferredProjectId: createdProject.Id,
            cancellationToken: cancellationToken);
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
            cancellationToken: cancellationToken);
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
                ParseLabels(EditLabels),
                EditMilestoneName,
                EditIssueTypeName,
                EditDescriptionFormat),
            cancellationToken);

        await LoadAsync(
            updatedIssue?.Id ?? issueId,
            updatedIssue is null
                ? "The selected issue could not be saved because it was not found."
                : $"Issue #{updatedIssue.Number} was saved with updated Phase 1 metadata.",
            cancellationToken: cancellationToken);
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
                DraftCommentBody,
                DraftCommentFormat),
            cancellationToken);

        if (comment is null)
        {
            DetailStatusMessage = "The comment could not be saved because the selected issue was not found.";
            return;
        }

        DraftCommentBody = string.Empty;
        ShowCommentWrite();
        await LoadAsync(
            issueId,
            "The selected issue was refreshed after saving the new comment.",
            cancellationToken: cancellationToken);
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
            cancellationToken: cancellationToken);
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

    private Task MoveProjectItemForwardAsync(
        ProjectIssueItemViewModel? item,
        CancellationToken cancellationToken)
    {
        return item?.NextBoardColumn is null
            ? Task.CompletedTask
            : MoveProjectItemToColumnAsync(item, item.NextBoardColumn, cancellationToken);
    }

    private Task MoveProjectItemBackwardAsync(
        ProjectIssueItemViewModel? item,
        CancellationToken cancellationToken)
    {
        return item?.PreviousBoardColumn is null
            ? Task.CompletedTask
            : MoveProjectItemToColumnAsync(item, item.PreviousBoardColumn, cancellationToken);
    }

    private async Task MoveProjectItemToColumnAsync(
        ProjectIssueItemViewModel item,
        string boardColumn,
        CancellationToken cancellationToken)
    {
        var movedItem = await _workspaceService.MoveProjectItemAsync(
            new MoveProjectItemInput(item.ProjectItemId, boardColumn),
            cancellationToken);
        if (movedItem is null)
        {
            ProjectStatusMessage = "The selected project item could not be moved because it no longer exists.";
            return;
        }

        ProjectStatusMessage = $"Issue #{movedItem.IssueNumber} moved to {movedItem.BoardColumn}.";
        if (SelectedProject is not null)
        {
            await LoadProjectDetailAsync(SelectedProject.Id, cancellationToken);
        }
    }

    private async Task AddProjectCustomFieldAsync(CancellationToken cancellationToken)
    {
        if (SelectedProject is null)
        {
            return;
        }

        var field = await _workspaceService.AddProjectCustomFieldAsync(
            new AddProjectCustomFieldInput(
                SelectedProject.Id,
                DraftCustomFieldName,
                DraftCustomFieldType,
                DraftCustomFieldOptions),
            cancellationToken);
        if (field is null)
        {
            ProjectStatusMessage = "The custom field could not be added because the project was not found.";
            return;
        }

        DraftCustomFieldName = string.Empty;
        DraftCustomFieldOptions = string.Empty;
        ProjectStatusMessage = $"Custom field \"{field.Name}\" is available on the selected project.";
        await LoadProjectDetailAsync(SelectedProject.Id, cancellationToken);
    }

    private async Task AddProjectSavedViewAsync(CancellationToken cancellationToken)
    {
        if (SelectedProject is null)
        {
            return;
        }

        var savedView = await _workspaceService.AddProjectSavedViewAsync(
            new AddProjectSavedViewInput(
                SelectedProject.Id,
                DraftSavedViewName,
                DraftSavedViewMode,
                DraftSavedViewFilter,
                DraftSavedViewSort,
                DraftSavedViewGroup),
            cancellationToken);
        if (savedView is null)
        {
            ProjectStatusMessage = "The saved view could not be added because the project was not found.";
            return;
        }

        DraftSavedViewName = string.Empty;
        DraftSavedViewFilter = string.Empty;
        DraftSavedViewSort = DefaultProjectSortField;
        DraftSavedViewGroup = NoProjectGroupingField;
        SelectedProjectViewMode = savedView.ViewMode;
        ProjectStatusMessage = $"Saved view \"{savedView.Name}\" was stored for this project.";
        await LoadProjectDetailAsync(SelectedProject.Id, cancellationToken);
    }

    private Task ApplyProjectSavedViewAsync(
        ProjectSavedViewViewModel? savedView,
        CancellationToken cancellationToken)
    {
        if (savedView is null)
        {
            return Task.CompletedTask;
        }

        SelectedProjectViewMode = savedView.SavedView.ViewMode;
        ProjectFilterText = savedView.SavedView.FilterText;
        ProjectSortText = string.IsNullOrWhiteSpace(savedView.SavedView.SortByField)
            ? DefaultProjectSortField
            : savedView.SavedView.SortByField;
        ProjectGroupByText = string.IsNullOrWhiteSpace(savedView.SavedView.GroupByField)
            ? NoProjectGroupingField
            : savedView.SavedView.GroupByField;
        ProjectStatusMessage = $"Saved view \"{savedView.Name}\" applied to the selected project.";
        return Task.CompletedTask;
    }

    private async Task ScheduleReminderAsync(CancellationToken cancellationToken)
    {
        if (SelectedIssue is null || DraftReminderAt is null)
        {
            return;
        }

        var reminder = await _workspaceService.ScheduleIssueReminderAsync(
            new ScheduleIssueReminderInput(
                SelectedIssue.Id,
                string.IsNullOrWhiteSpace(DraftReminderTitle)
                    ? $"Follow up on #{SelectedIssue.Number}"
                    : DraftReminderTitle,
                DraftReminderNote,
                DraftReminderAt.Value.ToUniversalTime()),
            cancellationToken);
        if (reminder is null)
        {
            DetailStatusMessage = "The reminder could not be scheduled because the selected issue was not found.";
            return;
        }

        DraftReminderTitle = string.Empty;
        DraftReminderNote = string.Empty;
        DraftReminderAt = DateTimeOffset.Now.AddHours(2);
        await LoadAsync(SelectedIssue.Id, cancellationToken: cancellationToken);
        DetailStatusMessage = $"Reminder \"{reminder.Title}\" was scheduled.";
    }

    private async Task DismissReminderAsync(
        IssueReminderViewModel? reminder,
        CancellationToken cancellationToken)
    {
        if (reminder is null)
        {
            return;
        }

        var dismissedReminder = await _workspaceService.DismissReminderAsync(
            new DismissReminderInput(reminder.Id),
            cancellationToken);
        if (dismissedReminder is null)
        {
            DetailStatusMessage = "The reminder could not be dismissed because it was not found.";
            return;
        }

        await LoadAsync(SelectedIssue?.Id, cancellationToken: cancellationToken);
        DetailStatusMessage = $"Reminder \"{dismissedReminder.Title}\" was dismissed.";
    }

    private async Task AddIssueRelationAsync(CancellationToken cancellationToken)
    {
        if (SelectedIssue is null)
        {
            return;
        }

        var targetIssue = _allIssues.FirstOrDefault(issue => issue.Number == DraftRelationTargetIssueNumber);
        if (targetIssue is null || targetIssue.Id == SelectedIssue.Id)
        {
            DetailStatusMessage = "The relation target could not be found.";
            return;
        }

        var relation = await _workspaceService.AddIssueRelationAsync(
            new AddIssueRelationInput(
                SelectedIssue.Id,
                targetIssue.Id,
                DraftRelationType),
            cancellationToken);
        if (relation is null)
        {
            DetailStatusMessage = "The issue relation could not be added.";
            return;
        }

        DraftRelationTargetIssueNumber = 0;
        await LoadAsync(SelectedIssue.Id, cancellationToken: cancellationToken);
        DetailStatusMessage = $"Relation to #{relation.TargetIssueNumber} was added.";
    }

    private async Task SaveIssueSearchAsync(CancellationToken cancellationToken)
    {
        var savedSearch = await _workspaceService.AddSavedIssueSearchAsync(
            new AddSavedIssueSearchInput(
                DraftSavedIssueSearchName,
                SearchText,
                DraftSavedIssueSearchPinned),
            cancellationToken);
        if (savedSearch is null)
        {
            StatusMessage = "The saved search could not be stored.";
            return;
        }

        DraftSavedIssueSearchName = string.Empty;
        await LoadAsync(SelectedIssue?.Id, $"Saved search \"{savedSearch.Name}\" was stored.", SelectedProject?.Id, cancellationToken);
    }

    private Task ApplySavedIssueSearchAsync(
        SavedIssueSearchViewModel? savedSearch,
        CancellationToken cancellationToken)
    {
        if (savedSearch is null)
        {
            return Task.CompletedTask;
        }

        BackToIssueList();
        SearchText = savedSearch.SavedSearch.QueryText;
        StatusMessage = $"Saved search \"{savedSearch.SavedSearch.Name}\" applied.";
        return Task.CompletedTask;
    }

    private async Task SavePreferencesAsync(CancellationToken cancellationToken)
    {
        var preferences = await _workspaceService.UpdateWorkspacePreferencesAsync(
            new UpdateWorkspacePreferencesInput(
                SelectedThemePreference,
                CompactDensityPreference,
                ShortcutProfilePreference),
            cancellationToken);

        HydratePreferences(preferences);
        StatusMessage = $"Preferences saved with {preferences.Theme} theme and {preferences.ShortcutProfile} shortcuts.";
    }

    private async Task ExportSelectionAsync(CancellationToken cancellationToken)
    {
        var result = await _workspaceService.ExportSelectionAsync(
            BuildCurrentExportOptions(),
            cancellationToken);

        LastExportPath = result.OutputPath;
        StatusMessage = $"Exported {result.IssueCount} issues to {result.OutputPath}.";
    }

    private async Task SaveExportPresetAsync(CancellationToken cancellationToken)
    {
        var preset = await _workspaceService.AddExportPresetAsync(
            new AddExportPresetInput(
                ExportPresetName,
                DraftExportScope,
                DraftExportFormat,
                DraftExportBodyFormat,
                DraftExportIncludeComments,
                DraftExportIncludeActivity,
                DraftExportIncludeAttachments,
                DraftExportIncludeClosedIssues),
            cancellationToken);
        if (preset is null)
        {
            StatusMessage = "The export preset could not be saved.";
            return;
        }

        ExportPresetName = string.Empty;
        await LoadAsync(SelectedIssue?.Id, $"Export preset \"{preset.Name}\" was saved.", SelectedProject?.Id, cancellationToken);
    }

    private Task ApplyExportPresetAsync(
        ExportPresetViewModel? preset,
        CancellationToken cancellationToken)
    {
        if (preset is null)
        {
            return Task.CompletedTask;
        }

        DraftExportScope = preset.Preset.Scope;
        DraftExportFormat = preset.Preset.Format;
        DraftExportBodyFormat = preset.Preset.BodyFormat;
        DraftExportIncludeComments = preset.Preset.IncludeComments;
        DraftExportIncludeActivity = preset.Preset.IncludeActivity;
        DraftExportIncludeAttachments = preset.Preset.IncludeAttachments;
        DraftExportIncludeClosedIssues = preset.Preset.IncludeClosedIssues;
        StatusMessage = $"Export preset \"{preset.Name}\" applied.";
        return Task.CompletedTask;
    }

    private async Task SaveProjectCustomFieldValueAsync(CancellationToken cancellationToken)
    {
        if (SelectedProject is null || SelectedProjectItemForFields is null || SelectedProjectCustomField is null)
        {
            return;
        }

        var updatedItem = await _workspaceService.UpdateProjectCustomFieldValueAsync(
            new UpdateProjectCustomFieldValueInput(
                SelectedProjectItemForFields.ProjectItemId,
                SelectedProjectCustomField.Field.Id,
                DraftCustomFieldValue),
            cancellationToken);
        if (updatedItem is null)
        {
            ProjectStatusMessage = "The field value could not be saved because the project item or field was not found.";
            return;
        }

        ProjectStatusMessage = $"{SelectedProjectCustomField.Name} was saved for issue #{updatedItem.IssueNumber}.";
        await LoadProjectDetailAsync(SelectedProject.Id, cancellationToken);
    }

    private bool CanCreateIssue()
    {
        return !IsBusy && !string.IsNullOrWhiteSpace(DraftTitle);
    }

    private bool CanCreateProject()
    {
        return !IsBusy && !string.IsNullOrWhiteSpace(NewProjectName);
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

    private bool CanAddIssueRelation()
    {
        return !IsBusy
            && !IsDetailBusy
            && SelectedIssue is not null
            && DraftRelationTargetIssueNumber > 0
            && DraftRelationTargetIssueNumber != SelectedIssue.Number;
    }

    private bool CanScheduleReminder()
    {
        return !IsBusy
            && !IsDetailBusy
            && SelectedIssue is not null
            && DraftReminderAt is not null;
    }

    private bool CanDismissReminder(IssueReminderViewModel? reminder)
    {
        return !IsBusy && !IsDetailBusy && reminder is not null && !reminder.IsDismissed;
    }

    private bool CanSaveIssueSearch()
    {
        return !IsBusy
            && !string.IsNullOrWhiteSpace(DraftSavedIssueSearchName)
            && !string.IsNullOrWhiteSpace(SearchText);
    }

    private static bool CanApplySavedIssueSearch(SavedIssueSearchViewModel? savedSearch)
    {
        return savedSearch is not null;
    }

    private bool CanExportSelection()
    {
        return !IsBusy
            && (DraftExportScope switch
            {
                ExportSelectionScope.CurrentIssue => SelectedIssue is not null,
                ExportSelectionScope.CurrentFilter => VisibleIssues.Count > 0,
                ExportSelectionScope.Project => SelectedProject is not null,
                _ => _allIssues.Count > 0,
            });
    }

    private bool CanSaveExportPreset()
    {
        return !IsBusy && !string.IsNullOrWhiteSpace(ExportPresetName);
    }

    private static bool CanApplyExportPreset(ExportPresetViewModel? preset)
    {
        return preset is not null;
    }

    private bool CanMoveProjectItemForward(ProjectIssueItemViewModel? item)
    {
        return !IsBusy && !IsProjectBusy && item?.HasNextColumn == true;
    }

    private bool CanMoveProjectItemBackward(ProjectIssueItemViewModel? item)
    {
        return !IsBusy && !IsProjectBusy && item?.HasPreviousColumn == true;
    }

    private bool CanAddProjectCustomField()
    {
        return !IsBusy
            && !IsProjectBusy
            && SelectedProject is not null
            && !string.IsNullOrWhiteSpace(DraftCustomFieldName);
    }

    private bool CanAddProjectSavedView()
    {
        return !IsBusy
            && !IsProjectBusy
            && SelectedProject is not null
            && !string.IsNullOrWhiteSpace(DraftSavedViewName);
    }

    private bool CanApplyProjectSavedView(ProjectSavedViewViewModel? savedView)
    {
        return !IsBusy && !IsProjectBusy && savedView is not null;
    }

    private bool CanSaveProjectCustomFieldValue()
    {
        return !IsBusy
            && !IsProjectBusy
            && SelectedProjectItemForFields is not null
            && SelectedProjectCustomField is not null;
    }

    private async Task LoadAsync(
        Guid? preferredIssueId = null,
        string? completionMessage = null,
        Guid? preferredProjectId = null,
        CancellationToken cancellationToken = default)
    {
        await _loadGate.WaitAsync(cancellationToken);

        try
        {
            IsBusy = true;
            CreateIssueCommand.NotifyCanExecuteChanged();
            CreateProjectCommand.NotifyCanExecuteChanged();
            ToggleSelectedIssueStateCommand.NotifyCanExecuteChanged();
            UpdateSelectedIssueCommand.NotifyCanExecuteChanged();
            DeleteSelectedIssueCommand.NotifyCanExecuteChanged();
            AddCommentCommand.NotifyCanExecuteChanged();
            AttachFileCommand.NotifyCanExecuteChanged();
            OpenAttachmentCommand.NotifyCanExecuteChanged();
            AddIssueRelationCommand.NotifyCanExecuteChanged();
            ScheduleReminderCommand.NotifyCanExecuteChanged();
            DismissReminderCommand.NotifyCanExecuteChanged();
            SaveIssueSearchCommand.NotifyCanExecuteChanged();
            ApplySavedIssueSearchCommand.NotifyCanExecuteChanged();
            SavePreferencesCommand.NotifyCanExecuteChanged();
            ExportSelectionCommand.NotifyCanExecuteChanged();
            SaveExportPresetCommand.NotifyCanExecuteChanged();
            ApplyExportPresetCommand.NotifyCanExecuteChanged();
            MoveProjectItemForwardCommand.NotifyCanExecuteChanged();
            MoveProjectItemBackwardCommand.NotifyCanExecuteChanged();
            AddProjectCustomFieldCommand.NotifyCanExecuteChanged();
            AddProjectSavedViewCommand.NotifyCanExecuteChanged();
            ApplyProjectSavedViewCommand.NotifyCanExecuteChanged();
            SaveProjectCustomFieldValueCommand.NotifyCanExecuteChanged();
            StatusMessage = "Loading Tracky workspace from the local SQLite file.";
            ProjectStatusMessage = "Loading Phase 2 projects from the local SQLite file.";

            var overview = await _workspaceService.GetOverviewAsync(cancellationToken);
            var projectSummaries = await _workspaceService.GetProjectsAsync(cancellationToken);
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
            _allMilestones.Clear();
            _allMilestones.AddRange(overview.Milestones);
            _allProjects.Clear();
            _allProjects.AddRange(projectSummaries.Select(static project => new ProjectSummaryViewModel(project)));
            RefreshIssueFilterOptions();
            RefreshWorkspaceReminders(overview.Reminders);
            RefreshExportPresets(overview.ExportPresets);
            RefreshSavedIssueSearches(overview.SavedIssueSearches);
            HydratePreferences(overview.Preferences);

            ApplyFilters(preferredIssueId);
            ApplyProjects(preferredProjectId ?? SelectedProject?.Id);
            StatusMessage = completionMessage
                ?? $"{overview.Issues.Count} issues synced from the local workspace.";
            ProjectStatusMessage = projectSummaries.Count == 0
                ? "No projects exist yet. Create a project or assign an issue to a project name."
                : $"{projectSummaries.Count} projects synced for Phase 2 views.";
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
            CreateProjectCommand.NotifyCanExecuteChanged();
            ToggleSelectedIssueStateCommand.NotifyCanExecuteChanged();
            UpdateSelectedIssueCommand.NotifyCanExecuteChanged();
            DeleteSelectedIssueCommand.NotifyCanExecuteChanged();
            AddCommentCommand.NotifyCanExecuteChanged();
            AttachFileCommand.NotifyCanExecuteChanged();
            OpenAttachmentCommand.NotifyCanExecuteChanged();
            AddIssueRelationCommand.NotifyCanExecuteChanged();
            ScheduleReminderCommand.NotifyCanExecuteChanged();
            DismissReminderCommand.NotifyCanExecuteChanged();
            SaveIssueSearchCommand.NotifyCanExecuteChanged();
            ApplySavedIssueSearchCommand.NotifyCanExecuteChanged();
            SavePreferencesCommand.NotifyCanExecuteChanged();
            ExportSelectionCommand.NotifyCanExecuteChanged();
            SaveExportPresetCommand.NotifyCanExecuteChanged();
            ApplyExportPresetCommand.NotifyCanExecuteChanged();
            MoveProjectItemForwardCommand.NotifyCanExecuteChanged();
            MoveProjectItemBackwardCommand.NotifyCanExecuteChanged();
            AddProjectCustomFieldCommand.NotifyCanExecuteChanged();
            AddProjectSavedViewCommand.NotifyCanExecuteChanged();
            ApplyProjectSavedViewCommand.NotifyCanExecuteChanged();
            SaveProjectCustomFieldValueCommand.NotifyCanExecuteChanged();
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

    private void StartProjectDetailLoad(Guid? projectId)
    {
        _projectLoadCts?.Cancel();
        _projectLoadCts?.Dispose();
        _projectLoadCts = null;

        if (projectId is null)
        {
            ClearProjectDetail();
            ProjectStatusMessage = "Select a project to load its board, table, calendar, and timeline views.";
            return;
        }

        ClearProjectDetail();
        var cancellationSource = new CancellationTokenSource();
        _projectLoadCts = cancellationSource;
        _ = LoadProjectDetailAsync(projectId.Value, cancellationSource.Token);
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

    private async Task LoadProjectDetailAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await _projectLoadGate.WaitAsync(CancellationToken.None);

        try
        {
            IsProjectBusy = true;
            MoveProjectItemForwardCommand.NotifyCanExecuteChanged();
            MoveProjectItemBackwardCommand.NotifyCanExecuteChanged();
            AddProjectCustomFieldCommand.NotifyCanExecuteChanged();
            AddProjectSavedViewCommand.NotifyCanExecuteChanged();
            ApplyProjectSavedViewCommand.NotifyCanExecuteChanged();
            SaveProjectCustomFieldValueCommand.NotifyCanExecuteChanged();
            ProjectStatusMessage = "Loading the selected project detail from the local workspace.";

            var detail = await _workspaceService.GetProjectDetailAsync(projectId, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (detail is null)
            {
                ClearProjectDetail();
                ProjectStatusMessage = "The selected project could not be found.";
                return;
            }

            ApplyProjectDetail(detail);
            ProjectStatusMessage = $"Loaded {detail.TableItems.Count} project items, {detail.CustomFields.Count} fields, and {detail.SavedViews.Count} saved views.";
        }
        catch (OperationCanceledException)
        {
            // 프로젝트 선택이 빠르게 바뀌면 이전 상세 조회는 조용히 취소한다.
        }
        catch (Exception exception)
        {
            ProjectStatusMessage = $"Project detail load failed: {exception.Message}";
        }
        finally
        {
            IsProjectBusy = false;
            MoveProjectItemForwardCommand.NotifyCanExecuteChanged();
            MoveProjectItemBackwardCommand.NotifyCanExecuteChanged();
            AddProjectCustomFieldCommand.NotifyCanExecuteChanged();
            AddProjectSavedViewCommand.NotifyCanExecuteChanged();
            ApplyProjectSavedViewCommand.NotifyCanExecuteChanged();
            SaveProjectCustomFieldValueCommand.NotifyCanExecuteChanged();
            _projectLoadGate.Release();
        }
    }

    private void ApplyFilters(Guid? preferredIssueId = null)
    {
        var searchQuery = IssueSearchParser.Parse(SearchText);
        var filteredIssues = SortIssues(
            _allIssues
                .Where(issue => MatchesScope(issue, ActiveScope))
                .Where(MatchesIssueDropdownFilters)
                .Where(issue => MatchesSearch(issue, searchQuery)),
            SelectedIssueSortOption)
            .ToList();

        VisibleIssues.Clear();
        foreach (var issue in filteredIssues)
        {
            VisibleIssues.Add(issue);
        }

        SelectedIssue = SelectPreferredIssue(filteredIssues, preferredIssueId);
        IssueListSummaryText = BuildIssueListSummary(filteredIssues.Count);
        ExportSelectionCommand.NotifyCanExecuteChanged();
    }

    private void ApplyProjects(Guid? preferredProjectId = null)
    {
        Projects.Clear();
        foreach (var project in _allProjects)
        {
            Projects.Add(project);
        }

        OnPropertyChanged(nameof(HasProjects));
        OnPropertyChanged(nameof(HasNoProjects));
        SelectedProject = SelectPreferredProject([.. Projects], preferredProjectId);
        RefreshSelectedRepositoryContent();
    }

    private void RefreshIssueFilterOptions()
    {
        // GitHub Issues의 헤더 드롭다운처럼, 현재 로컬 이슈에 존재하는 메타데이터만 필터 후보로 보여준다.
        RefreshOptionCollection(
            AvailableIssueAssigneeFilters,
            AnyIssueAssigneeFilter,
            _allIssues.Where(static issue => issue.HasAssignee).Select(static issue => issue.AssigneeText),
            UnassignedIssueFilter);
        RefreshOptionCollection(
            AvailableIssueLabelFilters,
            AnyIssueLabelFilter,
            _allIssues.SelectMany(static issue => issue.Labels));
        RefreshOptionCollection(
            AvailableIssueProjectFilters,
            AnyIssueProjectFilter,
            _allIssues.Where(static issue => issue.HasProject).Select(static issue => issue.ProjectText),
            NoProjectIssueFilter);
        RefreshOptionCollection(
            AvailableIssueMilestoneFilters,
            AnyIssueMilestoneFilter,
            _allIssues.Where(static issue => issue.HasMilestone).Select(static issue => issue.MilestoneText),
            NoMilestoneIssueFilter);
        RefreshOptionCollection(
            AvailableIssueTypeFilters,
            AnyIssueTypeFilter,
            _allIssues.Where(static issue => issue.HasIssueType).Select(static issue => issue.IssueTypeText));

        EnsureIssueFilterSelection(ref _selectedIssueAssigneeFilter, AnyIssueAssigneeFilter, AvailableIssueAssigneeFilters, nameof(SelectedIssueAssigneeFilter));
        EnsureIssueFilterSelection(ref _selectedIssueLabelFilter, AnyIssueLabelFilter, AvailableIssueLabelFilters, nameof(SelectedIssueLabelFilter));
        EnsureIssueFilterSelection(ref _selectedIssueProjectFilter, AnyIssueProjectFilter, AvailableIssueProjectFilters, nameof(SelectedIssueProjectFilter));
        EnsureIssueFilterSelection(ref _selectedIssueMilestoneFilter, AnyIssueMilestoneFilter, AvailableIssueMilestoneFilters, nameof(SelectedIssueMilestoneFilter));
        EnsureIssueFilterSelection(ref _selectedIssueTypeFilter, AnyIssueTypeFilter, AvailableIssueTypeFilters, nameof(SelectedIssueTypeFilter));
    }

    private void EnsureIssueFilterSelection(
        ref string selectedFilter,
        string defaultFilter,
        ObservableCollection<string> availableFilters,
        string propertyName)
    {
        if (availableFilters.Contains(selectedFilter))
        {
            return;
        }

        selectedFilter = defaultFilter;
        OnPropertyChanged(propertyName);
    }

    private static void RefreshOptionCollection(
        ObservableCollection<string> target,
        string defaultOption,
        IEnumerable<string> values,
        string? emptyOption = null)
    {
        target.Clear();
        target.Add(defaultOption);
        if (!string.IsNullOrWhiteSpace(emptyOption))
        {
            target.Add(emptyOption);
        }

        foreach (var value in values
                     .Where(static value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            target.Add(value);
        }
    }

    private void RefreshWorkspaceReminders(IReadOnlyList<IssueReminder> reminders)
    {
        WorkspaceReminders.Clear();
        foreach (var reminder in reminders)
        {
            WorkspaceReminders.Add(new IssueReminderViewModel(reminder));
        }

        OnPropertyChanged(nameof(HasWorkspaceReminders));
        OnPropertyChanged(nameof(HasNoWorkspaceReminders));
    }

    private void RefreshExportPresets(IReadOnlyList<ExportPreset> presets)
    {
        ExportPresets.Clear();
        foreach (var preset in presets)
        {
            ExportPresets.Add(new ExportPresetViewModel(preset));
        }

        OnPropertyChanged(nameof(HasExportPresets));
        OnPropertyChanged(nameof(HasNoExportPresets));
    }

    private void RefreshSavedIssueSearches(IReadOnlyList<SavedIssueSearch> savedSearches)
    {
        SavedIssueSearches.Clear();
        foreach (var savedSearch in savedSearches)
        {
            SavedIssueSearches.Add(new SavedIssueSearchViewModel(savedSearch));
        }

        OnPropertyChanged(nameof(HasSavedIssueSearches));
        OnPropertyChanged(nameof(HasNoSavedIssueSearches));
    }

    private void HydratePreferences(WorkspacePreferences preferences)
    {
        SelectedThemePreference = preferences.Theme;
        CompactDensityPreference = preferences.CompactDensity;
        ShortcutProfilePreference = preferences.ShortcutProfile;
    }

    private void SetIssueDetailPageActive(bool isActive)
    {
        if (_isIssueDetailPageActive == isActive)
        {
            return;
        }

        _isIssueDetailPageActive = isActive;
        OnIssueNavigationPropertiesChanged();
    }

    private void SetDescriptionEditMode(bool isEditMode)
    {
        if (_isDescriptionEditMode == isEditMode)
        {
            return;
        }

        _isDescriptionEditMode = isEditMode;
        OnPropertyChanged(nameof(IsDescriptionEditMode));
        OnPropertyChanged(nameof(IsDescriptionReadMode));
    }

    private void SetCommentPreviewMode(bool isPreviewMode)
    {
        if (_isCommentPreviewMode == isPreviewMode)
        {
            return;
        }

        _isCommentPreviewMode = isPreviewMode;
        OnPropertyChanged(nameof(IsCommentPreviewMode));
        OnPropertyChanged(nameof(IsCommentWriteMode));
    }

    private void OnDraftCommentPreviewChanged()
    {
        // Write/Preview 탭은 저장 전 원문을 실제 댓글 렌더링과 같은 경로로 보여 줘야 한다.
        // 그래서 본문과 포맷 중 하나라도 바뀌면 WebView 문서, fallback, 높이 추정을 함께 갱신한다.
        OnPropertyChanged(nameof(DraftCommentPreviewHtmlDocument));
        OnPropertyChanged(nameof(DraftCommentPreviewFallbackText));
        OnPropertyChanged(nameof(DraftCommentPreviewHeight));
    }

    private void OnIssueNavigationPropertiesChanged()
    {
        // 이슈 목록과 상세를 별도 화면 상태로 분리해 GitHub Issues처럼 행 선택 시 상세로 진입하고,
        // 뒤로 가기나 새 이슈 작성 준비 시에는 명확하게 목록 화면으로 돌아오게 한다.
        OnPropertyChanged(nameof(IsIssuesViewActive));
        OnPropertyChanged(nameof(IsIssueListViewVisible));
        OnPropertyChanged(nameof(IsIssueDetailViewActive));
        OnPropertyChanged(nameof(IsQuickCaptureVisible));
        OnPropertyChanged(nameof(IsIssueEditorVisible));
        OnPropertyChanged(nameof(IsIssueDetailPlaceholderVisible));
        OnPropertyChanged(nameof(IsSelectedIssueDetailVisible));
        OnPropertyChanged(nameof(IsRepositoryDashboardViewActive));
    }

    private void OnRepositoryTabPropertiesChanged()
    {
        // Repository 상세 탭은 GitHub 저장소 상단 탭처럼 한 번에 하나만 활성화한다.
        // Projects 탭 내부의 Board/Table/Calendar/Timeline 상태도 함께 알려야 XAML의 가시성 바인딩이 즉시 갱신된다.
        OnPropertyChanged(nameof(IsRepositoryIssuesTabActive));
        OnPropertyChanged(nameof(IsRepositoryMilestonesTabActive));
        OnPropertyChanged(nameof(IsRepositoryProjectsTabActive));
        OnPropertyChanged(nameof(IsRepositoryProjectBoardViewVisible));
        OnPropertyChanged(nameof(IsRepositoryProjectTableViewVisible));
        OnPropertyChanged(nameof(IsRepositoryProjectCalendarViewVisible));
        OnPropertyChanged(nameof(IsRepositoryProjectTimelineViewVisible));
    }

    private void OnRepositoryContentPropertiesChanged()
    {
        OnPropertyChanged(nameof(HasRepositoryIssues));
        OnPropertyChanged(nameof(HasNoRepositoryIssues));
        OnPropertyChanged(nameof(HasRepositoryMilestones));
        OnPropertyChanged(nameof(HasNoRepositoryMilestones));
        OnPropertyChanged(nameof(RepositoryIssueCountText));
        OnPropertyChanged(nameof(RepositoryMilestoneCountText));
        OnPropertyChanged(nameof(RepositoryProjectViewsText));
        OnPropertyChanged(nameof(RepositoryOpenIssuesText));
        OnPropertyChanged(nameof(RepositoryClosedIssuesText));
        OnPropertyChanged(nameof(RepositoryProjectsResultsText));
    }

    private void RefreshSelectedRepositoryContent()
    {
        RepositoryIssues.Clear();
        RepositoryMilestones.Clear();

        if (SelectedProject is null)
        {
            OnRepositoryContentPropertiesChanged();
            return;
        }

        var repositorySearchQuery = IssueSearchParser.Parse(RepositoryIssueSearchText);
        var repositoryIssues = _allIssues
            .Where(issue => issue.HasProject
                && issue.ProjectText.Equals(SelectedProject.Name, StringComparison.OrdinalIgnoreCase))
            .Where(issue => MatchesSearch(issue, repositorySearchQuery))
            .OrderByDescending(static issue => issue.Issue.UpdatedAtUtc)
            .ThenByDescending(static issue => issue.Number)
            .ToArray();

        foreach (var issue in repositoryIssues)
        {
            RepositoryIssues.Add(issue);
        }

        // MilestoneSummary는 워크스페이스 전체 집계이므로, Repository 화면에서는 선택한 Repository의 이슈만 다시 묶는다.
        // 마감일은 같은 이름의 워크스페이스 마일스톤에서 가져와 GitHub식 Repository별 진행률과 due date를 동시에 보여 준다.
        var milestoneDueDates = _allMilestones.ToDictionary(
            static milestone => milestone.Name,
            static milestone => milestone.DueDate,
            StringComparer.OrdinalIgnoreCase);
        var repositoryMilestones = repositoryIssues
            .Where(static issue => issue.HasMilestone)
            .GroupBy(static issue => issue.MilestoneText, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                milestoneDueDates.TryGetValue(group.Key, out var dueDate);
                return new RepositoryMilestoneViewModel(
                    group.Key,
                    dueDate,
                    group.Count(static issue => issue.IsOpen),
                    group.Count(static issue => issue.IsClosed));
            })
            .OrderBy(static milestone => milestone.DueDate is null)
            .ThenBy(static milestone => milestone.DueDate)
            .ThenBy(static milestone => milestone.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var milestone in repositoryMilestones)
        {
            RepositoryMilestones.Add(milestone);
        }

        OnRepositoryContentPropertiesChanged();
    }

    private void ApplyProjectDetail(ProjectDetail detail)
    {
        _selectedProjectDetail = detail;
        RefreshProjectArrangementOptions(detail.CustomFields);
        var selectedItemId = SelectedProjectItemForFields?.ProjectItemId;
        var selectedFieldId = SelectedProjectCustomField?.Field.Id;
        var filteredItems = detail.TableItems
            .Where(item => MatchesProjectFilter(item, ProjectFilterText))
            .ToArray();
        var filteredItemIds = filteredItems
            .Select(static item => item.ProjectItemId)
            .ToHashSet();
        var sortedTableItems = SortProjectItems(filteredItems, ProjectSortText)
            .ToArray();

        ProjectBoardColumns.Clear();
        foreach (var column in detail.BoardColumns)
        {
            var filteredColumnItems = SortProjectItems(
                    column.Items.Where(item => filteredItemIds.Contains(item.ProjectItemId)),
                    ProjectSortText)
                .ToArray();
            ProjectBoardColumns.Add(
                new ProjectBoardColumnViewModel(
                    new ProjectBoardColumn(
                        column.Name,
                        filteredColumnItems.Count(static item => item.State == IssueWorkflowState.Open),
                        filteredColumnItems)));
        }

        ProjectTableItems.Clear();
        foreach (var item in sortedTableItems)
        {
            ProjectTableItems.Add(new ProjectIssueItemViewModel(item));
        }
        ApplyProjectGroups(ProjectTableGroups, sortedTableItems, ProjectGroupByText);

        ProjectCalendarItems.Clear();
        var sortedCalendarItems = SortProjectItems(
                detail.CalendarItems.Where(item => filteredItemIds.Contains(item.ProjectItemId)),
                ProjectSortText)
            .ToArray();
        foreach (var item in sortedCalendarItems)
        {
            ProjectCalendarItems.Add(new ProjectIssueItemViewModel(item));
        }
        ApplyProjectGroups(ProjectCalendarGroups, sortedCalendarItems, ProjectGroupByText);

        ProjectTimelineItems.Clear();
        var sortedTimelineItems = SortProjectItems(
                detail.TimelineItems.Where(item => filteredItemIds.Contains(item.ProjectItemId)),
                ProjectSortText)
            .ToArray();
        foreach (var item in sortedTimelineItems)
        {
            ProjectTimelineItems.Add(new ProjectIssueItemViewModel(item));
        }
        ApplyProjectGroups(ProjectTimelineGroups, sortedTimelineItems, ProjectGroupByText);

        ProjectCustomFields.Clear();
        foreach (var field in detail.CustomFields)
        {
            ProjectCustomFields.Add(new ProjectCustomFieldViewModel(field));
        }

        ProjectSavedViews.Clear();
        foreach (var savedView in detail.SavedViews)
        {
            ProjectSavedViews.Add(new ProjectSavedViewViewModel(savedView));
        }

        SelectedProjectItemForFields = selectedItemId is null
            ? ProjectTableItems.FirstOrDefault()
            : ProjectTableItems.FirstOrDefault(item => item.ProjectItemId == selectedItemId.Value)
                ?? ProjectTableItems.FirstOrDefault();
        SelectedProjectCustomField = selectedFieldId is null
            ? ProjectCustomFields.FirstOrDefault()
            : ProjectCustomFields.FirstOrDefault(field => field.Field.Id == selectedFieldId.Value)
                ?? ProjectCustomFields.FirstOrDefault();
        OnPropertyChanged(nameof(HasProjectDetail));
        OnRepositoryContentPropertiesChanged();
    }

    private void ClearProjectDetail()
    {
        _selectedProjectDetail = null;
        ProjectBoardColumns.Clear();
        ProjectTableItems.Clear();
        ProjectTableGroups.Clear();
        ProjectCalendarItems.Clear();
        ProjectCalendarGroups.Clear();
        ProjectTimelineItems.Clear();
        ProjectTimelineGroups.Clear();
        ProjectCustomFields.Clear();
        ProjectSavedViews.Clear();
        RefreshProjectArrangementOptions([]);
        SelectedProjectItemForFields = null;
        SelectedProjectCustomField = null;
        OnPropertyChanged(nameof(HasProjectDetail));
        OnRepositoryContentPropertiesChanged();
    }

    private void HydrateCustomFieldValueDraft()
    {
        if (SelectedProjectItemForFields is null || SelectedProjectCustomField is null)
        {
            DraftCustomFieldValue = string.Empty;
            return;
        }

        DraftCustomFieldValue = SelectedProjectItemForFields.Item.CustomFieldValues.TryGetValue(
            SelectedProjectCustomField.Name,
            out var value)
            ? value
            : string.Empty;
    }

    private void HydrateEditDraft(IssueDetailViewModel? detail)
    {
        // 선택한 상세 이슈가 바뀔 때 편집 초안을 즉시 재구성해야,
        // 저장 버튼이 이전 이슈의 메타데이터를 실수로 다시 쓰지 않는다.
        if (detail is null)
        {
            EditTitle = string.Empty;
            EditDescription = string.Empty;
            EditDescriptionFormat = IssueContentFormat.Markdown;
            EditAssignee = string.Empty;
            EditPriority = IssuePriority.None;
            EditDueDate = null;
            EditProjectName = string.Empty;
            EditLabels = string.Empty;
            EditMilestoneName = string.Empty;
            EditIssueTypeName = string.Empty;
            SelectedCloseReason = IssueStateReason.Completed;
            return;
        }

        var summary = detail.Summary.Issue;
        EditTitle = summary.Title;
        EditDescription = detail.Detail.Description;
        EditDescriptionFormat = detail.Detail.DescriptionFormat;
        EditAssignee = summary.AssigneeDisplayName ?? string.Empty;
        EditPriority = summary.Priority;
        EditDueDate = summary.DueDate is null
            ? null
            : new DateTimeOffset(summary.DueDate.Value.ToDateTime(TimeOnly.MinValue));
        EditProjectName = summary.ProjectName ?? string.Empty;
        EditLabels = string.Join(", ", summary.Labels);
        EditMilestoneName = summary.MilestoneName ?? string.Empty;
        EditIssueTypeName = summary.IssueTypeName ?? string.Empty;
        SelectedCloseReason = summary.StateReason == IssueStateReason.None
            ? IssueStateReason.Completed
            : summary.StateReason;
    }

    private ExportOptions BuildCurrentExportOptions()
    {
        // UI는 현재 선택/필터 상태만 알고, 실제 Markdown/HTML/패키지 생성 규칙은 저장소가 담당한다.
        // 이 경계를 유지해야 export를 통합 테스트로 검증하기 쉽다.
        return new ExportOptions(
            DraftExportScope,
            DraftExportFormat,
            DraftExportBodyFormat,
            [.. VisibleIssues.Select(static issue => issue.Id)],
            SelectedIssue?.Id,
            SelectedProject?.Id,
            DraftExportIncludeComments,
            DraftExportIncludeActivity,
            DraftExportIncludeAttachments,
            DraftExportIncludeClosedIssues,
            null);
    }

    private static bool MatchesScope(IssueCardViewModel issue, IssueFilterScope scope) => scope switch
    {
        IssueFilterScope.Open => issue.IsOpen,
        IssueFilterScope.Closed => issue.IsClosed,
        _ => true,
    };

    private bool MatchesIssueDropdownFilters(IssueCardViewModel issue)
    {
        return MatchesSelectedAssigneeFilter(issue)
            && MatchesSelectedLabelFilter(issue)
            && MatchesSelectedProjectFilter(issue)
            && MatchesSelectedMilestoneFilter(issue)
            && MatchesSelectedTypeFilter(issue);
    }

    private bool MatchesSelectedAssigneeFilter(IssueCardViewModel issue)
    {
        return SelectedIssueAssigneeFilter == AnyIssueAssigneeFilter
            || SelectedIssueAssigneeFilter == UnassignedIssueFilter && !issue.HasAssignee
            || issue.HasAssignee && issue.AssigneeText.Equals(SelectedIssueAssigneeFilter, StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesSelectedLabelFilter(IssueCardViewModel issue)
    {
        return SelectedIssueLabelFilter == AnyIssueLabelFilter
            || issue.Labels.Any(label => label.Equals(SelectedIssueLabelFilter, StringComparison.OrdinalIgnoreCase));
    }

    private bool MatchesSelectedProjectFilter(IssueCardViewModel issue)
    {
        return SelectedIssueProjectFilter == AnyIssueProjectFilter
            || SelectedIssueProjectFilter == NoProjectIssueFilter && !issue.HasProject
            || issue.HasProject && issue.ProjectText.Equals(SelectedIssueProjectFilter, StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesSelectedMilestoneFilter(IssueCardViewModel issue)
    {
        return SelectedIssueMilestoneFilter == AnyIssueMilestoneFilter
            || SelectedIssueMilestoneFilter == NoMilestoneIssueFilter && !issue.HasMilestone
            || issue.HasMilestone && issue.MilestoneText.Equals(SelectedIssueMilestoneFilter, StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesSelectedTypeFilter(IssueCardViewModel issue)
    {
        return SelectedIssueTypeFilter == AnyIssueTypeFilter
            || issue.HasIssueType && issue.IssueTypeText.Equals(SelectedIssueTypeFilter, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<IssueCardViewModel> SortIssues(
        IEnumerable<IssueCardViewModel> issues,
        string sortOption)
    {
        return sortOption switch
        {
            "Oldest updated" => issues.OrderBy(static issue => issue.Issue.UpdatedAtUtc),
            "Most commented" => issues
                .OrderByDescending(static issue => issue.Issue.CommentCount)
                .ThenByDescending(static issue => issue.Issue.UpdatedAtUtc),
            "Least commented" => issues
                .OrderBy(static issue => issue.Issue.CommentCount)
                .ThenByDescending(static issue => issue.Issue.UpdatedAtUtc),
            "Highest priority" => issues
                .OrderBy(static issue => GetPrioritySortRank(issue.Issue.Priority))
                .ThenByDescending(static issue => issue.Issue.UpdatedAtUtc),
            "Due date" => issues
                .OrderBy(static issue => issue.Issue.DueDate ?? DateOnly.MaxValue)
                .ThenByDescending(static issue => issue.Issue.UpdatedAtUtc),
            _ => issues.OrderByDescending(static issue => issue.Issue.UpdatedAtUtc),
        };
    }

    private static int GetPrioritySortRank(IssuePriority priority) => priority switch
    {
        IssuePriority.Critical => 0,
        IssuePriority.High => 1,
        IssuePriority.Medium => 2,
        IssuePriority.Low => 3,
        _ => 4,
    };

    private string BuildIssueListSummary(int visibleIssueCount)
    {
        var terms = new List<string>
        {
            "is:issue",
            ActiveScope switch
            {
                IssueFilterScope.Open => "state:open",
                IssueFilterScope.Closed => "state:closed",
                _ => "state:all",
            },
        };

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            terms.Add(SearchText.Trim());
        }

        AddFilterPreview(terms, "assignee", SelectedIssueAssigneeFilter, AnyIssueAssigneeFilter, UnassignedIssueFilter, "none");
        AddFilterPreview(terms, "label", SelectedIssueLabelFilter, AnyIssueLabelFilter);
        AddFilterPreview(terms, "project", SelectedIssueProjectFilter, AnyIssueProjectFilter, NoProjectIssueFilter, "none");
        AddFilterPreview(terms, "milestone", SelectedIssueMilestoneFilter, AnyIssueMilestoneFilter, NoMilestoneIssueFilter, "none");
        AddFilterPreview(terms, "type", SelectedIssueTypeFilter, AnyIssueTypeFilter);

        return $"{visibleIssueCount} issues matching {string.Join(" ", terms)}";
    }

    private static void AddFilterPreview(
        List<string> terms,
        string key,
        string selectedValue,
        string defaultValue,
        string? emptyDisplayValue = null,
        string? emptyQueryValue = null)
    {
        if (selectedValue == defaultValue)
        {
            return;
        }

        var queryValue = selectedValue == emptyDisplayValue
            ? emptyQueryValue ?? selectedValue
            : selectedValue.Contains(' ')
                ? $"\"{selectedValue}\""
                : selectedValue;

        terms.Add($"{key}:{queryValue}");
    }

    private static bool MatchesSearch(IssueCardViewModel issue, IssueSearchQuery searchQuery)
    {
        if (searchQuery.TextTerms.Count == 0 && searchQuery.Operators.Count == 0)
        {
            return true;
        }

        return searchQuery.TextTerms.All(term => MatchesFreeText(issue, term))
            && searchQuery.Operators.All(searchOperator => MatchesIssueOperator(issue, searchOperator));
    }

    private static bool MatchesFreeText(IssueCardViewModel issue, string term)
    {
        return issue.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
            || issue.NumberText.Contains(term, StringComparison.OrdinalIgnoreCase)
            || issue.AssigneeText.Contains(term, StringComparison.OrdinalIgnoreCase)
            || issue.ProjectText.Contains(term, StringComparison.OrdinalIgnoreCase)
            || issue.MilestoneText.Contains(term, StringComparison.OrdinalIgnoreCase)
            || issue.IssueTypeText.Contains(term, StringComparison.OrdinalIgnoreCase)
            || issue.PriorityText.Contains(term, StringComparison.OrdinalIgnoreCase)
            || issue.StateDetailText.Contains(term, StringComparison.OrdinalIgnoreCase)
            || issue.Labels.Any(label => label.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesIssueOperator(IssueCardViewModel issue, IssueSearchOperator searchOperator)
    {
        var matches = searchOperator.Key.ToLowerInvariant() switch
        {
            "is" => MatchesIssueStateOperator(issue, searchOperator.Value),
            "label" => issue.Labels.Any(label => label.Contains(searchOperator.Value, StringComparison.OrdinalIgnoreCase)),
            "assignee" => MatchesAssigneeOperator(issue, searchOperator.Value),
            "project" => issue.ProjectText.Contains(searchOperator.Value, StringComparison.OrdinalIgnoreCase),
            "milestone" => issue.MilestoneText.Contains(searchOperator.Value, StringComparison.OrdinalIgnoreCase),
            "type" => issue.IssueTypeText.Equals(searchOperator.Value, StringComparison.OrdinalIgnoreCase)
                || issue.IssueTypeText.Contains(searchOperator.Value, StringComparison.OrdinalIgnoreCase),
            "priority" => FormatPriorityForFilter(issue.Issue.Priority).Equals(searchOperator.Value, StringComparison.OrdinalIgnoreCase),
            "reason" => FormatStateReasonForFilter(issue.Issue.StateReason).Equals(searchOperator.Value.Replace("-", "_", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase),
            "due" => MatchesDueOperator(issue, searchOperator.Value),
            "has" => MatchesHasOperator(issue, searchOperator.Value),
            "no" => !MatchesHasOperator(issue, searchOperator.Value),
            _ => MatchesFreeText(issue, $"{searchOperator.Key}:{searchOperator.Value}"),
        };

        return searchOperator.IsNegated ? !matches : matches;
    }

    private static bool MatchesIssueStateOperator(IssueCardViewModel issue, string value)
    {
        return value.Equals("open", StringComparison.OrdinalIgnoreCase)
            ? issue.IsOpen
            : value.Equals("closed", StringComparison.OrdinalIgnoreCase)
                ? issue.IsClosed
                : value.Equals("overdue", StringComparison.OrdinalIgnoreCase)
                    ? issue.IsOverdue
                    : value.Equals("due-today", StringComparison.OrdinalIgnoreCase) && issue.IsDueToday;
    }

    private static bool MatchesAssigneeOperator(IssueCardViewModel issue, string value)
    {
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return !issue.HasAssignee;
        }

        if (value.Equals("me", StringComparison.OrdinalIgnoreCase))
        {
            return issue.HasAssignee;
        }

        return issue.AssigneeText.Contains(value, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesDueOperator(IssueCardViewModel issue, string value)
    {
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return !issue.HasDueDate;
        }

        if (value.Equals("today", StringComparison.OrdinalIgnoreCase))
        {
            return issue.IsDueToday;
        }

        if (value.Equals("overdue", StringComparison.OrdinalIgnoreCase))
        {
            return issue.IsOverdue;
        }

        if (value.Equals("upcoming", StringComparison.OrdinalIgnoreCase) || value.Equals("soon", StringComparison.OrdinalIgnoreCase))
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            return issue.Issue.DueDate is DateOnly dueDate
                && issue.IsOpen
                && dueDate > today
                && dueDate <= today.AddDays(7);
        }

        return DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate)
            && issue.Issue.DueDate == parsedDate;
    }

    private static bool MatchesHasOperator(IssueCardViewModel issue, string value)
    {
        var normalizedValue = value.Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase);
        return normalizedValue.ToLowerInvariant() switch
        {
            "duedate" => issue.HasDueDate,
            "assignee" => issue.HasAssignee,
            "project" => issue.HasProject,
            "label" or "labels" => issue.HasLabels,
            "attachment" or "attachments" => issue.HasAttachments,
            "comment" or "comments" => issue.HasComments,
            _ => false,
        };
    }

    private static bool MatchesProjectFilter(ProjectIssueItem item, string rawFilter)
    {
        var tokens = IssueSearchParser.Tokenize(rawFilter);
        if (tokens.Count == 0)
        {
            return true;
        }

        return tokens.All(token => MatchesProjectFilterToken(item, token));
    }

    private static bool MatchesProjectFilterToken(ProjectIssueItem item, string token)
    {
        var isNegated = token[0] == '-' && token.Length > 1;
        var normalizedToken = isNegated ? token[1..] : token;
        var matches = MatchesProjectFilterTokenCore(item, normalizedToken.Trim('"'));

        return isNegated ? !matches : matches;
    }

    private static bool MatchesProjectFilterTokenCore(ProjectIssueItem item, string token)
    {
        if (token.Equals("is:open", StringComparison.OrdinalIgnoreCase))
        {
            return item.State == IssueWorkflowState.Open;
        }

        if (token.Equals("is:closed", StringComparison.OrdinalIgnoreCase))
        {
            return item.State == IssueWorkflowState.Closed;
        }

        if (token.Equals("has:due-date", StringComparison.OrdinalIgnoreCase))
        {
            return item.DueDate is not null;
        }

        var separatorIndex = token.IndexOf(':', StringComparison.Ordinal);
        if (separatorIndex > 0)
        {
            var key = token[..separatorIndex];
            var value = token[(separatorIndex + 1)..];
            return MatchesProjectFilterOperator(item, key, value);
        }

        return item.Title.Contains(token, StringComparison.OrdinalIgnoreCase)
            || item.IssueNumber.ToString(CultureInfo.InvariantCulture).Contains(token, StringComparison.OrdinalIgnoreCase)
            || item.BoardColumn.Contains(token, StringComparison.OrdinalIgnoreCase)
            || item.CustomFieldValues.Values.Any(value => value.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesProjectFilterOperator(ProjectIssueItem item, string key, string value)
    {
        if (key.Equals("priority", StringComparison.OrdinalIgnoreCase))
        {
            return FormatPriorityForFilter(item.Priority).Equals(value, StringComparison.OrdinalIgnoreCase);
        }

        if (key.Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            return item.BoardColumn.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Equals(value.Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase), StringComparison.OrdinalIgnoreCase)
                || item.BoardColumn.Equals(value, StringComparison.OrdinalIgnoreCase);
        }

        if (key.Equals("assignee", StringComparison.OrdinalIgnoreCase))
        {
            return (item.AssigneeDisplayName ?? string.Empty).Contains(value, StringComparison.OrdinalIgnoreCase);
        }

        if (key.Equals("reason", StringComparison.OrdinalIgnoreCase))
        {
            return FormatStateReasonForFilter(item.StateReason)
                .Equals(value.Replace("-", "_", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
        }

        if (key.Equals("due", StringComparison.OrdinalIgnoreCase))
        {
            return MatchesProjectDueOperator(item, value);
        }

        return item.CustomFieldValues.TryGetValue(key, out var fieldValue)
            && fieldValue.Contains(value, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesProjectDueOperator(ProjectIssueItem item, string value)
    {
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return item.DueDate is null;
        }

        if (item.DueDate is not DateOnly dueDate)
        {
            return false;
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        if (value.Equals("today", StringComparison.OrdinalIgnoreCase))
        {
            return dueDate == today;
        }

        if (value.Equals("overdue", StringComparison.OrdinalIgnoreCase))
        {
            return item.State == IssueWorkflowState.Open && dueDate < today;
        }

        if (value.Equals("upcoming", StringComparison.OrdinalIgnoreCase) || value.Equals("soon", StringComparison.OrdinalIgnoreCase))
        {
            return item.State == IssueWorkflowState.Open && dueDate > today && dueDate <= today.AddDays(7);
        }

        return DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate)
            && dueDate == parsedDate;
    }

    private static void ApplyProjectGroups(
        ObservableCollection<ProjectIssueGroupViewModel> target,
        IReadOnlyList<ProjectIssueItem> items,
        string groupByField)
    {
        target.Clear();
        if (!IsProjectGroupingEnabled(groupByField))
        {
            target.Add(new ProjectIssueGroupViewModel("All issues", items));
            return;
        }

        // 그룹핑은 저장된 뷰가 적용될 때도 같은 결과가 나와야 하므로,
        // 화면별 컬렉션이 아니라 ProjectIssueItem의 공통 메타데이터만 기준으로 묶는다.
        var groups = items
            .Select(item => new
            {
                Item = item,
                Group = CreateProjectGroupKey(item, groupByField),
            })
            .GroupBy(entry => entry.Group.Name)
            .OrderBy(static group => group.Min(entry => entry.Group.Rank))
            .ThenBy(static group => group.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            target.Add(new ProjectIssueGroupViewModel(group.Key, [.. group.Select(static entry => entry.Item)]));
        }
    }

    private void RefreshProjectArrangementOptions(IReadOnlyList<ProjectCustomField> customFields)
    {
        var customFieldNames = customFields
            .Select(static field => field.Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        ReplaceStringCollection(
            AvailableProjectSortFields,
            [.. BaseProjectSortFields.Concat(customFieldNames).Distinct(StringComparer.OrdinalIgnoreCase)]);
        ReplaceStringCollection(
            AvailableProjectGroupFields,
            [.. BaseProjectGroupFields.Concat(customFieldNames).Distinct(StringComparer.OrdinalIgnoreCase)]);

        if (!ContainsArrangementOption(AvailableProjectSortFields, ProjectSortText))
        {
            _projectSortText = DefaultProjectSortField;
            OnPropertyChanged(nameof(ProjectSortText));
            OnPropertyChanged(nameof(ProjectArrangementText));
        }

        if (!ContainsArrangementOption(AvailableProjectGroupFields, ProjectGroupByText))
        {
            _projectGroupByText = NoProjectGroupingField;
            OnPropertyChanged(nameof(ProjectGroupByText));
            OnPropertyChanged(nameof(ProjectArrangementText));
        }
    }

    private static void ReplaceStringCollection(ObservableCollection<string> target, IReadOnlyList<string> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }

    private static bool ContainsArrangementOption(IEnumerable<string> options, string value)
    {
        return options.Any(option => option.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<ProjectIssueItem> SortProjectItems(
        IEnumerable<ProjectIssueItem> items,
        string sortByField)
    {
        // 정렬은 서비스의 기본 조회 순서와 별개로 화면에서 즉시 바뀌어야 하므로,
        // 저장된 상세 데이터를 재조회하지 않고 ViewModel에서 안정적으로 재배열한다.
        var normalizedSort = NormalizeArrangementField(sortByField);
        return normalizedSort switch
        {
            "boardposition" => items
                .OrderBy(static item => GetBoardColumnRank(item.BoardColumn))
                .ThenBy(static item => item.SortOrder)
                .ThenBy(static item => item.IssueNumber),
            "issuenumber" => items.OrderBy(static item => item.IssueNumber),
            "title" => items
                .OrderBy(static item => item.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.IssueNumber),
            "priority" => items
                .OrderBy(static item => GetPriorityRank(item.Priority))
                .ThenBy(static item => item.IssueNumber),
            "assignee" => items
                .OrderBy(static item => string.IsNullOrWhiteSpace(item.AssigneeDisplayName) ? 1 : 0)
                .ThenBy(static item => item.AssigneeDisplayName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.IssueNumber),
            "duedate" => items
                .OrderBy(static item => item.DueDate ?? DateOnly.MaxValue)
                .ThenBy(static item => item.IssueNumber),
            "updated" => items
                .OrderByDescending(static item => item.UpdatedAtUtc)
                .ThenBy(static item => item.IssueNumber),
            "status" => items
                .OrderBy(static item => GetBoardColumnRank(item.BoardColumn))
                .ThenBy(static item => item.IssueNumber),
            _ => SortProjectItemsByCustomField(items, sortByField),
        };
    }

    private static IEnumerable<ProjectIssueItem> SortProjectItemsByCustomField(
        IEnumerable<ProjectIssueItem> items,
        string sortByField)
    {
        return items
            .OrderBy(item => string.IsNullOrWhiteSpace(GetCustomFieldValue(item, sortByField)) ? 1 : 0)
            .ThenBy(item => GetCustomFieldValue(item, sortByField), StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.IssueNumber);
    }

    private static ProjectGroupKey CreateProjectGroupKey(ProjectIssueItem item, string groupByField)
    {
        var normalizedGroup = NormalizeArrangementField(groupByField);
        return normalizedGroup switch
        {
            "status" => new ProjectGroupKey(item.BoardColumn, GetBoardColumnRank(item.BoardColumn)),
            "state" => item.State == IssueWorkflowState.Open
                ? new ProjectGroupKey("Open", 0)
                : new ProjectGroupKey($"Closed as {FormatStateReason(item.StateReason)}", 1),
            "priority" => new ProjectGroupKey(FormatPriorityForDisplay(item.Priority), GetPriorityRank(item.Priority)),
            "assignee" => string.IsNullOrWhiteSpace(item.AssigneeDisplayName)
                ? new ProjectGroupKey("Unassigned", int.MaxValue)
                : new ProjectGroupKey(item.AssigneeDisplayName, 0),
            "duedate" => item.DueDate is null
                ? new ProjectGroupKey("No due date", int.MaxValue)
                : new ProjectGroupKey($"Due {item.DueDate:MMM dd}", item.DueDate.Value.DayNumber),
            _ => CreateCustomFieldGroupKey(item, groupByField),
        };
    }

    private static ProjectGroupKey CreateCustomFieldGroupKey(ProjectIssueItem item, string groupByField)
    {
        var value = GetCustomFieldValue(item, groupByField);
        return string.IsNullOrWhiteSpace(value)
            ? new ProjectGroupKey($"No {groupByField}", int.MaxValue)
            : new ProjectGroupKey(value, 0);
    }

    private static bool IsProjectGroupingEnabled(string? groupByField)
    {
        return !string.IsNullOrWhiteSpace(groupByField)
            && !NormalizeArrangementField(groupByField).Equals(
                NormalizeArrangementField(NoProjectGroupingField),
                StringComparison.Ordinal);
    }

    private static string GetCustomFieldValue(ProjectIssueItem item, string fieldName)
    {
        return item.CustomFieldValues.FirstOrDefault(
            pair => pair.Key.Equals(fieldName, StringComparison.OrdinalIgnoreCase)).Value ?? string.Empty;
    }

    private static int GetBoardColumnRank(string boardColumn)
    {
        return boardColumn.ToLowerInvariant() switch
        {
            "to do" => 0,
            "in progress" => 1,
            "done" => 2,
            _ => 3,
        };
    }

    private static int GetPriorityRank(IssuePriority priority) => priority switch
    {
        IssuePriority.Critical => 0,
        IssuePriority.High => 1,
        IssuePriority.Medium => 2,
        IssuePriority.Low => 3,
        _ => 4,
    };

    private static string FormatPriorityForDisplay(IssuePriority priority) => priority switch
    {
        IssuePriority.Critical => "Critical",
        IssuePriority.High => "High",
        IssuePriority.Medium => "Medium",
        IssuePriority.Low => "Low",
        _ => "No priority",
    };

    private static string NormalizeArrangementField(string? value)
    {
        return new string((value ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
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
            return filteredIssues.FirstOrDefault(issue => issue.Id == preferredIssueId.Value);
        }

        if (SelectedIssue is not null)
        {
            return filteredIssues.FirstOrDefault(issue => issue.Id == SelectedIssue.Id);
        }

        return null;
    }

    private ProjectSummaryViewModel? SelectPreferredProject(
        List<ProjectSummaryViewModel> projects,
        Guid? preferredProjectId)
    {
        if (projects.Count == 0)
        {
            return null;
        }

        if (preferredProjectId is not null)
        {
            return projects.FirstOrDefault(project => project.Id == preferredProjectId.Value)
                ?? projects[0];
        }

        if (SelectedProject is not null)
        {
            return projects.FirstOrDefault(project => project.Id == SelectedProject.Id)
                ?? projects[0];
        }

        return projects[0];
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

    private static string FormatPriorityForFilter(IssuePriority priority) => priority switch
    {
        IssuePriority.Critical => "critical",
        IssuePriority.High => "high",
        IssuePriority.Medium => "medium",
        IssuePriority.Low => "low",
        _ => "none",
    };

    private static string FormatStateReasonForFilter(IssueStateReason reason) => reason switch
    {
        IssueStateReason.Completed => "completed",
        IssueStateReason.NotPlanned => "not_planned",
        IssueStateReason.Duplicate => "duplicate",
        _ => "none",
    };

    private sealed record ProjectGroupKey(string Name, int Rank);
}
