using System.ComponentModel;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
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
            // Avalonia 12에서는 BindingPlugins API가 제거되었고 DataAnnotations 검증도 기본 활성 경로가 아니다.
            // 따라서 Avalonia 11에서 쓰던 명시적 플러그인 제거 코드는 유지하지 않는다.

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

        // 테마는 단순 Light/Dark가 아니라 명시적인 4개 팔레트 계약이다.
        // 저장된 선호값을 즉시 리소스 브러시에 반영해 열린 창 전체가 같은 색상 체계를 공유하게 한다.
        var palette = GetThemePalette(viewModel.SelectedThemePreference);
        Current.RequestedThemeVariant = palette.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;

        SetBrushColor("TrackyCanvasBrush", palette.Canvas);
        SetBrushColor("TrackySurfaceBrush", palette.Surface);
        SetBrushColor("TrackySurfaceStrongBrush", palette.SurfaceStrong);
        SetBrushColor("TrackyBorderBrush", palette.Border);
        SetBrushColor("TrackyInkBrush", palette.Ink);
        SetBrushColor("TrackyMutedInkBrush", palette.MutedInk);
        SetBrushColor("TrackyAccentBrush", palette.Accent);
        SetBrushColor("TrackyAccentSoftBrush", palette.AccentSoft);
        SetBrushColor("TrackyNavBrush", palette.Nav);
        SetBrushColor("TrackyNavHoverBrush", palette.NavHover);
        SetBrushColor("TrackyNavActiveBrush", palette.NavActive);
        SetBrushColor("TrackyNavBorderBrush", palette.NavBorder);
        SetBrushColor("TrackyNavInkBrush", palette.NavInk);
        SetBrushColor("TrackyNavMutedInkBrush", palette.NavMutedInk);
        ApplyDensityPreference(viewModel.CompactDensityPreference);
    }

    private static ThemePalette GetThemePalette(AppThemePreference preference) => preference switch
    {
        AppThemePreference.BlueOrange => new ThemePalette(
            IsDark: false,
            Canvas: "#EFF6FF",
            Surface: "#FFFFFF",
            SurfaceStrong: "#DBEAFE",
            Border: "#93C5FD",
            Ink: "#172033",
            MutedInk: "#475569",
            Accent: "#EA580C",
            AccentSoft: "#FFEDD5",
            Nav: "#1D4ED8",
            NavHover: "#2563EB",
            NavActive: "#F97316",
            NavBorder: "#60A5FA",
            NavInk: "#FFFFFF",
            NavMutedInk: "#DBEAFE"),
        AppThemePreference.DarkBlue => new ThemePalette(
            IsDark: true,
            Canvas: "#0F172A",
            Surface: "#111827",
            SurfaceStrong: "#1E293B",
            Border: "#334155",
            Ink: "#F8FAFC",
            MutedInk: "#CBD5E1",
            Accent: "#38BDF8",
            AccentSoft: "#082F49",
            Nav: "#020617",
            NavHover: "#1E293B",
            NavActive: "#2563EB",
            NavBorder: "#334155",
            NavInk: "#F8FAFC",
            NavMutedInk: "#94A3B8"),
        AppThemePreference.DarkOrange => new ThemePalette(
            IsDark: true,
            Canvas: "#18110B",
            Surface: "#1C1917",
            SurfaceStrong: "#292524",
            Border: "#57534E",
            Ink: "#FAFAF9",
            MutedInk: "#D6D3D1",
            Accent: "#FB923C",
            AccentSoft: "#431407",
            Nav: "#0C0A09",
            NavHover: "#292524",
            NavActive: "#EA580C",
            NavBorder: "#57534E",
            NavInk: "#FAFAF9",
            NavMutedInk: "#A8A29E"),
        _ => new ThemePalette(
            IsDark: false,
            Canvas: "#F6F8FA",
            Surface: "#FFFFFF",
            SurfaceStrong: "#F6F8FA",
            Border: "#D0D7DE",
            Ink: "#24292F",
            MutedInk: "#57606A",
            Accent: "#0969DA",
            AccentSoft: "#DDF4FF",
            Nav: "#24292F",
            NavHover: "#3B434D",
            NavActive: "#0969DA",
            NavBorder: "#57606A",
            NavInk: "#F6F8FA",
            NavMutedInk: "#C9D1D9"),
    };

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

    private sealed record ThemePalette(
        bool IsDark,
        string Canvas,
        string Surface,
        string SurfaceStrong,
        string Border,
        string Ink,
        string MutedInk,
        string Accent,
        string AccentSoft,
        string Nav,
        string NavHover,
        string NavActive,
        string NavBorder,
        string NavInk,
        string NavMutedInk);
}
