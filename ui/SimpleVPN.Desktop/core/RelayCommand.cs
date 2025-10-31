using System;
using System.Windows.Input;

namespace SimpleVPN.Desktop
{
    public sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> _exec;
        private readonly Func<object?, bool> _can;

        public RelayCommand(Action<object?> exec, Func<object?, bool>? can = null)
        { _exec = exec; _can = can ?? (_ => true); }

        public bool CanExecute(object? parameter) => _can(parameter);
        public void Execute(object? parameter) => _exec(parameter);
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}
