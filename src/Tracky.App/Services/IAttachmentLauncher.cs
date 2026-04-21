namespace Tracky.App.Services;

public interface IAttachmentLauncher
{
    Task OpenAsync(string path);
}
