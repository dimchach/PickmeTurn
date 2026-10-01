using System;
using System.Threading;
using System.Windows;

namespace FreeTurnClient;

#nullable enable

public partial class App : Application
{
    private const string InstanceMutexName = @"Local\PickmeTurn.SingleInstance";
    private const string ShowWindowEventName = @"Local\PickmeTurn.ShowWindow";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showWindowEvent;
    private Thread? _showWindowThread;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _showWindowEvent = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            ShowWindowEventName);

        _instanceMutex = new Mutex(
            true,
            InstanceMutexName,
            out bool createdNew);

        if (!createdNew)
        {
            _showWindowEvent.Set();
            Shutdown();
            return;
        }

        _ownsMutex = true;

        _showWindowThread = new Thread(() =>
        {
            try
            {
                while (_showWindowEvent.WaitOne())
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (MainWindow is MainWindow window)
                            window.ShowMainWindow();
                    }));
                }
            }
            catch (ObjectDisposedException)
            {
            }
        })
        {
            IsBackground = true
        };

        _showWindowThread.Start();

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showWindowEvent?.Dispose();

        if (_ownsMutex)
            _instanceMutex?.ReleaseMutex();

        _instanceMutex?.Dispose();

        base.OnExit(e);
    }
}

