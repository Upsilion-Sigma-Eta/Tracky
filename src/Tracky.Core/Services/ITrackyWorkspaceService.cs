using Tracky.Core.Issues;
using Tracky.Core.Projects;
using Tracky.Core.Workspaces;

namespace Tracky.Core.Services;

public interface ITrackyWorkspaceService
{
    Task<WorkspaceOverview> GetOverviewAsync(CancellationToken cancellationToken = default);

    Task<IssueDetail?> GetIssueDetailAsync(Guid issueId, CancellationToken cancellationToken = default);

    Task<IssueListItem> CreateIssueAsync(CreateIssueInput input, CancellationToken cancellationToken = default);

    Task<IssueListItem?> UpdateIssueAsync(UpdateIssueInput input, CancellationToken cancellationToken = default);

    Task<IssueListItem?> UpdateIssueStateAsync(UpdateIssueStateInput input, CancellationToken cancellationToken = default);

    Task<bool> DeleteIssueAsync(Guid issueId, CancellationToken cancellationToken = default);

    Task<IssueComment?> AddIssueCommentAsync(AddIssueCommentInput input, CancellationToken cancellationToken = default);

    Task<IssueAttachment?> AddIssueAttachmentAsync(AddIssueAttachmentInput input, CancellationToken cancellationToken = default);

    Task<string?> ExportAttachmentToTempFileAsync(Guid attachmentId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectSummary>> GetProjectsAsync(CancellationToken cancellationToken = default);

    Task<ProjectDetail?> GetProjectDetailAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<ProjectSummary> CreateProjectAsync(CreateProjectInput input, CancellationToken cancellationToken = default);

    Task<ProjectIssueItem?> MoveProjectItemAsync(MoveProjectItemInput input, CancellationToken cancellationToken = default);

    Task<ProjectCustomField?> AddProjectCustomFieldAsync(
        AddProjectCustomFieldInput input,
        CancellationToken cancellationToken = default);

    Task<ProjectIssueItem?> UpdateProjectCustomFieldValueAsync(
        UpdateProjectCustomFieldValueInput input,
        CancellationToken cancellationToken = default);

    Task<ProjectSavedView?> AddProjectSavedViewAsync(
        AddProjectSavedViewInput input,
        CancellationToken cancellationToken = default);
}
