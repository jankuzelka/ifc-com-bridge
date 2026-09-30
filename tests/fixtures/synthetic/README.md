# Synthetic JSON fixtures

These inputs belong to the **synthetic** IFC models written by `SyntheticModels` (in `tools/IfcComBridge.Cli/Synthetic`):

- `building.ifc`: project, site, building, storey "Level 0" and one slab.
- `products.ifc`: `BEAM_A`, `CAT_1000`, `CAT_1200`, `CAT_1500`, `EXTRUDED_B` and `WALL_W`, with GlobalIds `0000000010080000000001` … `…06`.

Every name, GlobalId and entity label in these files exists in those models, and `SyntheticFixtureTests` checks it. The data is invented; the fixtures **do not work with any real model**. For real data see `docs/local-testing.md`.

| File | Purpose |
|---|---|
| `products-map.json` | Single products and a length catalogue. See the "deliberate cases" below. |
| `layout.json` | Groups `G1` (every placement path), `G2-empty` (resolves nothing, so it is skipped: quirk Q6) and `G3` (one product). |
| `transforms.json` | Complete 4×4 matrices for `products.ifc`: #77 `BEAM_A` translated by (100, 200, 0); #151 `CAT_1200` rotated +90° about Z, then moved +1000 in Y. |
| `edge-cases/transforms.partial-matrix.json` | Translation-only matrix: the directions collapse to (0, 0, 0) (quirk Q17). |
| `edge-cases/transforms.skipped-entries.json` | Entries without `id` or without `transformation`, an unknown label, and a non-product label (#9, `IfcProject`). `UpdateModel` returns false and nothing changes. |
| `edge-cases/transforms.missing-modified-products.json` | No `modified_products`: `NullReferenceException` (quirk Q5). |
| `edge-cases/layout.missing-rotation.json` | Group `OK` is placed. The set in group `BROKEN` has no `rotation`, so it throws, while the `OK` model stays loaded (quirks Q5, Q10). |

**Deliberate cases in the products map:**
- **`EXT`:** its GlobalId is unknown, so it resolves through `ifc_name`.
- **`GHOST`:** resolves to nothing.
- **`CAT-11`:** never reached, because the prefix lookup takes the first matching key, `CAT` (quirk Q8).

`IfcComBridge.Cli synthetic --out <dir>` writes both IFC models, these files and a ready-to-use `tests.local.json` into a folder outside the repository.

**Maintenance rules:**
- The files are embedded in the CLI assembly, so **file names must be unique** across sub-folders.
- The tests' golden summaries depend on `products-map.json` and `layout.json`. Change them only together with the goldens (see `docs/characterization-harness.md`).
