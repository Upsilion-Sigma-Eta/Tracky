namespace Tracky.Core.Issues;

// ReSharper disable NotAccessedPositionalProperty.Global
// 활동 로그의 식별자와 이슈 FK는 UI에서 항상 표시하지 않아도, SQLite 저장소와 이후 동기화 경계에서 보존해야 하는 도메인 데이터다.
public sealed record IssueActivityEntry(
    Guid Id,
    Guid IssueId,
    string EventType,
    string Summary,
    DateTimeOffset CreatedAtUtc);
