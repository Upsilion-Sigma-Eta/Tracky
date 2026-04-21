using Tracky.Core.Projects;

namespace Tracky.App.ViewModels;

public sealed class ProjectCustomFieldViewModel(ProjectCustomField field) : ViewModelBase
{
    public ProjectCustomField Field { get; } = field;

    public string Name => Field.Name;

    public string FieldTypeText => Field.FieldType switch
    {
        ProjectCustomFieldType.Number => "Number",
        ProjectCustomFieldType.Date => "Date",
        ProjectCustomFieldType.SingleSelect => "Single select",
        _ => "Text",
    };

    public string OptionsText => string.IsNullOrWhiteSpace(Field.OptionsText)
        ? "No options"
        : Field.OptionsText;

    public override string ToString()
    {
        return Name;
    }
}
