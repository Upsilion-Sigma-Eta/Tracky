using System.Collections.ObjectModel;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Tracky.Core.Exports;
using Tracky.Core.Issues;
using Tracky.Core.Preferences;
using Tracky.Core.Projects;
using Tracky.Core.Reminders;
using Tracky.Core.Search;
using Tracky.Core.Services;
using Tracky.Core.Workspaces;

namespace Tracky.Infrastructure.Persistence;

public sealed class SqliteTrackyWorkspaceService(TrackyWorkspacePathProvider pathProvider) : ITrackyWorkspaceService
{
    private const string BoardColumnTodo = "To do";
    private const string BoardColumnInProgress = "In progress";
    private const string BoardColumnDone = "Done";

    private static readonly string[] BoardColumns =
    [
        BoardColumnTodo,
        BoardColumnInProgress,
        BoardColumnDone,
    ];

    private static readonly string[] SeedLabels =
    [
        "foundation",
        "ux",
        "desktop",
        "local-first",
        "accessibility",
        "priority:high",
    ];

    private static readonly string[] LabelColors =
    [
        "#2F81F7",
        "#238636",
        "#BF8700",
        "#8957E5",
        "#D1242F",
        "#0E7490",
    ];

    private readonly TrackyWorkspacePathProvider _pathProvider = pathProvider;

    public SqliteTrackyWorkspaceService()
        : this(new TrackyWorkspacePathProvider())
    {
    }

    public async Task<WorkspaceOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenInitializedConnectionAsync(cancellationToken);
        var workspace = await GetWorkspaceAsync(connection, cancellationToken);
        var issues = await GetIssuesAsync(connection, cancellationToken);
        var metrics = IssueOverviewCalculator.Build(issues, DateOnly.FromDateTime(DateTime.Now));
        var reminders = await GetActiveRemindersAsync(connection, cancellationToken);
        var exportPresets = await GetExportPresetsAsync(connection, cancellationToken);
        var savedSearches = await GetSavedIssueSearchesAsync(connection, cancellationToken);
        var milestones = await GetMilestonesAsync(connection, cancellationToken);
        var issueTypes = await GetIssueTypesAsync(connection, cancellationToken);
        var preferences = await GetWorkspacePreferencesAsync(connection, cancellationToken);

        return new WorkspaceOverview(
            workspace.Name,
            workspace.Description,
            _pathProvider.GetDatabasePath(),
            metrics,
            issues,
            reminders,
            exportPresets,
            savedSearches,
            milestones,
            issueTypes,
            preferences);
    }

    public async Task<IssueDetail?> GetIssueDetailAsync(Guid issueId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenInitializedConnectionAsync(cancellationToken);
        var summary = await GetIssueByIdAsync(connection, issueId, cancellationToken);
        if (summary is null)
        {
            return null;
        }

        var description = await GetIssueDescriptionAsync(connection, issueId, cancellationToken);
        var comments = await GetIssueCommentsAsync(connection, issueId, cancellationToken);
        var attachments = await GetIssueAttachmentsAsync(connection, issueId, cancellationToken);
        var activity = await GetIssueActivityAsync(connection, issueId, cancellationToken);
        var reminders = await GetIssueRemindersAsync(connection, issueId, cancellationToken);
        var relations = await GetIssueRelationsAsync(connection, issueId, cancellationToken);

        return new IssueDetail(summary, description, comments, attachments, activity, reminders, relations);
    }

    public async Task<IssueListItem> CreateIssueAsync(
        CreateIssueInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Title);

        await using var connection = await OpenInitializedConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var workspace = await GetWorkspaceAsync(connection, cancellationToken);
        var issueId = Guid.NewGuid();
        var issueNumber = await GetNextIssueNumberAsync(connection, transaction, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        var createIssueCommand = connection.CreateCommand();
        createIssueCommand.Transaction = transaction;
        createIssueCommand.CommandText =
            """
            INSERT INTO issues (
                id,
                workspace_id,
                issue_number,
                title,
                description,
                state,
                state_reason,
                priority,
                assignee_display_name,
                due_date,
                project_name,
                milestone_name,
                issue_type_name,
                comment_count,
                attachment_count,
                updated_utc,
                created_utc
            ) VALUES (
                $id,
                $workspaceId,
                $issueNumber,
                $title,
                $description,
                $state,
                $stateReason,
                $priority,
                $assignee,
                $dueDate,
                $projectName,
                $milestoneName,
                $issueTypeName,
                0,
                0,
                $updatedUtc,
                $createdUtc
            );
            """;
        createIssueCommand.Parameters.AddWithValue("$id", issueId.ToString());
        createIssueCommand.Parameters.AddWithValue("$workspaceId", workspace.Id);
        createIssueCommand.Parameters.AddWithValue("$issueNumber", issueNumber);
        createIssueCommand.Parameters.AddWithValue("$title", input.Title.Trim());
        createIssueCommand.Parameters.AddWithValue(
            "$description",
            "Created from the Phase 1 quick capture flow. The next step is to expand this into the full issue body editor.");
        createIssueCommand.Parameters.AddWithValue("$state", SerializeState(IssueWorkflowState.Open));
        createIssueCommand.Parameters.AddWithValue("$stateReason", SerializeReason(IssueStateReason.None));
        createIssueCommand.Parameters.AddWithValue("$priority", SerializePriority(input.Priority));
        createIssueCommand.Parameters.AddWithValue("$assignee", (object?)Normalize(input.AssigneeDisplayName) ?? DBNull.Value);
        createIssueCommand.Parameters.AddWithValue("$dueDate", (object?)SerializeDate(input.DueDate) ?? DBNull.Value);
        createIssueCommand.Parameters.AddWithValue("$projectName", (object?)Normalize(input.ProjectName) ?? DBNull.Value);
        createIssueCommand.Parameters.AddWithValue("$milestoneName", (object?)Normalize(input.MilestoneName) ?? DBNull.Value);
        createIssueCommand.Parameters.AddWithValue("$issueTypeName", (object?)Normalize(input.IssueTypeName) ?? DBNull.Value);
        createIssueCommand.Parameters.AddWithValue("$updatedUtc", SerializeTimestamp(now));
        createIssueCommand.Parameters.AddWithValue("$createdUtc", SerializeTimestamp(now));
        await createIssueCommand.ExecuteNonQueryAsync(cancellationToken);

        await EnsureIssueMetadataAsync(
            connection,
            transaction,
            workspace.Id,
            input.MilestoneName,
            input.IssueTypeName,
            null,
            now,
            cancellationToken);
        await SyncLabelsAsync(
            connection,
            transaction,
            workspace.Id,
            issueId,
            input.Labels,
            cancellationToken);
        await SyncIssueProjectAsync(
            connection,
            transaction,
            workspace.Id,
            issueId,
            Normalize(input.ProjectName),
            IssueWorkflowState.Open,
            input.Priority,
            input.DueDate,
            now,
            cancellationToken);

        await InsertActivityEventAsync(
            connection,
            transaction,
            issueId,
            "issue.created",
            $"Issue #{issueNumber} was created in the Phase 1 workspace.",
            now,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return (await GetIssueByIdAsync(connection, issueId, cancellationToken))!;
    }

    public async Task<IssueListItem?> UpdateIssueAsync(
        UpdateIssueInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Title);

        await using var connection = await OpenInitializedConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var workspaceId = await GetWorkspaceIdForIssueAsync(connection, transaction, input.IssueId, cancellationToken);
        if (workspaceId is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var currentIssue = await GetIssueUpdateSnapshotAsync(
            connection,
            transaction,
            input.IssueId,
            cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var currentIssueState = await GetIssueStateAsync(connection, transaction, input.IssueId, cancellationToken);
        var updateCommand = connection.CreateCommand();
        updateCommand.Transaction = transaction;
        updateCommand.CommandText =
            """
            UPDATE issues
            SET title = $title,
                description = $description,
                priority = $priority,
                assignee_display_name = $assignee,
                due_date = $dueDate,
                project_name = $projectName,
                milestone_name = $milestoneName,
                issue_type_name = $issueTypeName,
                updated_utc = $updatedUtc
            WHERE id = $id;
            """;
        updateCommand.Parameters.AddWithValue("$id", input.IssueId.ToString());
        updateCommand.Parameters.AddWithValue("$title", input.Title.Trim());
        updateCommand.Parameters.AddWithValue("$description", input.Description.Trim());
        updateCommand.Parameters.AddWithValue("$priority", SerializePriority(input.Priority));
        updateCommand.Parameters.AddWithValue("$assignee", (object?)Normalize(input.AssigneeDisplayName) ?? DBNull.Value);
        updateCommand.Parameters.AddWithValue("$dueDate", (object?)SerializeDate(input.DueDate) ?? DBNull.Value);
        updateCommand.Parameters.AddWithValue("$projectName", (object?)Normalize(input.ProjectName) ?? DBNull.Value);
        updateCommand.Parameters.AddWithValue("$milestoneName", (object?)Normalize(input.MilestoneName) ?? DBNull.Value);
        updateCommand.Parameters.AddWithValue("$issueTypeName", (object?)Normalize(input.IssueTypeName) ?? DBNull.Value);
        updateCommand.Parameters.AddWithValue("$updatedUtc", SerializeTimestamp(now));

        var affectedRows = await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        if (affectedRows == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        // Phase 1의 기본 CRUD는 메타데이터와 본문을 한 번에 편집하는 흐름이므로,
        // 라벨 동기화와 활동 로그를 같은 트랜잭션 안에 묶어 상세 화면의 타임라인을 일관되게 유지한다.
        await EnsureIssueMetadataAsync(
            connection,
            transaction,
            workspaceId,
            input.MilestoneName,
            input.IssueTypeName,
            input.DueDate,
            now,
            cancellationToken);
        await SyncLabelsAsync(connection, transaction, workspaceId, input.IssueId, input.Labels, cancellationToken);
        await SyncIssueProjectAsync(
            connection,
            transaction,
            workspaceId,
            input.IssueId,
            Normalize(input.ProjectName),
            currentIssueState,
            input.Priority,
            input.DueDate,
            now,
            cancellationToken);
        await InsertActivityEventAsync(
            connection,
            transaction,
            input.IssueId,
            "issue.updated",
            BuildIssueUpdateSummary(currentIssue, input),
            now,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return await GetIssueByIdAsync(connection, input.IssueId, cancellationToken);
    }

    public async Task<IssueListItem?> UpdateIssueStateAsync(
        UpdateIssueStateInput input,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenInitializedConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var normalizedReason = NormalizeStateReason(input.State, input.Reason);

        var updateCommand = connection.CreateCommand();
        updateCommand.Transaction = transaction;
        updateCommand.CommandText =
            """
            UPDATE issues
            SET state = $state,
                state_reason = $stateReason,
                updated_utc = $updatedUtc
            WHERE id = $id;
            """;
        updateCommand.Parameters.AddWithValue("$id", input.IssueId.ToString());
        updateCommand.Parameters.AddWithValue("$state", SerializeState(input.State));
        updateCommand.Parameters.AddWithValue("$stateReason", SerializeReason(normalizedReason));
        updateCommand.Parameters.AddWithValue("$updatedUtc", SerializeTimestamp(now));

        var affectedRows = await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        if (affectedRows == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await SyncProjectItemStateAsync(connection, transaction, input.IssueId, input.State, now, cancellationToken);
        await InsertActivityEventAsync(
            connection,
            transaction,
            input.IssueId,
            "issue.state.changed",
            BuildStateTransitionSummary(input.State, normalizedReason),
            now,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return await GetIssueByIdAsync(connection, input.IssueId, cancellationToken);
    }

    public async Task<bool> DeleteIssueAsync(Guid issueId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenInitializedConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        // 댓글, 첨부, 활동 로그는 FK cascade로 함께 정리해 Phase 1 삭제 흐름이
        // 단일 SQLite 워크스페이스 파일 안에서 고아 데이터를 남기지 않도록 한다.
        var deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = transaction;
        deleteCommand.CommandText =
            """
            DELETE FROM issues
            WHERE id = $id;
            """;
        deleteCommand.Parameters.AddWithValue("$id", issueId.ToString());

        var affectedRows = await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return affectedRows > 0;
    }

    public async Task<IssueComment?> AddIssueCommentAsync(
        AddIssueCommentInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Body);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.AuthorDisplayName);

        await using var connection = await OpenInitializedConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        if (!await IssueExistsAsync(connection, transaction, input.IssueId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var comment = await InsertCommentAsync(
            connection,
            transaction,
            input.IssueId,
            Normalize(input.AuthorDisplayName)!,
            input.Body.Trim(),
            DateTimeOffset.UtcNow,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return comment;
    }

    public async Task<IssueAttachment?> AddIssueAttachmentAsync(
        AddIssueAttachmentInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input.FileName);
        ArgumentNullException.ThrowIfNull(input.Content);

        await using var connection = await OpenInitializedConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        if (!await IssueExistsAsync(connection, transaction, input.IssueId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var attachment = await InsertAttachmentAsync(
            connection,
            transaction,
            input.IssueId,
            Normalize(input.FileName)!,
            string.IsNullOrWhiteSpace(input.ContentType) ? "application/octet-stream" : input.ContentType,
            input.Content,
            DateTimeOffset.UtcNow,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return attachment;
    }

    public async Task<string?> ExportAttachmentToTempFileAsync(
        Guid attachmentId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenInitializedConnectionAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT file_name, content
            FROM attachments
            WHERE id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", attachmentId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        if (reader.IsDBNull(1))
        {
            return null;
        }

        var safeFileName = SanitizeFileName(reader.GetString(0));
        var exportDirectory = Path.Combine(
            Path.GetTempPath(),
            "Tracky",
            "attachments",
            attachmentId.ToString("N"));
        Directory.CreateDirectory(exportDirectory);

        var outputPath = Path.Combine(exportDirectory, safeFileName);
        var content = (byte[])reader["content"];

        // 첨부는 DB 안에 저장하지만, 실제 열기 동작은 운영체제의 기본 앱에 맡기는 편이
        // 크로스 플랫폼 Phase 1에서 가장 단순하고 예측 가능한 흐름이다.
        await File.WriteAllBytesAsync(outputPath, content, cancellationToken);
        return outputPath;
    }

    public async Task<IssueRelation?> AddIssueRelationAsync(
        AddIssueRelationInput input,
        CancellationToken cancellationToken = default)
    {
        if (input.SourceIssueId == input.TargetIssueId)
        {
            return null;
        }

        await using var connection = await OpenInitializedConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        if (!await IssueExistsAsync(connection, transaction, input.SourceIssueId, cancellationToken)
            || !await IssueExistsAsync(connection, transaction, input.TargetIssueId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT OR IGNORE INTO issue_relations (
                id,
                source_issue_id,
                target_issue_id,
                relation_type,
                created_utc
            ) VALUES (
                $id,
                $sourceIssueId,
                $targetIssueId,
                $relationType,
                $createdUtc
            );
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$sourceIssueId", input.SourceIssueId.ToString());
        command.Parameters.AddWithValue("$targetIssueId", input.TargetIssueId.ToString());
        command.Parameters.AddWithValue("$relationType", SerializeIssueRelationType(input.RelationType));
        command.Parameters.AddWithValue("$createdUtc", SerializeTimestamp(now));
        await command.ExecuteNonQueryAsync(cancellationToken);

        await InsertActivityEventAsync(
            connection,
            transaction,
            input.SourceIssueId,
            "issue.relation.added",
            $"Issue relation {FormatIssueRelationType(input.RelationType)} was added.",
            now,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return (await GetIssueRelationsAsync(connection, input.SourceIssueId, cancellationToken))
            .FirstOrDefault(relation => relation.TargetIssueId == input.TargetIssueId
                && relation.RelationType == input.RelationType);
    }

    public async Task<IssueReminder?> ScheduleIssueReminderAsync(
        ScheduleIssueReminderInput input,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenInitializedConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var workspaceId = await GetWorkspaceIdForIssueAsync(connection, transaction, input.IssueId, cancellationToken);
        if (workspaceId is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var issueTitle = await GetIssueTitleAsync(connection, transaction, input.IssueId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var reminder = await InsertReminderAsync(
            connection,
            transaction,
            workspaceId,
            input.IssueId,
            Normalize(input.Title) ?? $"Reminder for {issueTitle}",
            Normalize(input.Note) ?? string.Empty,
            input.RemindAtUtc.ToUniversalTime(),
            now,
            cancellationToken);

        await InsertActivityEventAsync(
            connection,
            transaction,
            input.IssueId,
            "issue.reminder.scheduled",
            $"Reminder scheduled for {reminder.RemindAtUtc.ToLocalTime():MMM dd, HH:mm}.",
            now,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return reminder;
    }

    public async Task<IssueReminder?> DismissReminderAsync(
        DismissReminderInput input,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenInitializedConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var reminder = await GetReminderByIdAsync(connection, transaction, input.ReminderId, cancellationToken);
        if (reminder is null || reminder.IsDismissed)
        {
            await transaction.RollbackAsync(cancellationToken);
            return reminder;
        }

        var dismissedAtUtc = DateTimeOffset.UtcNow;
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE reminders
            SET dismissed_utc = $dismissedUtc
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", input.ReminderId.ToString());
        command.Parameters.AddWithValue("$dismissedUtc", SerializeTimestamp(dismissedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);

        if (reminder.IssueId is Guid issueId)
        {
            await InsertActivityEventAsync(
                connection,
                transaction,
                issueId,
                "issue.reminder.dismissed",
                $"Reminder \"{reminder.Title}\" was dismissed.",
                dismissedAtUtc,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return reminder with { DismissedAtUtc = dismissedAtUtc };
    }

    public async Task<ExportResult> ExportSelectionAsync(
        ExportOptions options,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenInitializedConnectionAsync(cancellationToken);
        var issues = await ResolveExportIssuesAsync(connection, options, cancellationToken);
        var exportDirectory = PrepareExportDirectory(options.OutputDirectory);

        // 내보내기는 사용자의 현재 필터 상태를 파일 시스템 산출물로 바꾸는 경계이므로,
        // 쓰기 형식별 파일명과 첨부 배치를 한곳에서 결정해 테스트 가능하게 유지한다.
        return options.Format switch
        {
            ExportFormat.Markdown => await WriteMarkdownExportAsync(
                connection,
                exportDirectory,
                issues,
                options,
                cancellationToken),
            ExportFormat.Html => await WriteHtmlExportAsync(
                connection,
                exportDirectory,
                issues,
                options,
                cancellationToken),
            ExportFormat.FolderTree => await WriteFolderTreeExportAsync(
                connection,
                exportDirectory,
                issues,
                options,
                cancellationToken),
            ExportFormat.PortablePackage => await WritePortablePackageExportAsync(
                connection,
                exportDirectory,
                issues,
                options,
                cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.Format, "Unsupported export format."),
        };
    }

    public async Task<ExportPreset?> AddExportPresetAsync(
        AddExportPresetInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Name);

        await using var connection = await OpenInitializedConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var workspace = await GetWorkspaceAsync(connection, cancellationToken);
        var preset = await UpsertExportPresetAsync(
            connection,
            transaction,
            workspace.Id,
            input,
            DateTimeOffset.UtcNow,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return preset;
    }

    public async Task<SavedIssueSearch?> AddSavedIssueSearchAsync(
        AddSavedIssueSearchInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Name);

        await using var connection = await OpenInitializedConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var workspace = await GetWorkspaceAsync(connection, cancellationToken);
        var savedSearch = await UpsertSavedIssueSearchAsync(
            connection,
            transaction,
            workspace.Id,
            input,
            DateTimeOffset.UtcNow,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return savedSearch;
    }

    public async Task<WorkspacePreferences> UpdateWorkspacePreferencesAsync(
        UpdateWorkspacePreferencesInput input,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenInitializedConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var workspace = await GetWorkspaceAsync(connection, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO workspace_preferences (
                workspace_id,
                theme,
                compact_density,
                shortcut_profile,
                updated_utc
            ) VALUES (
                $workspaceId,
                $theme,
                $compactDensity,
                $shortcutProfile,
                $updatedUtc
            )
            ON CONFLICT(workspace_id) DO UPDATE SET
                theme = excluded.theme,
                compact_density = excluded.compact_density,
                shortcut_profile = excluded.shortcut_profile,
                updated_utc = excluded.updated_utc;
            """;
        command.Parameters.AddWithValue("$workspaceId", workspace.Id);
        command.Parameters.AddWithValue("$theme", SerializeTheme(input.Theme));
        command.Parameters.AddWithValue("$compactDensity", input.CompactDensity ? 1 : 0);
        command.Parameters.AddWithValue(
            "$shortcutProfile",
            string.IsNullOrWhiteSpace(input.ShortcutProfile) ? "Default" : input.ShortcutProfile.Trim());
        command.Parameters.AddWithValue("$updatedUtc", SerializeTimestamp(now));
        await command.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new WorkspacePreferences(
            input.Theme,
            input.CompactDensity,
            string.IsNullOrWhiteSpace(input.ShortcutProfile) ? "Default" : input.ShortcutProfile.Trim(),
            now);
    }

    public async Task<IReadOnlyList<ProjectSummary>> GetProjectsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenInitializedConnectionAsync(cancellationToken);
        return await GetProjectSummariesAsync(connection, cancellationToken);
    }

    public async Task<ProjectDetail?> GetProjectDetailAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenInitializedConnectionAsync(cancellationToken);
        return await GetProjectDetailFromConnectionAsync(connection, projectId, cancellationToken);
    }

    public async Task<ProjectSummary> CreateProjectAsync(
        CreateProjectInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Name);

        await using var connection = await OpenInitializedConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var workspace = await GetWorkspaceAsync(connection, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var projectId = await EnsureProjectAsync(
            connection,
            transaction,
            workspace.Id,
            Normalize(input.Name)!,
            Normalize(input.Description) ?? string.Empty,
            now,
            cancellationToken);

        await EnsureDefaultProjectMetadataAsync(connection, transaction, projectId, now, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return (await GetProjectSummaryByIdAsync(connection, projectId, cancellationToken))!;
    }

    public async Task<ProjectIssueItem?> MoveProjectItemAsync(
        MoveProjectItemInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input.BoardColumn);

        await using var connection = await OpenInitializedConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var normalizedColumn = NormalizeBoardColumn(input.BoardColumn);
        var itemRow = await GetProjectItemIdentityAsync(connection, transaction, input.ProjectItemId, cancellationToken);
        if (itemRow is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var nextSortOrder = await GetNextProjectItemSortOrderAsync(
            connection,
            transaction,
            itemRow.ProjectId,
            normalizedColumn,
            cancellationToken);

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE project_items
            SET board_column = $boardColumn,
                sort_order = $sortOrder,
                updated_utc = $updatedUtc
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", input.ProjectItemId.ToString());
        command.Parameters.AddWithValue("$boardColumn", normalizedColumn);
        command.Parameters.AddWithValue("$sortOrder", nextSortOrder);
        command.Parameters.AddWithValue("$updatedUtc", SerializeTimestamp(now));
        await command.ExecuteNonQueryAsync(cancellationToken);

        await TouchProjectAsync(connection, transaction, itemRow.ProjectId, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetProjectIssueItemByIdAsync(connection, input.ProjectItemId, cancellationToken);
    }

    public async Task<ProjectCustomField?> AddProjectCustomFieldAsync(
        AddProjectCustomFieldInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Name);

        await using var connection = await OpenInitializedConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        if (!await ProjectExistsAsync(connection, transaction, input.ProjectId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        await UpsertProjectCustomFieldAsync(
            connection,
            transaction,
            input.ProjectId,
            Normalize(input.Name)!,
            input.FieldType,
            Normalize(input.OptionsText) ?? string.Empty,
            now,
            cancellationToken);
        await TouchProjectAsync(connection, transaction, input.ProjectId, now, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return await GetProjectCustomFieldByNameAsync(connection, input.ProjectId, Normalize(input.Name)!, cancellationToken);
    }

    public async Task<ProjectIssueItem?> UpdateProjectCustomFieldValueAsync(
        UpdateProjectCustomFieldValueInput input,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenInitializedConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var itemRow = await GetProjectItemIdentityAsync(connection, transaction, input.ProjectItemId, cancellationToken);
        if (itemRow is null
            || !await ProjectCustomFieldBelongsToProjectAsync(
                connection,
                transaction,
                itemRow.ProjectId,
                input.CustomFieldId,
                cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var normalizedValue = Normalize(input.ValueText) ?? string.Empty;
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO project_custom_field_values (
                project_item_id,
                custom_field_id,
                value_text,
                updated_utc
            ) VALUES (
                $projectItemId,
                $customFieldId,
                $valueText,
                $updatedUtc
            )
            ON CONFLICT(project_item_id, custom_field_id) DO UPDATE SET
                value_text = excluded.value_text,
                updated_utc = excluded.updated_utc;
            """;
        command.Parameters.AddWithValue("$projectItemId", input.ProjectItemId.ToString());
        command.Parameters.AddWithValue("$customFieldId", input.CustomFieldId.ToString());
        command.Parameters.AddWithValue("$valueText", normalizedValue);
        command.Parameters.AddWithValue("$updatedUtc", SerializeTimestamp(now));
        await command.ExecuteNonQueryAsync(cancellationToken);

        // 커스텀 필드 값은 프로젝트 뷰의 정렬과 저장 뷰 필터에 영향을 주므로,
        // 값 저장 시 프로젝트와 아이템의 updated 시각을 같이 갱신해 목록 freshness를 유지한다.
        await TouchProjectItemAsync(connection, transaction, input.ProjectItemId, now, cancellationToken);
        await TouchProjectAsync(connection, transaction, itemRow.ProjectId, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetProjectIssueItemByIdAsync(connection, input.ProjectItemId, cancellationToken);
    }

    public async Task<ProjectSavedView?> AddProjectSavedViewAsync(
        AddProjectSavedViewInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Name);

        await using var connection = await OpenInitializedConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        if (!await ProjectExistsAsync(connection, transaction, input.ProjectId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        await UpsertProjectSavedViewAsync(
            connection,
            transaction,
            input.ProjectId,
            Normalize(input.Name)!,
            input.ViewMode,
            Normalize(input.FilterText) ?? string.Empty,
            Normalize(input.SortByField) ?? "Board position",
            Normalize(input.GroupByField) ?? string.Empty,
            now,
            cancellationToken);
        await TouchProjectAsync(connection, transaction, input.ProjectId, now, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return await GetProjectSavedViewByNameAsync(connection, input.ProjectId, Normalize(input.Name)!, cancellationToken);
    }

    private async Task<SqliteConnection> OpenInitializedConnectionAsync(CancellationToken cancellationToken)
    {
        var connectionStringBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = _pathProvider.GetDatabasePath(),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        };

        var connection = new SqliteConnection(connectionStringBuilder.ToString());
        await connection.OpenAsync(cancellationToken);

        var pragmaCommand = connection.CreateCommand();
        pragmaCommand.CommandText =
            """
            PRAGMA foreign_keys = ON;
            PRAGMA journal_mode = WAL;
            PRAGMA busy_timeout = 5000;
            """;
        await pragmaCommand.ExecuteNonQueryAsync(cancellationToken);

        await EnsureSchemaAsync(connection, cancellationToken);
        await SeedIfNeededAsync(connection, cancellationToken);
        await EnsureProjectRecordsForExistingIssuesAsync(connection, cancellationToken);
        return connection;
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        // 워크스페이스 DB 하나만 옮겨도 핵심 상세 흐름이 유지되도록,
        // 이슈 요약뿐 아니라 댓글/첨부/활동 로그 테이블까지 같은 파일에 함께 고정한다.
        var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS workspaces (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                description TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS issues (
                id TEXT PRIMARY KEY,
                workspace_id TEXT NOT NULL,
                issue_number INTEGER NOT NULL UNIQUE,
                title TEXT NOT NULL,
                description TEXT NOT NULL DEFAULT '',
                state TEXT NOT NULL,
                state_reason TEXT NOT NULL,
                priority TEXT NOT NULL,
                assignee_display_name TEXT NULL,
                due_date TEXT NULL,
                project_name TEXT NULL,
                milestone_name TEXT NULL,
                issue_type_name TEXT NULL,
                comment_count INTEGER NOT NULL DEFAULT 0,
                attachment_count INTEGER NOT NULL DEFAULT 0,
                updated_utc TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                FOREIGN KEY (workspace_id) REFERENCES workspaces(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS labels (
                id TEXT PRIMARY KEY,
                workspace_id TEXT NOT NULL,
                name TEXT NOT NULL,
                color_hex TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                UNIQUE(workspace_id, name),
                FOREIGN KEY (workspace_id) REFERENCES workspaces(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS issue_labels (
                issue_id TEXT NOT NULL,
                label_id TEXT NOT NULL,
                PRIMARY KEY (issue_id, label_id),
                FOREIGN KEY (issue_id) REFERENCES issues(id) ON DELETE CASCADE,
                FOREIGN KEY (label_id) REFERENCES labels(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS milestones (
                id TEXT PRIMARY KEY,
                workspace_id TEXT NOT NULL,
                name TEXT NOT NULL,
                due_date TEXT NULL,
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                UNIQUE(workspace_id, name),
                FOREIGN KEY (workspace_id) REFERENCES workspaces(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS issue_types (
                id TEXT PRIMARY KEY,
                workspace_id TEXT NOT NULL,
                name TEXT NOT NULL,
                color_hex TEXT NOT NULL,
                description TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                UNIQUE(workspace_id, name),
                FOREIGN KEY (workspace_id) REFERENCES workspaces(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS issue_comments (
                id TEXT PRIMARY KEY,
                issue_id TEXT NOT NULL,
                author_display_name TEXT NOT NULL,
                body TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                FOREIGN KEY (issue_id) REFERENCES issues(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS issue_relations (
                id TEXT PRIMARY KEY,
                source_issue_id TEXT NOT NULL,
                target_issue_id TEXT NOT NULL,
                relation_type TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                UNIQUE(source_issue_id, target_issue_id, relation_type),
                FOREIGN KEY (source_issue_id) REFERENCES issues(id) ON DELETE CASCADE,
                FOREIGN KEY (target_issue_id) REFERENCES issues(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS activity_events (
                id TEXT PRIMARY KEY,
                issue_id TEXT NOT NULL,
                event_type TEXT NOT NULL,
                summary TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                FOREIGN KEY (issue_id) REFERENCES issues(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS attachments (
                id TEXT PRIMARY KEY,
                issue_id TEXT NOT NULL,
                file_name TEXT NOT NULL,
                content_type TEXT NOT NULL,
                content BLOB NULL,
                size_bytes INTEGER NOT NULL,
                created_utc TEXT NOT NULL,
                FOREIGN KEY (issue_id) REFERENCES issues(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS reminders (
                id TEXT PRIMARY KEY,
                workspace_id TEXT NOT NULL,
                issue_id TEXT NULL,
                title TEXT NOT NULL,
                note TEXT NOT NULL,
                remind_utc TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                dismissed_utc TEXT NULL,
                FOREIGN KEY (workspace_id) REFERENCES workspaces(id) ON DELETE CASCADE,
                FOREIGN KEY (issue_id) REFERENCES issues(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS projects (
                id TEXT PRIMARY KEY,
                workspace_id TEXT NOT NULL,
                name TEXT NOT NULL,
                description TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                UNIQUE(workspace_id, name),
                FOREIGN KEY (workspace_id) REFERENCES workspaces(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS project_items (
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL,
                issue_id TEXT NOT NULL,
                board_column TEXT NOT NULL,
                sort_order INTEGER NOT NULL,
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                UNIQUE(project_id, issue_id),
                FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
                FOREIGN KEY (issue_id) REFERENCES issues(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS project_custom_fields (
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL,
                name TEXT NOT NULL,
                field_type TEXT NOT NULL,
                options_text TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                UNIQUE(project_id, name),
                FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS project_custom_field_values (
                project_item_id TEXT NOT NULL,
                custom_field_id TEXT NOT NULL,
                value_text TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                PRIMARY KEY (project_item_id, custom_field_id),
                FOREIGN KEY (project_item_id) REFERENCES project_items(id) ON DELETE CASCADE,
                FOREIGN KEY (custom_field_id) REFERENCES project_custom_fields(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS project_saved_views (
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL,
                name TEXT NOT NULL,
                view_mode TEXT NOT NULL,
                filter_text TEXT NOT NULL,
                sort_by_field TEXT NOT NULL,
                group_by_field TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                UNIQUE(project_id, name),
                FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS export_presets (
                id TEXT PRIMARY KEY,
                workspace_id TEXT NOT NULL,
                name TEXT NOT NULL,
                scope TEXT NOT NULL,
                format TEXT NOT NULL,
                body_format TEXT NOT NULL,
                include_comments INTEGER NOT NULL,
                include_activity INTEGER NOT NULL,
                include_attachments INTEGER NOT NULL,
                include_closed_issues INTEGER NOT NULL,
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                UNIQUE(workspace_id, name),
                FOREIGN KEY (workspace_id) REFERENCES workspaces(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS saved_issue_searches (
                id TEXT PRIMARY KEY,
                workspace_id TEXT NOT NULL,
                name TEXT NOT NULL,
                query_text TEXT NOT NULL,
                is_pinned INTEGER NOT NULL,
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                UNIQUE(workspace_id, name),
                FOREIGN KEY (workspace_id) REFERENCES workspaces(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS workspace_preferences (
                workspace_id TEXT PRIMARY KEY,
                theme TEXT NOT NULL,
                compact_density INTEGER NOT NULL,
                shortcut_profile TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                FOREIGN KEY (workspace_id) REFERENCES workspaces(id) ON DELETE CASCADE
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        await EnsureColumnAsync(connection, "issues", "description", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "issues", "milestone_name", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(connection, "issues", "issue_type_name", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(
            connection,
            "project_saved_views",
            "sort_by_field",
            "TEXT NOT NULL DEFAULT 'Board position'",
            cancellationToken);
    }

    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureProjectRecordsForExistingIssuesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, workspace_id, project_name, state, priority, due_date, updated_utc
            FROM issues
            WHERE project_name IS NOT NULL AND trim(project_name) <> '';
            """;

        var issueRows = new List<ProjectIssueSyncRow>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                issueRows.Add(
                    new ProjectIssueSyncRow(
                        Guid.Parse(reader.GetString(0)),
                        reader.GetString(1),
                        reader.GetString(2),
                        ParseState(reader.GetString(3)),
                        ParsePriority(reader.GetString(4)),
                        reader.IsDBNull(5) ? null : ParseStoredDate(reader.GetString(5)),
                        ParseStoredTimestamp(reader.GetString(6))));
            }
        }

        if (issueRows.Count == 0)
        {
            return;
        }

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        foreach (var issueRow in issueRows)
        {
            await SyncIssueProjectAsync(
                connection,
                transaction,
                issueRow.WorkspaceId,
                issueRow.IssueId,
                issueRow.ProjectName,
                issueRow.State,
                issueRow.Priority,
                issueRow.DueDate,
                issueRow.UpdatedAtUtc,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task SyncIssueProjectAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string workspaceId,
        Guid issueId,
        string? projectName,
        IssueWorkflowState state,
        IssuePriority priority,
        DateOnly? dueDate,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken)
    {
        // Phase 2에서는 기존 이슈의 project_name을 Project/ProjectItem 관계로 승격한다.
        // 이 동기화가 있어야 Phase 1 편집 화면에서 프로젝트명을 바꿔도 보드와 테이블이 같은 데이터를 본다.
        if (string.IsNullOrWhiteSpace(projectName))
        {
            var clearCommand = connection.CreateCommand();
            clearCommand.Transaction = transaction;
            clearCommand.CommandText = "DELETE FROM project_items WHERE issue_id = $issueId;";
            clearCommand.Parameters.AddWithValue("$issueId", issueId.ToString());
            await clearCommand.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        var now = updatedAtUtc;
        var projectId = await EnsureProjectAsync(
            connection,
            transaction,
            workspaceId,
            projectName,
            $"Issues grouped under {projectName}.",
            now,
            cancellationToken);
        var existingItem = await GetProjectItemForIssueAsync(
            connection,
            transaction,
            projectId,
            issueId,
            cancellationToken);

        if (existingItem is null)
        {
            var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText =
                """
                INSERT INTO project_items (
                    id,
                    project_id,
                    issue_id,
                    board_column,
                    sort_order,
                    created_utc,
                    updated_utc
                ) VALUES (
                    $id,
                    $projectId,
                    $issueId,
                    $boardColumn,
                    $sortOrder,
                    $createdUtc,
                    $updatedUtc
                );
                """;
            insertCommand.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            insertCommand.Parameters.AddWithValue("$projectId", projectId.ToString());
            insertCommand.Parameters.AddWithValue("$issueId", issueId.ToString());
            insertCommand.Parameters.AddWithValue("$boardColumn", GetDefaultBoardColumn(state, priority, dueDate));
            insertCommand.Parameters.AddWithValue(
                "$sortOrder",
                await GetNextProjectItemSortOrderAsync(
                    connection,
                    transaction,
                    projectId,
                    GetDefaultBoardColumn(state, priority, dueDate),
                    cancellationToken));
            insertCommand.Parameters.AddWithValue("$createdUtc", SerializeTimestamp(now));
            insertCommand.Parameters.AddWithValue("$updatedUtc", SerializeTimestamp(now));
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            var touchCommand = connection.CreateCommand();
            touchCommand.Transaction = transaction;
            touchCommand.CommandText =
                """
                UPDATE project_items
                SET updated_utc = $updatedUtc
                WHERE id = $id;
                """;
            touchCommand.Parameters.AddWithValue("$id", existingItem.ProjectItemId.ToString());
            touchCommand.Parameters.AddWithValue("$updatedUtc", SerializeTimestamp(now));
            await touchCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        var deleteOtherProjectsCommand = connection.CreateCommand();
        deleteOtherProjectsCommand.Transaction = transaction;
        deleteOtherProjectsCommand.CommandText =
            """
            DELETE FROM project_items
            WHERE issue_id = $issueId AND project_id <> $projectId;
            """;
        deleteOtherProjectsCommand.Parameters.AddWithValue("$issueId", issueId.ToString());
        deleteOtherProjectsCommand.Parameters.AddWithValue("$projectId", projectId.ToString());
        await deleteOtherProjectsCommand.ExecuteNonQueryAsync(cancellationToken);

        await EnsureDefaultProjectMetadataAsync(connection, transaction, projectId, now, cancellationToken);
        await TouchProjectAsync(connection, transaction, projectId, now, cancellationToken);
    }

    private static async Task SyncProjectItemStateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid issueId,
        IssueWorkflowState state,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken)
    {
        var projectIds = await GetProjectIdsForIssueAsync(connection, transaction, issueId, cancellationToken);
        if (projectIds.Count == 0)
        {
            return;
        }

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE project_items
            SET board_column = CASE
                    WHEN $state = 'closed' THEN $doneColumn
                    WHEN board_column = $doneColumn THEN $todoColumn
                    ELSE board_column
                END,
                updated_utc = $updatedUtc
            WHERE issue_id = $issueId;
            """;
        command.Parameters.AddWithValue("$issueId", issueId.ToString());
        command.Parameters.AddWithValue("$state", SerializeState(state));
        command.Parameters.AddWithValue("$doneColumn", BoardColumnDone);
        command.Parameters.AddWithValue("$todoColumn", BoardColumnTodo);
        command.Parameters.AddWithValue("$updatedUtc", SerializeTimestamp(updatedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);

        foreach (var projectId in projectIds)
        {
            await TouchProjectAsync(connection, transaction, projectId, updatedAtUtc, cancellationToken);
        }
    }

    private static async Task SeedIfNeededAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(1) FROM workspaces;";
        var workspaceCount = ReadScalarInt32(await countCommand.ExecuteScalarAsync(cancellationToken));
        if (workspaceCount > 0)
        {
            return;
        }

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var workspaceId = Guid.NewGuid().ToString();

        var insertWorkspaceCommand = connection.CreateCommand();
        insertWorkspaceCommand.Transaction = transaction;
        insertWorkspaceCommand.CommandText =
            """
            INSERT INTO workspaces (id, name, description, created_utc, updated_utc)
            VALUES ($id, $name, $description, $createdUtc, $updatedUtc);
            """;
        insertWorkspaceCommand.Parameters.AddWithValue("$id", workspaceId);
        insertWorkspaceCommand.Parameters.AddWithValue("$name", "Tracky Local Workspace");
        insertWorkspaceCommand.Parameters.AddWithValue("$description", "GitHub-style issue flow for a local-first Phase 1 foundation.");
        insertWorkspaceCommand.Parameters.AddWithValue("$createdUtc", SerializeTimestamp(now));
        insertWorkspaceCommand.Parameters.AddWithValue("$updatedUtc", SerializeTimestamp(now));
        await insertWorkspaceCommand.ExecuteNonQueryAsync(cancellationToken);

        foreach (var label in SeedLabels)
        {
            await EnsureLabelAsync(connection, transaction, workspaceId, label, cancellationToken);
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        await EnsureIssueTypeAsync(
            connection,
            transaction,
            workspaceId,
            "Task",
            GetIssueTypeColor("Task"),
            "Actionable implementation work.",
            now,
            cancellationToken);
        await EnsureIssueTypeAsync(
            connection,
            transaction,
            workspaceId,
            "Bug",
            GetIssueTypeColor("Bug"),
            "Unexpected behavior or regression.",
            now,
            cancellationToken);
        await EnsureIssueTypeAsync(
            connection,
            transaction,
            workspaceId,
            "Idea",
            GetIssueTypeColor("Idea"),
            "A captured product or implementation idea.",
            now,
            cancellationToken);
        await EnsureMilestoneAsync(
            connection,
            transaction,
            workspaceId,
            "MVP Readiness",
            today.AddDays(14),
            now,
            cancellationToken);

        var workspaceShellIssueId = await InsertSeedIssueAsync(
            connection,
            transaction,
            workspaceId,
            101,
            "Set up the default local workspace shell",
            "Establish the first-run shell so the workspace, sidebar, and All Issues entry point feel coherent before deeper modules land.",
            IssueWorkflowState.Open,
            IssueStateReason.None,
            IssuePriority.High,
            "Dabin",
            today,
            "Tracky Foundation",
            ["foundation", "desktop"],
            now.AddHours(-6),
            cancellationToken);

        var hierarchyIssueId = await InsertSeedIssueAsync(
            connection,
            transaction,
            workspaceId,
            102,
            "Design the All Issues home information hierarchy",
            "Prioritize the list view, urgency buckets, and side-panel editing flow so the home screen stays dense without becoming noisy.",
            IssueWorkflowState.Open,
            IssueStateReason.None,
            IssuePriority.Critical,
            "Dabin",
            today.AddDays(-1),
            "Tracky Foundation",
            ["ux", "priority:high"],
            now.AddHours(-3),
            cancellationToken);

        var stateReasonIssueId = await InsertSeedIssueAsync(
            connection,
            transaction,
            workspaceId,
            103,
            "Document state_reason semantics for closed issues",
            "Clarify how completed, not planned, and duplicate should behave in filters, detail screens, and later export rules.",
            IssueWorkflowState.Closed,
            IssueStateReason.Completed,
            IssuePriority.Medium,
            "Dabin",
            today.AddDays(-2),
            "Tracky Core",
            ["foundation"],
            now.AddHours(-30),
            cancellationToken);

        var attachmentIssueId = await InsertSeedIssueAsync(
            connection,
            transaction,
            workspaceId,
            104,
            "Sketch attachment storage inside the SQLite workspace",
            "Validate that attachments can live inside the workspace database so copying one DB file still preserves the personal archive.",
            IssueWorkflowState.Open,
            IssueStateReason.None,
            IssuePriority.High,
            "Dabin",
            today.AddDays(3),
            "Tracky Infrastructure",
            ["local-first", "desktop"],
            now.AddHours(-1),
            cancellationToken);

        await InsertSeedIssueAsync(
            connection,
            transaction,
            workspaceId,
            105,
            "Polish keyboard flow for quick issue capture",
            "Keep capture friction low enough that the inbox stays trustworthy during fast context switches.",
            IssueWorkflowState.Open,
            IssueStateReason.None,
            IssuePriority.Medium,
            null,
            today.AddDays(5),
            "Tracky UX",
            ["ux", "accessibility"],
            now.AddHours(-10),
            cancellationToken);

        await InsertCommentAsync(
            connection,
            transaction,
            workspaceShellIssueId,
            "Dabin",
            "The shell already feels much closer to a real product once the workspace summary and navigation scaffolding are visible together.",
            now.AddHours(-5.5),
            cancellationToken);

        await InsertCommentAsync(
            connection,
            transaction,
            hierarchyIssueId,
            "Dabin",
            "The home screen needs to show overdue and due-today items without hiding the broader issue queue.",
            now.AddHours(-2.4),
            cancellationToken);

        await InsertCommentAsync(
            connection,
            transaction,
            hierarchyIssueId,
            "Tracky",
            "A denser right panel is acceptable as long as issue creation and state changes remain one click away.",
            now.AddHours(-2.1),
            cancellationToken);

        await InsertCommentAsync(
            connection,
            transaction,
            stateReasonIssueId,
            "Dabin",
            "Closing with explicit reasons will matter later for search operators like reason:duplicate and exports.",
            now.AddHours(-29),
            cancellationToken);

        await InsertAttachmentAsync(
            connection,
            transaction,
            hierarchyIssueId,
            "all-issues-notes.md",
            "text/markdown",
            Encoding.UTF8.GetBytes(
                """
                # All Issues Notes

                - Keep the filter bar always visible.
                - Surface overdue and due today at a glance.
                - Preserve enough metadata density to feel GitHub-like.
                """),
            now.AddHours(-2),
            cancellationToken);

        await InsertAttachmentAsync(
            connection,
            transaction,
            attachmentIssueId,
            "sqlite-attachment-plan.txt",
            "text/plain",
            Encoding.UTF8.GetBytes(
                """
                Store binary content in SQLite first.
                Export to a temp file only when the user opens the attachment.
                Keep the DB portable as the primary sync story.
                """),
            now.AddMinutes(-45),
            cancellationToken);

        await InsertReminderAsync(
            connection,
            transaction,
            workspaceId,
            workspaceShellIssueId,
            "Review today's local workspace shell",
            "Seeded reminder used to exercise the Phase 3 due date and reminder rail.",
            now.AddHours(2),
            now.AddHours(-1),
            cancellationToken);

        await UpsertExportPresetAsync(
            connection,
            transaction,
            workspaceId,
            new AddExportPresetInput(
                "Filtered Markdown handoff",
                ExportSelectionScope.CurrentFilter,
                ExportFormat.Markdown,
                ExportBodyFormat.Markdown,
                true,
                true,
                false,
                false),
            now,
            cancellationToken);
        await UpsertExportPresetAsync(
            connection,
            transaction,
            workspaceId,
            new AddExportPresetInput(
                "Portable workspace package",
                ExportSelectionScope.Workspace,
                ExportFormat.PortablePackage,
                ExportBodyFormat.Html,
                true,
                true,
                true,
                true),
            now,
            cancellationToken);
        await UpsertSavedIssueSearchAsync(
            connection,
            transaction,
            workspaceId,
            new AddSavedIssueSearchInput(
                "Open due soon",
                "is:open due:upcoming",
                IsPinned: true),
            now,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<Guid> InsertSeedIssueAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string workspaceId,
        int issueNumber,
        string title,
        string description,
        IssueWorkflowState state,
        IssueStateReason reason,
        IssuePriority priority,
        string? assigneeDisplayName,
        DateOnly? dueDate,
        string? projectName,
        IReadOnlyList<string> labels,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken,
        string? milestoneName = "MVP Readiness",
        string? issueTypeName = "Task")
    {
        var issueId = Guid.NewGuid();
        var issueCommand = connection.CreateCommand();
        issueCommand.Transaction = transaction;
        issueCommand.CommandText =
            """
            INSERT INTO issues (
                id,
                workspace_id,
                issue_number,
                title,
                description,
                state,
                state_reason,
                priority,
                assignee_display_name,
                due_date,
                project_name,
                milestone_name,
                issue_type_name,
                comment_count,
                attachment_count,
                updated_utc,
                created_utc
            ) VALUES (
                $id,
                $workspaceId,
                $issueNumber,
                $title,
                $description,
                $state,
                $stateReason,
                $priority,
                $assignee,
                $dueDate,
                $projectName,
                $milestoneName,
                $issueTypeName,
                0,
                0,
                $updatedUtc,
                $createdUtc
            );
            """;
        issueCommand.Parameters.AddWithValue("$id", issueId.ToString());
        issueCommand.Parameters.AddWithValue("$workspaceId", workspaceId);
        issueCommand.Parameters.AddWithValue("$issueNumber", issueNumber);
        issueCommand.Parameters.AddWithValue("$title", title);
        issueCommand.Parameters.AddWithValue("$description", description);
        issueCommand.Parameters.AddWithValue("$state", SerializeState(state));
        issueCommand.Parameters.AddWithValue("$stateReason", SerializeReason(reason));
        issueCommand.Parameters.AddWithValue("$priority", SerializePriority(priority));
        issueCommand.Parameters.AddWithValue("$assignee", (object?)Normalize(assigneeDisplayName) ?? DBNull.Value);
        issueCommand.Parameters.AddWithValue("$dueDate", (object?)SerializeDate(dueDate) ?? DBNull.Value);
        issueCommand.Parameters.AddWithValue("$projectName", (object?)Normalize(projectName) ?? DBNull.Value);
        issueCommand.Parameters.AddWithValue("$milestoneName", (object?)Normalize(milestoneName) ?? DBNull.Value);
        issueCommand.Parameters.AddWithValue("$issueTypeName", (object?)Normalize(issueTypeName) ?? DBNull.Value);
        issueCommand.Parameters.AddWithValue("$updatedUtc", SerializeTimestamp(updatedAtUtc));
        issueCommand.Parameters.AddWithValue("$createdUtc", SerializeTimestamp(updatedAtUtc.AddDays(-2)));
        await issueCommand.ExecuteNonQueryAsync(cancellationToken);

        foreach (var label in labels)
        {
            var labelId = await EnsureLabelAsync(connection, transaction, workspaceId, label, cancellationToken);

            var linkCommand = connection.CreateCommand();
            linkCommand.Transaction = transaction;
            linkCommand.CommandText =
                """
                INSERT OR IGNORE INTO issue_labels (issue_id, label_id)
                VALUES ($issueId, $labelId);
                """;
            linkCommand.Parameters.AddWithValue("$issueId", issueId.ToString());
            linkCommand.Parameters.AddWithValue("$labelId", labelId);
            await linkCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await SyncIssueProjectAsync(
            connection,
            transaction,
            workspaceId,
            issueId,
            Normalize(projectName),
            state,
            priority,
            dueDate,
            updatedAtUtc,
            cancellationToken);

        await InsertActivityEventAsync(
            connection,
            transaction,
            issueId,
            "issue.seeded",
            $"Seeded issue #{issueNumber} for the Phase 1 workspace.",
            updatedAtUtc,
            cancellationToken);

        return issueId;
    }

    private static async Task<IssueComment> InsertCommentAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid issueId,
        string authorDisplayName,
        string body,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        var commentId = Guid.NewGuid();
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO issue_comments (id, issue_id, author_display_name, body, created_utc)
            VALUES ($id, $issueId, $authorDisplayName, $body, $createdUtc);
            """;
        command.Parameters.AddWithValue("$id", commentId.ToString());
        command.Parameters.AddWithValue("$issueId", issueId.ToString());
        command.Parameters.AddWithValue("$authorDisplayName", authorDisplayName);
        command.Parameters.AddWithValue("$body", body);
        command.Parameters.AddWithValue("$createdUtc", SerializeTimestamp(createdAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);

        await RefreshIssueCountersAsync(connection, transaction, issueId, createdAtUtc, cancellationToken);
        await InsertActivityEventAsync(
            connection,
            transaction,
            issueId,
            "issue.comment.added",
            $"{authorDisplayName} added a comment.",
            createdAtUtc,
            cancellationToken);

        return new IssueComment(commentId, issueId, authorDisplayName, body, createdAtUtc);
    }

    private static async Task<IssueAttachment> InsertAttachmentAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid issueId,
        string fileName,
        string contentType,
        byte[] content,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        var attachmentId = Guid.NewGuid();
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO attachments (id, issue_id, file_name, content_type, content, size_bytes, created_utc)
            VALUES ($id, $issueId, $fileName, $contentType, $content, $sizeBytes, $createdUtc);
            """;
        command.Parameters.AddWithValue("$id", attachmentId.ToString());
        command.Parameters.AddWithValue("$issueId", issueId.ToString());
        command.Parameters.AddWithValue("$fileName", fileName);
        command.Parameters.AddWithValue("$contentType", contentType);
        command.Parameters.AddWithValue("$content", content);
        command.Parameters.AddWithValue("$sizeBytes", content.LongLength);
        command.Parameters.AddWithValue("$createdUtc", SerializeTimestamp(createdAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);

        await RefreshIssueCountersAsync(connection, transaction, issueId, createdAtUtc, cancellationToken);
        await InsertActivityEventAsync(
            connection,
            transaction,
            issueId,
            "issue.attachment.added",
            $"Attachment \"{fileName}\" was added.",
            createdAtUtc,
            cancellationToken);

        return new IssueAttachment(attachmentId, issueId, fileName, contentType, content.LongLength, createdAtUtc);
    }

    private static async Task RefreshIssueCountersAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid issueId,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken)
    {
        // 요약 리스트와 상세 화면이 같은 숫자를 공유해야 하므로,
        // 댓글/첨부 변화가 생길 때마다 저장된 집계 컬럼도 즉시 맞춰 둔다.
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE issues
            SET comment_count = (
                    SELECT COUNT(1)
                    FROM issue_comments
                    WHERE issue_id = $issueId
                ),
                attachment_count = (
                    SELECT COUNT(1)
                    FROM attachments
                    WHERE issue_id = $issueId
                ),
                updated_utc = $updatedUtc
            WHERE id = $issueId;
            """;
        command.Parameters.AddWithValue("$issueId", issueId.ToString());
        command.Parameters.AddWithValue("$updatedUtc", SerializeTimestamp(updatedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IssueReminder> InsertReminderAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string workspaceId,
        Guid issueId,
        string title,
        string note,
        DateTimeOffset remindAtUtc,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        var reminderId = Guid.NewGuid();
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO reminders (
                id,
                workspace_id,
                issue_id,
                title,
                note,
                remind_utc,
                created_utc,
                dismissed_utc
            ) VALUES (
                $id,
                $workspaceId,
                $issueId,
                $title,
                $note,
                $remindUtc,
                $createdUtc,
                NULL
            );
            """;
        command.Parameters.AddWithValue("$id", reminderId.ToString());
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$issueId", issueId.ToString());
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$note", note);
        command.Parameters.AddWithValue("$remindUtc", SerializeTimestamp(remindAtUtc));
        command.Parameters.AddWithValue("$createdUtc", SerializeTimestamp(createdAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);

        return new IssueReminder(reminderId, issueId, title, note, remindAtUtc, createdAtUtc, null);
    }

    private static async Task<IssueReminder?> GetReminderByIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid reminderId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT id, issue_id, title, note, remind_utc, created_utc, dismissed_utc
            FROM reminders
            WHERE id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", reminderId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadReminder(reader)
            : null;
    }

    private static async Task<IReadOnlyList<IssueReminder>> GetActiveRemindersAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, issue_id, title, note, remind_utc, created_utc, dismissed_utc
            FROM reminders
            WHERE dismissed_utc IS NULL
            ORDER BY remind_utc ASC, created_utc ASC;
            """;

        return await ReadRemindersAsync(command, cancellationToken);
    }

    private static async Task<IReadOnlyList<IssueReminder>> GetIssueRemindersAsync(
        SqliteConnection connection,
        Guid issueId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, issue_id, title, note, remind_utc, created_utc, dismissed_utc
            FROM reminders
            WHERE issue_id = $issueId
            ORDER BY dismissed_utc IS NOT NULL ASC, remind_utc ASC, created_utc ASC;
            """;
        command.Parameters.AddWithValue("$issueId", issueId.ToString());

        return await ReadRemindersAsync(command, cancellationToken);
    }

    private static async Task<IReadOnlyList<IssueReminder>> ReadRemindersAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var reminders = new List<IssueReminder>();
        while (await reader.ReadAsync(cancellationToken))
        {
            reminders.Add(ReadReminder(reader));
        }

        return reminders;
    }

    private static IssueReminder ReadReminder(SqliteDataReader reader)
    {
        return new IssueReminder(
            Guid.Parse(reader.GetString(0)),
            reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
            reader.GetString(2),
            reader.GetString(3),
            ParseStoredTimestamp(reader.GetString(4)),
            ParseStoredTimestamp(reader.GetString(5)),
            reader.IsDBNull(6) ? null : ParseStoredTimestamp(reader.GetString(6)));
    }

    private static async Task<IReadOnlyList<IssueRelation>> GetIssueRelationsAsync(
        SqliteConnection connection,
        Guid issueId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                r.id,
                r.source_issue_id,
                r.target_issue_id,
                t.issue_number,
                t.title,
                r.relation_type,
                r.created_utc
            FROM issue_relations r
            INNER JOIN issues t ON t.id = r.target_issue_id
            WHERE r.source_issue_id = $issueId
            ORDER BY r.created_utc DESC, t.issue_number ASC;
            """;
        command.Parameters.AddWithValue("$issueId", issueId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var relations = new List<IssueRelation>();
        while (await reader.ReadAsync(cancellationToken))
        {
            relations.Add(
                new IssueRelation(
                    Guid.Parse(reader.GetString(0)),
                    Guid.Parse(reader.GetString(1)),
                    Guid.Parse(reader.GetString(2)),
                    reader.GetInt32(3),
                    reader.GetString(4),
                    ParseIssueRelationType(reader.GetString(5)),
                    ParseStoredTimestamp(reader.GetString(6))));
        }

        return relations;
    }

    private static async Task<string> GetIssueTitleAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid issueId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT title FROM issues WHERE id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", issueId.ToString());

        return (string?)await command.ExecuteScalarAsync(cancellationToken) ?? "selected issue";
    }

    private static async Task<ExportResult> WriteMarkdownExportAsync(
        SqliteConnection connection,
        string exportDirectory,
        IReadOnlyList<IssueListItem> issues,
        ExportOptions options,
        CancellationToken cancellationToken)
    {
        var outputPath = Path.Combine(exportDirectory, "tracky-export.md");
        var content = await BuildMarkdownDocumentAsync(connection, issues, options, cancellationToken);
        await File.WriteAllTextAsync(outputPath, content, Encoding.UTF8, cancellationToken);

        return new ExportResult(
            outputPath,
            issues.Count,
            options.IncludeAttachments ? await CountIncludedAttachmentsAsync(connection, issues, cancellationToken) : 0,
            options.Format);
    }

    private static async Task<ExportResult> WriteHtmlExportAsync(
        SqliteConnection connection,
        string exportDirectory,
        IReadOnlyList<IssueListItem> issues,
        ExportOptions options,
        CancellationToken cancellationToken)
    {
        var outputPath = Path.Combine(exportDirectory, "tracky-export.html");
        var markdown = await BuildMarkdownDocumentAsync(connection, issues, options, cancellationToken);
        var html =
            "<!doctype html><html><head><meta charset=\"utf-8\"><title>Tracky Export</title>" +
            "<style>body{font-family:Inter,Segoe UI,sans-serif;max-width:960px;margin:40px auto;line-height:1.55;color:#1f2937}" +
            "code,pre{font-family:Cascadia Mono,Consolas,monospace}.meta{color:#6b7280}hr{border:0;border-top:1px solid #e5e7eb;margin:28px 0}</style>" +
            "</head><body>" +
            ConvertMarkdownToHtml(markdown) +
            "</body></html>";
        await File.WriteAllTextAsync(outputPath, html, Encoding.UTF8, cancellationToken);

        return new ExportResult(
            outputPath,
            issues.Count,
            options.IncludeAttachments ? await CountIncludedAttachmentsAsync(connection, issues, cancellationToken) : 0,
            options.Format);
    }

    private static async Task<ExportResult> WriteFolderTreeExportAsync(
        SqliteConnection connection,
        string exportDirectory,
        IReadOnlyList<IssueListItem> issues,
        ExportOptions options,
        CancellationToken cancellationToken)
    {
        var attachmentCount = await WriteFolderTreeContentsAsync(
            connection,
            exportDirectory,
            issues,
            options,
            cancellationToken);

        return new ExportResult(exportDirectory, issues.Count, attachmentCount, options.Format);
    }

    private async Task<ExportResult> WritePortablePackageExportAsync(
        SqliteConnection connection,
        string exportDirectory,
        IReadOnlyList<IssueListItem> issues,
        ExportOptions options,
        CancellationToken cancellationToken)
    {
        var packageRoot = Path.Combine(exportDirectory, "package");
        Directory.CreateDirectory(packageRoot);
        var attachmentCount = await WriteFolderTreeContentsAsync(
            connection,
            packageRoot,
            issues,
            options,
            cancellationToken);

        File.Copy(_pathProvider.GetDatabasePath(), Path.Combine(packageRoot, "tracky-workspace.sqlite"), overwrite: true);
        await File.WriteAllTextAsync(
            Path.Combine(packageRoot, "manifest.json"),
            BuildPortableManifest(issues.Count, attachmentCount),
            Encoding.UTF8,
            cancellationToken);

        var packagePath = Path.Combine(exportDirectory, "tracky-portable-package.zip");
        ZipFile.CreateFromDirectory(packageRoot, packagePath);
        Directory.Delete(packageRoot, recursive: true);

        return new ExportResult(packagePath, issues.Count, attachmentCount, options.Format);
    }

    private static async Task<int> WriteFolderTreeContentsAsync(
        SqliteConnection connection,
        string targetDirectory,
        IReadOnlyList<IssueListItem> issues,
        ExportOptions options,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(targetDirectory);
        var issueDirectory = Path.Combine(targetDirectory, "issues");
        var attachmentRoot = Path.Combine(targetDirectory, "attachments");
        Directory.CreateDirectory(issueDirectory);
        Directory.CreateDirectory(attachmentRoot);

        var attachmentCount = 0;
        var index = new StringBuilder("# Tracky Folder Export").AppendLine().AppendLine();
        foreach (var issue in issues)
        {
            var fileExtension = options.BodyFormat == ExportBodyFormat.Html ? ".html" : ".md";
            var safeIssueFileName = $"issue-{issue.Number}-{SanitizeFileName(issue.Title)}{fileExtension}";
            var issueFilePath = Path.Combine(issueDirectory, safeIssueFileName);
            var issueMarkdown = await BuildSingleIssueMarkdownAsync(connection, issue, options, cancellationToken);
            var issueContent = options.BodyFormat == ExportBodyFormat.Html
                ? ConvertMarkdownToHtml(issueMarkdown)
                : issueMarkdown;

            await File.WriteAllTextAsync(issueFilePath, issueContent, Encoding.UTF8, cancellationToken);
            index.Append("- ")
                .Append('#')
                .Append(issue.Number.ToString(CultureInfo.InvariantCulture))
                .Append(' ')
                .Append(issue.Title)
                .Append(" -> issues/")
                .Append(safeIssueFileName)
                .AppendLine();

            if (options.IncludeAttachments)
            {
                attachmentCount += await WriteIssueAttachmentsAsync(
                    connection,
                    issue.Id,
                    Path.Combine(attachmentRoot, $"issue-{issue.Number}"),
                    cancellationToken);
            }
        }

        await File.WriteAllTextAsync(Path.Combine(targetDirectory, "index.md"), index.ToString(), Encoding.UTF8, cancellationToken);
        return attachmentCount;
    }

    private static async Task<string> BuildMarkdownDocumentAsync(
        SqliteConnection connection,
        IReadOnlyList<IssueListItem> issues,
        ExportOptions options,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder()
            .AppendLine("# Tracky Export")
            .AppendLine()
            .Append("Generated at ")
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
            .AppendLine(".")
            .AppendLine()
            .Append("Issue count: ")
            .AppendLine(issues.Count.ToString(CultureInfo.InvariantCulture))
            .AppendLine();

        foreach (var issue in issues)
        {
            builder.Append(await BuildSingleIssueMarkdownAsync(connection, issue, options, cancellationToken));
            builder.AppendLine();
            builder.AppendLine("---");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static async Task<string> BuildSingleIssueMarkdownAsync(
        SqliteConnection connection,
        IssueListItem issue,
        ExportOptions options,
        CancellationToken cancellationToken)
    {
        var description = await GetIssueDescriptionAsync(connection, issue.Id, cancellationToken);
        var builder = new StringBuilder()
            .Append("# #")
            .Append(issue.Number.ToString(CultureInfo.InvariantCulture))
            .Append(' ')
            .AppendLine(issue.Title)
            .AppendLine()
            .Append("- State: ")
            .AppendLine(SerializeState(issue.State))
            .Append("- Reason: ")
            .AppendLine(SerializeReason(issue.StateReason))
            .Append("- Priority: ")
            .AppendLine(SerializePriority(issue.Priority))
            .Append("- Assignee: ")
            .AppendLine(issue.AssigneeDisplayName ?? "Unassigned")
            .Append("- Project: ")
            .AppendLine(issue.ProjectName ?? "None")
            .Append("- Due date: ")
            .AppendLine(SerializeDate(issue.DueDate) ?? "None")
            .Append("- Labels: ")
            .AppendLine(FormatExportLabels(issue.Labels))
            .AppendLine()
            .AppendLine("## Description")
            .AppendLine()
            .AppendLine(FormatBodyForExport(description, options.BodyFormat))
            .AppendLine();

        if (options.IncludeComments)
        {
            var comments = await GetIssueCommentsAsync(connection, issue.Id, cancellationToken);
            builder.AppendLine("## Comments").AppendLine();
            foreach (var comment in comments)
            {
                builder.Append("### ")
                    .Append(comment.AuthorDisplayName)
                    .Append(" at ")
                    .AppendLine(comment.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
                    .AppendLine()
                    .AppendLine(FormatBodyForExport(comment.Body, options.BodyFormat))
                    .AppendLine();
            }
        }

        if (options.IncludeActivity)
        {
            var activity = await GetIssueActivityAsync(connection, issue.Id, cancellationToken);
            builder.AppendLine("## Activity").AppendLine();
            foreach (var item in activity)
            {
                builder.Append("- ")
                    .Append(item.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
                    .Append(" [")
                    .Append(item.EventType)
                    .Append("] ")
                    .AppendLine(item.Summary);
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static async Task<IReadOnlyList<IssueListItem>> ResolveExportIssuesAsync(
        SqliteConnection connection,
        ExportOptions options,
        CancellationToken cancellationToken)
    {
        var allIssues = await GetIssuesAsync(connection, cancellationToken);
        IEnumerable<IssueListItem> selectedIssues = options.Scope switch
        {
            ExportSelectionScope.CurrentIssue when options.IssueId is Guid issueId =>
                allIssues.Where(issue => issue.Id == issueId),
            ExportSelectionScope.CurrentFilter =>
                allIssues.Where(issue => options.IssueIds.Contains(issue.Id)),
            ExportSelectionScope.Project when options.ProjectId is Guid projectId =>
                await ResolveProjectIssuesAsync(connection, allIssues, projectId, cancellationToken),
            ExportSelectionScope.Workspace =>
                allIssues,
            _ => [],
        };

        if (!options.IncludeClosedIssues)
        {
            selectedIssues = selectedIssues.Where(static issue => issue.State == IssueWorkflowState.Open);
        }

        return [.. selectedIssues.OrderBy(static issue => issue.Number)];
    }

    private static async Task<IReadOnlyList<IssueListItem>> ResolveProjectIssuesAsync(
        SqliteConnection connection,
        IReadOnlyList<IssueListItem> issues,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var project = await GetProjectSummaryByIdAsync(connection, projectId, cancellationToken);
        if (project is null)
        {
            return [];
        }

        return [.. issues.Where(issue => string.Equals(issue.ProjectName, project.Name, StringComparison.OrdinalIgnoreCase))];
    }

    private static string PrepareExportDirectory(string? requestedDirectory)
    {
        var parentDirectory = string.IsNullOrWhiteSpace(requestedDirectory)
            ? Path.Combine(Path.GetTempPath(), "Tracky", "exports")
            : requestedDirectory;
        var exportDirectory = Path.Combine(
            parentDirectory,
            $"tracky-export-{DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)}");
        Directory.CreateDirectory(exportDirectory);
        return exportDirectory;
    }

    private static async Task<int> CountIncludedAttachmentsAsync(
        SqliteConnection connection,
        IReadOnlyList<IssueListItem> issues,
        CancellationToken cancellationToken)
    {
        var total = 0;
        foreach (var issue in issues)
        {
            total += (await GetIssueAttachmentsAsync(connection, issue.Id, cancellationToken)).Count;
        }

        return total;
    }

    private static async Task<int> WriteIssueAttachmentsAsync(
        SqliteConnection connection,
        Guid issueId,
        string targetDirectory,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT file_name, content
            FROM attachments
            WHERE issue_id = $issueId
            ORDER BY created_utc ASC, file_name ASC;
            """;
        command.Parameters.AddWithValue("$issueId", issueId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var attachmentCount = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.IsDBNull(1))
            {
                continue;
            }

            Directory.CreateDirectory(targetDirectory);
            var outputPath = Path.Combine(targetDirectory, SanitizeFileName(reader.GetString(0)));
            await File.WriteAllBytesAsync(outputPath, (byte[])reader["content"], cancellationToken);
            attachmentCount++;
        }

        return attachmentCount;
    }

    private static async Task<ExportPreset> UpsertExportPresetAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string workspaceId,
        AddExportPresetInput input,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO export_presets (
                id,
                workspace_id,
                name,
                scope,
                format,
                body_format,
                include_comments,
                include_activity,
                include_attachments,
                include_closed_issues,
                created_utc,
                updated_utc
            ) VALUES (
                $id,
                $workspaceId,
                $name,
                $scope,
                $format,
                $bodyFormat,
                $includeComments,
                $includeActivity,
                $includeAttachments,
                $includeClosedIssues,
                $createdUtc,
                $updatedUtc
            )
            ON CONFLICT(workspace_id, name) DO UPDATE SET
                scope = excluded.scope,
                format = excluded.format,
                body_format = excluded.body_format,
                include_comments = excluded.include_comments,
                include_activity = excluded.include_activity,
                include_attachments = excluded.include_attachments,
                include_closed_issues = excluded.include_closed_issues,
                updated_utc = excluded.updated_utc;
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$name", input.Name.Trim());
        command.Parameters.AddWithValue("$scope", SerializeExportSelectionScope(input.Scope));
        command.Parameters.AddWithValue("$format", SerializeExportFormat(input.Format));
        command.Parameters.AddWithValue("$bodyFormat", SerializeExportBodyFormat(input.BodyFormat));
        command.Parameters.AddWithValue("$includeComments", input.IncludeComments ? 1 : 0);
        command.Parameters.AddWithValue("$includeActivity", input.IncludeActivity ? 1 : 0);
        command.Parameters.AddWithValue("$includeAttachments", input.IncludeAttachments ? 1 : 0);
        command.Parameters.AddWithValue("$includeClosedIssues", input.IncludeClosedIssues ? 1 : 0);
        command.Parameters.AddWithValue("$createdUtc", SerializeTimestamp(now));
        command.Parameters.AddWithValue("$updatedUtc", SerializeTimestamp(now));
        await command.ExecuteNonQueryAsync(cancellationToken);

        return (await GetExportPresetByNameAsync(connection, transaction, workspaceId, input.Name.Trim(), cancellationToken))!;
    }

    private static async Task<IReadOnlyList<ExportPreset>> GetExportPresetsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var workspace = await GetWorkspaceAsync(connection, cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                id,
                name,
                scope,
                format,
                body_format,
                include_comments,
                include_activity,
                include_attachments,
                include_closed_issues,
                updated_utc
            FROM export_presets
            WHERE workspace_id = $workspaceId
            ORDER BY name ASC;
            """;
        command.Parameters.AddWithValue("$workspaceId", workspace.Id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var presets = new List<ExportPreset>();
        while (await reader.ReadAsync(cancellationToken))
        {
            presets.Add(ReadExportPreset(reader));
        }

        return presets;
    }

    private static async Task<ExportPreset?> GetExportPresetByNameAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string workspaceId,
        string name,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
                id,
                name,
                scope,
                format,
                body_format,
                include_comments,
                include_activity,
                include_attachments,
                include_closed_issues,
                updated_utc
            FROM export_presets
            WHERE workspace_id = $workspaceId AND lower(name) = lower($name)
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$name", name);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadExportPreset(reader)
            : null;
    }

    private static ExportPreset ReadExportPreset(SqliteDataReader reader)
    {
        return new ExportPreset(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            ParseExportSelectionScope(reader.GetString(2)),
            ParseExportFormat(reader.GetString(3)),
            ParseExportBodyFormat(reader.GetString(4)),
            reader.GetInt32(5) != 0,
            reader.GetInt32(6) != 0,
            reader.GetInt32(7) != 0,
            reader.GetInt32(8) != 0,
            ParseStoredTimestamp(reader.GetString(9)));
    }

    private static async Task<SavedIssueSearch> UpsertSavedIssueSearchAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string workspaceId,
        AddSavedIssueSearchInput input,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO saved_issue_searches (
                id,
                workspace_id,
                name,
                query_text,
                is_pinned,
                created_utc,
                updated_utc
            ) VALUES (
                $id,
                $workspaceId,
                $name,
                $queryText,
                $isPinned,
                $createdUtc,
                $updatedUtc
            )
            ON CONFLICT(workspace_id, name) DO UPDATE SET
                query_text = excluded.query_text,
                is_pinned = excluded.is_pinned,
                updated_utc = excluded.updated_utc;
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$name", input.Name.Trim());
        command.Parameters.AddWithValue("$queryText", input.QueryText.Trim());
        command.Parameters.AddWithValue("$isPinned", input.IsPinned ? 1 : 0);
        command.Parameters.AddWithValue("$createdUtc", SerializeTimestamp(now));
        command.Parameters.AddWithValue("$updatedUtc", SerializeTimestamp(now));
        await command.ExecuteNonQueryAsync(cancellationToken);

        return (await GetSavedIssueSearchByNameAsync(connection, transaction, workspaceId, input.Name.Trim(), cancellationToken))!;
    }

    private static async Task<IReadOnlyList<SavedIssueSearch>> GetSavedIssueSearchesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var workspace = await GetWorkspaceAsync(connection, cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, name, query_text, is_pinned, updated_utc
            FROM saved_issue_searches
            WHERE workspace_id = $workspaceId
            ORDER BY is_pinned DESC, name ASC;
            """;
        command.Parameters.AddWithValue("$workspaceId", workspace.Id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var savedSearches = new List<SavedIssueSearch>();
        while (await reader.ReadAsync(cancellationToken))
        {
            savedSearches.Add(ReadSavedIssueSearch(reader));
        }

        return savedSearches;
    }

    private static async Task<SavedIssueSearch?> GetSavedIssueSearchByNameAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string workspaceId,
        string name,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT id, name, query_text, is_pinned, updated_utc
            FROM saved_issue_searches
            WHERE workspace_id = $workspaceId AND lower(name) = lower($name)
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$name", name);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadSavedIssueSearch(reader)
            : null;
    }

    private static SavedIssueSearch ReadSavedIssueSearch(SqliteDataReader reader)
    {
        return new SavedIssueSearch(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3) != 0,
            ParseStoredTimestamp(reader.GetString(4)));
    }

    private static async Task<IReadOnlyList<MilestoneSummary>> GetMilestonesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                m.id,
                m.name,
                m.due_date,
                SUM(CASE WHEN i.state = 'open' THEN 1 ELSE 0 END),
                SUM(CASE WHEN i.state = 'closed' THEN 1 ELSE 0 END)
            FROM milestones m
            LEFT JOIN issues i ON lower(i.milestone_name) = lower(m.name)
            GROUP BY m.id, m.name, m.due_date
            ORDER BY m.due_date IS NULL ASC, m.due_date ASC, m.name ASC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var milestones = new List<MilestoneSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            milestones.Add(
                new MilestoneSummary(
                    Guid.Parse(reader.GetString(0)),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : ParseStoredDate(reader.GetString(2)),
                    ReadNullableInt32(reader, 3),
                    ReadNullableInt32(reader, 4)));
        }

        return milestones;
    }

    private static async Task<IReadOnlyList<IssueTypeDefinition>> GetIssueTypesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, name, color_hex, description
            FROM issue_types
            ORDER BY name ASC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var issueTypes = new List<IssueTypeDefinition>();
        while (await reader.ReadAsync(cancellationToken))
        {
            issueTypes.Add(
                new IssueTypeDefinition(
                    Guid.Parse(reader.GetString(0)),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3)));
        }

        return issueTypes;
    }

    private static async Task<WorkspacePreferences> GetWorkspacePreferencesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var workspace = await GetWorkspaceAsync(connection, cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT theme, compact_density, shortcut_profile, updated_utc
            FROM workspace_preferences
            WHERE workspace_id = $workspaceId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$workspaceId", workspace.Id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new WorkspacePreferences(
                ParseTheme(reader.GetString(0)),
                reader.GetInt32(1) != 0,
                reader.GetString(2),
                ParseStoredTimestamp(reader.GetString(3)))
            : WorkspacePreferences.Default;
    }

    private static string FormatExportLabels(IReadOnlyList<string> labels)
    {
        return labels.Count == 0 ? "None" : string.Join(", ", labels);
    }

    private static string FormatBodyForExport(string body, ExportBodyFormat bodyFormat)
    {
        return bodyFormat == ExportBodyFormat.Html
            ? ConvertMarkdownToHtml(body)
            : body;
    }

    private static string ConvertMarkdownToHtml(string markdown)
    {
        var builder = new StringBuilder();
        var inCodeBlock = false;
        var inList = false;

        foreach (var rawLine in markdown.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                if (inList)
                {
                    builder.AppendLine("</ul>");
                    inList = false;
                }

                builder.AppendLine(inCodeBlock ? "</code></pre>" : "<pre><code>");
                inCodeBlock = !inCodeBlock;
                continue;
            }

            if (inCodeBlock)
            {
                builder.AppendLine(WebUtility.HtmlEncode(line));
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                if (inList)
                {
                    builder.AppendLine("</ul>");
                    inList = false;
                }

                continue;
            }

            if (line.StartsWith("- [ ] ", StringComparison.Ordinal) || line.StartsWith("- [x] ", StringComparison.OrdinalIgnoreCase))
            {
                if (!inList)
                {
                    builder.AppendLine("<ul>");
                    inList = true;
                }

                var isChecked = line.StartsWith("- [x] ", StringComparison.OrdinalIgnoreCase);
                builder.Append("<li><input type=\"checkbox\" disabled");
                if (isChecked)
                {
                    builder.Append(" checked");
                }

                builder.Append("> ")
                    .Append(WebUtility.HtmlEncode(line[6..]))
                    .AppendLine("</li>");
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                if (!inList)
                {
                    builder.AppendLine("<ul>");
                    inList = true;
                }

                builder.Append("<li>")
                    .Append(WebUtility.HtmlEncode(line[2..]))
                    .AppendLine("</li>");
                continue;
            }

            if (inList)
            {
                builder.AppendLine("</ul>");
                inList = false;
            }

            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                builder.Append("<h1>")
                    .Append(WebUtility.HtmlEncode(line[2..]))
                    .AppendLine("</h1>");
            }
            else if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                builder.Append("<h2>")
                    .Append(WebUtility.HtmlEncode(line[3..]))
                    .AppendLine("</h2>");
            }
            else if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                builder.Append("<h3>")
                    .Append(WebUtility.HtmlEncode(line[4..]))
                    .AppendLine("</h3>");
            }
            else
            {
                builder.Append("<p>")
                    .Append(WebUtility.HtmlEncode(line))
                    .AppendLine("</p>");
            }
        }

        if (inList)
        {
            builder.AppendLine("</ul>");
        }

        if (inCodeBlock)
        {
            builder.AppendLine("</code></pre>");
        }

        return builder.ToString();
    }

    private static string BuildPortableManifest(int issueCount, int attachmentCount)
    {
        return $$"""
            {
              "product": "Tracky",
              "createdUtc": "{{DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)}}",
              "issueCount": {{issueCount.ToString(CultureInfo.InvariantCulture)}},
              "attachmentCount": {{attachmentCount.ToString(CultureInfo.InvariantCulture)}},
              "database": "tracky-workspace.sqlite"
            }
            """;
    }

    private static async Task<WorkspaceRow> GetWorkspaceAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, name, description
            FROM workspaces
            ORDER BY created_utc
            LIMIT 1;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Tracky workspace bootstrap did not create a default workspace.");
        }

        return new WorkspaceRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2));
    }

    private static async Task<int> GetNextIssueNumberAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(MAX(issue_number), 100) + 1 FROM issues;";
        return ReadScalarInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task EnsureIssueMetadataAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string workspaceId,
        string? milestoneName,
        string? issueTypeName,
        DateOnly? milestoneDueDate,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var normalizedMilestone = Normalize(milestoneName);
        if (normalizedMilestone is not null)
        {
            await EnsureMilestoneAsync(
                connection,
                transaction,
                workspaceId,
                normalizedMilestone,
                milestoneDueDate,
                now,
                cancellationToken);
        }

        var normalizedIssueType = Normalize(issueTypeName);
        if (normalizedIssueType is not null)
        {
            await EnsureIssueTypeAsync(
                connection,
                transaction,
                workspaceId,
                normalizedIssueType,
                GetIssueTypeColor(normalizedIssueType),
                $"{normalizedIssueType} issues are grouped by this local Tracky type.",
                now,
                cancellationToken);
        }
    }

    private static async Task<Guid> EnsureMilestoneAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string workspaceId,
        string name,
        DateOnly? dueDate,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO milestones (id, workspace_id, name, due_date, created_utc, updated_utc)
            VALUES ($id, $workspaceId, $name, $dueDate, $createdUtc, $updatedUtc)
            ON CONFLICT(workspace_id, name) DO UPDATE SET
                due_date = COALESCE(excluded.due_date, milestones.due_date),
                updated_utc = excluded.updated_utc;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$dueDate", (object?)SerializeDate(dueDate) ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdUtc", SerializeTimestamp(now));
        command.Parameters.AddWithValue("$updatedUtc", SerializeTimestamp(now));
        await command.ExecuteNonQueryAsync(cancellationToken);

        return id;
    }

    private static async Task<Guid> EnsureIssueTypeAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string workspaceId,
        string name,
        string colorHex,
        string description,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO issue_types (id, workspace_id, name, color_hex, description, created_utc, updated_utc)
            VALUES ($id, $workspaceId, $name, $colorHex, $description, $createdUtc, $updatedUtc)
            ON CONFLICT(workspace_id, name) DO UPDATE SET
                color_hex = excluded.color_hex,
                description = excluded.description,
                updated_utc = excluded.updated_utc;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$colorHex", colorHex);
        command.Parameters.AddWithValue("$description", description);
        command.Parameters.AddWithValue("$createdUtc", SerializeTimestamp(now));
        command.Parameters.AddWithValue("$updatedUtc", SerializeTimestamp(now));
        await command.ExecuteNonQueryAsync(cancellationToken);

        return id;
    }

    private static async Task<Guid> EnsureProjectAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string workspaceId,
        string projectName,
        string description,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var selectCommand = connection.CreateCommand();
        selectCommand.Transaction = transaction;
        selectCommand.CommandText =
            """
            SELECT id
            FROM projects
            WHERE workspace_id = $workspaceId AND lower(name) = lower($name)
            LIMIT 1;
            """;
        selectCommand.Parameters.AddWithValue("$workspaceId", workspaceId);
        selectCommand.Parameters.AddWithValue("$name", projectName);

        var existingId = await selectCommand.ExecuteScalarAsync(cancellationToken);
        if (existingId is string id)
        {
            return Guid.Parse(id);
        }

        var projectId = Guid.NewGuid();
        var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText =
            """
            INSERT INTO projects (id, workspace_id, name, description, created_utc, updated_utc)
            VALUES ($id, $workspaceId, $name, $description, $createdUtc, $updatedUtc);
            """;
        insertCommand.Parameters.AddWithValue("$id", projectId.ToString());
        insertCommand.Parameters.AddWithValue("$workspaceId", workspaceId);
        insertCommand.Parameters.AddWithValue("$name", projectName);
        insertCommand.Parameters.AddWithValue("$description", description);
        insertCommand.Parameters.AddWithValue("$createdUtc", SerializeTimestamp(now));
        insertCommand.Parameters.AddWithValue("$updatedUtc", SerializeTimestamp(now));
        await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        return projectId;
    }

    private static async Task EnsureDefaultProjectMetadataAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid projectId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // 기본 커스텀 필드와 저장 뷰는 프로젝트 화면이 처음 열릴 때 비어 보이지 않도록 하는 최소 Phase 2 씨앗이다.
        // INSERT OR IGNORE를 사용해 사용자가 나중에 같은 이름을 세밀하게 조정해도 부트스트랩이 값을 덮어쓰지 않는다.
        await InsertDefaultProjectCustomFieldAsync(
            connection,
            transaction,
            projectId,
            "Status",
            ProjectCustomFieldType.SingleSelect,
            string.Join(", ", BoardColumns),
            now,
            cancellationToken);
        await InsertDefaultProjectCustomFieldAsync(
            connection,
            transaction,
            projectId,
            "Target date",
            ProjectCustomFieldType.Date,
            string.Empty,
            now,
            cancellationToken);
        await InsertDefaultProjectCustomFieldAsync(
            connection,
            transaction,
            projectId,
            "Effort",
            ProjectCustomFieldType.Number,
            string.Empty,
            now,
            cancellationToken);

        await InsertDefaultProjectSavedViewAsync(
            connection,
            transaction,
            projectId,
            "Board",
            ProjectViewMode.Board,
            "is:open",
            "Board position",
            "Status",
            now,
            cancellationToken);
        await InsertDefaultProjectSavedViewAsync(
            connection,
            transaction,
            projectId,
            "Table",
            ProjectViewMode.Table,
            string.Empty,
            "Issue number",
            "Priority",
            now,
            cancellationToken);
        await InsertDefaultProjectSavedViewAsync(
            connection,
            transaction,
            projectId,
            "Calendar",
            ProjectViewMode.Calendar,
            "has:due-date",
            "Due date",
            "Due date",
            now,
            cancellationToken);
        await InsertDefaultProjectSavedViewAsync(
            connection,
            transaction,
            projectId,
            "Timeline",
            ProjectViewMode.Timeline,
            string.Empty,
            "Due date",
            "Due date",
            now,
            cancellationToken);
    }

    private static async Task InsertDefaultProjectCustomFieldAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid projectId,
        string name,
        ProjectCustomFieldType fieldType,
        string optionsText,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT OR IGNORE INTO project_custom_fields (
                id,
                project_id,
                name,
                field_type,
                options_text,
                created_utc,
                updated_utc
            ) VALUES (
                $id,
                $projectId,
                $name,
                $fieldType,
                $optionsText,
                $createdUtc,
                $updatedUtc
            );
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$fieldType", SerializeCustomFieldType(fieldType));
        command.Parameters.AddWithValue("$optionsText", optionsText);
        command.Parameters.AddWithValue("$createdUtc", SerializeTimestamp(now));
        command.Parameters.AddWithValue("$updatedUtc", SerializeTimestamp(now));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertDefaultProjectSavedViewAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid projectId,
        string name,
        ProjectViewMode viewMode,
        string filterText,
        string sortByField,
        string groupByField,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT OR IGNORE INTO project_saved_views (
                id,
                project_id,
                name,
                view_mode,
                filter_text,
                sort_by_field,
                group_by_field,
                created_utc,
                updated_utc
            ) VALUES (
                $id,
                $projectId,
                $name,
                $viewMode,
                $filterText,
                $sortByField,
                $groupByField,
                $createdUtc,
                $updatedUtc
            );
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$viewMode", SerializeProjectViewMode(viewMode));
        command.Parameters.AddWithValue("$filterText", filterText);
        command.Parameters.AddWithValue("$sortByField", sortByField);
        command.Parameters.AddWithValue("$groupByField", groupByField);
        command.Parameters.AddWithValue("$createdUtc", SerializeTimestamp(now));
        command.Parameters.AddWithValue("$updatedUtc", SerializeTimestamp(now));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertProjectCustomFieldAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid projectId,
        string name,
        ProjectCustomFieldType fieldType,
        string optionsText,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO project_custom_fields (
                id,
                project_id,
                name,
                field_type,
                options_text,
                created_utc,
                updated_utc
            ) VALUES (
                $id,
                $projectId,
                $name,
                $fieldType,
                $optionsText,
                $createdUtc,
                $updatedUtc
            )
            ON CONFLICT(project_id, name) DO UPDATE SET
                field_type = excluded.field_type,
                options_text = excluded.options_text,
                updated_utc = excluded.updated_utc;
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$fieldType", SerializeCustomFieldType(fieldType));
        command.Parameters.AddWithValue("$optionsText", optionsText);
        command.Parameters.AddWithValue("$createdUtc", SerializeTimestamp(now));
        command.Parameters.AddWithValue("$updatedUtc", SerializeTimestamp(now));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertProjectSavedViewAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid projectId,
        string name,
        ProjectViewMode viewMode,
        string filterText,
        string sortByField,
        string groupByField,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO project_saved_views (
                id,
                project_id,
                name,
                view_mode,
                filter_text,
                sort_by_field,
                group_by_field,
                created_utc,
                updated_utc
            ) VALUES (
                $id,
                $projectId,
                $name,
                $viewMode,
                $filterText,
                $sortByField,
                $groupByField,
                $createdUtc,
                $updatedUtc
            )
            ON CONFLICT(project_id, name) DO UPDATE SET
                view_mode = excluded.view_mode,
                filter_text = excluded.filter_text,
                sort_by_field = excluded.sort_by_field,
                group_by_field = excluded.group_by_field,
                updated_utc = excluded.updated_utc;
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$viewMode", SerializeProjectViewMode(viewMode));
        command.Parameters.AddWithValue("$filterText", filterText);
        command.Parameters.AddWithValue("$sortByField", sortByField);
        command.Parameters.AddWithValue("$groupByField", groupByField);
        command.Parameters.AddWithValue("$createdUtc", SerializeTimestamp(now));
        command.Parameters.AddWithValue("$updatedUtc", SerializeTimestamp(now));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> ProjectExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(1) FROM projects WHERE id = $id;";
        command.Parameters.AddWithValue("$id", projectId.ToString());
        return ReadScalarInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task TouchProjectAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid projectId,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE projects
            SET updated_utc = $updatedUtc
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", projectId.ToString());
        command.Parameters.AddWithValue("$updatedUtc", SerializeTimestamp(updatedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task TouchProjectItemAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid projectItemId,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE project_items
            SET updated_utc = $updatedUtc
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", projectItemId.ToString());
        command.Parameters.AddWithValue("$updatedUtc", SerializeTimestamp(updatedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> ProjectCustomFieldBelongsToProjectAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid projectId,
        Guid customFieldId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT COUNT(1)
            FROM project_custom_fields
            WHERE id = $customFieldId AND project_id = $projectId;
            """;
        command.Parameters.AddWithValue("$customFieldId", customFieldId.ToString());
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        return ReadScalarInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task<ProjectItemIdentity?> GetProjectItemIdentityAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid projectItemId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT project_id, issue_id
            FROM project_items
            WHERE id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", projectItemId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ProjectItemIdentity(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)))
            : null;
    }

    private static async Task<ProjectItemIdentity?> GetProjectItemForIssueAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid projectId,
        Guid issueId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT id, issue_id
            FROM project_items
            WHERE project_id = $projectId AND issue_id = $issueId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        command.Parameters.AddWithValue("$issueId", issueId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ProjectItemIdentity(
                projectId,
                Guid.Parse(reader.GetString(1)),
                Guid.Parse(reader.GetString(0)))
            : null;
    }

    private static async Task<int> GetNextProjectItemSortOrderAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid projectId,
        string boardColumn,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT COALESCE(MAX(sort_order), 0) + 1
            FROM project_items
            WHERE project_id = $projectId AND board_column = $boardColumn;
            """;
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        command.Parameters.AddWithValue("$boardColumn", boardColumn);
        return ReadScalarInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<IReadOnlyList<Guid>> GetProjectIdsForIssueAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid issueId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT DISTINCT project_id
            FROM project_items
            WHERE issue_id = $issueId;
            """;
        command.Parameters.AddWithValue("$issueId", issueId.ToString());

        var projectIds = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            projectIds.Add(Guid.Parse(reader.GetString(0)));
        }

        return projectIds;
    }

    private static async Task<IssueWorkflowState> GetIssueStateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid issueId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT state
            FROM issues
            WHERE id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", issueId.ToString());
        var state = await command.ExecuteScalarAsync(cancellationToken);
        return state is string text
            ? ParseState(text)
            : IssueWorkflowState.Open;
    }

    private static async Task<IssueUpdateSnapshot?> GetIssueUpdateSnapshotAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid issueId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT title, description, priority, assignee_display_name, due_date, project_name, milestone_name, issue_type_name
            FROM issues
            WHERE id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", issueId.ToString());

        string title;
        string description;
        IssuePriority priority;
        string? assigneeDisplayName;
        DateOnly? dueDate;
        string? projectName;
        string? milestoneName;
        string? issueTypeName;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            title = reader.GetString(0);
            description = reader.GetString(1);
            priority = ParsePriority(reader.GetString(2));
            assigneeDisplayName = reader.IsDBNull(3) ? null : reader.GetString(3);
            dueDate = reader.IsDBNull(4) ? null : ParseStoredDate(reader.GetString(4));
            projectName = reader.IsDBNull(5) ? null : reader.GetString(5);
            milestoneName = reader.IsDBNull(6) ? null : reader.GetString(6);
            issueTypeName = reader.IsDBNull(7) ? null : reader.GetString(7);
        }

        var labels = await GetIssueLabelsForUpdateAsync(connection, transaction, issueId, cancellationToken);
        return new IssueUpdateSnapshot(
            title,
            description,
            priority,
            assigneeDisplayName,
            dueDate,
            projectName,
            milestoneName,
            issueTypeName,
            labels);
    }

    private static async Task<IReadOnlyList<string>> GetIssueLabelsForUpdateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid issueId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT l.name
            FROM issue_labels il
            INNER JOIN labels l ON l.id = il.label_id
            WHERE il.issue_id = $issueId
            ORDER BY l.name ASC;
            """;
        command.Parameters.AddWithValue("$issueId", issueId.ToString());

        var labels = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            labels.Add(reader.GetString(0));
        }

        return labels;
    }

    private static string BuildIssueUpdateSummary(IssueUpdateSnapshot? snapshot, UpdateIssueInput input)
    {
        if (snapshot is null)
        {
            return "Issue title, body, and metadata were updated.";
        }

        var changes = new List<string>();
        if (!string.Equals(snapshot.Title, input.Title.Trim(), StringComparison.Ordinal))
        {
            changes.Add("title");
        }

        if (!string.Equals(snapshot.Description, input.Description.Trim(), StringComparison.Ordinal))
        {
            changes.Add("body");
        }

        if (snapshot.Priority != input.Priority)
        {
            changes.Add($"priority from {SerializePriority(snapshot.Priority)} to {SerializePriority(input.Priority)}");
        }

        var assignee = Normalize(input.AssigneeDisplayName);
        if (!string.Equals(snapshot.AssigneeDisplayName, assignee, StringComparison.OrdinalIgnoreCase))
        {
            changes.Add($"assignee from {snapshot.AssigneeDisplayName ?? "unassigned"} to {assignee ?? "unassigned"}");
        }

        if (snapshot.DueDate != input.DueDate)
        {
            changes.Add($"due date from {SerializeDate(snapshot.DueDate) ?? "none"} to {SerializeDate(input.DueDate) ?? "none"}");
        }

        var projectName = Normalize(input.ProjectName);
        if (!string.Equals(snapshot.ProjectName, projectName, StringComparison.OrdinalIgnoreCase))
        {
            changes.Add($"project from {snapshot.ProjectName ?? "none"} to {projectName ?? "none"}");
        }

        var milestoneName = Normalize(input.MilestoneName);
        if (!string.Equals(snapshot.MilestoneName, milestoneName, StringComparison.OrdinalIgnoreCase))
        {
            changes.Add($"milestone from {snapshot.MilestoneName ?? "none"} to {milestoneName ?? "none"}");
        }

        var issueTypeName = Normalize(input.IssueTypeName);
        if (!string.Equals(snapshot.IssueTypeName, issueTypeName, StringComparison.OrdinalIgnoreCase))
        {
            changes.Add($"type from {snapshot.IssueTypeName ?? "none"} to {issueTypeName ?? "none"}");
        }

        var normalizedLabels = input.Labels
            .Select(Normalize)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static label => label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!snapshot.Labels
            .OrderBy(static label => label, StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(normalizedLabels, StringComparer.OrdinalIgnoreCase))
        {
            changes.Add("labels");
        }

        return changes.Count == 0
            ? "Issue was saved without metadata changes."
            : $"Updated {string.Join(", ", changes)}.";
    }

    private static async Task SyncLabelsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string workspaceId,
        Guid issueId,
        IReadOnlyList<string> labels,
        CancellationToken cancellationToken)
    {
        var clearCommand = connection.CreateCommand();
        clearCommand.Transaction = transaction;
        clearCommand.CommandText = "DELETE FROM issue_labels WHERE issue_id = $issueId;";
        clearCommand.Parameters.AddWithValue("$issueId", issueId.ToString());
        await clearCommand.ExecuteNonQueryAsync(cancellationToken);

        foreach (var label in labels
            .Select(Normalize)
            .OfType<string>()
            .Where(static label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var labelId = await EnsureLabelAsync(connection, transaction, workspaceId, label, cancellationToken);

            var linkCommand = connection.CreateCommand();
            linkCommand.Transaction = transaction;
            linkCommand.CommandText =
                """
                INSERT INTO issue_labels (issue_id, label_id)
                VALUES ($issueId, $labelId);
                """;
            linkCommand.Parameters.AddWithValue("$issueId", issueId.ToString());
            linkCommand.Parameters.AddWithValue("$labelId", labelId);
            await linkCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<string> EnsureLabelAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string workspaceId,
        string labelName,
        CancellationToken cancellationToken)
    {
        var normalizedLabel = Normalize(labelName)!;

        var selectCommand = connection.CreateCommand();
        selectCommand.Transaction = transaction;
        selectCommand.CommandText =
            """
            SELECT id
            FROM labels
            WHERE workspace_id = $workspaceId AND lower(name) = lower($name)
            LIMIT 1;
            """;
        selectCommand.Parameters.AddWithValue("$workspaceId", workspaceId);
        selectCommand.Parameters.AddWithValue("$name", normalizedLabel);

        var existingId = await selectCommand.ExecuteScalarAsync(cancellationToken);
        if (existingId is string id)
        {
            return id;
        }

        var newId = Guid.NewGuid().ToString();
        var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText =
            """
            INSERT INTO labels (id, workspace_id, name, color_hex, created_utc)
            VALUES ($id, $workspaceId, $name, $colorHex, $createdUtc);
            """;
        insertCommand.Parameters.AddWithValue("$id", newId);
        insertCommand.Parameters.AddWithValue("$workspaceId", workspaceId);
        insertCommand.Parameters.AddWithValue("$name", normalizedLabel);
        insertCommand.Parameters.AddWithValue("$colorHex", GetLabelColor(normalizedLabel));
        insertCommand.Parameters.AddWithValue("$createdUtc", SerializeTimestamp(DateTimeOffset.UtcNow));
        await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        return newId;
    }

    private static string GetLabelColor(string labelName)
    {
        // 해시를 uint로 바꾸면 int.MinValue 같은 극단값도 예외 없이 팔레트 범위로 접을 수 있다.
        var hash = (uint)StringComparer.OrdinalIgnoreCase.GetHashCode(labelName);
        var index = (int)(hash % LabelColors.Length);
        return LabelColors[index];
    }

    private static async Task InsertActivityEventAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid issueId,
        string eventType,
        string summary,
        DateTimeOffset createdUtc,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO activity_events (id, issue_id, event_type, summary, created_utc)
            VALUES ($id, $issueId, $eventType, $summary, $createdUtc);
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$issueId", issueId.ToString());
        command.Parameters.AddWithValue("$eventType", eventType);
        command.Parameters.AddWithValue("$summary", summary);
        command.Parameters.AddWithValue("$createdUtc", SerializeTimestamp(createdUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string BuildStateTransitionSummary(IssueWorkflowState state, IssueStateReason reason)
    {
        var normalizedReason = NormalizeStateReason(state, reason);

        return state == IssueWorkflowState.Open
            ? "Issue was reopened for active work."
            : normalizedReason switch
            {
                IssueStateReason.Completed => "Issue was closed as completed.",
                IssueStateReason.NotPlanned => "Issue was closed as not planned.",
                IssueStateReason.Duplicate => "Issue was closed as a duplicate.",
                _ => "Issue state was updated.",
            };
    }

    private static IssueStateReason NormalizeStateReason(IssueWorkflowState state, IssueStateReason reason)
    {
        // 닫힌 이슈가 reason:none으로 남으면 목록 필터와 타임라인 의미가 흐려지므로,
        // Phase 1에서는 명시 사유가 비어 있을 때 completed를 안전한 기본값으로 사용한다.
        if (state == IssueWorkflowState.Open)
        {
            return IssueStateReason.None;
        }

        return reason == IssueStateReason.None
            ? IssueStateReason.Completed
            : reason;
    }

    private static async Task<IReadOnlyList<IssueListItem>> GetIssuesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                i.id,
                i.issue_number,
                i.title,
                i.state,
                i.state_reason,
                i.priority,
                i.assignee_display_name,
                i.due_date,
                i.updated_utc,
                i.project_name,
                i.comment_count,
                i.attachment_count,
                i.milestone_name,
                i.issue_type_name,
                l.name
            FROM issues i
            LEFT JOIN issue_labels il ON il.issue_id = i.id
            LEFT JOIN labels l ON l.id = il.label_id
            ORDER BY i.updated_utc DESC, i.issue_number DESC, l.name ASC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<IssueListItem>();
        IssueAccumulator? current = null;

        while (await reader.ReadAsync(cancellationToken))
        {
            var id = Guid.Parse(reader.GetString(0));
            if (current is null || current.Id != id)
            {
                if (current is not null)
                {
                    items.Add(current.ToIssueListItem());
                }

                current = new IssueAccumulator(
                    id,
                    reader.GetInt32(1),
                    reader.GetString(2),
                    ParseState(reader.GetString(3)),
                    ParseReason(reader.GetString(4)),
                    ParsePriority(reader.GetString(5)),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : ParseStoredDate(reader.GetString(7)),
                    ParseStoredTimestamp(reader.GetString(8)),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.GetInt32(10),
                    reader.GetInt32(11),
                    reader.IsDBNull(12) ? null : reader.GetString(12),
                    reader.IsDBNull(13) ? null : reader.GetString(13));
            }

            if (!reader.IsDBNull(14))
            {
                current.Labels.Add(reader.GetString(14));
            }
        }

        if (current is not null)
        {
            items.Add(current.ToIssueListItem());
        }

        return items;
    }

    private static async Task<IssueListItem?> GetIssueByIdAsync(
        SqliteConnection connection,
        Guid issueId,
        CancellationToken cancellationToken)
    {
        var issues = await GetIssuesAsync(connection, cancellationToken);
        return issues.FirstOrDefault(issue => issue.Id == issueId);
    }

    private static async Task<IReadOnlyList<ProjectSummary>> GetProjectSummariesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                p.id,
                p.name,
                p.description,
                p.updated_utc,
                COUNT(pi.id),
                SUM(CASE WHEN i.state = 'open' THEN 1 ELSE 0 END),
                SUM(CASE WHEN i.state = 'closed' THEN 1 ELSE 0 END)
            FROM projects p
            LEFT JOIN project_items pi ON pi.project_id = p.id
            LEFT JOIN issues i ON i.id = pi.issue_id
            GROUP BY p.id, p.name, p.description, p.updated_utc
            ORDER BY p.updated_utc DESC, p.name ASC;
            """;

        var projects = new List<ProjectSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            projects.Add(ReadProjectSummary(reader));
        }

        return projects;
    }

    private static async Task<ProjectSummary?> GetProjectSummaryByIdAsync(
        SqliteConnection connection,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                p.id,
                p.name,
                p.description,
                p.updated_utc,
                COUNT(pi.id),
                SUM(CASE WHEN i.state = 'open' THEN 1 ELSE 0 END),
                SUM(CASE WHEN i.state = 'closed' THEN 1 ELSE 0 END)
            FROM projects p
            LEFT JOIN project_items pi ON pi.project_id = p.id
            LEFT JOIN issues i ON i.id = pi.issue_id
            WHERE p.id = $projectId
            GROUP BY p.id, p.name, p.description, p.updated_utc
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$projectId", projectId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadProjectSummary(reader)
            : null;
    }

    private static async Task<ProjectDetail?> GetProjectDetailFromConnectionAsync(
        SqliteConnection connection,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var summary = await GetProjectSummaryByIdAsync(connection, projectId, cancellationToken);
        if (summary is null)
        {
            return null;
        }

        var items = await GetProjectIssueItemsAsync(connection, projectId, cancellationToken);
        var boardColumns = BoardColumns
            .Select(column =>
            {
                var columnItems = items
                    .Where(item => string.Equals(item.BoardColumn, column, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(static item => item.SortOrder)
                    .ThenBy(static item => item.IssueNumber)
                    .ToArray();

                return new ProjectBoardColumn(
                    column,
                    columnItems.Count(static item => item.State == IssueWorkflowState.Open),
                    columnItems);
            })
            .ToArray();
        var tableItems = items
            .OrderBy(static item => item.IssueNumber)
            .ToArray();
        var calendarItems = items
            .Where(static item => item.DueDate is not null)
            .OrderBy(static item => item.DueDate)
            .ThenBy(static item => item.IssueNumber)
            .ToArray();
        var timelineItems = items
            .OrderBy(static item => item.DueDate ?? DateOnly.MaxValue)
            .ThenBy(static item => item.IssueNumber)
            .ToArray();
        var customFields = await GetProjectCustomFieldsAsync(connection, projectId, cancellationToken);
        var savedViews = await GetProjectSavedViewsAsync(connection, projectId, cancellationToken);

        return new ProjectDetail(
            summary,
            boardColumns,
            tableItems,
            calendarItems,
            timelineItems,
            customFields,
            savedViews);
    }

    private static async Task<IReadOnlyList<ProjectIssueItem>> GetProjectIssueItemsAsync(
        SqliteConnection connection,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                pi.id,
                i.id,
                i.issue_number,
                i.title,
                i.state,
                i.state_reason,
                i.priority,
                i.assignee_display_name,
                i.due_date,
                pi.board_column,
                pi.sort_order,
                i.updated_utc
            FROM project_items pi
            INNER JOIN issues i ON i.id = pi.issue_id
            WHERE pi.project_id = $projectId
            ORDER BY pi.board_column ASC, pi.sort_order ASC, i.issue_number ASC;
            """;
        command.Parameters.AddWithValue("$projectId", projectId.ToString());

        var items = new List<ProjectIssueItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadProjectIssueItem(reader));
        }

        var valuesByItemId = await GetProjectCustomFieldValuesAsync(connection, projectId, cancellationToken);
        return [.. items.Select(item => item with
        {
            CustomFieldValues = valuesByItemId.GetValueOrDefault(
                item.ProjectItemId,
                ProjectIssueItem.EmptyCustomFieldValues),
        })];
    }

    private static async Task<ProjectIssueItem?> GetProjectIssueItemByIdAsync(
        SqliteConnection connection,
        Guid projectItemId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                pi.id,
                i.id,
                i.issue_number,
                i.title,
                i.state,
                i.state_reason,
                i.priority,
                i.assignee_display_name,
                i.due_date,
                pi.board_column,
                pi.sort_order,
                i.updated_utc
            FROM project_items pi
            INNER JOIN issues i ON i.id = pi.issue_id
            WHERE pi.id = $projectItemId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$projectItemId", projectItemId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var item = ReadProjectIssueItem(reader);
        return item with
        {
            CustomFieldValues = await GetProjectCustomFieldValuesForItemAsync(
                connection,
                item.ProjectItemId,
                cancellationToken),
        };
    }

    private static async Task<Dictionary<Guid, IReadOnlyDictionary<string, string>>> GetProjectCustomFieldValuesAsync(
        SqliteConnection connection,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                v.project_item_id,
                f.name,
                v.value_text
            FROM project_custom_field_values v
            INNER JOIN project_custom_fields f ON f.id = v.custom_field_id
            INNER JOIN project_items pi ON pi.id = v.project_item_id
            WHERE pi.project_id = $projectId
            ORDER BY f.name ASC;
            """;
        command.Parameters.AddWithValue("$projectId", projectId.ToString());

        var mutableValues = new Dictionary<Guid, Dictionary<string, string>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var projectItemId = Guid.Parse(reader.GetString(0));
            if (!mutableValues.TryGetValue(projectItemId, out var values))
            {
                values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                mutableValues[projectItemId] = values;
            }

            values[reader.GetString(1)] = reader.GetString(2);
        }

        return mutableValues.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyDictionary<string, string>)new ReadOnlyDictionary<string, string>(pair.Value));
    }

    private static async Task<IReadOnlyDictionary<string, string>> GetProjectCustomFieldValuesForItemAsync(
        SqliteConnection connection,
        Guid projectItemId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT f.name, v.value_text
            FROM project_custom_field_values v
            INNER JOIN project_custom_fields f ON f.id = v.custom_field_id
            WHERE v.project_item_id = $projectItemId
            ORDER BY f.name ASC;
            """;
        command.Parameters.AddWithValue("$projectItemId", projectItemId.ToString());

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values[reader.GetString(0)] = reader.GetString(1);
        }

        return values.Count == 0
            ? ProjectIssueItem.EmptyCustomFieldValues
            : new ReadOnlyDictionary<string, string>(values);
    }

    private static async Task<IReadOnlyList<ProjectCustomField>> GetProjectCustomFieldsAsync(
        SqliteConnection connection,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, project_id, name, field_type, options_text
            FROM project_custom_fields
            WHERE project_id = $projectId
            ORDER BY name ASC;
            """;
        command.Parameters.AddWithValue("$projectId", projectId.ToString());

        var fields = new List<ProjectCustomField>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            fields.Add(ReadProjectCustomField(reader));
        }

        return fields;
    }

    private static async Task<ProjectCustomField?> GetProjectCustomFieldByNameAsync(
        SqliteConnection connection,
        Guid projectId,
        string name,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, project_id, name, field_type, options_text
            FROM project_custom_fields
            WHERE project_id = $projectId AND lower(name) = lower($name)
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        command.Parameters.AddWithValue("$name", name);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadProjectCustomField(reader)
            : null;
    }

    private static async Task<IReadOnlyList<ProjectSavedView>> GetProjectSavedViewsAsync(
        SqliteConnection connection,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, project_id, name, view_mode, filter_text, sort_by_field, group_by_field, updated_utc
            FROM project_saved_views
            WHERE project_id = $projectId
            ORDER BY name ASC;
            """;
        command.Parameters.AddWithValue("$projectId", projectId.ToString());

        var savedViews = new List<ProjectSavedView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            savedViews.Add(ReadProjectSavedView(reader));
        }

        return savedViews;
    }

    private static async Task<ProjectSavedView?> GetProjectSavedViewByNameAsync(
        SqliteConnection connection,
        Guid projectId,
        string name,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, project_id, name, view_mode, filter_text, sort_by_field, group_by_field, updated_utc
            FROM project_saved_views
            WHERE project_id = $projectId AND lower(name) = lower($name)
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        command.Parameters.AddWithValue("$name", name);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadProjectSavedView(reader)
            : null;
    }

    private static async Task<bool> IssueExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid issueId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT COUNT(1)
            FROM issues
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", issueId.ToString());
        return ReadScalarInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task<string?> GetWorkspaceIdForIssueAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid issueId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT workspace_id
            FROM issues
            WHERE id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", issueId.ToString());

        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private static async Task<string> GetIssueDescriptionAsync(
        SqliteConnection connection,
        Guid issueId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT description
            FROM issues
            WHERE id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", issueId.ToString());

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result as string ?? string.Empty;
    }

    private static async Task<IReadOnlyList<IssueComment>> GetIssueCommentsAsync(
        SqliteConnection connection,
        Guid issueId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, issue_id, author_display_name, body, created_utc
            FROM issue_comments
            WHERE issue_id = $issueId
            ORDER BY created_utc ASC;
            """;
        command.Parameters.AddWithValue("$issueId", issueId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var comments = new List<IssueComment>();

        while (await reader.ReadAsync(cancellationToken))
        {
            comments.Add(
                new IssueComment(
                    Guid.Parse(reader.GetString(0)),
                    Guid.Parse(reader.GetString(1)),
                    reader.GetString(2),
                    reader.GetString(3),
                    ParseStoredTimestamp(reader.GetString(4))));
        }

        return comments;
    }

    private static async Task<IReadOnlyList<IssueAttachment>> GetIssueAttachmentsAsync(
        SqliteConnection connection,
        Guid issueId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, issue_id, file_name, content_type, size_bytes, created_utc
            FROM attachments
            WHERE issue_id = $issueId
            ORDER BY created_utc DESC, file_name ASC;
            """;
        command.Parameters.AddWithValue("$issueId", issueId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var attachments = new List<IssueAttachment>();

        while (await reader.ReadAsync(cancellationToken))
        {
            attachments.Add(
                new IssueAttachment(
                    Guid.Parse(reader.GetString(0)),
                    Guid.Parse(reader.GetString(1)),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt64(4),
                    ParseStoredTimestamp(reader.GetString(5))));
        }

        return attachments;
    }

    private static async Task<IReadOnlyList<IssueActivityEntry>> GetIssueActivityAsync(
        SqliteConnection connection,
        Guid issueId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, issue_id, event_type, summary, created_utc
            FROM activity_events
            WHERE issue_id = $issueId
            ORDER BY created_utc DESC, id DESC;
            """;
        command.Parameters.AddWithValue("$issueId", issueId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<IssueActivityEntry>();

        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(
                new IssueActivityEntry(
                    Guid.Parse(reader.GetString(0)),
                    Guid.Parse(reader.GetString(1)),
                    reader.GetString(2),
                    reader.GetString(3),
                    ParseStoredTimestamp(reader.GetString(4))));
        }

        return items;
    }

    private static ProjectSummary ReadProjectSummary(SqliteDataReader reader)
    {
        return new ProjectSummary(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            ReadNullableInt32(reader, 4),
            ReadNullableInt32(reader, 5),
            ReadNullableInt32(reader, 6),
            ParseStoredTimestamp(reader.GetString(3)));
    }

    private static ProjectIssueItem ReadProjectIssueItem(SqliteDataReader reader)
    {
        return new ProjectIssueItem(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            reader.GetInt32(2),
            reader.GetString(3),
            ParseState(reader.GetString(4)),
            ParseReason(reader.GetString(5)),
            ParsePriority(reader.GetString(6)),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : ParseStoredDate(reader.GetString(8)),
            reader.GetString(9),
            reader.GetInt32(10),
            ParseStoredTimestamp(reader.GetString(11)),
            ProjectIssueItem.EmptyCustomFieldValues);
    }

    private static ProjectCustomField ReadProjectCustomField(SqliteDataReader reader)
    {
        return new ProjectCustomField(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            reader.GetString(2),
            ParseCustomFieldType(reader.GetString(3)),
            reader.GetString(4));
    }

    private static ProjectSavedView ReadProjectSavedView(SqliteDataReader reader)
    {
        return new ProjectSavedView(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            reader.GetString(2),
            ParseProjectViewMode(reader.GetString(3)),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            ParseStoredTimestamp(reader.GetString(7)));
    }

    private static string SerializeState(IssueWorkflowState state) => state switch
    {
        IssueWorkflowState.Open => "open",
        IssueWorkflowState.Closed => "closed",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    private static IssueWorkflowState ParseState(string state) => state switch
    {
        "open" => IssueWorkflowState.Open,
        "closed" => IssueWorkflowState.Closed,
        _ => IssueWorkflowState.Open,
    };

    private static string SerializeReason(IssueStateReason reason) => reason switch
    {
        IssueStateReason.None => "none",
        IssueStateReason.Completed => "completed",
        IssueStateReason.NotPlanned => "not_planned",
        IssueStateReason.Duplicate => "duplicate",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };

    private static IssueStateReason ParseReason(string reason) => reason switch
    {
        "completed" => IssueStateReason.Completed,
        "not_planned" => IssueStateReason.NotPlanned,
        "duplicate" => IssueStateReason.Duplicate,
        _ => IssueStateReason.None,
    };

    private static string SerializePriority(IssuePriority priority) => priority switch
    {
        IssuePriority.None => "none",
        IssuePriority.Low => "low",
        IssuePriority.Medium => "medium",
        IssuePriority.High => "high",
        IssuePriority.Critical => "critical",
        _ => throw new ArgumentOutOfRangeException(nameof(priority), priority, null),
    };

    private static IssuePriority ParsePriority(string priority) => priority switch
    {
        "low" => IssuePriority.Low,
        "medium" => IssuePriority.Medium,
        "high" => IssuePriority.High,
        "critical" => IssuePriority.Critical,
        _ => IssuePriority.None,
    };

    private static string SerializeProjectViewMode(ProjectViewMode viewMode) => viewMode switch
    {
        ProjectViewMode.Board => "board",
        ProjectViewMode.Table => "table",
        ProjectViewMode.Calendar => "calendar",
        ProjectViewMode.Timeline => "timeline",
        _ => throw new ArgumentOutOfRangeException(nameof(viewMode), viewMode, null),
    };

    private static ProjectViewMode ParseProjectViewMode(string viewMode) => viewMode switch
    {
        "table" => ProjectViewMode.Table,
        "calendar" => ProjectViewMode.Calendar,
        "timeline" => ProjectViewMode.Timeline,
        _ => ProjectViewMode.Board,
    };

    private static string SerializeCustomFieldType(ProjectCustomFieldType fieldType) => fieldType switch
    {
        ProjectCustomFieldType.Text => "text",
        ProjectCustomFieldType.Number => "number",
        ProjectCustomFieldType.Date => "date",
        ProjectCustomFieldType.SingleSelect => "single_select",
        _ => throw new ArgumentOutOfRangeException(nameof(fieldType), fieldType, null),
    };

    private static ProjectCustomFieldType ParseCustomFieldType(string fieldType) => fieldType switch
    {
        "number" => ProjectCustomFieldType.Number,
        "date" => ProjectCustomFieldType.Date,
        "single_select" => ProjectCustomFieldType.SingleSelect,
        _ => ProjectCustomFieldType.Text,
    };

    private static string SerializeIssueRelationType(IssueRelationType relationType) => relationType switch
    {
        IssueRelationType.Related => "related",
        IssueRelationType.Blocks => "blocks",
        IssueRelationType.BlockedBy => "blocked_by",
        IssueRelationType.DuplicateOf => "duplicate_of",
        IssueRelationType.DuplicatedBy => "duplicated_by",
        IssueRelationType.Parent => "parent",
        IssueRelationType.Child => "child",
        _ => throw new ArgumentOutOfRangeException(nameof(relationType), relationType, null),
    };

    private static IssueRelationType ParseIssueRelationType(string relationType) => relationType switch
    {
        "blocks" => IssueRelationType.Blocks,
        "blocked_by" => IssueRelationType.BlockedBy,
        "duplicate_of" => IssueRelationType.DuplicateOf,
        "duplicated_by" => IssueRelationType.DuplicatedBy,
        "parent" => IssueRelationType.Parent,
        "child" => IssueRelationType.Child,
        _ => IssueRelationType.Related,
    };

    private static string FormatIssueRelationType(IssueRelationType relationType) => relationType switch
    {
        IssueRelationType.BlockedBy => "blocked by",
        IssueRelationType.DuplicateOf => "duplicate of",
        IssueRelationType.DuplicatedBy => "duplicated by",
        _ => SerializeIssueRelationType(relationType).Replace('_', ' '),
    };

    private static string SerializeTheme(AppThemePreference theme) => theme switch
    {
        AppThemePreference.System => "system",
        AppThemePreference.Light => "light",
        AppThemePreference.Dark => "dark",
        _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, null),
    };

    private static AppThemePreference ParseTheme(string theme) => theme switch
    {
        "system" => AppThemePreference.System,
        "dark" => AppThemePreference.Dark,
        _ => AppThemePreference.Light,
    };

    private static string GetIssueTypeColor(string issueTypeName)
    {
        return issueTypeName.ToLowerInvariant() switch
        {
            "bug" => "#D1242F",
            "idea" => "#8957E5",
            "task" => "#238636",
            "feature" => "#2F81F7",
            _ => "#0E7490",
        };
    }

    private static string SerializeExportSelectionScope(ExportSelectionScope scope) => scope switch
    {
        ExportSelectionScope.CurrentIssue => "current_issue",
        ExportSelectionScope.CurrentFilter => "current_filter",
        ExportSelectionScope.Project => "project",
        ExportSelectionScope.Workspace => "workspace",
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null),
    };

    private static ExportSelectionScope ParseExportSelectionScope(string scope) => scope switch
    {
        "current_issue" => ExportSelectionScope.CurrentIssue,
        "current_filter" => ExportSelectionScope.CurrentFilter,
        "project" => ExportSelectionScope.Project,
        _ => ExportSelectionScope.Workspace,
    };

    private static string SerializeExportFormat(ExportFormat format) => format switch
    {
        ExportFormat.Markdown => "markdown",
        ExportFormat.Html => "html",
        ExportFormat.FolderTree => "folder_tree",
        ExportFormat.PortablePackage => "portable_package",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };

    private static ExportFormat ParseExportFormat(string format) => format switch
    {
        "html" => ExportFormat.Html,
        "folder_tree" => ExportFormat.FolderTree,
        "portable_package" => ExportFormat.PortablePackage,
        _ => ExportFormat.Markdown,
    };

    private static string SerializeExportBodyFormat(ExportBodyFormat format) => format switch
    {
        ExportBodyFormat.Markdown => "markdown",
        ExportBodyFormat.Html => "html",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };

    private static ExportBodyFormat ParseExportBodyFormat(string format) => format switch
    {
        "html" => ExportBodyFormat.Html,
        _ => ExportBodyFormat.Markdown,
    };

    private static string NormalizeBoardColumn(string boardColumn)
    {
        var normalizedColumn = Normalize(boardColumn);
        if (normalizedColumn is null)
        {
            return BoardColumnTodo;
        }

        return BoardColumns.FirstOrDefault(
                column => string.Equals(column, normalizedColumn, StringComparison.OrdinalIgnoreCase))
            ?? BoardColumnTodo;
    }

    private static string GetDefaultBoardColumn(
        IssueWorkflowState state,
        IssuePriority priority,
        DateOnly? dueDate)
    {
        if (state == IssueWorkflowState.Closed)
        {
            return BoardColumnDone;
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        return priority is IssuePriority.Critical or IssuePriority.High || dueDate < today
            ? BoardColumnInProgress
            : BoardColumnTodo;
    }

    private static string? Normalize(string? text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? null
            : text.Trim();
    }

    private static string? SerializeDate(DateOnly? date)
    {
        return date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string SerializeTimestamp(DateTimeOffset timestamp)
    {
        return timestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
    }

    private static int ReadScalarInt32(object? value)
    {
        // SQLite의 집계 결과는 boxed scalar로 돌아오므로, 변환 문화권을 고정해 Windows 로캘 차이를 차단한다.
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static int ReadNullableInt32(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? 0
            : ReadScalarInt32(reader.GetValue(ordinal));
    }

    private static DateOnly ParseStoredDate(string value)
    {
        // 저장 포맷은 SerializeDate의 ISO 텍스트이며, 읽기 역시 invariant 규칙으로만 수행한다.
        return DateOnly.Parse(value, CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset ParseStoredTimestamp(string value)
    {
        // SerializeTimestamp가 UTC ISO-8601 텍스트를 쓰므로, 파싱도 같은 invariant 포맷으로 맞춘다.
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string([.. fileName.Select(character => invalidCharacters.Contains(character) ? '_' : character)]);
        return string.IsNullOrWhiteSpace(sanitized)
            ? "tracky-attachment.bin"
            : sanitized;
    }

    private sealed record WorkspaceRow(string Id, string Name, string Description);

    private sealed record ProjectIssueSyncRow(
        Guid IssueId,
        string WorkspaceId,
        string ProjectName,
        IssueWorkflowState State,
        IssuePriority Priority,
        DateOnly? DueDate,
        DateTimeOffset UpdatedAtUtc);

    private sealed record ProjectItemIdentity(
        Guid ProjectId,
        Guid IssueId,
        Guid ProjectItemId = default);

    private sealed record IssueUpdateSnapshot(
        string Title,
        string Description,
        IssuePriority Priority,
        string? AssigneeDisplayName,
        DateOnly? DueDate,
        string? ProjectName,
        string? MilestoneName,
        string? IssueTypeName,
        IReadOnlyList<string> Labels);

    private sealed class IssueAccumulator(
        Guid id,
        int number,
        string title,
        IssueWorkflowState state,
        IssueStateReason stateReason,
        IssuePriority priority,
        string? assigneeDisplayName,
        DateOnly? dueDate,
        DateTimeOffset updatedAtUtc,
        string? projectName,
        int commentCount,
        int attachmentCount,
        string? milestoneName,
        string? issueTypeName)
    {
        public Guid Id { get; } = id;

        public int Number { get; } = number;

        public string Title { get; } = title;

        public IssueWorkflowState State { get; } = state;

        public IssueStateReason StateReason { get; } = stateReason;

        public IssuePriority Priority { get; } = priority;

        public string? AssigneeDisplayName { get; } = assigneeDisplayName;

        public DateOnly? DueDate { get; } = dueDate;

        public DateTimeOffset UpdatedAtUtc { get; } = updatedAtUtc;

        public string? ProjectName { get; } = projectName;

        public int CommentCount { get; } = commentCount;

        public int AttachmentCount { get; } = attachmentCount;

        public string? MilestoneName { get; } = milestoneName;

        public string? IssueTypeName { get; } = issueTypeName;

        public List<string> Labels { get; } = [];

        public IssueListItem ToIssueListItem()
        {
            return new IssueListItem(
                Id,
                Number,
                Title,
                State,
                StateReason,
                Priority,
                AssigneeDisplayName,
                DueDate,
                UpdatedAtUtc,
                ProjectName,
                CommentCount,
                AttachmentCount,
                Labels.Count == 0 ? IssueListItem.EmptyLabels : [.. Labels],
                MilestoneName,
                IssueTypeName);
        }
    }
}
