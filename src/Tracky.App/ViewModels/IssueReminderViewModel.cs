using System.Globalization;
using Tracky.Core.Reminders;

namespace Tracky.App.ViewModels;

public sealed class IssueReminderViewModel(IssueReminder reminder) : ViewModelBase
{
    public IssueReminder Reminder { get; } = reminder;

    public Guid Id => Reminder.Id;

    public string Title => Reminder.Title;

    public string Note => string.IsNullOrWhiteSpace(Reminder.Note)
        ? "No reminder note"
        : Reminder.Note;

    public bool IsDismissed => Reminder.IsDismissed;

    public bool IsDue => !IsDismissed && Reminder.RemindAtUtc <= DateTimeOffset.UtcNow;

    public string StatusText => IsDismissed
        ? "Dismissed"
        : IsDue
            ? "Due now"
            : "Scheduled";

    public string RemindAtText => Reminder.RemindAtUtc
        .ToLocalTime()
        .ToString("MMM dd, HH:mm", CultureInfo.CurrentCulture);

    public string StatusBadgeBackground => IsDismissed
        ? "#F3F4F6"
        : IsDue
            ? "#FEE2E2"
            : "#DBEAFE";

    public string StatusBadgeForeground => IsDismissed
        ? "#6B7280"
        : IsDue
            ? "#991B1B"
            : "#1D4ED8";
}
