using System.Windows.Input;

namespace Tracky.App.ViewModels;

// 외부 MVVM source generator 대신 ViewModel 안의 명시적 command 연결을 유지하기 위한 작은 동기 command 래퍼다.
public sealed class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return true;
    }

    public void Execute(object? parameter)
    {
        execute();
    }

    public void NotifyCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
