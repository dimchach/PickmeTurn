using System;
using System.Threading;
using System.Windows;

namespace FreeTurnClient;

#nullable enable

public partial class App : Application
{
    private const string InstanceMutexName = @"Local\PickmeTurn.SingleInstance";
    private const string ShowWindowEventName = @"Local\PickmeTurn.ShowWindow";
    private const string ShutdownEventName = @"Local\PickmeTurn.Shutdown";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showWindowEvent;
    private EventWaitHandle? _shutdownEvent;
    private Thread? _showWindowThread;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _showWindowEvent = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            ShowWindowEventName);
        _shutdownEvent = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            ShutdownEventName);

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

        var showWindowEvent = _showWindowEvent;
        var shutdownEvent = _shutdownEvent;

        _showWindowThread = new Thread(() =>
        {
            try
            {
                var events = new WaitHandle[] { showWindowEvent, shutdownEvent };

                while (true)
                {
                    var signaled = WaitHandle.WaitAny(events);

                    if (signaled == 0)
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (MainWindow is MainWindow window)
                                window.ShowMainWindow();
                        }));
                    }
                    else if (signaled == 1)
                    {
                        Dispatcher.BeginInvoke(new Action(async () =>
                        {
                            if (MainWindow is MainWindow window)
                                await window.ExitForUpdateAsync();
                        }));
                    }
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
        _shutdownEvent?.Dispose();

        if (_ownsMutex)
            _instanceMutex?.ReleaseMutex();

        _instanceMutex?.Dispose();

        base.OnExit(e);
    }
}

