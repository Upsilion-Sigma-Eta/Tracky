namespace Tracky.Core.Issues;

// ReSharper disable NotAccessedPositionalProperty.Global
// 댓글 식별자와 이슈 FK는 Phase 1 UI에서 직접 표시하지 않아도, 이후 편집/동기화 기능을 위해 보존하는 값이다.
public sealed record IssueComment(
    Guid Id,
    Guid IssueId,
    string AuthorDisplayName,
    string Body,
    DateTimeOffset CreatedAtUtc);
