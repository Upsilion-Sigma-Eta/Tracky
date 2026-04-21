using Tracky.Core.Issues;

namespace Tracky.App.ViewModels;

public sealed class IssueDetailViewModel(IssueDetail detail) : ViewModelBase
{
    public IssueDetail Detail { get; } = detail;

    public IssueCardViewModel Summary { get; } = new IssueCardViewModel(detail.Summary);

    public string DescriptionText => string.IsNullOrWhiteSpace(Detail.Description)
        ? "No description has been written for this issue yet."
        : Detail.Description;

    public IReadOnlyList<IssueCommentViewModel> Comments { get; } = [.. detail.Comments.Select(static comment => new IssueCommentViewModel(comment))];

    public IReadOnlyList<IssueAttachmentViewModel> Attachments { get; } = [.. detail.Attachments.Select(static attachment => new IssueAttachmentViewModel(attachment))];

    public IReadOnlyList<IssueActivityViewModel> Activity { get; } = [.. detail.Activity.Select(static activity => new IssueActivityViewModel(activity))];

    public bool HasComments => Comments.Count > 0;

    public bool HasNoComments => !HasComments;

    public bool HasAttachments => Attachments.Count > 0;

    public bool HasNoAttachments => !HasAttachments;

    public bool HasActivity => Activity.Count > 0;

    public bool HasNoActivity => !HasActivity;

    public string CommentCountText => Comments.Count == 1
        ? "1 comment"
        : $"{Comments.Count} comments";

    public string AttachmentCountText => Attachments.Count == 1
        ? "1 attachment"
        : $"{Attachments.Count} attachments";
}
