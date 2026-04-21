using System.Globalization;
using Tracky.Core.Issues;

namespace Tracky.App.ViewModels;

public sealed class IssueAttachmentViewModel(IssueAttachment attachment) : ViewModelBase
{
    public IssueAttachment Attachment { get; } = attachment;

    public Guid Id => Attachment.Id;

    public string FileName => Attachment.FileName;

    public string ContentType => Attachment.ContentType;

    public string CreatedText => Attachment.CreatedAtUtc.ToLocalTime().ToString("MMM dd, HH:mm", CultureInfo.CurrentCulture);

    public string SizeText
    {
        get
        {
            const double kilobyte = 1024d;
            const double megabyte = 1024d * 1024d;

            return Attachment.SizeBytes switch
            {
                >= (long)megabyte => $"{(Attachment.SizeBytes / megabyte).ToString("F1", CultureInfo.CurrentCulture)} MB",
                >= (long)kilobyte => $"{(Attachment.SizeBytes / kilobyte).ToString("F1", CultureInfo.CurrentCulture)} KB",
                _ => $"{Attachment.SizeBytes} B",
            };
        }
    }
}
