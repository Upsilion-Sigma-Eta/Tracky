using Tracky.Core.Issues;
using Tracky.Core.Workspaces;

namespace Tracky.Core.Services;

public interface ITrackyWorkspaceService
{
    Task<WorkspaceOverview> GetOverviewAsync(CancellationToken cancellationToken = default);

    Task<IssueDetail?> GetIssueDetailAsync(Guid issueId, CancellationToken cancellationToken = default);

    Task<IssueListItem> CreateIssueAsync(CreateIssueInput input, CancellationToken cancellationToken = default);

    Task<IssueListItem?> UpdateIssueStateAsync(UpdateIssueStateInput input, CancellationToken cancellationToken = default);

    Task<IssueComment?> AddIssueCommentAsync(AddIssueCommentInput input, CancellationToken cancellationToken = default);

    Task<IssueAttachment?> AddIssueAttachmentAsync(AddIssueAttachmentInput input, CancellationToken cancellationToken = default);

    Task<string?> ExportAttachmentToTempFileAsync(Guid attachmentId, CancellationToken cancellationToken = default);
}
