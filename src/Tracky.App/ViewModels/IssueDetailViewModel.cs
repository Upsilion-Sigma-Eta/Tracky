using Tracky.Core.Issues;

namespace Tracky.App.ViewModels;

public sealed class IssueDetailViewModel(IssueDetail detail) : ViewModelBase
{
    public IssueDetail Detail { get; } = detail;

    public IssueCardViewModel Summary { get; } = new IssueCardViewModel(detail.Summary);

    public string DescriptionText => string.IsNullOrWhiteSpace(Detail.Description)
        ? "No description has been written for this issue yet."
        : Detail.Description;

    public IReadOnlyList<IssueContentBlockViewModel> DescriptionBlocks { get; } = IssueContentRenderer.Render(
        detail.Description,
        detail.DescriptionFormat,
        "No description has been written for this issue yet.");

    // 실제 화면은 로컬 WebView에서 HTML/CSS를 렌더링하고, 헤드리스 테스트는 아래 fallback 텍스트를 사용한다.
    // 두 경로가 같은 원문과 포맷 값을 공유해야 Markdown/HTML 저장 계약이 UI에서도 흔들리지 않는다.
    public string DescriptionHtmlDocument { get; } = IssueHtmlDocumentRenderer.RenderDocument(
        detail.Description,
        detail.DescriptionFormat,
        "No description has been written for this issue yet.");

    public string DescriptionFallbackText => string.Join(
        Environment.NewLine,
        DescriptionBlocks.Select(static block => block.DisplayText));

    public double DescriptionPreviewHeight { get; } = IssueHtmlDocumentRenderer.EstimatePreviewHeight(detail.Description);

    public string DescriptionFormatText => FormatContentKind(Detail.DescriptionFormat);

    public IReadOnlyList<IssueCommentViewModel> Comments { get; } = [.. detail.Comments.Select(static comment => new IssueCommentViewModel(comment))];

    public IReadOnlyList<IssueAttachmentViewModel> Attachments { get; } = [.. detail.Attachments.Select(static attachment => new IssueAttachmentViewModel(attachment))];

    public IReadOnlyList<IssueActivityViewModel> Activity { get; } = [.. detail.Activity.Select(static activity => new IssueActivityViewModel(activity))];

    public IReadOnlyList<IssueReminderViewModel> Reminders { get; } = [.. detail.Reminders.Select(static reminder => new IssueReminderViewModel(reminder))];

    public IReadOnlyList<IssueRelationViewModel> Relations { get; } = [.. detail.Relations.Select(static relation => new IssueRelationViewModel(relation))];

    public bool HasComments => Comments.Count > 0;

    public bool HasNoComments => !HasComments;

    public bool HasAttachments => Attachments.Count > 0;

    public bool HasNoAttachments => !HasAttachments;

    public bool HasActivity => Activity.Count > 0;

    public bool HasNoActivity => !HasActivity;

    public bool HasReminders => Reminders.Count > 0;

    public bool HasNoReminders => !HasReminders;

    public bool HasRelations => Relations.Count > 0;

    public bool HasNoRelations => !HasRelations;

    public string CommentCountText => Comments.Count == 1
        ? "1 comment"
        : $"{Comments.Count} comments";

    public string AttachmentCountText => Attachments.Count == 1
        ? "1 attachment"
        : $"{Attachments.Count} attachments";

    public string ReminderCountText => Reminders.Count == 1
        ? "1 reminder"
        : $"{Reminders.Count} reminders";

    public string RelationCountText => Relations.Count == 1
        ? "1 relation"
        : $"{Relations.Count} relations";

    internal static string FormatContentKind(IssueContentFormat format) => format switch
    {
        IssueContentFormat.Html => "HTML",
        _ => "Markdown",
    };
}
