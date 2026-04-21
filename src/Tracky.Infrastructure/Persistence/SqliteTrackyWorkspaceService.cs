using System.Text;
using Microsoft.Data.Sqlite;
using Tracky.Core.Issues;
using Tracky.Core.Services;
using Tracky.Core.Workspaces;

namespace Tracky.Infrastructure.Persistence;

public sealed class SqliteTrackyWorkspaceService : ITrackyWorkspaceService
{
    private static readonly string[] SeedLabels =
    [
        "foundation",
        "ux",
        "desktop",
        "local-first",
        "accessibility",
        "priority:high",
    ];

    private readonly TrackyWorkspacePathProvider _pathProvider;
    private readonly IssueOverviewCalculator _overviewCalculator;

    public SqliteTrackyWorkspaceService()
        : this(new TrackyWorkspacePathProvider(), new IssueOverviewCalculator())
    {
    }

    public SqliteTrackyWorkspaceService(
        TrackyWorkspacePathProvider pathProvider,
        IssueOverviewCalculator overviewCalculator)
    {
        _pathProvider = pathProvider;
        _overviewCalculator = overviewCalculator;
    }

    public async Task<WorkspaceOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenInitializedConnectionAsync(cancellationToken);
        var workspace = await GetWorkspaceAsync(connection, cancellationToken);
        var issues = await GetIssuesAsync(connection, cancellationToken);
        var metrics = _overviewCalculator.Build(issues, DateOnly.FromDateTime(DateTime.Now));

        return new WorkspaceOverview(
            workspace.Name,
            workspace.Description,
            _pathProvider.GetDatabasePath(),
            metrics,
            issues);
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

        return new IssueDetail(summary, description, comments, attachments, activity);
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
        createIssueCommand.Parameters.AddWithValue("$updatedUtc", SerializeTimestamp(now));
        createIssueCommand.Parameters.AddWithValue("$createdUtc", SerializeTimestamp(now));
        await createIssueCommand.ExecuteNonQueryAsync(cancellationToken);

        await SyncLabelsAsync(
            connection,
            transaction,
            workspace.Id,
            issueId,
            input.Labels,
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

    public async Task<IssueListItem?> UpdateIssueStateAsync(
        UpdateIssueStateInput input,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenInitializedConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var normalizedReason = input.State == IssueWorkflowState.Open
            ? IssueStateReason.None
            : input.Reason;

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

            CREATE TABLE IF NOT EXISTS issue_comments (
                id TEXT PRIMARY KEY,
                issue_id TEXT NOT NULL,
                author_display_name TEXT NOT NULL,
                body TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                FOREIGN KEY (issue_id) REFERENCES issues(id) ON DELETE CASCADE
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
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        await EnsureColumnAsync(connection, "issues", "description", "TEXT NOT NULL DEFAULT ''", cancellationToken);
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

    private async Task SeedIfNeededAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(1) FROM workspaces;";
        var workspaceCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));
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
        CancellationToken cancellationToken)
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

    private async Task<IssueComment> InsertCommentAsync(
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

    private async Task<IssueAttachment> InsertAttachmentAsync(
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
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private async Task SyncLabelsAsync(
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
            .Where(static label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.OrdinalIgnoreCase)!)
        {
            var labelId = await EnsureLabelAsync(connection, transaction, workspaceId, label!, cancellationToken);

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
        var colors = new[]
        {
            "#2F81F7",
            "#238636",
            "#BF8700",
            "#8957E5",
            "#D1242F",
            "#0E7490",
        };

        var index = Math.Abs(labelName.GetHashCode(StringComparison.OrdinalIgnoreCase)) % colors.Length;
        return colors[index];
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
        return state == IssueWorkflowState.Open
            ? "Issue was reopened for active work."
            : reason switch
            {
                IssueStateReason.Completed => "Issue was closed as completed.",
                IssueStateReason.NotPlanned => "Issue was closed as not planned.",
                IssueStateReason.Duplicate => "Issue was closed as a duplicate.",
                _ => "Issue state was updated.",
            };
    }

    private async Task<IReadOnlyList<IssueListItem>> GetIssuesAsync(
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
                    reader.IsDBNull(7) ? null : DateOnly.Parse(reader.GetString(7)),
                    DateTimeOffset.Parse(reader.GetString(8)),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.GetInt32(10),
                    reader.GetInt32(11));
            }

            if (!reader.IsDBNull(12))
            {
                current.Labels.Add(reader.GetString(12));
            }
        }

        if (current is not null)
        {
            items.Add(current.ToIssueListItem());
        }

        return items;
    }

    private async Task<IssueListItem?> GetIssueByIdAsync(
        SqliteConnection connection,
        Guid issueId,
        CancellationToken cancellationToken)
    {
        var issues = await GetIssuesAsync(connection, cancellationToken);
        return issues.FirstOrDefault(issue => issue.Id == issueId);
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
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
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
                    DateTimeOffset.Parse(reader.GetString(4))));
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
                    DateTimeOffset.Parse(reader.GetString(5))));
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
                    DateTimeOffset.Parse(reader.GetString(4))));
        }

        return items;
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

    private static string? Normalize(string? text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? null
            : text.Trim();
    }

    private static string? SerializeDate(DateOnly? date)
    {
        return date?.ToString("yyyy-MM-dd");
    }

    private static string SerializeTimestamp(DateTimeOffset timestamp)
    {
        return timestamp.UtcDateTime.ToString("O");
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(fileName.Select(character => invalidCharacters.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized)
            ? "tracky-attachment.bin"
            : sanitized;
    }

    private sealed record WorkspaceRow(string Id, string Name, string Description);

    private sealed class IssueAccumulator
    {
        public IssueAccumulator(
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
            int attachmentCount)
        {
            Id = id;
            Number = number;
            Title = title;
            State = state;
            StateReason = stateReason;
            Priority = priority;
            AssigneeDisplayName = assigneeDisplayName;
            DueDate = dueDate;
            UpdatedAtUtc = updatedAtUtc;
            ProjectName = projectName;
            CommentCount = commentCount;
            AttachmentCount = attachmentCount;
        }

        public Guid Id { get; }

        public int Number { get; }

        public string Title { get; }

        public IssueWorkflowState State { get; }

        public IssueStateReason StateReason { get; }

        public IssuePriority Priority { get; }

        public string? AssigneeDisplayName { get; }

        public DateOnly? DueDate { get; }

        public DateTimeOffset UpdatedAtUtc { get; }

        public string? ProjectName { get; }

        public int CommentCount { get; }

        public int AttachmentCount { get; }

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
                Labels.Count == 0 ? IssueListItem.EmptyLabels : Labels.ToArray());
        }
    }
}
