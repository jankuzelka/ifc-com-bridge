# Input contracts

This document describes the inputs **exactly as the current code handles them**. It is based on a
reading of the library code in `src/IfcComBridge`: `LoadIfcJson`, `FindMostSuitableObjectToDimensions`,
`CopyProduct`, `CreateProductsRelations`, `UpdateIfcPosition` and `SetIfcPosition`.

- "(tested)" marks rules that the characterization tests in `tests/IfcComBridge.Tests` pin down.
- **"Not established"** marks questions the source code does not answer. Nothing here is inferred
  from field names alone.
- Q-numbers refer to the known quirks, which are kept unchanged on purpose. [quirks.md](quirks.md) lists them with their tests.

All examples come from the synthetic fixtures in `tests/fixtures/synthetic`.

## 0. How the inputs reach the library

| Input | COM method (runtime class) | `tests.local.json` / CLI | Form |
|---|---|---|---|
| building IFC | `LoadIfcJson(fileIfcBuilding, …)` | `composition.building`, `--building` | file path |
| products IFC | `LoadIfcJson(…, fileIfcProducts, …)` | `composition.products`, `--products` | file path |
| products map | `LoadIfcJson(…, productsMapJson, …)` | `composition.productsMap`, `--products-map` | see below |
| layout | `LoadIfcJson(…, fileJson)` | `composition.layout`, `--layout` | file path |
| single model | `LoadIfc(ifcFile)` | `model.ifc`, `--ifc` | file path |
| transforms | `UpdateModel(jsonParametersFile, modelIndex)` | `model.transforms`, `--transforms` | file path |

**The products map is the one input with two forms.**

- **COM:** `LoadIfcJson` expects it as **inline JSON text** (a string), not as a path. It is parsed with `JObject.Parse(productsMapJson)`.
  - The method **disposes all currently loaded models before parsing**. Malformed text or a non-object root therefore throws and leaves the runtime with no models.
- **CLI and `tests.local.json`:** the products map is a **file path**. The harness then uses one of the library's two entry points:
  - the runtime API (`compose --api runtime`, baseline step `compose/runtime`) reads the file and passes its **text**, exactly as a COM client would;
  - the static helper (`--api utils`, step `compose/utils`) parses the file with `ModelFiles.LoadJson` and casts it to an object.

**Other interface details:**
- The optional missing-items log (`aJsonItemNotFoundLogger`) exists only on the internal helper `ProductLayoutComposer.LoadIfcJson`; COM clients cannot reach it.
- In the class, `UpdateModel`'s parameter is named `jsonParameters`, but it is a **file path**, read with `LoadJson` (the interface names it `jsonParametersFile`).

**JSON parsing:** all JSON is parsed by Newtonsoft.Json (`JToken.Parse` / `JObject.Parse`, default settings). Whether non-standard JSON such as comments is accepted is Newtonsoft's behaviour and **not relied upon** here.

**Units:**
- The JSON code **never reads or converts units**. Every length in the JSON is used as a raw number together with coordinates of the **products model**, and the output model takes the products model's units (Q3, tested).
- **Not established:** which unit that is. The synthetic fixtures use millimetres.
- Angles are degrees.

## 1. Building IFC

- **IFC4 only.** Anything else throws `FileNotFoundException("Invalid schema version. Only IFC4 is supported.")` (Q11, tested).
- **Opened in memory:** `IfcStore.Open(file, editor, -1)`; `-1` means an in-memory model for every IFC file.
- **Copied into every output model:**
  - **every instance** is copied, with owner histories stripped (`CreateCopyOfBuilding`);
  - at least one instance is required, otherwise it throws `Exception("No IFC instances")`.
- **Spatial containment:** all placed products are added to the **first** `IfcRelContainedInSpatialStructure` of the output, whichever storey that relates to.
  - If there is none, a new relation to the first `IfcBuildingStorey` is created (and a storey too, if needed).
- **Units:** the building's units are replaced by the products model's units (Q3, tested).
- **Representation contexts:** the products model's contexts are added (Q19, tested via golden summary).
- **Owner histories:** they are stripped while copying, and the code then deletes every owner history that no `IfcRoot` references; in the tested cases none remain. The person, organization and application entities copied from the building remain, unreferenced (tested).

## 2. Products IFC

- **IFC4 only.**
- **Candidates:** every `IfcBeam` (including subtypes) and every `IfcWallStandardCase`.
  - Other element types, including an `IfcWall` that is not `IfcWallStandardCase`, are never candidates.
  - At least one candidate is required, otherwise it throws `Exception("No beams in the products")`.
- **GlobalIds** of candidates must be unique, otherwise `ArgumentException` (Q12, tested).
- **Names:** duplicates are allowed. The name lookup keeps the **last** candidate with a given name, in enumeration order: beams first, then walls, each in xBIM instance order.
- **Placement:** to be re-placed, a candidate needs `ObjectPlacement = IfcLocalPlacement` whose `RelativePlacement` is an `IfcAxis2Placement3D` with both `Axis` and `RefDirection` set.
  - A null `Axis` or `RefDirection` throws `NullReferenceException`.
  - With any other placement type, the copy silently keeps its original placement.
  - `PlacementRelTo` is copied along. Copies are **not** re-parented under the building storey.
- **Length scaling** is applied to two geometry types only:
  - `IfcFacetedBrep` poly-loops;
  - `IfcExtrudedAreaSolid` with an `IfcArbitraryClosedProfileDef` whose outer curve is an `IfcPolyline`.

  Scaling acts along the product's **local X** axis, about x = `center_x` (default 0), and only on the first copy per source product and rounded scale (tested).
  - **Not established:** that the product's length really runs along local X. The code simply assumes it.
- **Relations copied for placed products:**
  - `IfcRelDefinesByType`, `IfcRelDefinesByProperties`, `IfcRelAssociatesClassification`;
  - `IfcRelAssociatesMaterial`, plus the material definition representations and material properties of an `IfcMaterial` or of the layers of an `IfcMaterialLayerSetUsage`;
  - `IfcPresentationLayerAssignment`;
  - `IfcStyledItem`, but **only** for faceted B-reps. Copying B-rep styles is tested; that other geometry types lose their styles follows from the code (only `IfcFacetedBrep.StyledByItem` is collected).

  Copying of these relations is tested.

## 3. Products map (JSON object)

```json
{
  "BEAM&A": { "ifc_guid": "0000000010080000000001", "ifc_name": "BEAM_A" },
  "CAT": [
    { "ifc_guid": "0000000010080000000002", "ifc_name": "CAT_1000", "width": 100, "length": 1000, "length_upscale": 0 },
    { "ifc_guid": "0000000010080000000004", "ifc_name": "CAT_1500", "width": 100, "length": 1500, "length_upscale": 1 }
  ],
  "EXT": { "ifc_guid": "0000000000000000000000", "ifc_name": "EXTRUDED_B", "rotation": 90, "center_x": 400, "center_y": 50, "center_z": 25 }
}
```

**The root must be an object.**

**Key lookup** is done with the layout set's **HTML-decoded** `name`:
1. an exact key (ordinal, case-sensitive: `JObject.TryGetValue`);
2. otherwise the **first key in document order** for which `name.StartsWith(key)` holds (Q8, tested).
   - `String.StartsWith(string)` compares case-sensitively using the **current culture**.
   - An empty key `""` would match every name that reaches it.
3. otherwise the set is skipped. The log says "Requested JSON item not found" (tested).

**Values** are either an object (a single product) or an array (a catalogue of variants). Any other value type throws when the library indexes it.

**Product parameters** (the object itself, or the chosen catalogue entry):

| Field | Type | Required | How the code uses it |
|---|---|---|---|
| `ifc_guid` | string | **yes**: read with `.ToString()`, missing → `NullReferenceException` | exact match against candidate GlobalIds (22-character IFC form) |
| `ifc_name` | string | only when `ifc_guid` matches no candidate; missing then → `NullReferenceException` | exact match against candidate `Name` |
| `rotation` | number, degrees | no | added to the set's angle |
| `center_x` | number, length | no | placement offset (see §4); also the pivot of length scaling |
| `center_y` | number, length | no | placement offset |
| `center_z` | number, length | no | placement Z = −`center_z` |
| anything else | – | – | ignored |

If neither the GlobalId nor the name resolves, the set is skipped ("Requested IFC item not found", tested).

**Catalogue entries** must be objects. A non-object element throws `InvalidCastException`.

| Field | Type | Required | How the code uses it |
|---|---|---|---|
| `width` | number | yes, for every entry the scan reaches | compared with the measured width, tolerance 20 |
| `length` | number | yes | compared with the required length |
| `length_upscale` | integer | only on the **last** entry, and only when no entry fits | `0` = not upscalable, anything else = upscalable |
| product parameters above | | | the chosen entry is the product's parameters |

- **Order:** entries are scanned in array order and the first fit wins, so the scan effectively expects ascending lengths. **Not enforced.**
- **Empty array:** nothing fits and there is no last entry, so the set is skipped ("IFC object with suitable length not found").

**Selection algorithm** (tested in `ProductSelectionTests`):
1. `angle` = the set's `rotation.angle`, default **360**.
2. Every `vertices` point (and every `clipvertices` point) is rotated by **+angle**: counter-clockwise, in degrees, about the origin.
3. `w` is the X extent and `h` the Y extent of the rotated points.
4. **Clipping** (only if `clipvertices` is non-empty): the extents shrink to the clip box wherever it lies inside them.
   - The clip offset is half the shrink, signed: −(max − clipMax)/2 at the top/right, +(clipMin − min)/2 at the bottom/left.
   - This yields `clippedW` and `clippedH`.
5. **Scan** the entries in order. For each entry:
   - if |`width` − `w`| > 20, swap `w`↔`h`, `clippedW`↔`clippedH` and the two clip offsets (the swap persists for later entries, Q4);
   - if `length` + 1 ≥ `h`, choose the entry. If additionally `length` > `clippedH` + 5, then scale = `clippedH` / `length` and offset = the Y clip offset; otherwise scale 1, offset 0.
6. **If nothing was chosen**, take the last entry:
   - with `length_upscale` = 0, reject it if `length` + 5 < `clippedH`, otherwise use it unscaled;
   - with any other `length_upscale`, scale = `clippedH` / `length` and offset = the Y clip offset.

Note the split: the **unclipped** `h` decides whether an entry fits, while the **clipped** height decides the scale.

## 4. Layout (JSON array of groups)

```json
[
  {
    "group_id": "G1",
    "sets": [
      { "set_id": "S1", "id": "1", "name": "BEAM&amp;A", "rotation": { "angle": 0 },
        "vertices": [[0, 0], [1000, 0], [1000, 100], [0, 100]], "translate": { "x": 500, "y": 0 } }
    ]
  }
]
```

**The root must be an array.** Its elements are processed in order as groups. `LoadIfcJson` is lazy, so the layout is read when enumeration starts.

**Group object:**

| Field | Type | Required | How the code uses it |
|---|---|---|---|
| `group_id` | any JSON value (a string is typical) | read only for groups that produced at least one product; missing then → `NullReferenceException` | the output model's group id (`GetModelGroupId`), via `.ToString()` |
| `sets` | array | **yes** (missing → `NullReferenceException`) | |

A group in which no set yields a product produces **no** output model, so output indices are not group indices (Q6, tested).

**Set object:**

| Field | Type | Required | How the code uses it |
|---|---|---|---|
| `set_id` | any JSON value | **yes** (`.ToString()`) | description text and log only |
| `id` | any JSON value | **yes** (`.ToString()`) | description text and log only |
| `name` | string | **yes** | HTML-decoded (`WebUtility.HtmlDecode`) and used for the products-map lookup; appended to the copy's name |
| `rotation` | object | **yes** whenever a product is copied or a catalogue is evaluated (missing → `NullReferenceException`, Q5, tested) | |
| `rotation.angle` | number, degrees | no, default **360** | |
| `vertices` | array of `[x, y]` | only for catalogue products (missing → `NullReferenceException`) | rectangle measured after rotation; extra coordinates ignored. An empty array is **not characterized**. |
| `clipvertices` | array of `[x, y]` | no; catalogue products only | clipping (see §3) |
| `translate` | object `{ "x": n, "y": n }` | no, default (0, 0) | if present, **both** `x` and `y` are required (missing → `NullReferenceException`); a non-object value is ignored |
| anything else | – | – | ignored |

**Output for each placed set** (tested):
- `Name` = `"<product name> [<decoded set name>]"`
- `Description` = `"JSON [ID: <id>, SET_ID: <set_id>]"`

**Missing-items log** (static helper only), one line per skipped set:

```text
Set_id "<set_id>" - id "<id>" - name "<name>": Requested JSON item not found
Set_id "<set_id>" - id "<id>" - name "<name>": IFC object with suitable length not found
Set_id "<set_id>" - id "<id>" - name "<name>": Requested IFC item not found, ifc_guid: "<guid>", ifc_name: "<name>"
```

**Placement of a copy** (`CopyProduct`, tested in `ComposeTests`):

```text
angle    = set.rotation.angle (default 360) + parameters.rotation (default 0)
base     = (translate.x, translate.y, -center_z)
v        = (-center_x + c, -center_y)
           c = 1 for a single-object entry (Q1); c = the clip offset for a catalogue entry
r        = v rotated by -angle (counter-clockwise rotation by -angle)
Location     = (base.x + r.x, base.y + r.y, base.z)
Axis         = (0, 0, 1)
RefDirection = (cos(-angle), sin(-angle), 0)
```

- These values are written into the copy's `IfcAxis2Placement3D`, i.e. relative to the `PlacementRelTo` inherited from the products model.
- Selection rotates the rectangle by **+angle**, while the placement uses **−angle**. **Not established:** which rotation convention the upstream client uses.
- **Length scaling:** local X is stretched about x = `center_x` by the selected scale, for the first copy per source product and ⌊scale × 1000⌋. Later copies at the same rounded scale share the geometry items (tested). Repeated copies of one product keep its GlobalId (Q13, tested).

## 5. Transforms (JSON object, for `UpdateModel`)

```json
{
  "modified_products": [
    { "id": 151,
      "transformation": { "0": 0, "1": 1, "2": 0, "3": 0, "4": -1, "5": 0, "6": 0, "7": 0,
                          "8": 0, "9": 0, "10": 1, "11": 0, "12": 0, "13": 1000, "14": 0, "15": 1 } }
  ]
}
```

**The root must be an object with a `modified_products` array** (missing → `NullReferenceException`, Q5, tested).

**Entries:**

| Field | Type | Required | How the code uses it |
|---|---|---|---|
| `id` | integer | no: an entry without it is skipped | **STEP entity label** (`#id`) of a product in the model the call targets |
| `transformation` | object with keys `"0"`…`"15"` | no: an entry without it is skipped | 4×4 matrix, see below |

- **Where `id` comes from:**
  - after `LoadIfc`, it is the file's own labels;
  - for a composed model, it is the in-memory labels, which a saved copy preserves (tested).
- An `id` that is not an `IfcProduct` with `IfcLocalPlacement` + `IfcAxis2Placement3D` is skipped silently. This includes unknown labels (tested).
- The call returns `true` when at least one entry was applied (tested).
- **All entries of one call run in one transaction.** An exception, for example a placement without `Axis`/`RefDirection` (Q7), rolls back every entry of that call.

**Matrix layout:**
- The values `"0"`…`"15"` fill `XbimMatrix3D(M11, M12, M13, M14, M21, …, M44)` in that order. The code comment says "4x4 matrix ordered by columns".
- Points transform as row vectors (p′ = p·M), so the **translation is `"12"`, `"13"`, `"14"`** (tested). This equals a column-major 4×4 in the column-vector convention, as used by WebGL/three.js.
- **Not established:** which system produces the matrices upstream.

**How each part of the placement is transformed:**
- **Location:** by the full matrix. Missing keys default to the identity (`"0"`, `"5"`, `"10"`, `"15"` → 1, all others → 0).
- **Axis and RefDirection:** by the rotation part only (`"0"`–`"2"`, `"4"`–`"6"`, `"8"`–`"10"`). Missing keys default to **0**, so a translation-only matrix collapses both directions to (0, 0, 0) (Q17, tested).
  - The directions are **not** normalized or orthogonalized.
  - **Send all 16 values.**
- **Frame:** the placement's own values are transformed, i.e. **in the frame of `PlacementRelTo`**, not world coordinates.
- **Shared entities:** an `IfcCartesianPoint` or `IfcDirection` shared by several placements changes for all of them (Q7, tested). In composed models, merged B-rep points can share an entity with other uses of the same coordinates (quirks.md, O-1).
- **Not established:** projective behaviour (`"3"`, `"7"`, `"11"` ≠ 0). Use `0, 0, 0` and `"15": 1`.

## 6. How the inputs relate

```text
layout set.name ──(HTML-decode; exact key, else first prefix key)──► products-map entry
products-map entry ──(ifc_guid, else ifc_name)──► candidate in products IFC (IfcBeam / IfcWallStandardCase)
building IFC ──(copied entirely)──► every output model
layout group ──(≥ 1 placed product)──► one output model, id = group_id
transforms id ──(entity label)──► product in the model given by modelIndex
```

## 7. What the source does not establish

- The length unit of the JSON values. It is whatever the products model uses; nothing converts.
- The rotation convention of the upstream client (selection uses +angle, placement uses −angle).
- That catalogue entries are sorted by ascending length (the scan relies on it; nothing checks it).
- Whether `id`, `set_id` and `group_id` should be strings or numbers. Both work via `.ToString()`.
- Numbers given as strings (e.g. `"100"`): Newtonsoft's conversion is not characterized.
- Empty `vertices` arrays and projective matrices.
- That the products model's placements are meaningful relative to the building. `PlacementRelTo` is inherited, not re-parented.
