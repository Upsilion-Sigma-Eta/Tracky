namespace Tracky.Core.Projects;

public sealed record AddProjectCustomFieldInput(
    Guid ProjectId,
    string Name,
    ProjectCustomFieldType FieldType,
    string OptionsText);
