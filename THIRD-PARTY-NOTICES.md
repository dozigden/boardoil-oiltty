# Third-party notices

OilTTY is built with .NET and uses the development and test dependencies listed
below. This file is informational; the referenced upstream license files are
authoritative.

## Adapted material

OilTTY's terminal mosaic and the repository images that display it adapt the
[BoardOil](https://github.com/dozigden/boardoil) logo. BoardOil and its branding
are available under the [MIT License](https://github.com/dozigden/boardoil/blob/main/LICENSE),
copyright (c) 2026 Luke Easter. The separately installed BoardOil service is not
distributed with OilTTY.

## Components distributed with OilTTY

The OilTTY application has no direct third-party NuGet package dependencies.
This repository distributes source code, not compiled .NET binaries.

A framework-dependent build requires a separately installed .NET 10 runtime,
but may include a native .NET apphost. A self-contained build includes the
apphost and runtime for its target platform. Anyone redistributing generated
binaries should include the applicable .NET license and third-party notices.
OilTTY's self-contained publish configuration copies those files from the
selected runtime pack next to the application as `DOTNET-LICENSE.txt` and
`DOTNET-THIRD-PARTY-NOTICES.txt`.

- [.NET runtime](https://github.com/dotnet/runtime) — the license is
  platform-dependent; the files copied into each self-contained publish are
  authoritative.

## Development and test dependencies

These packages are restored for `OilTTY.Tests` and are not included in OilTTY's
published application output.

| Project | Packages | Version | License |
| --- | --- | --- | --- |
| [Application Insights for .NET](https://github.com/microsoft/ApplicationInsights-dotnet) | `Microsoft.ApplicationInsights` | 2.23.0 | [MIT](https://github.com/microsoft/ApplicationInsights-dotnet/blob/2faa7e8b157a431daa2e71785d68abd5fa817b53/LICENSE) |
| [.NET runtime](https://github.com/dotnet/runtime) | `Microsoft.Bcl.AsyncInterfaces`; `Microsoft.Win32.Registry` | 6.0.0; 5.0.0 | [MIT](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) |
| [Microsoft Testing Platform](https://github.com/microsoft/testfx) | `Microsoft.Testing.Extensions.Telemetry`; `Microsoft.Testing.Extensions.TrxReport.Abstractions`; `Microsoft.Testing.Platform`; `Microsoft.Testing.Platform.MSBuild` | 1.9.1 | [MIT](https://github.com/microsoft/testfx/blob/cb5afc3bb9bb01ebd75b57f89e8358b914ee2a49/LICENSE) |
| [VSTest](https://github.com/microsoft/vstest) | `Microsoft.CodeCoverage`; `Microsoft.NET.Test.Sdk`; `Microsoft.TestPlatform.ObjectModel`; `Microsoft.TestPlatform.TestHost` | 18.8.1 | [MIT](https://github.com/microsoft/vstest/blob/190d2811e952d2143288aa136b6f6fe31d93a437/LICENSE) |
| [xUnit.net](https://github.com/xunit/xunit) | `xunit.v3`; `xunit.v3.assert`; `xunit.v3.common`; `xunit.v3.core.mtp-v1`; `xunit.v3.extensibility.core`; `xunit.v3.mtp-v1`; `xunit.v3.runner.common`; `xunit.v3.runner.inproc.console` | 3.2.2 | [Apache-2.0](https://github.com/xunit/xunit/blob/728c1dce012cd82193035dddfeaba184baaa88c6/LICENSE) |
| [xUnit.net Analyzers](https://github.com/xunit/xunit.analyzers) | `xunit.analyzers` | 1.27.0 | [Apache-2.0](https://github.com/xunit/xunit.analyzers/blob/a2260df3e96395e6b513e5c7485bd6c53806871e/LICENSE) |
| [xUnit.net Visual Studio adapter](https://github.com/xunit/visualstudio.xunit) | `xunit.runner.visualstudio` | 3.1.5 | [Apache-2.0](https://github.com/xunit/visualstudio.xunit/blob/1b188a7b0a069d7fc94ae3c0b251f1302b602b63/License.txt) |

The package list includes direct and transitive dependencies from the resolved
`OilTTY.Tests` dependency graph.
