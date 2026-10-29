using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MessageBox = System.Windows.MessageBox;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Diagnostics;
using ToolDock.Models;
using ToolDock.Services;

namespace ToolDock;

public partial class App : System.Windows.Application
{
    private const int MaxSupportedInstances = 32;
    private NativeTrayIcon? _trayIcon;
    private ContextMenu? _trayMenu;
    private MainWindow? _mainWindow;
    private ConfigService? _configService;
    private DockConfig? _config;
    private MenuItem? _showDockItem;
    private MenuItem? _disableDockItem;
    private MenuItem? _settingsItem;
    private MenuItem? _reloadDockItem;
    private MenuItem? _reloadAppItem;
    private MenuItem? _troubleshootingRootItem;
    private MenuItem? _troubleshootingOpenFolderItem;
    private MenuItem? _troubleshootingResetThisInstanceItem;
    private MenuItem? _troubleshootingResetAllInstancesItem;
    private MenuItem? _troubleshootingHardResetItem;
    private MenuItem? _exitItem;
    private Mutex? _instanceMutex;
    private int _instanceIndex = 1;
    private string _instanceLabel = string.Empty;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if (TroubleshootingReset.TryRunWorker(e.Args))
        {
            Shutdown();
            return;
        }

        var startupOptions = ParseStartupOptions(e.Args);
        ReserveInstanceSlot();
        if (!startupOptions.IsSpawnedLaunch && startupOptions.RequestedInstances > 1)
        {
            LaunchAdditionalInstances(startupOptions.RequestedInstances - 1);
        }
        var dataDirectory = AppPaths.DataDirectory;
        DurableFile.ClearStaleTemps(dataDirectory);
        var configPath = _instanceIndex == 1
            ? Path.Combine(dataDirectory, "config.json")
            : Path.Combine(dataDirectory, $"config.instance{_instanceIndex}.json");

        _configService = new ConfigService(configPath);
        _config = _configService.Load();
        _config.PropertyChanged += Config_PropertyChanged;

        _mainWindow = new MainWindow(_configService, _config, _instanceLabel);
        _mainWindow.Show();
        SetupTrayIcon();

        if (startupOptions.OpenSettingsOnLaunch)
        {
            Dispatcher.BeginInvoke(new Action(() => _mainWindow?.OpenSettings()), System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    public void OpenExecutableFolder()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            exePath = Process.GetCurrentProcess().MainModule?.FileName;
        }

        var dir = string.IsNullOrWhiteSpace(exePath)
            ? AppContext.BaseDirectory
            : (Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory);

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = dir,
                UseShellExecute = true
            });
        }
        catch
        {
            // ignore
        }
    }

    public void BeginResetThisInstance()
    {
        if (!StartResetWorker("this", _instanceIndex.ToString(), Environment.ProcessId.ToString()))
        {
            return;
        }

        ExitApplication();
    }

    public void BeginResetAllInstances()
    {
        var pids = SnapshotInstancePids();
        if (!StartResetWorker("all", pids))
        {
            return;
        }

        StopOtherInstances(pids);
        ExitApplication();
    }

    public void BeginHardReset()
    {
        if (!ConfirmHardReset())
        {
            return;
        }

        var pids = SnapshotInstancePids();
        if (!StartResetWorker("hard", pids))
        {
            return;
        }

        StopOtherInstances(pids);
        ExitApplication();
    }

    private bool ConfirmHardReset()
    {
        var language = _config?.Language ?? AppLanguage.English;
        var result = MessageBox.Show(
            LocalizationService.Get(language, "TroubleshootingHardResetConfirmMessage"),
            LocalizationService.Get(language, "TroubleshootingHardResetConfirmTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        return result == MessageBoxResult.Yes;
    }

    private static bool StartResetWorker(string mode, params string[] values)
    {
        var exePath = ExecutablePath();
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return false;
        }

        try
        {
            var arguments = TroubleshootingReset.WorkerArgument + " " + mode;
            if (values.Length > 0)
            {
                arguments += " " + string.Join(' ', values);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? "",
                UseShellExecute = false
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string[] SnapshotInstancePids()
    {
        var exePath = ExecutablePath();
        var pids = new List<int> { Environment.ProcessId };
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return [Environment.ProcessId.ToString()];
        }

        var processName = Path.GetFileNameWithoutExtension(exePath);
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (string.Equals(path, exePath, StringComparison.OrdinalIgnoreCase) && !pids.Contains(process.Id))
                {
                    pids.Add(process.Id);
                }
            }
            catch
            {
                // Another user's process, or one that has already exited.
            }
            finally
            {
                process.Dispose();
            }
        }

        return pids.Select(pid => pid.ToString()).ToArray();
    }

    private static void StopOtherInstances(IEnumerable<string> pids)
    {
        foreach (var value in pids)
        {
            if (!int.TryParse(value, out var pid) || pid == Environment.ProcessId)
            {
                continue;
            }

            try
            {
                using var process = Process.GetProcessById(pid);
                process.Kill();
            }
            catch
            {
                // Already closed, or this process cannot stop it.
            }
        }
    }

    private static string? ExecutablePath()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            exePath = Process.GetCurrentProcess().MainModule?.FileName;
        }

        return exePath;
    }

    public void RestartApplication(bool openSettingsAfterRestart, string? extraArgs = null)
    {
        _mainWindow?.SaveNow();

        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            exePath = Process.GetCurrentProcess().MainModule?.FileName;
        }

        if (!string.IsNullOrWhiteSpace(exePath))
        {
            try
            {
                var parts = new List<string>();
                if (openSettingsAfterRestart)
                {
                    parts.Add("--open-settings");
                }

                if (!string.IsNullOrWhiteSpace(extraArgs))
                {
                    parts.Add(extraArgs.Trim());
                }

                var args = string.Join(" ", parts);
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = args,
                    UseShellExecute = true
                });
            }
            catch
            {
                // ignore; user can restart manually
            }
        }

        ExitApplication();
    }

    private void SetupTrayIcon()
    {
        var tip = string.IsNullOrWhiteSpace(_instanceLabel) ? "ToolDock" : $"ToolDock {_instanceLabel}";
        _trayIcon = new NativeTrayIcon(tip);
        _trayIcon.LeftClick += OnTrayLeftClick;
        _trayIcon.RightClick += OnTrayRightClick;

        _trayMenu = new ContextMenu();
        _settingsItem = new MenuItem();
        _settingsItem.Click += (_, _) => _mainWindow?.OpenSettings();
        _trayMenu.Items.Add(_settingsItem);

        _showDockItem = new MenuItem();
        _showDockItem.Click += (_, _) => ShowDockAndFocus();
        _trayMenu.Items.Add(_showDockItem);

        _disableDockItem = new MenuItem { IsCheckable = true };
        _disableDockItem.Click += (_, _) => ToggleDockDisabledFromTray();
        _trayMenu.Items.Add(_disableDockItem);

        _reloadDockItem = new MenuItem();
        _reloadDockItem.Click += (_, _) => _mainWindow?.ReloadDock();
        _trayMenu.Items.Add(_reloadDockItem);

        _reloadAppItem = new MenuItem();
        _reloadAppItem.Click += (_, _) => RestartApplication(openSettingsAfterRestart: true);
        _trayMenu.Items.Add(_reloadAppItem);

        _trayMenu.Items.Add(new Separator());

        _troubleshootingRootItem = new MenuItem();
        _troubleshootingOpenFolderItem = new MenuItem();
        _troubleshootingOpenFolderItem.Click += (_, _) => OpenExecutableFolder();
        _troubleshootingRootItem.Items.Add(_troubleshootingOpenFolderItem);

        _troubleshootingResetThisInstanceItem = new MenuItem();
        _troubleshootingResetThisInstanceItem.Click += (_, _) => BeginResetThisInstance();
        _troubleshootingRootItem.Items.Add(_troubleshootingResetThisInstanceItem);

        _troubleshootingResetAllInstancesItem = new MenuItem();
        _troubleshootingResetAllInstancesItem.Click += (_, _) => BeginResetAllInstances();
        _troubleshootingRootItem.Items.Add(_troubleshootingResetAllInstancesItem);

        _troubleshootingHardResetItem = new MenuItem();
        _troubleshootingHardResetItem.Click += (_, _) => BeginHardReset();
        _troubleshootingRootItem.Items.Add(_troubleshootingHardResetItem);

        _trayMenu.Items.Add(_troubleshootingRootItem);

        _exitItem = new MenuItem();
        _exitItem.Click += (_, _) => ExitApplication();
        _trayMenu.Items.Add(_exitItem);

        RefreshTrayTexts();
        ApplyTrayTheme();
    }

    private void DisposeTrayIcon()
    {
        if (_trayIcon is not null)
        {
            _trayIcon.LeftClick -= OnTrayLeftClick;
            _trayIcon.RightClick -= OnTrayRightClick;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _trayMenu = null;
    }

    private void ShowDockAndFocus() => _mainWindow?.ShowDock();

    private void ToggleDockDisabledFromTray()
    {
        if (_config is null || _disableDockItem is null)
        {
            return;
        }

        _config.DockDisabled = _disableDockItem.IsChecked == true;
    }

    private void RefreshTrayDisableDockState()
    {
        var disabled = _config?.DockDisabled == true;
        if (_disableDockItem is not null)
        {
            _disableDockItem.IsChecked = disabled;
        }

        if (_showDockItem is not null)
        {
            _showDockItem.IsEnabled = !disabled;
        }
    }

    private void ExitApplication()
    {
        _mainWindow?.SaveNow();
        DisposeTrayIcon();
        _instanceMutex?.Dispose();
        _instanceMutex = null;

        if (_mainWindow is not null)
        {
            _mainWindow.Close();
        }

        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mainWindow?.SaveNow();
        if (_config is not null)
        {
            _config.PropertyChanged -= Config_PropertyChanged;
        }
        DisposeTrayIcon();
        _instanceMutex?.Dispose();
        _instanceMutex = null;
        base.OnExit(e);
    }

    private void ReserveInstanceSlot()
    {
        var workspaceSignature = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(AppPaths.DataDirectory))).Substring(0, 12);
        const int maxSlots = 32;
        for (var slot = 1; slot <= maxSlots; slot++)
        {
            var mutexName = $@"Local\ToolDock_{workspaceSignature}_Instance_{slot}";
            var mutex = new Mutex(initiallyOwned: true, name: mutexName, out var createdNew);
            if (!createdNew)
            {
                mutex.Dispose();
                continue;
            }

            _instanceMutex = mutex;
            _instanceIndex = slot;
            _instanceLabel = slot == 1 ? string.Empty : $"({slot})";
            return;
        }

        _instanceIndex = Environment.ProcessId;
        _instanceLabel = $"({_instanceIndex})";
    }

    private static StartupOptions ParseStartupOptions(string[] args)
    {
        var requestedInstances = 1;
        var isSpawnedLaunch = false;
        var openSettingsOnLaunch = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, "--spawned", StringComparison.OrdinalIgnoreCase))
            {
                isSpawnedLaunch = true;
                continue;
            }

            if (string.Equals(arg, "--open-settings", StringComparison.OrdinalIgnoreCase))
            {
                openSettingsOnLaunch = true;
                continue;
            }

            if (arg.StartsWith("--instances=", StringComparison.OrdinalIgnoreCase))
            {
                var valueText = arg["--instances=".Length..];
                if (int.TryParse(valueText, out var parsedInline))
                {
                    requestedInstances = Math.Clamp(parsedInline, 1, MaxSupportedInstances);
                }

                continue;
            }

            if (string.Equals(arg, "--instances", StringComparison.OrdinalIgnoreCase) &&
                i + 1 < args.Length &&
                int.TryParse(args[i + 1], out var parsed))
            {
                requestedInstances = Math.Clamp(parsed, 1, MaxSupportedInstances);
                i++;
            }
        }

        return new StartupOptions(
            requestedInstances,
            isSpawnedLaunch,
            openSettingsOnLaunch);
    }

    private static void LaunchAdditionalInstances(int additionalCount)
    {
        if (additionalCount <= 0)
        {
            return;
        }

        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            exePath = Process.GetCurrentProcess().MainModule?.FileName;
        }

        if (string.IsNullOrWhiteSpace(exePath))
        {
            return;
        }

        var spawnCount = Math.Clamp(additionalCount, 0, MaxSupportedInstances - 1);
        for (var i = 0; i < spawnCount; i++)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = "--instances 1 --spawned",
                    UseShellExecute = true
                });
            }
            catch
            {
                // Ignore individual spawn failures to keep primary instance usable.
            }
        }
    }

    private readonly record struct StartupOptions(
        int RequestedInstances,
        bool IsSpawnedLaunch,
        bool OpenSettingsOnLaunch);

    private void Config_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DockConfig.Language))
        {
            RefreshTrayTexts();
        }
        else if (e.PropertyName == nameof(DockConfig.Theme))
        {
            ApplyTrayTheme();
        }
        else if (e.PropertyName == nameof(DockConfig.DockDisabled))
        {
            RefreshTrayDisableDockState();
        }
    }

    private void RefreshTrayTexts()
    {
        if (_config is null)
        {
            return;
        }

        var language = _config.Language;
        if (_showDockItem is not null)
        {
            _showDockItem.Header = LocalizationService.Get(language, "TrayShowDock");
        }

        if (_disableDockItem is not null)
        {
            _disableDockItem.Header = LocalizationService.Get(language, "TrayDisableDock");
        }

        RefreshTrayDisableDockState();

        if (_settingsItem is not null)
        {
            _settingsItem.Header = LocalizationService.Get(language, "TraySettings");
        }

        if (_reloadDockItem is not null)
        {
            _reloadDockItem.Header = LocalizationService.Get(language, "TrayReloadDock");
        }

        if (_reloadAppItem is not null)
        {
            _reloadAppItem.Header = LocalizationService.Get(language, "TrayReloadApp");
        }

        if (_exitItem is not null)
        {
            _exitItem.Header = LocalizationService.Get(language, "TrayExit");
        }

        if (_troubleshootingRootItem is not null)
        {
            _troubleshootingRootItem.Header = LocalizationService.Get(language, "TrayTroubleshooting");
        }

        if (_troubleshootingOpenFolderItem is not null)
        {
            _troubleshootingOpenFolderItem.Header = LocalizationService.Get(language, "TroubleshootingOpenExeFolder");
        }

        if (_troubleshootingResetThisInstanceItem is not null)
        {
            _troubleshootingResetThisInstanceItem.Header = LocalizationService.Get(language, "TroubleshootingResetThisInstance");
        }

        if (_troubleshootingResetAllInstancesItem is not null)
        {
            _troubleshootingResetAllInstancesItem.Header = LocalizationService.Get(language, "TroubleshootingResetAllInstances");
        }

        if (_troubleshootingHardResetItem is not null)
        {
            _troubleshootingHardResetItem.Header = LocalizationService.Get(language, "TroubleshootingHardReset");
        }
    }

    private void OnTrayLeftClick() => _mainWindow?.OpenSettings(SettingsCategory.QuickAccess);

    private void OnTrayRightClick()
    {
        if (_trayMenu is null || _trayIcon is null || _config is null)
        {
            return;
        }

        ApplyTrayTheme();
        RefreshTrayTexts();
        _trayIcon.ShowContextMenu(_trayMenu);
    }

    private void ApplyTrayTheme()
    {
        if (_trayMenu is null || _config is null)
        {
            return;
        }

        DockContextMenuTheme.Apply(_trayMenu, _config.Theme);
    }
}
