using System.Windows.Input;

namespace Tracky.App.ViewModels;

// CommandParameter가 필요한 비동기 command용 래퍼이며, 현재는 첨부 파일 열기 흐름에서 사용한다.
public sealed class AsyncRelayCommand<T>(
    Func<T?, CancellationToken, Task> execute,
    Func<T?, bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return canExecute?.Invoke(ConvertParameter(parameter)) ?? true;
    }

    public async void Execute(object? parameter)
    {
        await ExecuteAsync(ConvertParameter(parameter));
    }

    public Task ExecuteAsync(T? parameter)
    {
        return execute(parameter, CancellationToken.None);
    }

    public void NotifyCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    private static T? ConvertParameter(object? parameter)
    {
        return parameter is null
            ? default
            : (T)parameter;
    }
}
