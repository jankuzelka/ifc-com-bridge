# Characterization harness

The harness pins down the **current** behaviour of the COM library, so that any change can be shown to keep it, or to change exactly what was intended. It consists of:

| Part | Purpose |
|---|---|
| `tools/IfcComBridge.Cli` | net472 x64 command-line tool that drives the library: engine check, load/save, WexBIM export, compose, update. It also produces deterministic summaries, compares them, captures baselines, validates the private configuration and writes synthetic inputs. |
| `tests/IfcComBridge.Tests` | xUnit characterization tests on synthetic IFC4 models built in code. Always runs. |
| `tests/IfcComBridge.IntegrationTests` | The same kinds of checks against your private models, described by **one** private `tests.local.json` (see [local-testing.md](local-testing.md)). Skipped without it. |
| `tests/fixtures/synthetic` | Synthetic JSON inputs that match the synthetic IFC models (see its README). |
| `scripts/*.ps1` | `build`, `test`, `capture-baseline`, `compare-baseline`; the last three take `-Config`. COM registration and its smoke test: `register-com`, `unregister-com`, `com-smoke-test` (see [com-contract.md](com-contract.md)). |

The harness never modifies the library project. The project itself, its dependencies, the two compatibility pins and the deployment layout are described in [library-build.md](library-build.md).

The harness projects reference the library through `eng/LibraryReference.props`:
- **The library:** a normal project reference. The implementation types are internal, and the library grants `InternalsVisibleTo` to `IfcComBridge.Cli` and `IfcComBridge.Tests`.
- **The geometry engine:** the `Xbim.Geometry.Engine.Interop` package, so that `Xbim.Geometry.Engine64.dll` is copied next to the harness binaries.

Other rules:
- **No COM registration:** the library project forces `RegisterForComInterop=false`, even against a value passed to the build, and `scripts/build.ps1` passes it for every project. Building never touches the registry and needs no elevation.
- **Output folder:** the library is built into `src/IfcComBridge/bin/x64/<Configuration>/`, which holds exactly the deployment layout: `IfcComBridge.dll`, `IfcComBridge.pdb` and `Xbim.Geometry.Engine64.dll`.

Build `IfcComBridge.sln` or run the scripts.

## Build and test

```powershell
.\scripts\build.ps1                                              # Debug|x64; -Configuration Release also works
.\scripts\test.ps1                                               # unit + integration (integration skipped: no private config)
.\scripts\test.ps1 -Config D:\private\tests.local.json            # + private-data tests for the configured scenarios
.\scripts\test.ps1 -Config D:\private\tests.local.json -BaselineDir D:\private\baseline\reference
.\scripts\test.ps1 -UnitOnly
```

Test results go to `%TEMP%\IfcComBridge.TestResults` by default, outside the repository, because integration output can quote names from private models.

## Private data: one configuration file

All private inputs are described by one `tests.local.json` **outside** the repository. The repository only contains the placeholder `tests.local.example.json`.

- The format, validation rules and scenarios are described in [local-testing.md](local-testing.md).
- The exact input rules are in [input-contracts.md](input-contracts.md).
- There are no environment variables for inputs. `scripts/test.ps1` passes only the configuration **path** to the test host, for the duration of the run.

`IfcComBridge.Cli synthetic --out <dir>` writes a complete synthetic stand-in set (both IFC models, the JSON fixtures and a `tests.local.json`). With it, every private-data test and the baseline workflow can be exercised without private data.

## Baseline workflow (regression safety for every change)

1. **Before changing the library:** build the harness against the unmodified library and capture a baseline into a folder **outside** the repository:

   ```powershell
   .\scripts\build.ps1
   .\scripts\capture-baseline.ps1 -Config D:\private\tests.local.json -OutDir D:\private\baseline\reference
   ```

   The script and the CLI refuse any folder inside the repository. They detect it by `IfcComBridge.sln` in any parent folder.

2. **After every change:** rebuild and compare with the same configuration:

   ```powershell
   .\scripts\build.ps1
   .\scripts\compare-baseline.ps1 -Config D:\private\tests.local.json -BaselineDir D:\private\baseline\reference
   ```

   The comparison first checks that the configured inputs are the baseline's: the same roles, and byte-identical files by SHA-256 in `manifest.json`.
   - Exit 0 means identical.
   - Exit 1 means differences, which are listed; the fresh run is kept and its path printed.
   - Exit 3 means the configuration is invalid, or the inputs are not the baseline's.

   `.\scripts\test.ps1 -Config … -BaselineDir …` runs the same comparison as the integration test `CurrentLibrary_MatchesTheCapturedBaseline`.

A baseline contains:

- `manifest.json`: input file names and hashes only, no folder paths
- `engine/`, `single/`, `compose/runtime/`, `compose/utils/`, `update/`, each with `*.summary.json` plus the raw IFC/WexBIM outputs
- the missing-items log (as a summary)

A step that throws is recorded as `error.summary.json`, because an exception is behaviour too.

## CLI

```text
IfcComBridge.Cli help
IfcComBridge.Cli engine-check [--library <dir\IfcComBridge.dll>]
IfcComBridge.Cli load-save --ifc <in> --out <out> [--force]
IfcComBridge.Cli wexbim    --ifc <in> --out <out.wexbim> [--force]
IfcComBridge.Cli compose   --building <ifc> --products <ifc> --products-map <json> --layout <json> --out <dir>
                           [--api runtime|utils] [--missing-log <file>] [--with-wexbim] [--force]
IfcComBridge.Cli update    --ifc <in> --transforms <json> --out <out.ifc> [--wexbim-out <file>] [--force]
IfcComBridge.Cli summary   --ifc <file> [--wexbim <file>] --out <file|-> [--include-identity] [--force]
IfcComBridge.Cli compare   --baseline <file|dir> --current <file|dir> [--ignore a,b]
IfcComBridge.Cli baseline  --out <dir> (--config <tests.local.json> | [--ifc ..] [--transforms ..] [--building .. --products .. --products-map .. --layout ..])
IfcComBridge.Cli baseline-compare --config <tests.local.json> --baseline <dir> [--ignore a,b] [--keep]
IfcComBridge.Cli config    --config <tests.local.json> [--json]
IfcComBridge.Cli synthetic --out <dir> [--force]
```

**Exit codes:** 0 ok, 1 check failed or differences, 2 error, 3 configuration error, 64 usage error.

**Output safety:** the CLI refuses to write over any input. It will not overwrite existing outputs without `--force`, and it will not write into a folder that holds an input.

**`engine-check --library`** loads a given DLL into an isolated AppDomain, which mimics an in-process COM host without registering anything. Use it to validate a deployment folder:

- `IfcComBridge.dll` alone **fails**: the geometry engine cannot be loaded from inside the DLL.
- `IfcComBridge.dll` plus `Xbim.Geometry.Engine64.dll` side by side **works**.

**`compose` has two APIs:**
- `--api runtime` is the COM path: `LoadIfcJson` with the products map passed as JSON **text**, read from the file.
- `--api utils` calls the library's internal static helper directly, bypassing the runtime class. Only this path offers the optional missing-items log.

## What a summary contains

**Included:**
- schema and entity counts per type
- GlobalId statistics (including duplicates)
- project units and representation contexts
- spatial structure
- per product:
  - label, type, name, description
  - relative and world placement
  - containment, type, property sets, materials, classifications
  - representations: items, styles, point counts, bounding boxes
- point statistics
- optionally the WexBIM header (counts, regions in canonical order)

**Excluded on purpose:** file paths, header timestamps, GlobalId values (new entities get random GUIDs), and a WexBIM file hash. xBIM writes equal-population regions in a non-deterministic order, so identical code does not produce identical WexBIM bytes.

**Identity** (header author and organization, `IfcApplication`/`IfcOrganization`/`IfcPerson`) is opt-in (`--include-identity`), because it can carry private names.

## Notes for maintainers

- [quirks.md](quirks.md) lists the behaviour the tests pin on purpose (Q1–Q19), with the test for each quirk. A test named `Qn_…` pins quirk Qn.
- [known-issues.md](known-issues.md), KI-1: binding redirects in a host's `.exe.config` can break the library, because Costura takes part in assembly resolution. The compatibility pins keep the tested host behaviour; a general fix is a separate change.
- `tests/IfcComBridge.Tests/Com` pins the COM contract ([com-contract.md](com-contract.md)): identifiers, visibility, the dual interface with its DispIds and vtable order, the exported type library, and native IDispatch and vtable calls.
- The tests call the library's internal classes directly through `InternalsVisibleTo`.
- The golden files in `tests/IfcComBridge.Tests/Golden` are summaries of **synthetic** fixtures only. To regenerate them after an intended change:
  1. Set `IFCCOMBRIDGE_UPDATE_GOLDEN` to that folder.
  2. Run the tests once.
  3. Review the diff before committing.
