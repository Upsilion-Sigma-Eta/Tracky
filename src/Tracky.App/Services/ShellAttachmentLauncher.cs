using System.Diagnostics;

namespace Tracky.App.Services;

public sealed class ShellAttachmentLauncher : IAttachmentLauncher
{
    public Task OpenAsync(string path)
    {
        Process.Start(
            new ProcessStartInfo(path)
            {
                UseShellExecute = true,
            });

        return Task.CompletedTask;
    }
}
