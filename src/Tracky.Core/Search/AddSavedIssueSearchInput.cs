namespace Tracky.Core.Search;

public sealed record AddSavedIssueSearchInput(
    string Name,
    string QueryText,
    bool IsPinned);
