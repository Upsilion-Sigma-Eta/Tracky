namespace Tracky.Core.Projects;

public sealed record UpdateProjectCustomFieldValueInput(
    Guid ProjectItemId,
    Guid CustomFieldId,
    string ValueText);
