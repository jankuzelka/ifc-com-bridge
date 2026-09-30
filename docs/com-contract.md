# COM contract

This page describes the COM contract of IfcComBridge:
- the contract itself;
- how to register it;
- how it is tested.

The .NET names are: assembly `IfcComBridge.dll`, interface `IfcComBridge.IComRuntime`, class `IfcComBridge.ComRuntime`.

## Identity

| | Value |
|---|---|
| ProgID | `IfcComBridge.Runtime` |
| CLSID | `{393AE64D-08F9-4652-A40C-FF133845E01D}` (class `IfcComBridge.ComRuntime`) |
| IID | `{CAFFDF75-6C90-4661-A477-CE89DE1AE2E3}` (interface `IComRuntime`, dual) |
| LIBID | `{B147CBDC-5FE3-42E6-93CF-469B331F8C5C}`, version 1.0, win64, library name `IfcComBridge` |
| Server | in-process, 64-bit only: `mscoree.dll` loads `IfcComBridge.dll` (.NET Framework 4.7.2) |
| Threading model | Both |

Every identifier, including the DispIds, is defined once, in `src/IfcComBridge/Com/ComIdentity.cs`. The tests pin these values independently (`tests/IfcComBridge.Tests/Com/ExpectedComContract.cs`).

## Visibility

- **Opt-in visibility.** The assembly is `[ComVisible(false)]`. Only `IComRuntime` and `ComRuntime` are `[ComVisible(true)]`, and they are the only public types; everything else is internal. This keeps RegAsm working on the deployed folder (`IfcComBridge.dll` and `Xbim.Geometry.Engine64.dll` only): RegAsm inspects the public constructors of every public class, and a public class whose constructor needs the xBIM assemblies on disk would make it fail.
- **One creatable class.** `ComRuntime` is the only creatable class, and the only one RegAsm registers. It has `ClassInterfaceType.None` and the default interface `IComRuntime`.
- **No class interface.** `ToString`, `Equals`, `GetHashCode` and `GetType` are therefore not reachable through IDispatch.
- **Two extra listed interfaces.** The type library lists two more, non-default interfaces on the class, because every .NET class has them: `_Object` (System.Object) and `IDisposable`. Use `IComRuntime`.

## The interface

The interface is dual. Late-bound clients (IDispatch: PHP, VBScript, JScript, VBA late binding) and early-bound clients (vtable, compiled against the type library) use the same members.

| DispId | vtable slot | Member | |
|---|---|---|---|
| 1 | 7 | `VARIANT_BOOL DoXBimLibTest()` | true when the geometry engine loads |
| 2 | 8 | `void StopwatchStart()` | |
| 3 | 9 | `double StopwatchStop()` | seconds; 0 without `StopwatchStart` |
| 4 | 10 | `long LoadIfcJson(BSTR fileIfcBuilding, BSTR fileIfcProducts, BSTR productsMapJson, BSTR fileJson)` | products map as JSON **text**; returns the number of models |
| 5 | 11 | `BSTR GetModelGroupId(long modelIndex)` | |
| 6 | 12 | `void LoadIfc(BSTR ifcFile)` | |
| 7 | 13 | `void SaveIfc(BSTR ifcFile, [optional, defaultvalue(0)] long modelIndex)` | |
| 8 | 14 | `void SaveWexbim(BSTR wexbimFile, [optional, defaultvalue(0)] long modelIndex)` | |
| 9 | 15 | `VARIANT_BOOL UpdateModel(BSTR jsonParametersFile, [optional, defaultvalue(0)] long modelIndex)` | transforms JSON **file path** |
| 10 | 16 | `void Dispose()` | releases the loaded models |

- **HRESULTs.** In the vtable, every method returns an HRESULT, and results come back as `[out, retval]`.
- **Errors.** A .NET exception becomes a COM error with the exception's HRESULT and message. For example, `GetModelGroupId(0)` without models gives `0x80131502`, "Index was out of range…". Through IDispatch the error arrives as `DISP_E_EXCEPTION` with that description. The usual cases:

  | Exception | HRESULT | Typical cause |
  |---|---|---|
  | `ArgumentOutOfRangeException` | `0x80131502` | a model index outside the loaded models, including any index before loading or after `Dispose` |
  | `FileNotFoundException` | `0x80070002` | a missing input file, **or an input that is not IFC4** (quirk Q11) |
  | `InvalidDataException` | `0x80131501` | an empty file path |
  | `Newtonsoft.Json.JsonReaderException` | `0x80131500` | malformed JSON, or a products map that is not an object |
  | `ArgumentException` | `0x80070057` | a duplicate GlobalId among the products (Q12) |
  | `NullReferenceException` | `0x80004003` | a required JSON field is missing (Q5), or a placement without Axis or RefDirection (Q7) |
  | `FileLoadException` | `0x80131621` | `Xbim.Geometry.Engine64.dll` cannot be loaded (`DoXBimLibTest`, `SaveWexbim`) |
  | `Exception` | `0x80131500` | no products, or an empty building model |

  The HRESULTs are those of .NET Framework 4.x.
- **Lifetime.** `LoadIfc` and `LoadIfcJson` replace all loaded models. Call `Dispose` when done; otherwise the models are released only when .NET collects the object.
- **Threads.** The class is registered with `ThreadingModel` Both but takes no locks, and all instances share process-wide state (quirk Q9). Do not call one object from several threads at once.
- **Reference.** Every member is documented in the XML comments of `src/IfcComBridge/Com/IComRuntime.cs`. The quirks the members show are in [quirks.md](quirks.md).
- **Inputs.** The input rules are in [input-contracts.md](input-contracts.md).
- **Stability.** The member order (the vtable) and the DispIds are fixed. A different member list needs a new interface with a new IID.

## Registration

Builds and tests never register COM. Registration writes to `HKEY_CLASSES_ROOT`, so it needs an elevated PowerShell. The scripts never elevate themselves, and they ask for confirmation (`-Confirm:$false` skips it).

```powershell
.\scripts\register-com.ps1 -DryRun                                        # checks only; writes nothing
.\scripts\register-com.ps1 -Library D:\IfcComBridge\IfcComBridge.dll      # elevated: registers
.\scripts\com-smoke-test.ps1                                              # late-bound test of the registration
.\scripts\unregister-com.ps1 -DryRun                                      # shows what would be removed
.\scripts\unregister-com.ps1 -Library D:\IfcComBridge\IfcComBridge.dll    # elevated: unregisters
```

Deploy first (`IfcComBridge.dll` and `Xbim.Geometry.Engine64.dll` in one folder), then register that folder. Without `-Library`, the scripts use `src\IfcComBridge\bin\x64\Release\IfcComBridge.dll`.

- **Class.** Registered by the 64-bit .NET Framework RegAsm with `/codebase`: the ProgID, the CLSID, and `InprocServer32` = `mscoree.dll` with the `CodeBase` of `IfcComBridge.dll`.
- **RegAsm's RA0000 warning.** RegAsm warns on stderr that `/codebase` is meant for strong-named assemblies ("Registering an unsigned assembly with /codebase…"). The library is not signed; see the deployment decisions below. The scripts accept this one warning only when RegAsm exits with 0. A nonzero exit code, an error, or any other warning (for example "no types were registered") fails the step, and RegAsm's full output is shown.
- **PowerShell versions.** The scripts run in Windows PowerShell 5.1 and PowerShell 7. They start native programs (RegAsm, the helper, cscript, the CLI) through `System.Diagnostics.Process` and read the exit code, stdout and stderr separately (`Invoke-NativeCommand` in `com-common.ps1`). They never use `& program 2>&1`: under `$ErrorActionPreference = 'Stop'`, Windows PowerShell 5.1 turns such stderr output into a terminating `NativeCommandError`, even when the program succeeded.
- **Type library.** Written next to the library as `IfcComBridge.tlb` and registered: `HKCR\TypeLib\{LIBID}` and `HKCR\Interface\{IID}` with the universal marshaler. Early-bound clients compile against it, and COM needs it to marshal the interface between apartments or processes. Late-bound in-process clients do not need it. `-NoTypeLibrary` skips it.
- **Why not `RegAsm /tlb`.** With `ComVisible(false)` on the assembly, the type library exporter loads every type of the assembly. Some private types (compiler-generated iterators) need Newtonsoft.Json, which only Costura provides. RegAsm and TlbExp do not run Costura's module initializer, so on the two-file deployment they fail with "Type library exporter cannot load type". The scripts use the same exporter (`TypeLibConverter`) after running the initializer (`scripts\com-typelib-export.ps1`). The result describes identically to TlbExp's output when all dependencies are on disk.
- **The dry run** writes a `.reg` file with RegAsm's class registration and an unregistered copy of the type library into a folder under `%TEMP%`. It then checks:
  - exactly one creatable class, and the ProgID `IfcComBridge.Runtime`;
  - the ProgID points to the CLSID and back;
  - `InprocServer32` is `mscoree.dll` with the runtime class, .NET 4 and `ThreadingModel` Both, and its `CodeBase` is the library;
  - RegAsm registers nothing else;
  - the type library: LIBID, version 1.0 and win64, one creatable coclass, and a dual, automation-compatible default interface with explicit DispIds;
  - the geometry engine next to the library.

  It also lists the keys the type library registration writes, and the current registration state of the machine, read-only.

### Open deployment decisions

- **Strong-name signing (not done).** `IfcComBridge.dll` has no strong name, hence RegAsm's RA0000 warning with `/codebase`. The warning is not a reason to sign.
  - Signing changes the assembly identity: the registration's `Assembly` value would get a public key token.
  - It needs a key and a policy for keeping it.
  - It is one of the decisions for how the library is released and deployed, together with Authenticode code signing and the installation folder.
  - Until it is taken, the warning is expected and reported as such.

## Testing

| What | Where | Registration needed |
|---|---|---|
| Contract by reflection: visibility, identifiers, dual interface, vtable order, DispIds, signatures | `tests/IfcComBridge.Tests/Com/ComContractTests.cs` | no |
| Type library, exported in memory and read through `ITypeLib`/`ITypeInfo` | `Com/ComTypeLibraryTests.cs` | no |
| Native calls into the COM callable wrapper: `QueryInterface`, `IDispatch::GetIDsOfNames`/`Invoke` with VARIANTs, vtable calls, errors as HRESULTs | `Com/ComCallTests.cs` | no (object created in-process) |
| What RegAsm and the type library registration would write | `register-com.ps1 -DryRun` | no |
| Activation by ProgID and late-bound calls from a native client (64-bit cscript, JScript) through an activation context; with `-Config`/`-BaselineDir`, every characterization step through COM, compared with the baseline | `com-smoke-test.ps1 -Mode RegistrationFree` | no |
| Activation through the registry (ProgID → CLSID → `InprocServer32` → `CodeBase`) | `com-smoke-test.ps1` (Registered mode) | **yes** |

Registered mode has been run on a real registration of the final `IfcComBridge.dll`: registration, activation through the registry and the smoke test passed, and the registration was removed again afterwards.

Not checked yet:
- marshaling the dual interface between apartments or processes;
- a real PHP client.

Registration-free mode copies the 64-bit `cscript.exe` into a temporary host folder. This is because the .NET runtime looks for the library in the folder of the client executable.

## Clients

64-bit JScript or VBScript through `cscript`: see the example client in the [README](../README.md#example-client), and the client that `com-smoke-test.ps1` writes (`smoke-test.js` in its output folder).

64-bit PHP with the COM extension. This is a sketch; it has **not** been tested yet:

```php
$runtime = new COM('IfcComBridge.Runtime', null, CP_UTF8); // UTF-8 paths and JSON text
if (!$runtime->DoXBimLibTest()) { throw new Exception('Xbim.Geometry.Engine64.dll not loadable'); }
$runtime->LoadIfc('D:\\models\\building.ifc');
$runtime->SaveIfc('D:\\out\\building.ifc');   // modelIndex defaults to 0
$runtime->Dispose();
```

Call `Dispose` when done. Otherwise the loaded models are released only when .NET collects the object.
