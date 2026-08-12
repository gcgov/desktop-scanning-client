# Third-party notices

ScanBridge itself is MIT-licensed. It ships with the following third-party components,
which remain under their own licenses.

## NAPS2.Sdk (and companion packages)

- Packages: `NAPS2.Sdk`, `NAPS2.Images.Gdi`, `NAPS2.Sdk.Worker.Win32` (and their
  dependencies, including `NAPS2.NTwain`, `NAPS2.Wia`, `NAPS2.Escl`, `NAPS2.PdfSharp`)
- Copyright © NAPS2 contributors
- License: **GNU Lesser General Public License v2.1** (LGPL-2.1)
- Source: <https://github.com/cyanfish/naps2>
- License text: <https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html>

ScanBridge uses these libraries **unmodified**, as separate assemblies in the
application folder. As required by LGPL §6, you may replace them with your own
(possibly modified) builds of the libraries: build NAPS2.Sdk from the source link above
and drop the resulting DLLs (and `NAPS2.Worker.exe`) over the ones in the ScanBridge
installation folder. ScanBridge release builds are never trimmed, merged, or statically
linked against these assemblies.

## Serilog

- Packages: `Serilog`, `Serilog.Extensions.Logging`, `Serilog.Sinks.File`
- License: Apache-2.0
- Source: <https://github.com/serilog/serilog>

## ASP.NET Core / .NET runtime

- The self-contained publish includes the .NET and ASP.NET Core runtimes
- License: MIT
- Source: <https://github.com/dotnet/aspnetcore>
