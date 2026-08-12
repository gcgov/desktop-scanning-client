# ScanBridge HTTP API

Base URL: `http://127.0.0.1:<port>` — default port **7226**. The listener binds to
loopback only. All request and response bodies are JSON (camelCase) unless noted.

Browsers must be subject to the CORS allowlist: the calling page's origin has to be
listed under *Allowed website origins* in ScanBridge settings. Same-machine tools
(curl, desktop apps) are not subject to CORS.

Requests from browsers should pass `targetAddressSpace: 'loopback'` in the `fetch`
options to satisfy Chrome's Local Network Access rules (Chrome 142+).

## Error shape

Non-2xx responses (except 404s from unknown URLs) use:

```json
{ "error": { "code": "scannerBusy", "message": "A scan is already in progress." } }
```

| Code | Meaning |
|---|---|
| `scannerBusy` | Another scan job is running; scanners are exclusive. |
| `noScannerConfigured` | No scanner selected in settings and no `scannerId` given. |
| `deviceOffline` | The scanner is unreachable (off, unplugged). |
| `noPages` | The scan produced zero pages (empty feeder). |
| `canceled` | The job was canceled. |
| `scanFailed` | Any other scan failure; `message` has details. |
| `notReady` | Document requested before the job completed. |
| `notFound` | Unknown job id. |

## Endpoints

### `GET /api/v1/status`

Handshake / discovery. Use this to detect whether ScanBridge is installed and running.

```json
{ "app": "ScanBridge", "version": "1.0.0", "apiVersion": 1,
  "scannerConfigured": true, "scannerName": "Canon imageFORMULA R40" }
```

### `GET /api/v1/scanners?driver=wia&refresh=1`

Lists available scanners. `driver` is `wia` (default), `twain` or `escl`; omitting it
uses the configured driver. Results are cached per driver; `refresh=1` re-enumerates.
ESCL enumeration searches the local network and can take a few seconds.

```json
[ { "id": "{6BDD1FC6-...}", "name": "Canon imageFORMULA R40", "driver": "wia" } ]
```

### `POST /api/v1/scans`

Starts a scan job. Body is optional; every field falls back to the configured default:

```json
{
  "scannerId": null,
  "driver": null,
  "paperSource": "feeder",      // "flatbed" | "feeder" | "duplex"
  "duplex": false,               // shorthand for paperSource: "duplex"
  "dpi": 300,
  "colorMode": "color",         // "color" | "grayscale" | "blackAndWhite"
  "pageSize": "letter",         // "letter" | "legal" | "a4"
  "excludeBlankPages": false,
  "autoDeskew": false
}
```

Responses: `202 Accepted` with `{ "jobId": "…" }` (Location header points at the job),
`409` `scannerBusy`, `422` `noScannerConfigured`.

### `GET /api/v1/scans/{jobId}`

Polls a job.

```json
{ "jobId": "…", "status": "scanning", "pagesScanned": 2, "error": null }
```

`status` is `pending | scanning | processing | completed | failed | canceled`. When
`failed`/`canceled`, `error` carries `{code, message}`.

### `GET /api/v1/scans/{jobId}/document`

Returns the finished scan as `application/pdf`. `409 notReady` while the job is still
running; `410` with the failure code if the job ended without a document; `404` for
unknown ids.

### `DELETE /api/v1/scans/{jobId}`

Cancels a running job, or discards a finished one (freeing its PDF from memory).
Always `204`. Finished jobs are also discarded automatically after 10 minutes.

## Typical client flow

1. `GET /status` — if unreachable, tell the user to install/start ScanBridge.
2. `POST /scans` with `{}`.
3. Poll `GET /scans/{jobId}` every second; show `pagesScanned`.
4. On `completed`, `GET /scans/{jobId}/document` → Blob.
5. `DELETE /scans/{jobId}` when done (optional but polite).
