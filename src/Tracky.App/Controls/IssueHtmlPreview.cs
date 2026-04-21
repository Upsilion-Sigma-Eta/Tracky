using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Tracky.App.Controls;

public sealed class IssueHtmlPreview : ContentControl
{
    public static readonly StyledProperty<string?> HtmlContentProperty =
        AvaloniaProperty.Register<IssueHtmlPreview, string?>(nameof(HtmlContent));

    public static readonly StyledProperty<string?> FallbackTextProperty =
        AvaloniaProperty.Register<IssueHtmlPreview, string?>(nameof(FallbackText));

    public static readonly StyledProperty<double> PreviewHeightProperty =
        AvaloniaProperty.Register<IssueHtmlPreview, double>(nameof(PreviewHeight), 180);

    public string? HtmlContent
    {
        get => GetValue(HtmlContentProperty);
        set => SetValue(HtmlContentProperty, value);
    }

    public string? FallbackText
    {
        get => GetValue(FallbackTextProperty);
        set => SetValue(FallbackTextProperty, value);
    }

    public double PreviewHeight
    {
        get => GetValue(PreviewHeightProperty);
        set => SetValue(PreviewHeightProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdatePreviewContent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == HtmlContentProperty
            || change.Property == FallbackTextProperty
            || change.Property == PreviewHeightProperty)
        {
            UpdatePreviewContent();
        }
    }

    private void UpdatePreviewContent()
    {
        if (CanUseNativeWebView())
        {
            UpdateNativeWebView();
            return;
        }

        UpdateTextFallback();
    }

    private void UpdateTextFallback()
    {
        // 헤드리스 테스트와 ScrollViewer 내부 배치에서는 native WebView 표면을 쓰지 않는다.
        // 특히 native WebView는 스크롤 오프셋과 클리핑을 무시해 issue body가 헤더 위에 떠 보일 수 있어 텍스트 fallback으로 낮춘다.
        Content = new TextBlock
        {
            Text = FallbackText,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Application.Current?.Resources.TryGetResource("TrackyInkBrush", null, out var brush) == true
                ? brush as IBrush
                : null,
        };
    }

    private void UpdateNativeWebView()
    {
        if (Content is NativeWebView webView)
        {
            webView.Height = PreviewHeight;
            NavigateToCurrentHtml(webView);
            return;
        }

        webView = new NativeWebView
        {
            Height = PreviewHeight,
            MinHeight = 120,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ClipToBounds = true,
        };

        // Avalonia 12 공식 WebView는 HtmlContent 속성 대신 NavigateToString 메서드로 문서 문자열을 주입한다.
        // 컨트롤을 Content에 붙인 직후 현재 HTML을 전달해 초기 표시와 이후 바인딩 갱신 경로를 동일하게 유지한다.
        Content = webView;
        NavigateToCurrentHtml(webView);
    }

    private void NavigateToCurrentHtml(NativeWebView webView)
    {
        webView.NavigateToString(HtmlContent ?? string.Empty);
    }

    private bool CanUseNativeWebView()
    {
        return IsNativeWebViewEnabled()
            && IsAttachedToVisualHost()
            && !IsHostedInsideScrollViewer();
    }

    private bool IsAttachedToVisualHost()
    {
        return this.GetVisualAncestors().Any();
    }

    private bool IsHostedInsideScrollViewer()
    {
        return this.GetVisualAncestors().OfType<ScrollViewer>().Any();
    }

    private static bool IsNativeWebViewEnabled()
    {
        return !string.Equals(
            Environment.GetEnvironmentVariable("TRACKY_DISABLE_WEBVIEW"),
            "1",
            StringComparison.OrdinalIgnoreCase);
    }
}
