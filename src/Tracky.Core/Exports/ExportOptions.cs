namespace Tracky.Core.Exports;

public sealed record ExportOptions(
    ExportSelectionScope Scope,
    ExportFormat Format,
    ExportBodyFormat BodyFormat,
    IReadOnlyList<Guid> IssueIds,
    Guid? IssueId,
    Guid? ProjectId,
    bool IncludeComments,
    bool IncludeActivity,
    bool IncludeAttachments,
    bool IncludeClosedIssues,
    string? OutputDirectory);
