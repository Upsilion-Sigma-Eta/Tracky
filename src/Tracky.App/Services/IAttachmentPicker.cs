namespace Tracky.App.Services;

public interface IAttachmentPicker
{
    Task<string?> PickAttachmentAsync();
}
