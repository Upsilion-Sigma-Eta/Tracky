namespace Tracky.Core.Exports;

public sealed record AddExportPresetInput(
    string Name,
    ExportSelectionScope Scope,
    ExportFormat Format,
    ExportBodyFormat BodyFormat,
    bool IncludeComments,
    bool IncludeActivity,
    bool IncludeAttachments,
    bool IncludeClosedIssues);
