namespace Tracky.Core.Exports;

public sealed record ExportPreset(
    Guid Id,
    string Name,
    ExportSelectionScope Scope,
    ExportFormat Format,
    ExportBodyFormat BodyFormat,
    bool IncludeComments,
    bool IncludeActivity,
    bool IncludeAttachments,
    bool IncludeClosedIssues,
    DateTimeOffset UpdatedAtUtc);
