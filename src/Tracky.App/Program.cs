using Avalonia;

namespace Tracky.App;

// ReSharper disable once ClassNeverInstantiated.Global
// 앱은 static Main에서 시작하므로 Program 인스턴스를 만들지 않는 것이 정상이다.
internal sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    // ReSharper disable once MemberCanBePrivate.Global
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>();

        // Rider nullable 분석이 Avalonia builder 생성을 보수적으로 추론하므로,
        // fluent 설정을 이어 붙이기 전에 앱 진입점의 필수 객체 계약을 먼저 고정한다.
        ArgumentNullException.ThrowIfNull(builder);
        
        return builder
            .UsePlatformDetect()
            .WithInterFont()!
            .LogToTrace();
    }
}
