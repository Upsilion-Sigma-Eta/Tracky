using Tracky.Core.Issues;

namespace Tracky.App.ViewModels;

public sealed class IssueActivityViewModel : ViewModelBase
{
    public IssueActivityViewModel(IssueActivityEntry activity)
    {
        Activity = activity;
    }

    public IssueActivityEntry Activity { get; }

    public string Summary => Activity.Summary;

    public string CreatedText => Activity.CreatedAtUtc.ToLocalTime().ToString("MMM dd, HH:mm");

    public string EventLabel => Activity.EventType switch
    {
        "issue.created" => "Created",
        "issue.comment.added" => "Comment",
        "issue.attachment.added" => "Attachment",
        "issue.state.changed" => "State",
        _ => "Activity",
    };
}
