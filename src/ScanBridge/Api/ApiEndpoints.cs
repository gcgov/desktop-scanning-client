using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using ScanBridge.Scanning;
using ScanBridge.Settings;

namespace ScanBridge.Api;

/// <summary>
/// Maps the localhost HTTP surface. All responses are camelCase JSON except the
/// finished document, which is served as application/pdf.
/// </summary>
public static class ApiEndpoints
{
    public static void Map(WebApplication app, SettingsStore settingsStore, ScannerService scannerService, ScanJobManager jobManager)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        var api = app.MapGroup("/api/v1");

        api.MapGet("/status", () =>
        {
            var settings = settingsStore.Current;
            return Results.Ok(new StatusResponse(
                App: "ScanBridge",
                Version: version,
                ApiVersion: 1,
                ScannerConfigured: !string.IsNullOrEmpty(settings.ScannerDeviceId),
                ScannerName: settings.ScannerDeviceName));
        });

        api.MapGet("/scanners", async (CancellationToken cancellationToken, string? driver = null, bool refresh = false) =>
        {
            var settings = settingsStore.Current;
            var scanners = await scannerService.ListScannersAsync(
                driver ?? settings.ScannerDriver, refresh, cancellationToken);
            return Results.Ok(scanners.Select(s => new ScannerDto(s.Id, s.Name, s.Driver)));
        });

        api.MapPost("/scans", (StartScanRequest? body) =>
        {
            body ??= new StartScanRequest();
            var settings = settingsStore.Current;

            var deviceId = body.ScannerId ?? settings.ScannerDeviceId;
            if (string.IsNullOrEmpty(deviceId))
            {
                return Error(StatusCodes.Status422UnprocessableEntity, ScanErrorCodes.NoScannerConfigured,
                    "No scanner is configured. Open ScanBridge settings and select a scanner.");
            }

            // A device id passed by the caller may not match the configured device; only
            // reuse the configured name/driver when the id is the configured one.
            var usingConfiguredDevice = body.ScannerId is null || body.ScannerId == settings.ScannerDeviceId;
            var defaults = settings.Defaults;
            var paperSource = body.Duplex == true ? "duplex" : body.PaperSource ?? defaults.PaperSource;
            var request = new ScanRequest(
                DeviceId: deviceId,
                DeviceName: usingConfiguredDevice ? settings.ScannerDeviceName : null,
                Driver: (usingConfiguredDevice ? settings.ScannerDriver : body.Driver) ?? body.Driver ?? "wia",
                PaperSource: paperSource,
                Dpi: body.Dpi ?? defaults.Dpi,
                ColorMode: body.ColorMode ?? defaults.ColorMode,
                PageSize: body.PageSize ?? defaults.PageSize,
                ExcludeBlankPages: body.ExcludeBlankPages ?? defaults.ExcludeBlankPages,
                AutoDeskew: body.AutoDeskew ?? defaults.AutoDeskew);

            var job = jobManager.TryStart(request, out var errorCode);
            if (job is null)
            {
                return Error(StatusCodes.Status409Conflict, errorCode ?? ScanErrorCodes.ScannerBusy,
                    "A scan is already in progress.");
            }

            return Results.Accepted($"/api/v1/scans/{job.Id}", new StartScanResponse(job.Id));
        });

        api.MapGet("/scans/{jobId}", (string jobId) =>
        {
            var job = jobManager.Get(jobId);
            if (job is null)
            {
                return Error(StatusCodes.Status404NotFound, "notFound", "Unknown scan job.");
            }

            return Results.Ok(ToResponse(job));
        });

        api.MapGet("/scans/{jobId}/document", (string jobId) =>
        {
            var job = jobManager.Get(jobId);
            if (job is null)
            {
                return Error(StatusCodes.Status404NotFound, "notFound", "Unknown scan job.");
            }

            if (job.Status != ScanJobStatus.Completed)
            {
                if (job.IsTerminal)
                {
                    return Error(StatusCodes.Status410Gone, job.ErrorCode ?? ScanErrorCodes.ScanFailed,
                        job.ErrorMessage ?? "The scan did not complete.");
                }

                return Error(StatusCodes.Status409Conflict, "notReady", "The scan has not finished yet.");
            }

            return Results.File(job.PdfBytes!, contentType: "application/pdf", fileDownloadName: "scan.pdf");
        });

        api.MapDelete("/scans/{jobId}", (string jobId) =>
        {
            jobManager.CancelOrDiscard(jobId);
            return Results.NoContent();
        });
    }

    private static ScanJobResponse ToResponse(ScanJob job) => new(
        JobId: job.Id,
        Status: job.Status switch
        {
            ScanJobStatus.Pending => "pending",
            ScanJobStatus.Scanning => "scanning",
            ScanJobStatus.Processing => "processing",
            ScanJobStatus.Completed => "completed",
            ScanJobStatus.Failed => "failed",
            ScanJobStatus.Canceled => "canceled",
            _ => "unknown",
        },
        PagesScanned: job.PagesScanned,
        Error: job.ErrorCode is null ? null : new ApiErrorBody(job.ErrorCode, job.ErrorMessage ?? string.Empty));

    private static IResult Error(int statusCode, string code, string message) =>
        Results.Json(new ApiErrorResponse(new ApiErrorBody(code, message)), statusCode: statusCode);
}
