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
        if (UseNativeWebView())
        {
            UpdateNativeWebView();
            return;
        }

        // 헤드리스 테스트에서는 OS WebView 호스트가 없으므로 같은 데이터 바인딩을 텍스트 대체 뷰로 검증한다.
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

    private static bool UseNativeWebView()
    {
        return !string.Equals(
            Environment.GetEnvironmentVariable("TRACKY_DISABLE_WEBVIEW"),
            "1",
            StringComparison.OrdinalIgnoreCase);
    }
}
