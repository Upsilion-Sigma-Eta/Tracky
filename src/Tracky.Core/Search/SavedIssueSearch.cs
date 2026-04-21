namespace Tracky.Core.Search;

public sealed record SavedIssueSearch(
    Guid Id,
    string Name,
    string QueryText,
    bool IsPinned,
    DateTimeOffset UpdatedAtUtc);
