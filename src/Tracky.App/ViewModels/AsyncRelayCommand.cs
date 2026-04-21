using System.Windows.Input;

namespace Tracky.App.ViewModels;

// 비동기 UI command를 외부 MVVM 라이브러리 없이 표현해, 실행 조건과 갱신 시점을 ViewModel 코드에서 직접 추적한다.
public sealed class AsyncRelayCommand(
    Func<CancellationToken, Task> execute,
    Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return canExecute?.Invoke() ?? true;
    }

    public async void Execute(object? parameter)
    {
        await ExecuteAsync(parameter);
    }

    public Task ExecuteAsync(object? parameter = null)
    {
        return execute(CancellationToken.None);
    }

    public void NotifyCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
