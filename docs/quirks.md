# Quirks Q1–Q19

The library keeps some surprising behaviour, on purpose. COM clients may already depend on it, so changing any of it is a behaviour change. Such a change needs a decision of its own, new characterization tests and a new baseline; it does not belong in an unrelated change. None of the quirks below has been fixed.

Each quirk is tagged `Qn` in the source comments where it happens. The status column says how well it is established:

- **Tested:** a characterization test in `tests/IfcComBridge.Tests` pins the behaviour. The test is named.
- **Observed:** seen in golden summaries or regression outputs, without a dedicated test.
- **Code reading:** it follows from the code; nothing executes it on purpose.

Host binding redirects are a separate, deployment-level problem: [known-issues.md](known-issues.md), KI-1. The input rules the quirks refer to are in [input-contracts.md](input-contracts.md).

## Summary

| | Behaviour | Where | Status |
|---|---|---|---|
| Q1 | Single-object products are shifted by 1 along their local X | `ProductLayoutComposer` | Tested |
| Q2 | `throw ex` resets the stack trace of composition errors | `ProductLayoutComposer` | Tested |
| Q3 | The output uses the products model's units | `ProductRelationsCopier` | Tested |
| Q4 | The width/length swap persists across catalogue entries | `ProductSelector` | Tested |
| Q5 | A missing `rotation` or `modified_products` throws `NullReferenceException` | `ProductSelector`, `ProductCopier`, `PlacementUpdater` | Tested |
| Q6 | Groups without a placed product yield no model: indices are not group positions | `ProductLayoutComposer` | Tested |
| Q7 | `UpdateModel` needs Axis and RefDirection and moves shared points and directions | `PlacementUpdater`, `ProductCopier` | Tested |
| Q8 | The prefix lookup takes the first matching key in document order | `ProductLayoutComposer` | Tested |
| Q9 | Static state is shared by the whole process | `LoggingSetup`, `EditorIdentity`, `ProductLayoutComposer`, `ModelFiles` | Code reading |
| Q10 | A failed `LoadIfcJson` keeps the models composed before the failure | `ComRuntime`, `ProductLayoutComposer` | Tested |
| Q11 | A schema other than IFC4 throws `FileNotFoundException` | `ModelFiles` | Tested |
| Q12 | A duplicate GlobalId among the products throws `ArgumentException` | `ProductLayoutComposer` | Tested |
| Q13 | Repeated copies of one product share its GlobalId | `ProductCopier` | Tested |
| Q14 | Extruded profiles are processed once per vertex: quadratic work | `ProductCopier` | Code reading |
| Q15 | Composition always merges duplicate points (optimization Size) | `ProductLayoutComposer`, `BrepPointOptimizer` | Tested |
| Q16 | Saving deletes an existing target file before writing | `ModelFiles` | Tested |
| Q17 | A partial matrix collapses Axis and RefDirection | `PlacementUpdater` | Tested |
| Q18 | WexBIM files of identical runs can differ in region order | xBIM (`SaveAsWexBim`) | Observed |
| Q19 | Composed models contain several equal representation contexts | `ProductRelationsCopier`, `ProductCopier` | Observed |

## Details

### Q1: single-object products are shifted by 1

`LoadIfcJson` starts every set with `centerOffset = 1`. Only catalogue selection replaces it, with 0 or the clip offset. A product-map entry that is a single object therefore keeps the 1, and its copy is moved 1 unit (of the products model) along the copy's local X. It looks unintended, but that is not established.

Test: `ComposeTests.Copies_ArePlacedFromTranslateRotationAndCatalogueOffsets` (the data row marked Q1).

### Q2: the stack trace of composition errors is reset

`LoadIfcJson` catches every exception thrown while a group is composed, disposes that group's unfinished model and rethrows with `throw ex`. The stack trace then starts in `ProductLayoutComposer`, and the frames of the method that failed, such as `ProductCopier.CopyProduct`, are lost. The exception type and message are unchanged.

Test: `ComposeTests.Q10_Q2_RuntimeKeepsModelsComposedBeforeAFailure_AndTheStackTraceIsReset`.

### Q3: the output uses the products model's units

`ProductRelationsCopier` removes the unit assignment in use. Its condition, `!Any(c => c.UnitsInContext != item)`, deletes a unit assignment unless some context uses a different one; with the building's single `IfcProject`, that is the building's own assignment. It reads like an inverted "used by no context", but whether that is intended is not established. A later step then assigns a copy of the products project's units.

No coordinate is converted. The building's numbers are kept and read in the products model's units, so **both models should use the same length unit**. The JSON lengths are in that unit too.

Test: `ComposeTests.Q3_OutputUsesTheProductModelsUnits`.

### Q4: the width/length swap persists

When a catalogue entry's `width` differs from the measured width by more than 20, the selector swaps width and length, including the clipped values and the clip offsets. The swap is not undone, so the following entries are measured swapped as well.

Test: `ProductSelectionTests.Q4_SwapPersistsAcrossCandidates`.

### Q5: missing `rotation` or `modified_products`

- A layout set without a `rotation` object throws `NullReferenceException` when a product is copied or a catalogue is evaluated. A missing `rotation.angle` is fine: it defaults to 360.
- A transforms file without `modified_products` throws `NullReferenceException`.

Tests: `ProductSelectionTests.Q5_MissingRotation_ThrowsNullReference`, `PlacementTests.Q5_UpdateIfcPosition_WithoutModifiedProducts_ThrowsNullReference`, `SyntheticFixtureTests.EdgeCase_Q5_MissingModifiedProducts_ThrowsNullReference`, and the composition case in `ComposeTests.Q10_Q2_…`.

### Q6: model indices are not group positions

A layout group in which no set produces a product yields no model, and later groups move up one index. `LoadIfcJson` returns the number of models, not the number of groups.

Workaround: map indices to groups with `GetModelGroupId(index)`.

Test: `ComposeTests.Q6_GroupsWithoutAnyPlacedProduct_AreSkipped`.

### Q7: `UpdateModel` edits shared entities and needs complete placements

- The placement's `IfcCartesianPoint` and `IfcDirection` entities are edited in place. If an exporter shares one of them between several placements, all of them move.
- A placement without `Axis` or `RefDirection` throws `NullReferenceException`. All entries of the call run in one transaction, so every entry of that call is rolled back.
- Composition has the same requirement: a source product whose placement lacks `Axis` or `RefDirection` makes `LoadIfcJson` throw.

Copies made by `LoadIfcJson` get placement entities of their own, so moving one copy does not move another. See also observation O-1 below.

Tests: `PlacementTests.Q7_MissingAxis_ThrowsNullReference`, `PlacementTests.Q7_SharedPointEntity_MovesEveryPlacementUsingIt`.

### Q8: the first prefix key wins

When no products-map key equals the set name, the first key in document order that the name starts with wins, even if a longer key also matches. `String.StartsWith(string)` compares case-sensitively, with the current culture.

Test: `ComposeTests.Q8_PrefixFallback_TakesTheFirstMatchingKey`.

### Q9: process-wide static state

Shared by every `ComRuntime` instance and thread in the process:

- the Serilog logger and xBIM's logger factory, configured by the first instance (`LoggingSetup`). The check that decides whether to configure is not synchronized;
- the editor credentials (`EditorIdentity.Shared`);
- `ProductLayoutComposer.CurrentOptimization`, which nothing sets;
- xBIM's model provider factory, set on every load (`ModelFiles.LoadIfcModel`).

The class is registered with `ThreadingModel` Both, but it takes no locks. Do not call one instance from several threads at once. Concurrent use of several instances is not characterized.

Status: code reading; no test.

### Q10: a failed `LoadIfcJson` keeps earlier models

`LoadIfcJson` disposes the old models, then composes group by group into the runtime's list. When a group throws, the models of the groups before it stay loaded, although the call failed and returned no count.

Workaround: after a failed call, load again, or release the object and create a new one. Probing `GetModelGroupId(i)` until it throws shows what is still loaded.

Test: `ComposeTests.Q10_Q2_RuntimeKeepsModelsComposedBeforeAFailure_AndTheStackTraceIsReset`.

### Q11: a wrong schema throws `FileNotFoundException`

A file that is not IFC4 throws `FileNotFoundException("Invalid schema version. Only IFC4 is supported.")`, HRESULT `0x80070002`, the same as a missing file. Clients may depend on that HRESULT.

Tests: `FileValidationTests.Q11_LoadIfcModel_Ifc2x3_ThrowsFileNotFound`, `RuntimeTests.Q11_LoadIfc_Ifc2x3_ThrowsFileNotFound`.

### Q12: a duplicate GlobalId among the products throws

The product candidates (`IfcBeam` and `IfcWallStandardCase`) are indexed by GlobalId with `Dictionary.Add`, so a duplicate throws `ArgumentException` before any group is composed.

Test: `ComposeTests.Q12_DuplicateGlobalIdInProducts_ThrowsArgumentException`.

### Q13: repeated copies share a GlobalId

`InsertCopy` copies the GlobalId unchanged. Every copy of one source product in a composed model has the same GlobalId, which breaks the IFC rule that GlobalIds are unique. Viewers and tools that key by GlobalId may merge or drop these copies.

Test: `ComposeTests.Q13_RepeatedCopiesOfOneProduct_ShareItsGlobalId`. The golden summary of group G1 records the duplicate.

### Q14: quadratic vertex loop for extruded profiles

For an `IfcExtrudedAreaSolid` with a polyline profile, `UpdateVertexWidth` runs once per vertex, each time over the whole point list. A processed-point set makes the repeats no-ops, so the result is correct but the work grows with the square of the number of profile points.

Status: code reading; a performance issue only. The result is covered by `ComposeTests.Geometry_IsStretchedAlongLocalXOnly`.

### Q15: duplicate points are always merged

`ProductLayoutComposer.CurrentOptimization` defaults to `Size`, and neither COM nor the library changes it. After composition, `BrepPointOptimizer` therefore redirects poly-loops and polylines to one point per coordinate triple and deletes the duplicates. COM clients cannot switch it off.

Test: `ComposeTests.Q15_DefaultOptimizationIsSize_WhichMergesDuplicatePoints`.

### Q16: saving deletes the target first

`SaveIfc` and `SaveWexbim` delete an existing file before they write. If writing then fails, the old file is gone. `SaveWexbim` deletes it before it generates the geometry, so a failure there also loses the old file.

Test: `RuntimeTests.Q16_SaveIfc_ReplacesAnExistingFile`, for the replacement. The loss on failure is code reading.

### Q17: a partial matrix collapses the directions

`UpdateModel` transforms the location with the full matrix, where a missing value defaults to the identity. The directions use only the rotation part, where a missing value defaults to 0. A translation-only matrix therefore sets `Axis` and `RefDirection` to (0, 0, 0). The transformed directions are not normalized.

Workaround: always send all 16 values.

Tests: `PlacementTests.Q17_PartialMatrix_DefaultsDifferForLocationAndDirections`, `SyntheticFixtureTests.EdgeCase_Q17_PartialMatrix_CollapsesTheDirections`.

### Q18: WexBIM region order is not deterministic

xBIM writes regions of equal population in no fixed order, so two runs of identical code on the same model can produce different WexBIM bytes. This is xBIM's behaviour, not code of this library.

The harness compensates: `WexbimHeaderReader` reports regions in a canonical order, and summaries contain no WexBIM file hash.

Status: observed in repeated baseline runs.

### Q19: several equal representation contexts

A composed model holds:
- the building's representation contexts;
- copies of the products project's contexts, which are added to the project;
- further copies made through the per-length-scale product maps. The copied representations reference these, but the project does not list them.

The synthetic group G1 has six `Model` contexts, two of them attached to the project.

Status: observed; the golden summary `compose-G1.summary.json` records it (`ComposeTests.GoldenSummaries_OfSyntheticComposition`).

## Other observations

These came up while documenting the code. They are not numbered quirks, and nothing was changed.

| | Observation | Status |
|---|---|---|
| O-1 | `BrepPointOptimizer` keeps, for each coordinate triple, the point enumerated last. It redirects only poly-loop and polyline references to that point, so the kept point can also be used elsewhere. In the synthetic G1 output, B-rep vertices share their point with the `Location` of an extruded solid's `Position`. If a **product placement's** `Location` were the kept point, `UpdateModel` on that product would move those vertices too (Q7). | Observed for the extrusion position; the product-placement case is theoretical and not characterized |
| O-2 | When `LoadIfcModel` rejects a schema (Q11), the model it opened for the check is not disposed; the garbage collector reclaims it. | Code reading |
| O-3 | If the building has no `IfcRelContainedInSpatialStructure`, the copies go into a new relation to the first storey. If there is no storey either, a bare `IfcBuildingStorey` is created, which nothing aggregates into the building. xBIM may give it an owner history made from the library's editor identity. | Code reading; not characterized |
| O-4 | The log calls every product candidate a beam, walls included. | Code reading |
