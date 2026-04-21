using Tracky.Core.Issues;

namespace Tracky.App.ViewModels;

public sealed class IssueCommentViewModel : ViewModelBase
{
    public IssueCommentViewModel(IssueComment comment)
    {
        Comment = comment;
    }

    public IssueComment Comment { get; }

    public string AuthorDisplayName => Comment.AuthorDisplayName;

    public string Body => Comment.Body;

    public string CreatedText => Comment.CreatedAtUtc.ToLocalTime().ToString("MMM dd, HH:mm");
}
