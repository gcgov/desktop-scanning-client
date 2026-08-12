namespace ScanBridge.Scanning;

public enum ScanJobStatus
{
    Pending,
    Scanning,
    Processing,
    Completed,
    Failed,
    Canceled,
}

/// <summary>Machine-readable error codes surfaced to API clients.</summary>
public static class ScanErrorCodes
{
    public const string ScannerBusy = "scannerBusy";
    public const string NoScannerConfigured = "noScannerConfigured";
    public const string DeviceOffline = "deviceOffline";
    public const string NoPages = "noPages";
    public const string Canceled = "canceled";
    public const string ScanFailed = "scanFailed";
}

/// <summary>Thrown by the scan pipeline with a machine-readable code from <see cref="ScanErrorCodes"/>.</summary>
public sealed class ScanBridgeException : Exception
{
    public ScanBridgeException(string code, string message, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
    }

    public string Code { get; }
}

/// <summary>
/// Resolved parameters for one scan: request overrides already merged over configured defaults.
/// All values are plain strings/primitives so the job layer stays free of NAPS2 types.
/// </summary>
public sealed record ScanRequest(
    string? DeviceId,
    string? DeviceName,
    string Driver,
    string PaperSource,
    int Dpi,
    string ColorMode,
    string PageSize,
    bool ExcludeBlankPages,
    bool AutoDeskew);

public sealed class ScanJob
{
    public string Id { get; } = Guid.NewGuid().ToString("N");

    public ScanJobStatus Status { get; internal set; } = ScanJobStatus.Pending;

    public int PagesScanned { get; internal set; }

    public string? ErrorCode { get; internal set; }

    public string? ErrorMessage { get; internal set; }

    public byte[]? PdfBytes { get; internal set; }

    public DateTimeOffset CreatedUtc { get; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? FinishedUtc { get; internal set; }

    internal CancellationTokenSource Cts { get; } = new();

    public bool IsTerminal => Status is ScanJobStatus.Completed or ScanJobStatus.Failed or ScanJobStatus.Canceled;
}
