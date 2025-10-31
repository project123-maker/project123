using System;
using System.Windows.Input;

namespace SimpleVPN.Desktop
{
    public sealed class RelayCommand : ICommand
    {
        readonly Action _run;
        readonly Func<bool> _can;
        public RelayCommand(Action run, Func<bool> can = null) { _run = run; _can = can; }
        public bool CanExecute(object _) => _can?.Invoke() ?? true;
        public void Execute(object _) => _run();
        public event EventHandler CanExecuteChanged;
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
