using System.Net;
using System.Text.RegularExpressions;
using Markdig;
using Tracky.Core.Issues;

namespace Tracky.App.ViewModels;

internal static partial class IssueHtmlDocumentRenderer
{
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public static string RenderDocument(string body, IssueContentFormat format, string emptyText)
    {
        // 이 렌더러의 목적은 sanitizing이 아니라 로컬 전용 issue body 미리보기다.
        // HTML 전체 문서는 사용자가 작성한 CSS cascade를 깨지 않도록 원문 그대로 WebView에 전달한다.
        var source = string.IsNullOrWhiteSpace(body)
            ? $"<p>{WebUtility.HtmlEncode(emptyText)}</p>"
            : body;

        // Markdown은 GitHub식 작성 경험의 기본값이므로 HTML 문서화 전에 Markdig로 변환한다.
        // Markdown 안에 포함된 raw HTML과 style 속성도 WebView 단계에서 실제 브라우저 규칙으로 해석된다.
        if (format == IssueContentFormat.Markdown)
        {
            source = Markdown.ToHtml(source, MarkdownPipeline);
        }

        return LooksLikeFullDocument(source)
            ? source
            : WrapFragment(source);
    }

    public static double EstimatePreviewHeight(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return 120;
        }

        var lineCount = Math.Max(1, body.ReplaceLineEndings("\n").Split('\n').Length);
        var textLength = StripTags(body).Length;
        var semanticBlockCount = BlockTagRegex().Count(body);
        var estimatedWrappedLines = Math.Max(lineCount, textLength / 86 + 1);

        // WebView의 실제 DOM 높이를 읽지 못하는 환경에서도 긴 issue body가 작은 내부 스크롤에 갇히지 않도록,
        // HTML 블록 태그 수를 보정값으로 더해 상세 화면의 외부 스크롤 흐름에 가깝게 높이를 잡는다.
        return Math.Clamp(estimatedWrappedLines * 22 + semanticBlockCount * 16 + 72, 140, 1200);
    }

    private static string WrapFragment(string html)
    {
        // HTML fragment에는 최소 GitHub markdown-body 계열의 기본값만 제공한다.
        // 사용자가 fragment 안에 둔 <style>이나 inline style은 이 기본 CSS보다 뒤에서 해석되어 표현 의도를 유지한다.
        return $$"""
            <!doctype html>
            <html>
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <style>
                html, body {
                  margin: 0;
                  padding: 0;
                  background: transparent;
                  color: #24292f;
                  font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
                  font-size: 14px;
                  line-height: 1.5;
                }

                * {
                  box-sizing: border-box;
                }

                body {
                  overflow: hidden;
                }

                .tracky-html-render {
                  padding: 0;
                }

                h1, h2, h3, h4, h5, h6 {
                  margin: 0 0 0.65em;
                  font-weight: 600;
                  line-height: 1.25;
                }

                h1 { font-size: 2em; }
                h2 { font-size: 1.5em; }
                h3 { font-size: 1.25em; }

                p, ul, ol, blockquote, pre, table {
                  margin-top: 0;
                  margin-bottom: 1em;
                }

                img, video, canvas, svg {
                  max-width: 100%;
                  height: auto;
                }

                pre {
                  overflow: auto;
                  padding: 12px;
                  background: #f6f8fa;
                  border-radius: 6px;
                }

                code {
                  font-family: ui-monospace, SFMono-Regular, Consolas, "Liberation Mono", Menlo, monospace;
                }

                :not(pre) > code {
                  padding: 0.2em 0.4em;
                  background: rgba(175, 184, 193, 0.2);
                  border-radius: 6px;
                }

                blockquote {
                  padding: 0 1em;
                  color: #57606a;
                  border-left: 0.25em solid #d0d7de;
                }

                table {
                  border-spacing: 0;
                  border-collapse: collapse;
                  width: max-content;
                  max-width: 100%;
                }

                th, td {
                  padding: 6px 13px;
                  border: 1px solid #d0d7de;
                }
              </style>
            </head>
            <body>
              <article class="tracky-html-render">
                {{html}}
              </article>
            </body>
            </html>
            """;
    }

    private static bool LooksLikeFullDocument(string html)
    {
        return FullDocumentRegex().IsMatch(html);
    }

    private static string StripTags(string html)
    {
        return TagRegex().Replace(html, string.Empty);
    }

    [GeneratedRegex(@"<!doctype\s+html|<html[\s>]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FullDocumentRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"</?(p|div|section|article|ul|ol|li|blockquote|pre|table|tr|h[1-6])\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BlockTagRegex();
}
