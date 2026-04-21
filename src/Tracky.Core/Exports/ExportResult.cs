namespace Tracky.Core.Exports;

public sealed record ExportResult(
    string OutputPath,
    int IssueCount,
    int AttachmentCount,
    ExportFormat Format);
