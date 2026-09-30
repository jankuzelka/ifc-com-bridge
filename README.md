# IfcComBridge

IfcComBridge is a Windows COM library for working with IFC4 building models from COM clients such as PHP, VBScript or C++. It wraps the [xBIM Toolkit](https://github.com/xBimTeam) behind one small, late-binding-friendly interface.

IfcComBridge is an independent project. It uses the xBIM Toolkit as a dependency, but it is not an official xBIM project and is not affiliated with or endorsed by the xBIM team or XBIM Ltd.

A client can:

- **load and save** IFC4 models;
- **compose** models: copy a building model and place products from a catalogue model into it, one output model per layout group, driven by JSON;
- **move products** by applying 4×4 transformation matrices to their placements;
- **export WexBIM**, the binary geometry format of the xBIM web viewer.

## What it is not

- **IFC4 only.** IFC2x3 and IFC4x3 files are rejected.
- **Not a general IFC editor or merge tool.** Composition copies products and their relations and can stretch them along one axis. It does not convert units, re-parent placements or create new GlobalIds; see [docs/quirks.md](docs/quirks.md).
- **Not thread-safe.** Instances share process-wide state.
- **Windows x64 and .NET Framework 4.7.2 only.** There is no 32-bit build and no .NET (Core) build, and it is an in-process server only.
- **Source only.** The repository contains no binaries, and there are no releases or NuGet packages. You build the library yourself.

## Platform and dependencies

| | |
|---|---|
| OS | Windows, x64 |
| Runtime | .NET Framework 4.7.2 or later, plus the Microsoft Visual C++ 2015–2022 Redistributable (x64) for the native geometry engine |
| IFC toolkit | xBIM Essentials 5.1.341 and xBIM Geometry 5.1.437 (CDDL-1.0; the geometry engine includes Open CASCADE, LGPL-2.1 with an exception) |
| Other packages | Newtonsoft.Json, Serilog, Microsoft.Extensions.Logging; Fody and Costura.Fody at build time |
| Deployment | two files: `IfcComBridge.dll`, which embeds its managed dependencies (Costura), and the native `Xbim.Geometry.Engine64.dll` next to it |

Details: [docs/library-build.md](docs/library-build.md). Licences: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Repository layout

```text
IfcComBridge.sln
src/IfcComBridge/                     the library, the COM server
tools/IfcComBridge.Cli/               command-line harness: drives the library, writes summaries, baselines and synthetic data
tests/IfcComBridge.Tests/             unit and characterization tests on synthetic models (no private data)
tests/IfcComBridge.IntegrationTests/  the same checks on your own models, configured in tests.local.json
tests/fixtures/synthetic/             synthetic JSON inputs
scripts/                              build, test, baselines, COM registration, COM smoke test
eng/                                  shared MSBuild settings of the harness
docs/                                 documentation
```

## Building

Prerequisites:
- Windows x64;
- Visual Studio or Visual Studio Build Tools with MSBuild, the .NET Framework 4.7.2 targeting pack and the test platform. The scripts locate them with `vswhere`. The project is built and tested with Visual Studio 2026 (MSBuild 18); Visual Studio 2022 is expected to work but has not been tested;
- access to nuget.org for the package restore;
- Windows PowerShell 5.1 or PowerShell 7;
- a short repository path, such as `C:\src\IfcComBridge`. Costura, which runs during the build, limits the paths of its cache files under `obj\` to 255 characters, even when Windows long paths are enabled. With a repository path longer than about 159 characters the build fails with "Fody/Costura: Path length is too large". After such a failure, do not simply build again in the same folder: the retry reports only a Fody warning ("already processed") and leaves the dependency DLLs loose in the output. Build a fresh copy in a short path instead.

From the repository root:

```powershell
.\scripts\build.ps1                          # Debug|x64
.\scripts\build.ps1 -Configuration Release   # Release|x64
```

The library is written to `src\IfcComBridge\bin\x64\<Configuration>\`, as `IfcComBridge.dll`, `IfcComBridge.pdb` and `Xbim.Geometry.Engine64.dll`. **Building never registers COM**, and it needs no elevation.

## Try it with synthetic data

The CLI drives the library the way a COM client does, without registering anything. The example below makes invented stand-in models and runs every feature on them. Run it from the repository root after a Release build. It writes only into a new folder under `%TEMP%`.

```powershell
$cli  = '.\tools\IfcComBridge.Cli\bin\x64\Release\net472\IfcComBridge.Cli.exe'
$work = Join-Path ([IO.Path]::GetTempPath()) "IfcComBridge-demo-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
New-Item -ItemType Directory $work | Out-Null

# 1. Synthetic inputs: building.ifc, products.ifc and the JSON that belongs to them
& $cli synthetic --out "$work\input"

# 2. Check that the native geometry engine loads
& $cli engine-check

# 3. Compose one model per layout group, as IFC and WexBIM
& $cli compose --building "$work\input\building.ifc" --products "$work\input\products.ifc" `
    --products-map "$work\input\products-map.json" --layout "$work\input\layout.json" `
    --out "$work\composed" --with-wexbim

# 4. Move two products with 4x4 matrices and save the result
& $cli update --ifc "$work\input\products.ifc" --transforms "$work\input\transforms.json" --out "$work\updated.ifc"

# 5. Look at a result as a deterministic JSON summary
& $cli summary --ifc "$work\composed\000_G1.ifc" --out -
```

Step 3 writes `000_G1` and `001_G3` (`.ifc` and `.wexbim`). The layout's group `G2-empty` places no product and therefore yields no model (quirk Q6). The library logs its progress to the console. `IfcComBridge.Cli help` lists every command.

The input formats (products map, layout, transforms) are specified in [docs/input-contracts.md](docs/input-contracts.md).

## Using it through COM

| | |
|---|---|
| ProgID | `IfcComBridge.Runtime` |
| CLSID | `{393AE64D-08F9-4652-A40C-FF133845E01D}` |
| Interface | `IComRuntime`, dual, `{CAFFDF75-6C90-4661-A477-CE89DE1AE2E3}` |

| Member | |
|---|---|
| `DoXBimLibTest()` | true when the native geometry engine loads; throws otherwise |
| `StopwatchStart()`, `StopwatchStop()` | a simple timer; returns seconds |
| `LoadIfcJson(buildingIfc, productsIfc, productsMapJson, layoutJson)` | composes models and returns their count. **`productsMapJson` is JSON text**; the other three are file paths |
| `GetModelGroupId(modelIndex)` | the layout `group_id` of a composed model, or the file path after `LoadIfc` |
| `LoadIfc(ifcFile)` | loads one IFC4 file as model 0 |
| `SaveIfc(ifcFile [, modelIndex = 0])` | writes a model as IFC |
| `SaveWexbim(wexbimFile [, modelIndex = 0])` | generates the geometry and writes WexBIM |
| `UpdateModel(transformsJsonFile [, modelIndex = 0])` | applies the transforms file and returns true if anything changed |
| `Dispose()` | releases all models |

Loading replaces all models. Errors arrive as COM errors carrying the .NET exception's HRESULT and message. The complete contract, including DispIds, errors and threading, is in [docs/com-contract.md](docs/com-contract.md) and in the XML documentation of `src/IfcComBridge/Com/IComRuntime.cs`.

### Registration

> [!WARNING]
> **COM registration changes the whole machine.** It writes to `HKEY_CLASSES_ROOT` from an elevated PowerShell, and every 64-bit process on the machine can then create the class.
> - Register only a folder you have deployed on purpose, not a build output folder: the registration points at the DLL file itself.
> - Always run `-DryRun` first and read its report.
> - Unregister with the same DLL you registered.
> - The library runs inside the client's process. A crash in it, or in the native engine, takes the client down, and the host's binding redirects can break it ([docs/known-issues.md](docs/known-issues.md), KI-1).
> - Saving deletes an existing target file first (quirk Q16).

```powershell
# 1. Deploy: copy the two files into a folder of their own
New-Item -ItemType Directory D:\IfcComBridge | Out-Null
Copy-Item .\src\IfcComBridge\bin\x64\Release\IfcComBridge.dll, `
          .\src\IfcComBridge\bin\x64\Release\Xbim.Geometry.Engine64.dll D:\IfcComBridge

# 2. Check without writing anything (no elevation needed)
.\scripts\register-com.ps1 -Library D:\IfcComBridge\IfcComBridge.dll -DryRun

# 3. Register (elevated PowerShell; asks for confirmation) and test
.\scripts\register-com.ps1 -Library D:\IfcComBridge\IfcComBridge.dll
.\scripts\com-smoke-test.ps1

# Later: remove the registration (elevated)
.\scripts\unregister-com.ps1 -Library D:\IfcComBridge\IfcComBridge.dll
```

To try COM without touching the registry, `.\scripts\com-smoke-test.ps1 -Mode RegistrationFree -Configuration Release` runs a native late-bound client through an activation context instead.

### Example client

A JScript client for the 64-bit Windows Script Host. It composes the synthetic inputs from the example above and saves every model. Save it as `compose.js` and run `%SystemRoot%\System32\cscript.exe //Nologo //E:JScript compose.js <folder with the synthetic inputs>`. `//E:JScript` names the engine explicitly, because on some machines `.js` files are associated with something else.

```javascript
var folder = WScript.Arguments(0);
function readUtf8(path) {
    var stream = new ActiveXObject("ADODB.Stream");
    stream.Type = 2; stream.Charset = "utf-8"; stream.Open(); stream.LoadFromFile(path);
    var text = stream.ReadText(); stream.Close();
    return text.charCodeAt(0) === 0xFEFF ? text.substring(1) : text;
}
var runtime = new ActiveXObject("IfcComBridge.Runtime");
try {
    runtime.DoXBimLibTest();                                  // throws if the engine cannot be loaded
    var count = runtime.LoadIfcJson(folder + "\\building.ifc", folder + "\\products.ifc",
                                    readUtf8(folder + "\\products-map.json"),  // JSON text, not a path
                                    folder + "\\layout.json");
    for (var i = 0; i < count; i++) {
        var group = runtime.GetModelGroupId(i);               // index != group position (quirk Q6)
        runtime.SaveIfc(folder + "\\composed-" + group + ".ifc", i);
        runtime.SaveWexbim(folder + "\\composed-" + group + ".wexbim", i);
        WScript.Echo(i + ": " + group);
    }
} finally {
    runtime.Dispose();                                        // release the models now, not at garbage collection
}
```

The synthetic group ids are safe as file names; real ones may not be.

## Tests

```powershell
.\scripts\test.ps1                                   # 167 unit and characterization tests; the 5 integration tests are skipped
.\scripts\test.ps1 -Config "$work\input\tests.local.json"   # + 4 integration tests on the synthetic set from above; the baseline test needs -BaselineDir
```

- **Unit and characterization tests** use synthetic IFC4 models built in code, and synthetic JSON fixtures (`tests/fixtures/synthetic`). They pin the current behaviour, quirks included.
- **Integration tests** run the same kinds of checks on models you describe in one private `tests.local.json`, outside the repository.
- **Baselines** capture every output before a change and compare after it.

See [docs/local-testing.md](docs/local-testing.md) and [docs/characterization-harness.md](docs/characterization-harness.md).

## Limitations

- **Quirks.** Nineteen documented quirks (Q1–Q19) are kept on purpose because clients may depend on them. Examples: repeated copies of a product share a GlobalId, the output takes the products model's units, and model indices are not layout group positions. See [docs/quirks.md](docs/quirks.md).
- **Host binding redirects** in a COM host's `.exe.config` can crash or break the library ([docs/known-issues.md](docs/known-issues.md), KI-1).
- **In memory.** Every model is held in memory, and WexBIM export regenerates the geometry each time.
- **Not signed.** The assembly has no strong name and no Authenticode signature; see [docs/com-contract.md](docs/com-contract.md).

## Documentation

| Document | Contents |
|---|---|
| [docs/com-contract.md](docs/com-contract.md) | COM identity, interface, errors, registration and its tests |
| [docs/input-contracts.md](docs/input-contracts.md) | exact rules for the IFC and JSON inputs |
| [docs/quirks.md](docs/quirks.md) | quirks Q1–Q19 and other observations, with their tests |
| [docs/known-issues.md](docs/known-issues.md) | KI-1: host binding redirects |
| [docs/library-build.md](docs/library-build.md) | project settings, dependencies, compatibility pins, deployment, source layout |
| [docs/characterization-harness.md](docs/characterization-harness.md) | CLI, tests, summaries and the baseline workflow |
| [docs/local-testing.md](docs/local-testing.md) | testing with your own models |

## Author

Jan Kuželka — [https://kuzelka.dev](https://kuzelka.dev)

## Support

If you find this library useful, you can support my work.

[![Support my work](https://img.shields.io/badge/Support%20my%20work-2F81F7?logo=buy-me-a-coffee&logoColor=white)](https://buymeacoffee.com/jankuzelka)

## Licence

IfcComBridge is released under the [MIT License](LICENSE), © 2023–2026 Jan Kuželka.

The MIT License covers this repository's own source code. The repository contains no binaries and no third-party code apart from one schema file that Fody generates (see the notices); NuGet downloads the dependencies when you build. The xBIM Toolkit, Open CASCADE and the other dependencies have their own licences, summarized in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Read that file before you distribute a binary build.

xBIM and the xBIM Toolkit are projects of the xBIM team; IfcComBridge is not part of them.
