# Local testing with private models

The characterization harness can check the current library against your **own** IFC models. All
private inputs are described by **one** JSON file, `tests.local.json`. Keep that file, the models and
all baselines **outside** the repository: the tools refuse a configuration, an input or an output
folder inside it.

For the exact rules each input must follow, see [input-contracts.md](input-contracts.md). For the
harness itself (CLI, summaries, golden files), see
[characterization-harness.md](characterization-harness.md).

## 1. The configuration file

Copy `tests.local.example.json` from the repository root to a private folder and edit the paths:

```json
{
  "version": 1,
  "scenarios": {
    "model": {
      "ifc": "D:/IFC/model.ifc",
      "transforms": "D:/IFC/transforms.json"
    },
    "composition": {
      "building": "D:/IFC/building.ifc",
      "products": "D:/IFC/products.ifc",
      "productsMap": "D:/IFC/products-map.json",
      "layout": "D:/IFC/layout.json"
    }
  }
}
```

- `version` must be `1`.
- **Scenarios are optional.** Leave one out, or set it to `null`, to skip its tests. `"scenarios": {}` enables nothing.
- `model.transforms` is optional. When `composition` is present, **all four** of its paths are required.
- **Paths:** relative paths are resolved against the folder of the configuration file, not the current directory. Forward and back slashes both work.
- **Validation is strict:** unknown properties, scenarios or fields are errors, so a typo cannot silently disable a scenario.
- **Every problem is reported at once.** The checks cover:
  - missing files and directories given instead of files;
  - the IFC `ISO-10303-21` header and schema: the library accepts **IFC4 only**, so IFC2x3 and IFC4X3 are rejected;
  - the JSON root shapes the library cannot work without: products map = object, layout = array, transforms = object with a `modified_products` array.

Check a configuration without running anything:

```powershell
.\tools\IfcComBridge.Cli\bin\x64\Debug\net472\IfcComBridge.Cli.exe config --config D:\private\tests.local.json
```

Exit code 0 means valid; 3 means invalid, with every problem listed.

One parser and validator (`LocalTestConfiguration` in the CLI) serves everything:
- the CLI commands `config`, `baseline` and `baseline-compare`;
- the scripts, which only call those commands;
- the integration tests.

## 2. What each input represents

| Input | What it is | Used by |
|---|---|---|
| `model.ifc` | Any IFC4 model. | Load/save round trip, WexBIM export, summary determinism, and the model the transforms apply to. |
| `model.transforms` | `{"modified_products":[{"id":<entity label>,"transformation":{"0".."15"}}]}`. Each id is an entity label (`#n`) **in `model.ifc`**. | `UpdateModel` |
| `composition.building` | The building model. It is copied **completely** into every output model. | `LoadIfcJson` |
| `composition.products` | The product catalogue model. Its `IfcBeam` and `IfcWallStandardCase` elements are the products that can be placed. | `LoadIfcJson` |
| `composition.productsMap` | JSON object: set name → product (by `ifc_guid`, falling back to `ifc_name`), or → an array of length variants. | `LoadIfcJson` |
| `composition.layout` | JSON array of groups; each group holds sets (placements). One output model is produced per group that places at least one product. | `LoadIfcJson` |

How the composition inputs relate:

```text
layout set.name ──(exact key, else first prefix key)──► products-map entry
products-map entry ──(ifc_guid, else ifc_name)──► IfcBeam / IfcWallStandardCase in products IFC
building IFC ──(copied entirely)──► every output model
layout group ──(at least one placed product)──► one output model
```

- **Units:** all lengths in the products map and layout are raw numbers in the **products model's** unit; nothing converts.
- **Products map:** through COM, `LoadIfcJson` receives it as **JSON text**. In the configuration and the CLI it is a **file path**, and the harness reads the file and passes its text exactly as a COM client would.

## 3. Which tests need which inputs

| Configured | Integration tests that run | Baseline steps |
|---|---|---|
| nothing (or no `-Config`) | none; all 5 are skipped (normal CI mode) | – |
| `model.ifc` only (one IFC4 file) | `LoadSaveAndWexbim_Succeed_AndSavingPreservesTheModel`, `Summary_IsDeterministic` | `engine`, `single` |
| `model.ifc` + `model.transforms` | the above + `UpdateModel_Runs_AndTheResultCanBeSaved` | + `update` |
| `composition` (all four) | `Compose_RuntimeAndStaticHelper_ProduceIdenticalModels_AndAreDeterministic` | + `compose/runtime`, `compose/utils` |
| any of the above + `-BaselineDir` | + `CurrentLibrary_MatchesTheCapturedBaseline` | – |

- **Skipped vs failed:** a scenario that is not configured is **skipped**, with the reason shown. A configuration that is **invalid** makes the tests **fail**, listing every problem. `scripts\test.ps1` validates the configuration first and stops before running anything if it is invalid.
- **Unit and characterization tests** (`IfcComBridge.Tests`, 167 tests) always run. They need **no** private data: they use synthetic models built in code.

## 4. Baseline capture and comparison with one JSON

```powershell
.\scripts\build.ps1

# before a change, from the unmodified library, into a folder outside the repository
.\scripts\capture-baseline.ps1 -Config D:\private\tests.local.json -OutDir D:\private\baseline\reference

# after the change
.\scripts\compare-baseline.ps1 -Config D:\private\tests.local.json -BaselineDir D:\private\baseline\reference

# the same comparison as part of the test run
.\scripts\test.ps1 -Config D:\private\tests.local.json -BaselineDir D:\private\baseline\reference

# the same steps through late-bound COM from a native client, without registering anything
.\scripts\com-smoke-test.ps1 -Mode RegistrationFree -Config D:\private\tests.local.json -BaselineDir D:\private\baseline\reference
```

`com-smoke-test.ps1` compares the COM path only: the `single`, `compose/runtime` and `update` steps, plus the engine result. The static helper step (`compose/utils`) cannot be reached through COM. See [com-contract.md](com-contract.md).

**What `compare-baseline` checks and reports:**
- It first checks that the configured inputs are the baseline's: the same roles, and byte-identical files by SHA-256 from `manifest.json`. Otherwise it exits with code 3 and says which input differs.
- It re-runs every step into `%TEMP%` and compares the summaries:
  - exit 0 means identical, and the temporary run is deleted;
  - exit 1 means differences, which are listed, and the run is kept for inspection.

**What a baseline contains:** summaries plus the raw IFC/WexBIM outputs. It records input **file names and hashes only**, no folder paths. It still describes your private models, so keep it private.

## 5. Substituting your own data for the synthetic fixtures

The repository ships synthetic JSON fixtures (`tests/fixtures/synthetic`). They reference names, GlobalIds and entity labels that exist **only** in the synthetic IFC models written by the harness. **They will not work with your models.** To see a complete, working set first:

```powershell
.\tools\IfcComBridge.Cli\bin\x64\Debug\net472\IfcComBridge.Cli.exe synthetic --out D:\scratch\ifc-synthetic
.\scripts\test.ps1 -Config D:\scratch\ifc-synthetic\tests.local.json
```

This writes `building.ifc`, `products.ifc`, the fixtures, edge cases and a `tests.local.json` with relative paths. Every private-data test then runs against stand-in data.

**To use your own models:**
1. **Model scenario:** point `model.ifc` at any IFC4 file. If you add `model.transforms`, its `id` values must be entity labels of **that** file. List them with:

   ```powershell
   IfcComBridge.Cli.exe summary --ifc D:\IFC\model.ifc --out -
   ```

   Each product has a `label`. Send all 16 matrix values; missing values collapse the directions (quirk Q17).
2. **Composition:** use your building and products IFC files together with the products map and layout that belong to **those** models. Every `ifc_guid` or `ifc_name` must exist among the products model's `IfcBeam`/`IfcWallStandardCase` elements, and every layout set `name` must match a products-map key (exactly or by prefix).
3. Validate with `config --config`, then capture a baseline **before** you change the library (section 4).

## 6. Privacy and safety rules the tools enforce

- The configuration, every input, every baseline and every test result stay **outside** the repository. The repository is detected by `IfcComBridge.sln` in a parent folder.
- `.gitignore` also excludes `tests.local.json`, IFC and WexBIM files, and `private/`.
- Only the configuration **path** reaches the test host (`scripts\test.ps1` sets `IFCCOMBRIDGE_TEST_CONFIG` for the run and restores it afterwards). For runs from an IDE you may set that variable yourself.
- Test results are written to `%TEMP%\IfcComBridge.TestResults` by default.
- **Nothing registers COM:**
  - the library project forces `RegisterForComInterop=false`, whatever value is passed to the build;
  - `scripts\build.ps1` also passes `RegisterForComInterop=false` for every project;
  - only `scripts\register-com.ps1` and `scripts\unregister-com.ps1` write registration, and only when run without `-DryRun`, in an elevated PowerShell, after confirmation. `com-smoke-test.ps1` never registers.
