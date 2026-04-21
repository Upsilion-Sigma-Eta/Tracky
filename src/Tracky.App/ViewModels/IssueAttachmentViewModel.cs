using Tracky.Core.Issues;

namespace Tracky.App.ViewModels;

public sealed class IssueAttachmentViewModel : ViewModelBase
{
    public IssueAttachmentViewModel(IssueAttachment attachment)
    {
        Attachment = attachment;
    }

    public IssueAttachment Attachment { get; }

    public Guid Id => Attachment.Id;

    public string FileName => Attachment.FileName;

    public string ContentType => Attachment.ContentType;

    public string CreatedText => Attachment.CreatedAtUtc.ToLocalTime().ToString("MMM dd, HH:mm");

    public string SizeText
    {
        get
        {
            const double kilobyte = 1024d;
            const double megabyte = 1024d * 1024d;

            return Attachment.SizeBytes switch
            {
                >= (long)megabyte => $"{Attachment.SizeBytes / megabyte:F1} MB",
                >= (long)kilobyte => $"{Attachment.SizeBytes / kilobyte:F1} KB",
                _ => $"{Attachment.SizeBytes} B",
            };
        }
    }
}
