# Third-party notices

This file is not legal advice. It records what the licence metadata and licence files say, where they were found, and what could not be verified.

## This repository

- **Source code only.** The repository contains no compiled binaries (DLL, EXE, PDB, TLB), no NuGet packages or package caches, and no third-party source code.
- **Licence.** The code in this repository is © 2023–2026 Jan Kuželka and is licensed under the MIT License ([LICENSE](LICENSE)), with one exception:
  - `src/IfcComBridge/FodyWeavers.xsd` is a schema file that Fody generates from the configuration schema Costura.Fody ships. Its content comes from Fody (MIT License, Copyright (c) Simon Cropp) and Costura (MIT License, Copyright (c) 2012 Simon Cropp and contributors). The MIT License text is the one in [LICENSE](LICENSE), with those copyright lines.
- **Independence.** IfcComBridge is an independent project. It uses the xBIM Toolkit as a dependency but is not an official xBIM project and is not affiliated with or endorsed by the xBIM team or XBIM Ltd.

## Dependencies

NuGet downloads the dependencies from nuget.org when the solution is built. They are not part of this repository, keep their own licences, and are not covered by the MIT License of this repository.

The sections below list them. This matters to anyone who distributes a **build** of the library, because a build contains third-party code:
- `IfcComBridge.dll` embeds the managed dependencies of section 1;
- `Xbim.Geometry.Engine64.dll` is xBIM's native geometry engine (section 2).

### How this was collected

On 2026-09-25, from the packages restored for `IfcComBridge.sln` (93 packages):
- the `.nuspec` metadata of each package: licence expression or licence URL, copyright and authors;
- the licence and notice files included in the packages;
- the source repositories, for packages without a licence file. The xBIM packages point to the `master` branch of their repositories; the files were read there and at the xBIM tags noted below.

The list of embedded assemblies comes from the resources of a Release build of `IfcComBridge.dll`, and the native engine was inspected directly. "Declared" means the licence the package metadata states.

### 1. Embedded in a build of IfcComBridge.dll

Costura.Fody compresses these assemblies into `IfcComBridge.dll` as resources, for the xBIM assemblies, Esent.Interop and Costura together with their symbol files.

| Assemblies | Package (version) | Declared licence | Copyright notice | Licence text |
|---|---|---|---|---|
| Xbim.Common, Xbim.Ifc, Xbim.Ifc2x3, Xbim.Ifc4, Xbim.IO.MemoryModel, Xbim.IO.Esent, Xbim.Tessellator | Xbim.* 5.1.341, through Xbim.Essentials 5.1.341 | CDDL-1.0 | Copyright © XBIM Ltd | Not in the packages. `LICENCE.md` ("XBIM Licence", the CDDL) in [xBimTeam/XbimEssentials](https://github.com/xBimTeam/XbimEssentials), the URL Xbim.Essentials declares. |
| Xbim.ModelGeometry.Scene, Xbim.Geometry.Engine.Interop | 5.1.437, through Xbim.Geometry 5.1.437 | CDDL-1.0 | Copyright © XBIM Ltd | Not in the packages. `LICENCE.md` in [xBimTeam/XbimGeometry](https://github.com/xBimTeam/XbimGeometry). |
| Esent.Interop | ManagedEsent 1.9.4 | **Not verified** (see the open questions). The package declares only `http://managedesent.codeplex.com/license`, and CodePlex is shut down. | Copyright (c) Microsoft. All Rights Reserved. | – |
| Microsoft.Extensions.Configuration, .Configuration.Abstractions, .Configuration.Binder, .DependencyInjection, .Logging, .Logging.Abstractions | 3.1.3 each | Apache-2.0 | © Microsoft Corporation. All rights reserved. | Not in the packages. `LICENSE.txt` (Apache-2.0) of [dotnet/extensions](https://github.com/dotnet/extensions) at tag `v3.1.3`, which has no NOTICE file. |
| Microsoft.Extensions.Options | 7.0.1 | MIT | © Microsoft Corporation | `LICENSE.TXT` (Copyright (c) .NET Foundation and Contributors) and `THIRD-PARTY-NOTICES.TXT` in the package |
| Microsoft.Extensions.Primitives, Microsoft.Extensions.DependencyInjection.Abstractions, Microsoft.Bcl.AsyncInterfaces | 7.0.0 each | MIT | © Microsoft Corporation | `LICENSE.TXT` and `THIRD-PARTY-NOTICES.TXT` in each package |
| System.Buffers, System.Memory, System.Numerics.Vectors, System.Threading.Tasks.Extensions, System.ValueTuple | 4.5.1, 4.5.5, 4.5.0, 4.5.4, 4.5.0 | MIT (URL of the dotnet/corefx `LICENSE.TXT`) | © Microsoft Corporation | `LICENSE.TXT` (Copyright (c) .NET Foundation and Contributors) and `THIRD-PARTY-NOTICES.TXT` in each package |
| System.Runtime.CompilerServices.Unsafe | 6.0.0 | MIT | © Microsoft Corporation | `LICENSE.TXT` and `THIRD-PARTY-NOTICES.TXT` in the package |
| System.Diagnostics.DiagnosticSource | 4.3.0 | **MICROSOFT .NET LIBRARY license**, not an open-source licence (`http://go.microsoft.com/fwlink/?LinkId=329770`) | © Microsoft Corporation | `dotnet_library_license.txt` and `ThirdPartyNotices.txt` in the package |
| Newtonsoft.Json | 13.0.3 | MIT | Copyright (c) 2007 James Newton-King | `LICENSE.md` in the package |
| Serilog, Serilog.Extensions.Logging, Serilog.Sinks.Console | 3.0.1, 3.1.0, 4.1.0 | Apache-2.0 | none in the packages; authors "Serilog Contributors" (Serilog.Extensions.Logging: "Microsoft, Serilog Contributors") | Not in the packages. `LICENSE` (Apache-2.0) in the [serilog](https://github.com/serilog) repositories, which have no NOTICE file (checked on their current default branches). |
| Costura (runtime helper) | Costura.Fody 5.7.0 | MIT | Copyright (c) 2012 Simon Cropp and contributors | Not in the package. `LICENSE` of [Fody/Costura](https://github.com/Fody/Costura) at tag `5.7.0`. |

The library opens every model in memory and does not use Esent, but `Xbim.IO.Esent` references Esent.Interop, so it is embedded. System.Diagnostics.DiagnosticSource comes in transitively and is embedded, but no COM call loads it.

### 2. The native geometry engine

`Xbim.Geometry.Engine64.dll` is deployed next to `IfcComBridge.dll`, because it cannot be embedded. It comes from Xbim.Geometry.Engine.Interop 5.1.437.

- **xBIM.** The package declares CDDL-1.0, and the DLL's version resource says "Copyright © XBIM Ltd". The xBIM licence file adds: "For the Geometry Engine library please also consult the documents in the Xbim.Geometry.Engine\OCC folder."
- **Open CASCADE Technology (OCCT).** The DLL contains OCCT code, linked statically: it holds OCCT classes and imports no OCCT DLL. The `Xbim.Geometry.Engine/OCC` folder of XbimGeometry contains `LICENSE_LGPL_21.txt` and `OCCT_LGPL_EXCEPTION.txt`. They are identical to the files in [Open-Cascade-SAS/OCCT](https://github.com/Open-Cascade-SAS/OCCT): the **GNU LGPL 2.1 with the Open CASCADE Exception 1.0**. The package itself contains no licence files, neither xBIM's nor OCCT's.
- **Visual C++ runtime.** The DLL imports `MSVCP140.dll`, `VCRUNTIME140.dll` and `VCRUNTIME140_1.dll`, so the target machine needs the Microsoft Visual C++ 2015–2022 Redistributable (x64). The redistributable is not part of this project, and it comes with Microsoft's own licence terms.

### 3. Build tools

Used only while building; nothing of them is distributed, except Costura's runtime helper (section 1).

| Package | Declared licence | Notice |
|---|---|---|
| Fody 6.8.0 | MIT (`License.txt` in the package) | Copyright (c) Simon Cropp |
| Costura.Fody 5.7.0 | MIT | Copyright (c) 2012 Simon Cropp and contributors |

Building also uses the .NET SDK, MSBuild and the .NET Framework 4.7.2 reference assemblies of the local Visual Studio or Build Tools installation.

### 4. Restore-only packages

These appear in the library's restore graph, transitively through Costura.Fody and the geometry interop, but nothing from them reaches the build output:

- **MICROSOFT .NET LIBRARY license:** NETStandard.Library 1.6.1, Microsoft.NETCore.Platforms 1.1.0, Microsoft.Win32.Primitives 4.3.0, and 42 `System.*` 4.3.0 packages. The one package of this family that is embedded, System.Diagnostics.DiagnosticSource 4.3.0, is listed in section 1.
- **Metapackages without code:** Xbim.Essentials 5.1.341 and Xbim.Geometry 5.1.437, with the xBIM licence URLs above.

### 5. Test-only packages

Used by the test projects in `tests/`; never part of the library.

| Packages | Declared licence |
|---|---|
| xunit 2.9.3, xunit.core, xunit.assert, xunit.extensibility.core, xunit.extensibility.execution 2.9.3, xunit.analyzers 1.18.0, xunit.runner.visualstudio 2.8.2 | Apache-2.0 |
| xunit.abstractions 2.0.3 | licence URL of the xunit repository (`license.txt`) |
| Microsoft.NET.Test.Sdk 17.12.0, Microsoft.CodeCoverage 17.12.0, Microsoft.TestPlatform.ObjectModel 17.10.0 | MIT |
| System.Collections.Immutable 1.5.0, System.Reflection.Metadata 1.6.0 | MIT (dotnet/corefx licence URL) |

## Open questions

None of these affects this source-only repository, which contains no dependency code. They matter only if someone distributes a build.

1. **ManagedEsent 1.9.4.** The licence that applied to this version could not be retrieved. It was published on 2016-07-05 with the CodePlex licence URL only. The project's GitHub repository, [microsoft/ManagedEsent](https://github.com/microsoft/ManagedEsent), has had an MIT `LICENSE.md` since 2017-09-01, and ManagedEsent 2.0.0 (2020) declares MIT.
2. **System.Diagnostics.DiagnosticSource 4.3.0** is embedded under the MICROSOFT .NET LIBRARY license. Its distribution terms include adding significant primary functionality, protective terms for end users, and indemnification. It is not loaded at run time; removing it from the embedded set would be a separate, tested build change.
3. **Open CASCADE in the engine DLL.** The LGPL 2.1 obligations for a statically linked build, such as the availability of the OCCT source and the relinking provisions, and how the Open CASCADE Exception applies to it, have not been assessed. The exact OCCT version in Xbim.Geometry.Engine.Interop 5.1.437 was not verified: the xBIM tag `v5.1.288` has OCCT 7.3.0, the `master` branch 7.6.3.

## If you distribute a build

The repository publishes no binaries. Whoever builds and distributes `IfcComBridge.dll` with `Xbim.Geometry.Engine64.dll` should at least:

1. **Include the notices:** this repository's `LICENSE`, this file, and the licence texts named in sections 1 and 2:
   - the CDDL-1.0 (xBIM);
   - the LGPL 2.1 and the Open CASCADE Exception (OCCT);
   - the Apache License 2.0 (Microsoft.Extensions 3.1.3, Serilog);
   - the MIT licences with their copyright notices;
   - Microsoft's `THIRD-PARTY-NOTICES.TXT` files from the packages.
2. **Tell recipients where the xBIM source is.** Under section 3.1 of the CDDL-1.0, covered software distributed in executable form must also be available in source form, and its recipients must be told how to obtain it. The xBIM sources are public on GitHub.
3. **Resolve the open questions above.**

**Keep this file up to date.** Any change of a package version or of the Costura-embedded set changes it.
