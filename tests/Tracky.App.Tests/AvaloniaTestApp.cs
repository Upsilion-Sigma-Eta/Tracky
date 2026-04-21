using Avalonia;
using Avalonia.Headless;
using TrackyApp = Tracky.App.App;

[assembly: AvaloniaTestApplication(typeof(Tracky.App.Tests.AvaloniaTestApp))]

namespace Tracky.App.Tests;

public static class AvaloniaTestApp
{
    public static AppBuilder BuildAvaloniaApp()
    {
        // GUI 단위 테스트는 실제 OS 창을 띄우지 않고 XAML과 바인딩만 검증해야 하므로,
        // 프로덕션 App 리소스는 그대로 쓰되 렌더링 백엔드는 headless로 바꾼다.
        Environment.SetEnvironmentVariable("TRACKY_DISABLE_WEBVIEW", "1");
        return AppBuilder.Configure<TrackyApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}
