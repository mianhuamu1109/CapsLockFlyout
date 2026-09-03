using FluentFlyoutWPF.CapsFlyout;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace FluentFlyoutWPF;

public partial class App : System.Windows.Application
{
    private const string MutexName = @"Local\CapsLockFlyout.4F3D3F2D-03C4-4E68-9211-6CC2E46B3380";
    private const string ExitEventName = @"Local\CapsLockFlyout.Exit.4F3D3F2D-03C4-4E68-9211-6CC2E46B3380";
    private const string ShowSettingsEventName = @"Local\CapsLockFlyout.Settings.4F3D3F2D-03C4-4E68-9211-6CC2E46B3380";
    private Mutex? _singleInstanceMutex;
    private bool _ownsMutex;
    private EventWaitHandle? _exitEvent;
    private RegisteredWaitHandle? _exitRegistration;
    private EventWaitHandle? _showSettingsEvent;
    private RegisteredWaitHandle? _showSettingsRegistration;
    private MainWindow? _flyout;
    private StartupSettingsWindow? _settingsWindow;
    private Forms.NotifyIcon? _trayIcon;
    private Forms.ContextMenuStrip? _trayMenu;
    private Drawing.Icon? _trayIconImage;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            WriteCrashLog(args.ExceptionObject as Exception ?? new Exception(args.ExceptionObject?.ToString()));

        try
        {
            AppStorage.EnsureDataDirectory();

            if (e.Args.Contains("--service", StringComparer.OrdinalIgnoreCase))
            {
                Shutdown(WindowsServiceHost.Run());
                return;
            }

            if (e.Args.Contains("--install-service", StringComparer.OrdinalIgnoreCase))
            {
                Shutdown(WindowsServiceRegistration.Install(out _) ? 0 : 1);
                return;
            }

            if (e.Args.Contains("--uninstall-service", StringComparer.OrdinalIgnoreCase))
            {
                Shutdown(WindowsServiceRegistration.Uninstall(out _) ? 0 : 1);
                return;
            }

            var previewRequested = e.Args.Contains("--preview", StringComparer.OrdinalIgnoreCase);
            if (e.Args.Contains("--exit", StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    using var existingExitEvent = EventWaitHandle.OpenExisting(ExitEventName);
                    _ = existingExitEvent.Set();
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                }
                WaitForExistingInstanceToExit();
                Shutdown();
                return;
            }

            // Remove the registry-based launcher used by earlier builds. The
            // service is now the sole supported automatic-start mechanism.
            StartupRegistration.RemoveLegacyRunRegistration();

            _singleInstanceMutex = new Mutex(true, MutexName, out var isFirstInstance);
            _ownsMutex = isFirstInstance;
            if (!isFirstInstance)
            {
                if (!e.Args.Contains("--startup", StringComparer.OrdinalIgnoreCase) &&
                    !previewRequested)
                {
                    using var showEvent = new EventWaitHandle(
                        false,
                        EventResetMode.AutoReset,
                        ShowSettingsEventName);
                    _ = showEvent.Set();
                }
                Shutdown();
                return;
            }

            _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
            _exitRegistration = ThreadPool.RegisterWaitForSingleObject(
                _exitEvent,
                (_, _) => Dispatcher.BeginInvoke(Shutdown),
                null,
                Timeout.Infinite,
                executeOnlyOnce: false);

            _showSettingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEventName);
            _showSettingsRegistration = ThreadPool.RegisterWaitForSingleObject(
                _showSettingsEvent,
                (_, _) => Dispatcher.BeginInvoke(ShowSettingsWindow),
                null,
                Timeout.Infinite,
                executeOnlyOnce: false);

            _flyout = new MainWindow();
            MainWindow = _flyout;

            // Create the HWND once, then keep it hidden while the monitors run.
            _flyout.Show();
            if (previewRequested)
                _flyout.ShowPreview();
            else
                _flyout.HideImmediately();

            CreateTrayIcon();

            var silent = e.Args.Contains("--startup", StringComparer.OrdinalIgnoreCase) ||
                         previewRequested;
            if (!silent)
                Dispatcher.BeginInvoke(ShowSettingsWindow);
        }
        catch (Exception ex)
        {
            WriteCrashLog(ex);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        _trayMenu?.Dispose();
        _trayMenu = null;
        _trayIconImage?.Dispose();
        _trayIconImage = null;
        _flyout?.Dispose();
        _showSettingsRegistration?.Unregister(null);
        _showSettingsEvent?.Dispose();
        _exitRegistration?.Unregister(null);
        _exitEvent?.Dispose();
        if (_singleInstanceMutex is not null)
        {
            if (_ownsMutex)
                _singleInstanceMutex.ReleaseMutex();
            _singleInstanceMutex.Dispose();
        }
        base.OnExit(e);
    }

    private void CreateTrayIcon()
    {
        try
        {
            var resource = GetResourceStream(new Uri("Resources/FluentFlyout2.ico", UriKind.Relative));
            if (resource?.Stream is not null)
            {
                using var sourceIcon = new Drawing.Icon(resource.Stream);
                _trayIconImage = (Drawing.Icon)sourceIcon.Clone();
            }

            _trayMenu = new Forms.ContextMenuStrip();
            var settingsItem = new Forms.ToolStripMenuItem("设置");
            settingsItem.Click += (_, _) => RunOnDispatcher(ShowSettingsWindow);
            var previewItem = new Forms.ToolStripMenuItem("显示预览");
            previewItem.Click += (_, _) => RunOnDispatcher(() => _flyout?.ShowPreview());
            var exitItem = new Forms.ToolStripMenuItem("退出程序");
            exitItem.Click += (_, _) => RunOnDispatcher(Shutdown);
            _trayMenu.Items.Add(settingsItem);
            _trayMenu.Items.Add(previewItem);
            _trayMenu.Items.Add(new Forms.ToolStripSeparator());
            _trayMenu.Items.Add(exitItem);

            _trayIcon = new Forms.NotifyIcon
            {
                Icon = _trayIconImage,
                Text = "CapsLockFlyout",
                ContextMenuStrip = _trayMenu,
                Visible = true
            };
            _trayIcon.DoubleClick += (_, _) => RunOnDispatcher(ShowSettingsWindow);
        }
        catch (Exception exception)
        {
            WriteCrashLog(exception);
        }
    }

    private void RunOnDispatcher(Action action)
    {
        if (!Dispatcher.HasShutdownStarted)
            Dispatcher.BeginInvoke(action);
    }

    private static void WaitForExistingInstanceToExit()
    {
        try
        {
            using var existingMutex = Mutex.OpenExisting(MutexName);
            if (existingMutex.WaitOne(TimeSpan.FromSeconds(5)))
                existingMutex.ReleaseMutex();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }
        catch (AbandonedMutexException)
        {
            // The previous process has already gone away.
        }
    }

    private void ShowSettingsWindow()
    {
        if (_settingsWindow is not null)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
                _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Show();
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new StartupSettingsWindow();
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog(e.Exception);
        e.Handled = false;
    }

    private static void WriteCrashLog(Exception exception)
    {
        try
        {
            AppStorage.EnsureDataDirectory();
            File.AppendAllText(
                AppStorage.ErrorLogPath,
                $"{DateTime.Now:O}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
