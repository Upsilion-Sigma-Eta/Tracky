using System.Globalization;
using Tracky.Core.Issues;

namespace Tracky.App.ViewModels;

public sealed class IssueCommentViewModel(IssueComment comment) : ViewModelBase
{
    public IssueComment Comment { get; } = comment;

    public string AuthorDisplayName => Comment.AuthorDisplayName;

    public string Body => Comment.Body;

    public string CreatedText => Comment.CreatedAtUtc.ToLocalTime().ToString("MMM dd, HH:mm", CultureInfo.CurrentCulture);
}
