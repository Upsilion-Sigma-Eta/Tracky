using Tracky.App.Services;

namespace Tracky.App.Tests.TestDoubles;

public sealed class TestAttachmentLauncher : IAttachmentLauncher
{
    public List<string> OpenedPaths { get; } = [];

    public Task OpenAsync(string path)
    {
        OpenedPaths.Add(path);
        return Task.CompletedTask;
    }
}
