using Tracky.Core.Reminders;

namespace Tracky.Core.Issues;

public sealed record IssueDetail(
    IssueListItem Summary,
    string Description,
    IReadOnlyList<IssueComment> Comments,
    IReadOnlyList<IssueAttachment> Attachments,
    IReadOnlyList<IssueActivityEntry> Activity,
    IReadOnlyList<IssueReminder> Reminders,
    IReadOnlyList<IssueRelation> Relations);
