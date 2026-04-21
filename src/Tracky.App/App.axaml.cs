using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Tracky.App.Services;
using Tracky.App.ViewModels;
using Tracky.App.Views;
using Tracky.Infrastructure.Persistence;

namespace Tracky.App;

// ReSharper disable once PartialTypeWithSinglePart
// Avalonia XAML 컴파일러가 App.axaml에서 partial 짝을 생성하므로 code-behind 쪽 partial 선언을 유지한다.
public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();

            var mainWindow = new MainWindow();
            var mainViewModel = new MainWindowViewModel(
                new SqliteTrackyWorkspaceService(),
                new WindowAttachmentPicker(mainWindow),
                new ShellAttachmentLauncher());
            mainWindow.DataContext = mainViewModel;

            // 앱 창이 열린 뒤 로컬 워크스페이스를 초기화하면 첫 페인트를 막지 않으면서도,
            // SQLite 부트스트랩과 Phase 1 홈 화면 로딩을 같은 진입점으로 묶을 수 있다.
            mainWindow.Opened += async (_, _) => await mainViewModel.InitializeAsync();
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void DisableAvaloniaDataAnnotationValidation()
    {
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
