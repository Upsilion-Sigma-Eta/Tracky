using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Markdig;
using Tracky.Core.Issues;

namespace Tracky.App.ViewModels;

public sealed class IssueContentBlockViewModel(IssueContentBlockKind kind, string text)
{
    public IssueContentBlockKind Kind { get; } = kind;

    public string Text { get; } = text;

    public string DisplayText => Kind switch
    {
        IssueContentBlockKind.ListItem => $"- {Text}",
        IssueContentBlockKind.Quote => $"> {Text}",
        _ => Text,
    };

    public bool IsHeading1 => Kind == IssueContentBlockKind.Heading1;

    public bool IsHeading2 => Kind == IssueContentBlockKind.Heading2;

    public bool IsHeading3 => Kind == IssueContentBlockKind.Heading3;

    public bool IsCodeBlock => Kind == IssueContentBlockKind.CodeBlock;

    public bool IsQuote => Kind == IssueContentBlockKind.Quote;

    public bool IsListItem => Kind == IssueContentBlockKind.ListItem;
}

public enum IssueContentBlockKind
{
    Paragraph,
    Heading1,
    Heading2,
    Heading3,
    ListItem,
    CodeBlock,
    Quote,
}

internal static partial class IssueContentRenderer
{
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    private static readonly HashSet<string> BlockElementNames =
    [
        "article",
        "blockquote",
        "div",
        "h1",
        "h2",
        "h3",
        "h4",
        "h5",
        "h6",
        "li",
        "ol",
        "p",
        "pre",
        "section",
        "table",
        "ul",
    ];

    public static IReadOnlyList<IssueContentBlockViewModel> Render(
        string body,
        IssueContentFormat format,
        string emptyText)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return [new IssueContentBlockViewModel(IssueContentBlockKind.Paragraph, emptyText)];
        }

        var html = format == IssueContentFormat.Markdown
            ? Markdown.ToHtml(body, MarkdownPipeline)
            : body;

        var blocks = RenderHtml(html);
        if (blocks.Count > 0)
        {
            return blocks;
        }

        // 잘못된 HTML 조각도 편집 원문을 그대로 노출하지 않고 사용자가 읽을 수 있는 텍스트로 낮춘다.
        var fallbackText = NormalizeInlineText(HtmlEntity.DeEntitize(body));
        return [new IssueContentBlockViewModel(IssueContentBlockKind.Paragraph, fallbackText)];
    }

    private static List<IssueContentBlockViewModel> RenderHtml(string html)
    {
        var document = new HtmlDocument
        {
            OptionAutoCloseOnEnd = true,
            OptionCheckSyntax = false,
            OptionFixNestedTags = true,
        };
        document.LoadHtml(html);

        var blocks = new List<IssueContentBlockViewModel>();
        AppendNodeBlocks(document.DocumentNode, blocks);
        return blocks;
    }

    private static void AppendNodeBlocks(HtmlNode node, List<IssueContentBlockViewModel> blocks)
    {
        if (node.NodeType == HtmlNodeType.Text)
        {
            AddBlock(blocks, IssueContentBlockKind.Paragraph, NormalizeInlineText(node.InnerText));
            return;
        }

        if (node.NodeType != HtmlNodeType.Document && node.NodeType != HtmlNodeType.Element)
        {
            return;
        }

        var name = node.Name.ToLowerInvariant();
        if (name is "script" or "style" or "head" or "meta" or "link")
        {
            return;
        }

        switch (name)
        {
            case "#document":
            case "html":
            case "body":
                AppendChildBlocks(node, blocks);
                break;
            case "h1":
                AddBlock(blocks, IssueContentBlockKind.Heading1, CollectInlineText(node));
                break;
            case "h2":
                AddBlock(blocks, IssueContentBlockKind.Heading2, CollectInlineText(node));
                break;
            case "h3":
            case "h4":
            case "h5":
            case "h6":
                AddBlock(blocks, IssueContentBlockKind.Heading3, CollectInlineText(node));
                break;
            case "p":
                AddBlock(blocks, IssueContentBlockKind.Paragraph, CollectInlineText(node));
                break;
            case "pre":
                AddBlock(blocks, IssueContentBlockKind.CodeBlock, HtmlEntity.DeEntitize(node.InnerText).Trim('\r', '\n'));
                break;
            case "blockquote":
                AddBlock(blocks, IssueContentBlockKind.Quote, CollectInlineText(node));
                break;
            case "ul":
            case "ol":
                AppendListBlocks(node, blocks);
                break;
            case "li":
                AddBlock(blocks, IssueContentBlockKind.ListItem, CollectInlineText(node));
                break;
            case "table":
                AppendTableBlocks(node, blocks);
                break;
            case "br":
                break;
            default:
                if (HasBlockChild(node))
                {
                    AppendChildBlocks(node, blocks);
                }
                else
                {
                    AddBlock(blocks, IssueContentBlockKind.Paragraph, CollectInlineText(node));
                }

                break;
        }
    }

    private static void AppendChildBlocks(HtmlNode node, List<IssueContentBlockViewModel> blocks)
    {
        foreach (var child in node.ChildNodes)
        {
            AppendNodeBlocks(child, blocks);
        }
    }

    private static void AppendListBlocks(HtmlNode node, List<IssueContentBlockViewModel> blocks)
    {
        foreach (var listItem in node.ChildNodes.Where(static child => child.Name.Equals("li", StringComparison.OrdinalIgnoreCase)))
        {
            AddBlock(blocks, IssueContentBlockKind.ListItem, CollectInlineText(listItem));
        }
    }

    private static void AppendTableBlocks(HtmlNode node, List<IssueContentBlockViewModel> blocks)
    {
        foreach (var row in node.Descendants("tr"))
        {
            var cells = row.ChildNodes
                .Where(static child => child.Name.Equals("th", StringComparison.OrdinalIgnoreCase)
                    || child.Name.Equals("td", StringComparison.OrdinalIgnoreCase))
                .Select(CollectInlineText)
                .Where(static text => !string.IsNullOrWhiteSpace(text));

            AddBlock(blocks, IssueContentBlockKind.Paragraph, string.Join(" | ", cells));
        }
    }

    private static bool HasBlockChild(HtmlNode node)
    {
        return node.ChildNodes.Any(static child =>
            child.NodeType == HtmlNodeType.Element
            && BlockElementNames.Contains(child.Name.ToLowerInvariant()));
    }

    private static string CollectInlineText(HtmlNode node)
    {
        var builder = new StringBuilder();
        AppendInlineText(node, builder);
        return NormalizeInlineText(builder.ToString());
    }

    private static void AppendInlineText(HtmlNode node, StringBuilder builder)
    {
        if (node.NodeType == HtmlNodeType.Text)
        {
            builder.Append(HtmlEntity.DeEntitize(node.InnerText));
            return;
        }

        if (node.NodeType != HtmlNodeType.Element && node.NodeType != HtmlNodeType.Document)
        {
            return;
        }

        var name = node.Name.ToLowerInvariant();
        if (name is "script" or "style" or "head" or "meta" or "link")
        {
            return;
        }

        if (name == "br")
        {
            builder.AppendLine();
            return;
        }

        foreach (var child in node.ChildNodes)
        {
            AppendInlineText(child, builder);
        }

        if (name is "p" or "div")
        {
            builder.AppendLine();
        }
    }

    private static void AddBlock(
        List<IssueContentBlockViewModel> blocks,
        IssueContentBlockKind kind,
        string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            blocks.Add(new IssueContentBlockViewModel(kind, text));
        }
    }

    private static string NormalizeInlineText(string text)
    {
        var normalizedLineEndings = text.ReplaceLineEndings("\n");
        var collapsedSpaces = HorizontalWhitespaceRegex().Replace(normalizedLineEndings, " ");
        var lines = collapsedSpaces
            .Split('\n')
            .Select(static line => line.Trim())
            .Where(static line => line.Length > 0);
        return string.Join(Environment.NewLine, lines);
    }

    [GeneratedRegex(@"[ \t\f\v]+")]
    private static partial Regex HorizontalWhitespaceRegex();
}
