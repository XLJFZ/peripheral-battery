using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace BluetoothBatteryMonitor.Tests;

// WPF Application and tray controls must share one living dispatcher for the
// entire test process. Per-test STA threads leave Application.Current owned by
// a dead thread and make subsequent tray tests fail or hang during cleanup.
internal static class WpfTestHost
{
    private static readonly Lazy<Dispatcher> Ui = new(() =>
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                ready.SetResult(app.Dispatcher);
                app.Run();
            }
            catch (Exception ex) { ready.TrySetException(ex); }
        }) { IsBackground = true, Name = "WPF test dispatcher" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task.WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
    });

    public static void Run(Action action) => Run(() => { action(); return true; });

    public static T Run<T>(Func<T> action)
    {
        var dispatcher = Ui.Value;
        if (dispatcher.CheckAccess()) return action();
        return dispatcher.InvokeAsync(action).Task.WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
    }
}
