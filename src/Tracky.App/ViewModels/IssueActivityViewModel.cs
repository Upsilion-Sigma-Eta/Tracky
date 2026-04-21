using System.Globalization;
using Tracky.Core.Issues;

namespace Tracky.App.ViewModels;

public sealed class IssueActivityViewModel(IssueActivityEntry activity) : ViewModelBase
{
    public IssueActivityEntry Activity { get; } = activity;

    public string Summary => Activity.Summary;

    public string CreatedText => Activity.CreatedAtUtc.ToLocalTime().ToString("MMM dd, HH:mm", CultureInfo.CurrentCulture);

    public string EventLabel => Activity.EventType switch
    {
        "issue.created" => "Created",
        "issue.comment.added" => "Comment",
        "issue.attachment.added" => "Attachment",
        "issue.state.changed" => "State",
        _ => "Activity",
    };
}
