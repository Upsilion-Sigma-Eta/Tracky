namespace Tracky.Core.Search;

public sealed record IssueSearchOperator(
    string Key,
    string Value,
    bool IsNegated);
