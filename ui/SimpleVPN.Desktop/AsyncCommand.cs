using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SimpleVPN.Desktop
{
    public sealed class AsyncCommand : ICommand
    {
        readonly Func<CancellationToken, Task> _handler;
        CancellationTokenSource _cts;
        public event EventHandler CanExecuteChanged { add { } remove { } }
        public AsyncCommand(Func<CancellationToken, Task> handler) => _handler = handler;

        public bool CanExecute(object parameter) => true;
        public async void Execute(object parameter)
        {
            _cts?.Cancel(); _cts = new CancellationTokenSource();
            try { await _handler(_cts.Token); } catch { /* swallow UI errors */ }
        }
    }
}
