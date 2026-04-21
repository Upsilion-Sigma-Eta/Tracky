using System.Globalization;
using Tracky.Core.Issues;

namespace Tracky.App.ViewModels;

public sealed class IssueCommentViewModel(IssueComment comment) : ViewModelBase
{
    public IssueComment Comment { get; } = comment;

    public string AuthorDisplayName => Comment.AuthorDisplayName;

    public string Body => Comment.Body;

    public IReadOnlyList<IssueContentBlockViewModel> BodyBlocks { get; } = IssueContentRenderer.Render(
        comment.Body,
        comment.BodyFormat,
        string.Empty);

    // 댓글 본문도 이슈 설명과 동일한 WebView 문서 경로를 사용해 HTML의 CSS 표현력을 잃지 않게 한다.
    // BodyBlocks는 WebView를 끄는 테스트 환경과 텍스트 fallback을 위한 보조 모델로 유지한다.
    public string BodyHtmlDocument { get; } = IssueHtmlDocumentRenderer.RenderDocument(
        comment.Body,
        comment.BodyFormat,
        string.Empty);

    public string BodyFallbackText => string.Join(
        Environment.NewLine,
        BodyBlocks.Select(static block => block.DisplayText));

    public double BodyPreviewHeight { get; } = IssueHtmlDocumentRenderer.EstimatePreviewHeight(comment.Body);

    public string BodyFormatText => IssueDetailViewModel.FormatContentKind(Comment.BodyFormat);

    public string CreatedText => Comment.CreatedAtUtc.ToLocalTime().ToString("MMM dd, HH:mm", CultureInfo.CurrentCulture);
}
