namespace Tracky.Core.Projects;

public sealed record ProjectCustomField(
    Guid Id,
    Guid ProjectId,
    string Name,
    ProjectCustomFieldType FieldType,
    string OptionsText);
