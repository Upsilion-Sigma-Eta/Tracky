using Tracky.App.Services;

namespace Tracky.App.Tests.TestDoubles;

public sealed class TestAttachmentPicker : IAttachmentPicker
{
    public string? NextPath { get; set; }

    public int PickCount { get; private set; }

    public Task<string?> PickAttachmentAsync()
    {
        PickCount++;
        return Task.FromResult(NextPath);
    }
}
