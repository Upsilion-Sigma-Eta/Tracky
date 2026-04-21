using Tracky.Core.Issues;

namespace Tracky.App.ViewModels;

public sealed class IssueDetailViewModel : ViewModelBase
{
    public IssueDetailViewModel(IssueDetail detail)
    {
        Detail = detail;
        Summary = new IssueCardViewModel(detail.Summary);
        Comments = detail.Comments.Select(static comment => new IssueCommentViewModel(comment)).ToArray();
        Attachments = detail.Attachments.Select(static attachment => new IssueAttachmentViewModel(attachment)).ToArray();
        Activity = detail.Activity.Select(static activity => new IssueActivityViewModel(activity)).ToArray();
    }

    public IssueDetail Detail { get; }

    public IssueCardViewModel Summary { get; }

    public string DescriptionText => string.IsNullOrWhiteSpace(Detail.Description)
        ? "No description has been written for this issue yet."
        : Detail.Description;

    public IReadOnlyList<IssueCommentViewModel> Comments { get; }

    public IReadOnlyList<IssueAttachmentViewModel> Attachments { get; }

    public IReadOnlyList<IssueActivityViewModel> Activity { get; }

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
