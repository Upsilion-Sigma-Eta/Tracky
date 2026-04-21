using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Tracky.App.Services;

public sealed class WindowAttachmentPicker(Window window) : IAttachmentPicker
{
    private readonly Window _window = window;

    public async Task<string?> PickAttachmentAsync()
    {
        if (!_window.StorageProvider.CanOpen)
        {
            return null;
        }

        var files = await _window.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Attach a file to the selected issue",
                AllowMultiple = false,
            });

        return files.Count == 0
            ? null
            : files[0].TryGetLocalPath();
    }
}
