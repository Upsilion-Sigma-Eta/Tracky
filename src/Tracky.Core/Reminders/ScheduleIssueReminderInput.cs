namespace Tracky.Core.Reminders;

public sealed record ScheduleIssueReminderInput(
    Guid IssueId,
    string Title,
    string Note,
    DateTimeOffset RemindAtUtc);
