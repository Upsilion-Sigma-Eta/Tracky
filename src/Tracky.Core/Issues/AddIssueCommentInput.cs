namespace Tracky.Core.Issues;

public sealed record AddIssueCommentInput(
    Guid IssueId,
    string AuthorDisplayName,
    string Body);
