namespace Tracky.Core.Issues;

public sealed record AddIssueAttachmentInput(
    Guid IssueId,
    string FileName,
    string ContentType,
    byte[] Content);
