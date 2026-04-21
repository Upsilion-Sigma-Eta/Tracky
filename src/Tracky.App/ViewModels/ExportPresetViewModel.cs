using Tracky.Core.Exports;

namespace Tracky.App.ViewModels;

public sealed class ExportPresetViewModel(ExportPreset preset) : ViewModelBase
{
    public ExportPreset Preset { get; } = preset;

    public string Name => Preset.Name;

    public string Summary =>
        $"{Preset.Scope} · {Preset.Format} · {Preset.BodyFormat}";
}
