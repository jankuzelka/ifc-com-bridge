# Library project, dependencies and deployment

This page describes the library project, `src/IfcComBridge/IfcComBridge.csproj`: its settings, its dependencies, the files a build produces and how to deploy them.

## Project format

The library project is an SDK-style project (`Microsoft.NET.Sdk`): `net472`, x64 only. The build is deterministic, so unchanged sources give byte-identical compiler output.

| Setting | Why |
|---|---|
| `PlatformTarget` x64; `Platforms` AnyCPU;x64 | x64 output in every configuration, also when a build names no platform |
| `LangVersion` 7.3 | the C# version the code was written for |
| `GenerateAssemblyInfo` false | the assembly attributes stay in source: versions and `InternalsVisibleTo` in `Properties/AssemblyInfo.cs`, the COM attributes in `Com/ComIdentity.cs` |
| default items | the project has its own folder, so the SDK's default file globs include exactly its sources |
| `AppendTargetFrameworkToOutputPath` false | output stays in `bin\<Platform>\<Configuration>\` |
| `CopyLocalLockFileAssemblies`, `CopyDebugSymbolFilesFromPackages` true | Costura embeds what is copied to the output, package symbols included; without these the embedded set would change |
| `DebugType` full (Debug), pdbonly (Release) | classic Windows PDBs |
| `FodyAfterTargets` AfterCompile | Fody runs after every compile step, as it does on MSBuild 16 and earlier. With the default of MSBuild 17 and later it runs only when the compiler runs; a build that skips compilation then also skips removing the embedded references from the copy list, and all dependency DLLs appear next to the library in the output folder. |
| `RegisterForComInterop` false, `TreatAsLocalProperty` | building never registers COM, even with `/p:RegisterForComInterop=true` |

**Building never registers COM.** Registration is a separate deployment step (`scripts\register-com.ps1`).

## Dependencies

Top-level package references:

| Package | Version | Purpose |
|---|---|---|
| Xbim.Essentials | 5.1.341 | IFC model API (Xbim.Common, Ifc, Ifc2x3, Ifc4, IO.MemoryModel, IO.Esent, Tessellator) |
| Xbim.Geometry | 5.1.437 | geometry: Xbim.Geometry.Engine.Interop with the native engine, Xbim.ModelGeometry.Scene |
| Newtonsoft.Json | 13.0.3 | JSON inputs |
| Serilog | 3.0.1 | logging |
| Serilog.Extensions.Logging | 3.1.0 | routes Microsoft.Extensions.Logging (used by xBIM) to Serilog |
| Serilog.Sinks.Console | 4.1.0 | console output of the log |
| Microsoft.Extensions.Logging | 3.1.3 | logger factory used by xBIM |
| Microsoft.Extensions.Options | 7.0.1 | **compatibility pin**, see below |
| System.ValueTuple | 4.5.0 | **compatibility pin**, see below |
| Fody | 6.8.0 | build-time weaving (private) |
| Costura.Fody | 5.7.0 | embeds the managed dependencies (private) |

Framework references: `System`, `System.Core`, and `System.IO.Compression` for `ZipArchive`.

Some `System.*` 4.3.x packages appear in the restore graph, transitively through Costura.Fody and the geometry interop. Only one of them ends up in the output: System.Diagnostics.DiagnosticSource 4.3.0 is embedded, although no COM call loads it. Its licence is not an open-source licence; see [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md).

Licences of all dependencies: [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md).

### Compatibility pins

Neither pin is needed to compile. Both fix the versions that Costura embeds, because a COM host's own binding redirects interact with Costura assembly resolution (see [known-issues.md](known-issues.md), KI-1). Changing them may alter runtime behaviour in COM hosts.

- **Microsoft.Extensions.Options 7.0.1** (assembly 7.0.0.1). Without the pin, Microsoft.Extensions.Logging 3.1.3 would resolve Options 3.1.3. The effect on hosts that redirect Options in their `.exe.config` would be:
  - a redirect to 7.0.0.1 works with the pin, and would crash the host without it;
  - a redirect to 3.1.3.0 crashes the host with the pin, and would work without it.

  Host configurations that redirect Options to 7.0.0.1 are part of the tested host-redirect matrix; the pin keeps them working.
- **System.ValueTuple 4.5.0** (assembly 4.0.3.0). .NET Framework 4.7.2 contains ValueTuple, and the library normally uses the framework's copy. A host that redirects System.ValueTuple to 4.0.3.0 without shipping the DLL works only because the library embeds 4.0.3.0. Without the pin, every call fails in such a host.

Do not remove or change these pins without re-running host-redirect checks as described in KI-1.

### Embedded assemblies

`IfcComBridge.dll` embeds 43 files, plus Costura's index: the managed dependencies, and the symbol files of the xBIM assemblies, Esent.Interop and Costura.

What the library loads at run time:
- Microsoft.Extensions.Logging and Microsoft.Extensions.Logging.Abstractions 3.1.3;
- Microsoft.Extensions.Options 7.0.1;
- Newtonsoft.Json;
- Serilog, Serilog.Extensions.Logging and Serilog.Sinks.Console;
- the xBIM assemblies;
- System.ValueTuple 4.0.3.0, only when a host redirects to it.

Also embedded: Microsoft.Extensions.Primitives, Microsoft.Extensions.Configuration (with .Abstractions and .Binder) and Microsoft.Extensions.DependencyInjection (with .Abstractions); Microsoft.Bcl.AsyncInterfaces; Esent.Interop; System.Buffers, System.Diagnostics.DiagnosticSource, System.Memory, System.Numerics.Vectors, System.Runtime.CompilerServices.Unsafe and System.Threading.Tasks.Extensions; Costura.

No COM call loads any of the six assemblies below, so host redirects for them have no effect. A COM-host simulation checked this with redirects both to the embedded version and to a higher version.

| Assembly | Embedded version |
|---|---|
| Esent.Interop (and its symbols) | 1.9.4.0 |
| Microsoft.Bcl.AsyncInterfaces | 7.0.0.0 |
| Microsoft.Extensions.Configuration, .Abstractions, .Binder | 3.1.3.0 |
| Microsoft.Extensions.DependencyInjection | 3.1.3.0 |
| Microsoft.Extensions.DependencyInjection.Abstractions | 7.0.0.0 |
| System.Diagnostics.DiagnosticSource | 4.0.1.0 |

## Why Costura

The library is an in-process COM server. It runs inside the host's process, under the host's configuration:

- **No redirects of its own.** Its own `.config` file, and with it any binding redirect, is never read. The project therefore has no `app.config`.
- **Conflicting versions.** Its dependencies disagree on versions: Xbim.Common is built against Microsoft.Extensions.Logging 2.1.1, but 3.1.3 ships.
- **Resolution by name.** Costura embeds the managed dependencies and resolves them by name, whatever version is requested. That makes the library self-contained and independent of binding redirects of its own.
- **Two-file deployment.** The deployment is two files, and COM registration has only one managed assembly to point to.

The native `Xbim.Geometry.Engine64.dll` cannot be loaded from inside the library and must be deployed next to it. A host's own binding redirects can still interfere with Costura; see KI-1.

## Deployment layout

The build output is in `src\IfcComBridge\bin\x64\<Configuration>\`:

| File | |
|---|---|
| `IfcComBridge.dll` | the library, with the managed dependencies embedded |
| `IfcComBridge.pdb` | symbols (optional) |
| `Xbim.Geometry.Engine64.dll` | the native geometry engine; must be in the same folder as `IfcComBridge.dll` |

Deploy `IfcComBridge.dll` and `Xbim.Geometry.Engine64.dll` to one folder; no other file is needed. `IfcComBridge.Cli engine-check --library <folder>\IfcComBridge.dll` validates such a folder without registering anything.

The target machine needs:
- .NET Framework 4.7.2 or later;
- the Microsoft Visual C++ 2015–2022 Redistributable (x64). The engine imports `MSVCP140.dll`, `VCRUNTIME140.dll` and `VCRUNTIME140_1.dll`.

Then register the folder with `scripts\register-com.ps1`; try `-DryRun` first. Registration writes `IfcComBridge.tlb`, the type library for early-bound clients, next to the library. The COM contract, registration and its tests are described in [com-contract.md](com-contract.md).

### Binaries are not part of the repository

The repository publishes source code only: no DLL, type library, PDB, installer or package, and `.gitignore` keeps build output out of it. A build of the two deployment files contains third-party code, though: `IfcComBridge.dll` embeds the xBIM assemblies and the other packages listed above, and `Xbim.Geometry.Engine64.dll` contains xBIM code and Open CASCADE. If you distribute such a build, read [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md) first.

## Source layout

Only the COM interface and class are public; everything else is internal. The CLI and the tests reach the internal classes through `InternalsVisibleTo`.

| Folder, namespace | Class | Contents |
|---|---|---|
| `Com/`, `IfcComBridge` | `IComRuntime`, `ComRuntime` | the COM interface and class |
| | `ComIdentity`, `RuntimeDispIds` | every COM identifier |
| `Composition/`, `IfcComBridge.Composition` | `ProductLayoutComposer` | `LoadIfcJson`: one model per layout group; the copy of the building |
| | `ProductSelector` | the catalogue variant and length scale for a layout set |
| | `ProductCopier` | the copy of one product, with its new placement and scaled geometry |
| | `ProductRelationsCopier` | the relations of the copies; units, owner histories, project contexts |
| | `CopyFilters` | the property filter for copying without owner histories |
| | `ComposedModel` | a loaded or composed model and its group id |
| `Placement/`, `IfcComBridge.Placement` | `PlacementUpdater` | `UpdateModel`: 4×4 transformations of placements |
| `Geometry/`, `IfcComBridge.Geometry` | `BrepPointOptimizer` | merging of duplicate points after composition |
| | `ExactPoint3Comparer` | exact point equality |
| | `PlanarMath` | 2-D rotations |
| `IO/`, `IfcComBridge.IO` | `ModelFiles` | input checks, JSON and IFC loading, saving IFC and WexBIM, geometry generation |
| | `FileCompression` | optional zip output (not used by any caller) |
| `Infrastructure/`, `IfcComBridge.Infrastructure` | `LoggingSetup` | process-wide logging to the console |
| | `EditorIdentity` | the editor identity xBIM requires |

- **Static state (Q9)** is process-wide:
  - the Serilog configuration and xBIM's logger factory (`LoggingSetup`);
  - the shared editor credentials (`EditorIdentity.Shared`);
  - `ProductLayoutComposer.CurrentOptimization`;
  - xBIM's model provider factory, set on every load (`ModelFiles.LoadIfcModel`).
- **Comments.** The classes explain their assumptions: the rollback transactions on the source model, the instance-handle maps, frames and matrix conventions, units, static state and lifetime. Each quirk is tagged `Qn` where it happens ([quirks.md](quirks.md)).

## Verifying a change

Build and test with the scripts, then compare with a baseline captured before the change: see [characterization-harness.md](characterization-harness.md). A change that should not alter behaviour must leave the tests green and the baseline identical.
