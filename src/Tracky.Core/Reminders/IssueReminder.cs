namespace Tracky.Core.Reminders;

public sealed record IssueReminder(
    Guid Id,
    Guid? IssueId,
    string Title,
    string Note,
    DateTimeOffset RemindAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? DismissedAtUtc)
{
    public bool IsDismissed => DismissedAtUtc is not null;
}
