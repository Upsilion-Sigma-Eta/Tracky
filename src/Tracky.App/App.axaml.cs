using System.ComponentModel;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Tracky.Core.Preferences;
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

            ApplyAppearancePreferences(mainViewModel);
            mainViewModel.PropertyChanged += (_, args) => ApplyAppearancePreferencesWhenNeeded(mainViewModel, args);

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

    private static void ApplyAppearancePreferencesWhenNeeded(
        MainWindowViewModel viewModel,
        PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MainWindowViewModel.SelectedThemePreference)
            or nameof(MainWindowViewModel.CompactDensityPreference))
        {
            ApplyAppearancePreferences(viewModel);
        }
    }

    private static void ApplyAppearancePreferences(MainWindowViewModel viewModel)
    {
        if (Current is null)
        {
            return;
        }

        // Phase3 환경설정은 DB 저장만이 아니라 실제 앱 테마와 팔레트까지 반영되어야
        // Preferences / Appearance 모듈의 사용자 체감 계약을 만족한다.
        Current.RequestedThemeVariant = viewModel.SelectedThemePreference switch
        {
            AppThemePreference.Dark => ThemeVariant.Dark,
            AppThemePreference.Light => ThemeVariant.Light,
            _ => ThemeVariant.Default,
        };

        var useDarkPalette = viewModel.SelectedThemePreference == AppThemePreference.Dark;
        SetBrushColor("TrackyCanvasBrush", useDarkPalette ? "#111827" : "#F4EFE7");
        SetBrushColor("TrackySurfaceBrush", useDarkPalette ? "#171717" : "#FFFDF8");
        SetBrushColor("TrackySurfaceStrongBrush", useDarkPalette ? "#1F2937" : "#FFF8EF");
        SetBrushColor("TrackyBorderBrush", useDarkPalette ? "#374151" : "#E7E5E4");
        SetBrushColor("TrackyInkBrush", useDarkPalette ? "#F9FAFB" : "#1F2937");
        SetBrushColor("TrackyMutedInkBrush", useDarkPalette ? "#D1D5DB" : "#6B7280");
        SetBrushColor("TrackyAccentBrush", useDarkPalette ? "#2DD4BF" : "#0F766E");
        SetBrushColor("TrackyAccentSoftBrush", useDarkPalette ? "#134E4A" : "#D1FAE5");
        SetBrushColor("TrackyNavBrush", useDarkPalette ? "#030712" : "#171717");
        SetBrushColor("TrackyNavInkBrush", "#FAFAF9");
        SetBrushColor("TrackyNavMutedInkBrush", useDarkPalette ? "#9CA3AF" : "#A8A29E");
        ApplyDensityPreference(viewModel.CompactDensityPreference);
    }

    private static void SetBrushColor(string resourceKey, string colorHex)
    {
        if (Current?.Resources.TryGetResource(resourceKey, null, out var resource) == true
            && resource is SolidColorBrush brush
            && Color.TryParse(colorHex, out var color))
        {
            brush.Color = color;
        }
    }

    private static void ApplyDensityPreference(bool compactDensity)
    {
        if (Current is null)
        {
            return;
        }

        foreach (var style in Current.Styles)
        {
            if (style.GetType().FullName != "Avalonia.Themes.Fluent.FluentTheme")
            {
                continue;
            }

            // FluentTheme의 DensityStyle 타입은 Avalonia 버전에 묶여 있으므로 리플렉션으로 좁게 변경한다.
            // 이렇게 해두면 Phase3 설정 저장값이 실제 컨트롤 밀도에도 반영되면서 패키지 세부 타입 의존을 줄일 수 있다.
            var densityProperty = style.GetType().GetProperty("DensityStyle");
            if (densityProperty is null)
            {
                return;
            }

            var densityValueText = compactDensity ? "Compact" : "Normal";
            var densityValue = densityProperty.PropertyType.IsEnum
                ? Enum.Parse(densityProperty.PropertyType, densityValueText, ignoreCase: true)
                : densityValueText;
            densityProperty.SetValue(style, densityValue);
            return;
        }
    }
}
