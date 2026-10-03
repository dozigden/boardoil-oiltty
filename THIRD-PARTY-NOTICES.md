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

OilTTY directly uses the following package. It is included in published
application output.

| Project | Package | Version | Package author | Declared license |
| --- | --- | --- | --- | --- |
| [StbImageSharp](https://github.com/StbSharp/StbImageSharp) | `StbImageSharp` | 2.30.16 | StbImageSharpTeam | `Unlicense OR MIT` |

OilTTY uses StbImageSharp under the MIT alternative. The license expression,
package author, repository URL, and repository commit below come from the
`StbImageSharp.nuspec` embedded in NuGet package version 2.30.16:

- License expression: `Unlicense OR MIT`
- Package author: `StbImageSharpTeam`
- Repository: <https://github.com/StbSharp/StbImageSharp>
- Repository commit: `125af70cb557033f2c46aec8e82eaaf72ac49817`

That package does not contain a standalone license file. Its repository README
describes the project license as "Public Domain or MIT". `STB-LICENSE.txt`,
distributed alongside OilTTY, preserves the dual-license notice from the
underlying stb software and its copyright attribution to Sean Barrett. It is
not an attribution of the StbImageSharp C# wrapper to Sean Barrett.

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
| [.NET runtime](https://github.com/dotnet/runtime) | `Microsoft.Bcl.AsyncInterfaces`; `Microsoft.Win32.Registry`; `System.Security.AccessControl` | 6.0.0; 5.0.0; 6.0.1 | [MIT](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) |
| [Microsoft Testing Platform](https://github.com/microsoft/testfx) | `Microsoft.Testing.Extensions.Telemetry`; `Microsoft.Testing.Extensions.TrxReport.Abstractions`; `Microsoft.Testing.Platform`; `Microsoft.Testing.Platform.MSBuild` | 2.4.0 | [MIT](https://github.com/microsoft/testfx/blob/a2a92fdb11ad38cd55b31223c4cfbb070fa01c05/LICENSE) |
| [VSTest](https://github.com/microsoft/vstest) | `Microsoft.CodeCoverage`; `Microsoft.NET.Test.Sdk`; `Microsoft.TestPlatform.ObjectModel`; `Microsoft.TestPlatform.TestHost` | 18.10.1 | [MIT](https://github.com/microsoft/vstest/blob/87dfd4b2d2bacd91ad69e009ade6f4715b3b46ec/LICENSE) |
| [xUnit.net](https://github.com/xunit/xunit) | `xunit.v3`; `xunit.v3.assert`; `xunit.v3.common`; `xunit.v3.core.mtp-v2`; `xunit.v3.extensibility.core`; `xunit.v3.mtp-v2`; `xunit.v3.runner.common`; `xunit.v3.runner.inproc.console` | 4.0.1 | [Apache-2.0](https://github.com/xunit/xunit/blob/8ed8aa354c7298e157a0fc2dcd61b95df345256a/LICENSE) |
| [xUnit.net Analyzers](https://github.com/xunit/xunit.analyzers) | `xunit.analyzers` | 2.1.0 | [Apache-2.0](https://github.com/xunit/xunit.analyzers/blob/cf90c99d73c3df1c53d54b60cd19130a06287382/LICENSE) |
| [xUnit.net Visual Studio adapter](https://github.com/xunit/visualstudio.xunit) | `xunit.runner.visualstudio` | 4.0.0 | [Apache-2.0](https://github.com/xunit/visualstudio.xunit/blob/05679a7ab5ca2461d06880faaefe26770e0fdf77/License.txt) |

The package list includes direct and transitive dependencies from the resolved
`OilTTY.Tests` dependency graph.
