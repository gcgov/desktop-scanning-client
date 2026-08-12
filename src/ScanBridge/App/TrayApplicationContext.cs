using System.Diagnostics;
using ScanBridge.Hosting;
using ScanBridge.Scanning;
using ScanBridge.Settings;
using Serilog;

namespace ScanBridge.App;

/// <summary>
/// The tray icon and its menu. ScanBridge has no main window — the tray icon is the app.
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly SettingsStore _settingsStore;
    private readonly ScannerService _scannerService;
    private readonly ScanJobManager _jobManager;
    private readonly WebHostRunner _webHostRunner;
    private readonly NotifyIcon _notifyIcon;
    private readonly SynchronizationContext _syncContext;
    private SettingsForm? _settingsForm;
    private bool _testScanRunning;

    public TrayApplicationContext(
        SettingsStore settingsStore,
        ScannerService scannerService,
        ScanJobManager jobManager,
        WebHostRunner webHostRunner,
        bool openSettingsOnStartup)
    {
        _settingsStore = settingsStore;
        _scannerService = scannerService;
        _jobManager = jobManager;
        _webHostRunner = webHostRunner;
        _syncContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        menu.Items.Add("Scan test page", null, (_, _) => RunTestScan());
        menu.Items.Add("Open log folder", null, (_, _) => OpenLogFolder());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "ScanBridge",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => ShowSettings();

        _jobManager.JobFinished += OnJobFinished;
        _webHostRunner.StatusChanged += OnListenerStatusChanged;

        if (_webHostRunner.LastError is { } startupError)
        {
            ShowBalloon("ScanBridge", startupError, ToolTipIcon.Warning);
        }

        if (openSettingsOnStartup)
        {
            // Let the message loop start before opening the window.
            _syncContext.Post(_ => ShowSettings(), null);
        }
    }

    private static Icon LoadIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "scanbridge.ico");
            if (File.Exists(iconPath))
            {
                return new Icon(iconPath);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not load the tray icon; falling back to the stock icon");
        }

        return SystemIcons.Application;
    }

    private void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Activate();
            return;
        }

        _settingsForm = new SettingsForm(_settingsStore, _scannerService, _webHostRunner);
        _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm.Show();
    }

    private async void RunTestScan()
    {
        if (_testScanRunning)
        {
            return;
        }

        var settings = _settingsStore.Current;
        if (string.IsNullOrEmpty(settings.ScannerDeviceId))
        {
            ShowBalloon("ScanBridge", "No scanner is configured yet — open Settings first.", ToolTipIcon.Warning);
            ShowSettings();
            return;
        }

        var defaults = settings.Defaults;
        var request = new ScanRequest(
            DeviceId: settings.ScannerDeviceId,
            DeviceName: settings.ScannerDeviceName,
            Driver: settings.ScannerDriver ?? "wia",
            PaperSource: defaults.PaperSource,
            Dpi: defaults.Dpi,
            ColorMode: defaults.ColorMode,
            PageSize: defaults.PageSize,
            ExcludeBlankPages: defaults.ExcludeBlankPages,
            AutoDeskew: defaults.AutoDeskew);

        var job = _jobManager.TryStart(request, out var errorCode);
        if (job is null)
        {
            ShowBalloon("ScanBridge", "A scan is already in progress.", ToolTipIcon.Warning);
            return;
        }

        _testScanRunning = true;
        try
        {
            ShowBalloon("ScanBridge", "Scanning a test page…", ToolTipIcon.Info);
            while (!job.IsTerminal)
            {
                await Task.Delay(500);
            }

            if (job.Status == ScanJobStatus.Completed && job.PdfBytes is { } pdfBytes)
            {
                var pdfPath = Path.Combine(Path.GetTempPath(), $"scanbridge-test-{job.Id}.pdf");
                await File.WriteAllBytesAsync(pdfPath, pdfBytes);
                Process.Start(new ProcessStartInfo(pdfPath) { UseShellExecute = true });
            }

            // Failure balloons come from OnJobFinished, shared with browser-initiated jobs.
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Test scan failed");
            ShowBalloon("ScanBridge", $"Test scan failed: {ex.Message}", ToolTipIcon.Error);
        }
        finally
        {
            _testScanRunning = false;
            _jobManager.CancelOrDiscard(job.Id);
        }
    }

    private void OpenLogFolder()
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScanBridge", "logs");
        Directory.CreateDirectory(logDirectory);
        Process.Start(new ProcessStartInfo(logDirectory) { UseShellExecute = true });
    }

    private void OnJobFinished(ScanJob job)
    {
        if (job.Status == ScanJobStatus.Failed)
        {
            ShowBalloon("ScanBridge — scan failed", job.ErrorMessage ?? "The scan failed.", ToolTipIcon.Error);
        }
    }

    private void OnListenerStatusChanged()
    {
        if (_webHostRunner.LastError is { } error)
        {
            ShowBalloon("ScanBridge", error, ToolTipIcon.Warning);
        }
    }

    private void ShowBalloon(string title, string message, ToolTipIcon icon)
    {
        // Events can fire on worker threads; NotifyIcon belongs to the UI thread.
        _syncContext.Post(_ => _notifyIcon.ShowBalloonTip(5000, title, message, icon), null);
    }

    private void ExitApplication()
    {
        _notifyIcon.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _jobManager.JobFinished -= OnJobFinished;
            _webHostRunner.StatusChanged -= OnListenerStatusChanged;
            _notifyIcon.Dispose();
        }

        base.Dispose(disposing);
    }
}
