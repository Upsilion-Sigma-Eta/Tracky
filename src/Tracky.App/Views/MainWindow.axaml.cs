using Avalonia.Controls;

namespace Tracky.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosed(EventArgs e)
    {
        // MainWindowViewModel이 내부 동기화 리소스를 소유하므로, 창 종료 시 DataContext 수명도 명확히 닫는다.
        (DataContext as IDisposable)?.Dispose();
        base.OnClosed(e);
    }
}
