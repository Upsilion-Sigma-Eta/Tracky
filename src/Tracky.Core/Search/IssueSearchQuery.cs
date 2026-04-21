namespace Tracky.Core.Search;

public sealed record IssueSearchQuery(
    IReadOnlyList<string> TextTerms,
    IReadOnlyList<IssueSearchOperator> Operators)
{
    public static readonly IssueSearchQuery Empty = new([], []);
}
