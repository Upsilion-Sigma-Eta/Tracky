namespace Tracky.Core.Issues;

// ReSharper disable NotAccessedPositionalProperty.Global
// 첨부 파일의 이슈 FK는 현재 화면에서 직접 읽지 않아도, 저장소 무결성과 export 추적을 위해 모델에 남긴다.
public sealed record IssueAttachment(
    Guid Id,
    Guid IssueId,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset CreatedAtUtc);
