# Incremental XML Cache + Faster Game Loading — Working Notes

_Last updated: 2026-06-20. Living doc — iterate freely._

Context: we're making Missile Girl / Gagarin's all-or-nothing `Unified.xml` cache
**incremental** via a persisted dependency graph, so a single mod change recomputes only
affected defs instead of forcing a ~1-hour full rebuild. Prototype split into Pieces A–C
(each a PR on the `Jeffrharr/MissileGirl` fork); D–E deferred until A's in-game capture +
C's offline proof check out. Separately, we want to contribute **safe** optimizations to
Taranchuk's Faster Game Loading (FGL).

Current PR state on the fork: #1 build fix, #2 Piece B (classification), #3 Piece A
(capture), #4 Piece C (replay harness, 10/10 zero-diff, ~7–8x on the common path). All
four have file-level description headers. Piece A capture build is deployed for an in-game
cold-load test (flag `GagarinPrefs.CaptureProvenance`, writes `DependencyGraph.json` to the
MissileGirl cache folder).

---

## Pieces D & E (deferred — flesh out as we go)

### Decision (2026-06-20): Piece C is a validated POC, NOT merged
C proved the dirty-set algorithm (10/10 zero-diff) and ~7–8× speedup, but against synthetic
fixtures + a *minimal fake apply model* — not RimWorld's real PatchOperation engine. So it is
not production code and PR #4 should not be merged (keep the branch as a reference). What D
inherits from C (branch `feat/replay-harness`):
- **`DirtySet.cs`** — the reorder/add/remove-aware dirty-set algorithm; this is D's recompute core.
- **The change-case matrix** (`Program.cs`/`Harness.cs`: update / reorder / add / remove / hazard)
  — becomes D/E's integration-test spec, re-pointed at the real engine for true zero-diff-vs-rebuild.
- Maybe `Contracts.cs` (graph/classification DTOs).
Throwaway: `ApplyModel.cs` (fake apply), the synthetic fixture builders, `Json.cs` (dup).

### Piece D — Persist + in-place mutate

**M1 (dirty-set diagnostic) — IMPLEMENTED, PR #6 (`feat/incremental-dirtyset`).** Pure
`DirtySetComputer` + `DependencyGraphModel` (read side + JSON parser) + `DirtySetDiagnostic`
driver (flag `GagarinPrefs.DirtySetDiagnostic`, default OFF). Computes, on a real changed
load, which defs would need recompute → `DirtySet.json`, alongside the normal rebuild (zero
risk). 33/33 unit tests (C's matrix on the real computer); offline-validated on the real
6.5MB graph. The change→node join is trivial: hash key = `FullFilePath` = node `SourceFile`.
M1 = STRUCTURAL set only (changed defs / changed-mod patches / reorder / inheritance closure);
precise wildcard-flip hazard deferred to M2 (needs def bodies). **IN-GAME VALIDATED 2026-06-20**
(134-mod list, 28,691 nodes): removed a leaf mod (`scherub.stonecuttingextended`) →
changedAssets=14, dirty=**8 (0.03%)**, ALL 8 attributed to the removed mod, zero over-dirtying.
Change detection + computer work end-to-end on real data. Caveats: closure didn't fire here
(concrete defs directly seeded → inheritanceAdded=0; closure only unit-tested); best-case change
(leaf removal, no wildcard/cross-mod fan-out); computeMs=649 (dominated by parsing the 6.5MB
JSON graph → binary/columnar format worth it before the production path); abstract nodes have
null sourceFile so abstract-base file changes aren't seen by the join (known precision gap).
**The 0.03% dirty ratio is the M2 go signal — recompute 8 defs vs rebuilding 28,691.**
Artifacts: `MissileGirl-metrics/DirtySet-remove-stonecutting-*.json`, `134mods-*-D-seed.json`.
Key fact confirmed: incremental only speeds the patch/combine phase; `ParseAndProcessXML`
(building ~16k Def objects) runs every load regardless — that bounds D's prize.

**M2a (superset-safe dirty set) — IMPLEMENTED on `feat/wildcard-rematch` (off `feat/incremental-dirtyset`).**
Closes the wildcard-flip hazard M1 left open: a *changed* mod's patch predicate can newly match
an otherwise-unchanged def, and none of M1's structural seeds (changed def bodies / a changed
mod's OLD matched nodes / reorder) reach it — a subset error a later recompute would silently
honour. Fix = re-evaluate changed mods' **CURRENT** patch predicates against the *current raw def
bodies* (read from `Context.XmlAssets`, which `LoadModXML` populates pre-patch even on a cache
hit — no disk read) and seed any NEW match (current matches minus the patch's baseline
`matchedNodeIds`, paired by stable patch id). **Key correctness point caught during prep:** the
widened predicate lives ONLY in the current patch op, not the baseline graph (the graph stores the
OLD xpath, captured at apply time). So the driver reflects each changed mod's live PatchOperation
`xpath` field (same id scheme as the capture, via `ProvenanceRecorder.GetChildPatches` +
`PatchIdWalker`) — re-testing the graph's stored xpath would be blind to exactly the change M2a
exists to catch (and the PerformanceSearch widen-test would read 0).
Folded into `DirtySetComputer` as **Seed 4** *before* the inheritance closure, so a newly-matched
abstract base still fans out to descendants. Pieces:
- `WildcardRematch.cs` — pure (System.Xml only) `NewlyMatched(graph, changedMods, candidateDoc)`
  + a once-built reusable `<Defs>` candidate doc + xpath-memoized matching. Document-path
  fallbacks (`Defs`, `Defs/…[n]/…` — nodes outside any def) are excluded so they can't produce
  phantom flips.
- `DirtySetComputer.Compute` — new optional `wildcardFlipSeeds` param + `SeedWildcardFlip` count
  (M1 path unchanged: null = pure structural lower bound).
- `DirtySetDiagnostic` — gathers raw def nodes from `Context.XmlAssets`, computes flips, passes
  them as seeds, emits `seedWildcard`/`wildcardFlip` in the log + `DirtySet.json`.
Scope notes: re-tests ALL of a changed mod's edges (identity included — harmless, resolves to
the same baseline def; avoids needing Piece B's classifier here; wildcard-only is a perf
refinement). Direction handled = NEW matches only; OLD-match shrink/removal already covered by
Seed 2; unchanged-mod flips need a changed def body, which is already dirty (Seed 1). Tests:
**43/43** offline (10 new in `WildcardRematchTests.cs`), mod DLL builds clean. Approximation
carried into M2b: candidates are RAW (pre-patch) bodies, so a match that only appears after an
earlier patch mutates a body isn't seen until the real-engine recompute — the true gate stays
the M2b/E zero-diff-vs-rebuild check.

**IN-GAME VALIDATED 2026-06-21** (134-mod list; vehicle = `joof.performancesearch` with a temp
`PatchOperationAdd` probe widened narrow→`Defs/ThingDef[apparel]` between two cold loads). Run B
(widened) logged `M2a wildcard re-test changedMods=1 currentPredicates=1 defNodes=28779 flips=237`
→ `seedWildcard=237`, dirty=**245**/28682 (0.85%) with seedDefs/seedPatch/seedReorder all 0 and
inh=+8. Decisive: (a) flips went **0→237** vs the pre-fix run, (b) 237 **exactly matches the real
engine's** apply count (Run-2 capture had joof#0 matched=237), (c) the 245 dirty is *purely* M2a's
contribution, (d) 8 abstract-base matches (`@HatBase`, `@ArmorHelmetMakeableBase`, …) fanned out
through the inheritance closure (+8) — superset-safety confirmed. computeMs=2503 (re-testing the
wide predicate over 28,779 raw bodies; M1 structural was ~600ms — a columnar/indexed candidate doc
is the obvious M2b-era optimization). Artifacts in `MissileGirl-metrics/`:
`M2a-baseline-narrow-*.json`, `M2a-dirtyset-WIDE-*.json`.

**Two bugs found & fixed during the in-game bring-up:**
1. **Predicate source** — the first cut re-tested the *baseline graph's* xpath, which can't see a
   *widened* predicate (it lives only in the current op). Fixed to reflect the live PatchOperation
   `xpath` field of changed mods (same id scheme as the capture). Without this the widen-test reads 0.
2. **Candidate-body timing** — read raw bodies from `Context.XmlAssets`, but that's filled by a
   *separate* `LoadModXML` postfix whose Harmony order vs ours isn't pinned → empty candidate doc →
   `seedWildcard=0` (the symptom on the first real run). Fixed to take bodies from `LoadModXML`'s
   `__result` directly. A one-line `M2a wildcard re-test` input-count log makes a zero attributable.

Launch note (Steam Deck): the bare `RimWorldLinux` binary under the tool sandbox crashes with a
Boehm-GC SIGSEGV (`GC_mark_from`) ~46s in; launch with the sandbox disabled. Kill with
`pkill -9 -x RimWorldLinux` (NOT `-f` — that matches the launching shell's own command line). Cold
load to menu ≈ 4 min; completion marker in Player.log = `GAGARIN: ... Provenance captured`.

Remaining D milestones: M2b recompute (real PatchOperation engine) + splice; M3/E three-way
cache integration + zero-diff gate.

### Piece D — M2b (real-engine recompute + splice + zero-diff gate)

Cache schema (confirmed from `CachedDefHelper`): `Unified.xml` = `<DefXmlStorage>` → many
`<Item path="src" [resolved="true"]>` each wrapping ONE fully-resolved+post-patch def element.
Only CONCRETE defs are in it (abstract bases never become Def objects → never Registered →
absent), so an item keys by `{DefType}/{defName}`. Splice unit = replace/add/remove `<Item>`s by
def id. Rebuild flow on a cache MISS: `ApplyPatches` prefix `CachedDefHelper.Prepare()` → defs
registered as parsed (`DirectXmlLoader_Patch` → `Register`) → `ParseAndProcessXML` postfix
(Priority.Last) `CachedDefHelper.Save()` writes Unified.xml then `ProvenanceRecorder.Save()`.
A patch-file content change DOES invalidate the cache (`"Patches changed!"` → miss → rebuild),
so the gate/recompute paths actually fire on patch edits. Prior Unified.xml is deleted in the
`LoadModXML` postfix on a miss, so anything needing it must snapshot in a `LoadModXML` prefix.

**Engine APIs (decompiled 2026-06-21):** `LoadedModManager.ApplyPatches(XmlDocument xmlDoc,
Dictionary<XmlNode,LoadableXmlAsset> assetlookup)` just loops `runningMods.SelectMany(rm =>
rm.Patches)` calling `item.Apply(xmlDoc)` — so the real patch engine can be driven over a small
sub-document (non-matching patches are no-ops). `XmlInheritance` (static): `TryRegister(node, mod)`
→ `Resolve()` → `GetResolvedNodeFor(node)` returns the merged `resolvedXmlNode` (child =
parent.resolvedXmlNode.CloneNode(deep) merged with child). **Hazard: XmlInheritance is a GLOBAL
static** (`resolvedNodes`/`unresolvedNodes`/`nodesByName` + `Clear()`); driving it for recompute
during/after the live load would clobber the load's own state. M2b-2b must run it on isolated
state (snapshot+restore, or a point where the live load is provably done with it) — the gate is
what proves we got it right.

**Staged plan (build the safety net before the risky recompute):**
- **M2b-1 — real-engine dirty-set GATE — DONE: committed `49fcb09`, in-game PASS** (nonDirty
  Mismatches=0 over 27,574 concrete defs, dirty=245, on the apparel-probe change; gateMs ~5s).
  Original entry below for the mechanics:
- **M2b-1 mechanics — IMPLEMENTED (offline 48/48; in-game PASS).** New
  `UnifiedCacheDiff.cs` (pure: index a DefXmlStorage by def id; `NonDirtyMismatches` =
  ids NOT in the dirty set whose resolved XML differs between prior cache and rebuild) +
  `DirtySetGate.cs` driver (snapshots prior Unified.xml in a LoadModXML prefix; `Run()` called
  from the `ParseAndProcessXML` postfix right after `Save()` so ordering is deterministic;
  consumes `DirtySetDiagnostic.LastDirtySet`; writes `GateReport.json`, logs
  `Dirty-set gate PASS/FAIL nonDirtyMismatches=N`). Flag `GagarinPrefs.DirtySetGate` (needs
  `DirtySetDiagnostic` on too). This finally closes the "open correctness gate": it proves
  against the REAL engine that the dirty set is a true superset — a non-dirty mismatch = the
  silent-staleness subset error. **Expected PASS (0 mismatches) on the apparel-probe change**;
  a FAIL would expose a real M1/M2a gap. Reuses the validated in-game harness; no recompute yet.
- **M2b-2a — splice (pure) — DONE: committed `3a57b7e`, offline 52/52.** `UnifiedCacheSplice.cs`:
  given the baseline DefXmlStorage + recomputed defs (by id) + removed ids, produces the new cache
  (replace/add/remove `<Item>`s, reuse the rest verbatim). Round-trip test proves splicing the
  rebuild's values for the dirty defs reproduces the full rebuild byte-for-byte.
- **M2b-2b (first attempt) — recompute-from-raw in a DIRTY-ONLY sub-doc — DEAD END
  (`dead-end/m2b-2b-subdoc`, 2026-06-21). Superseded by the sibling-expansion fix below.**
  `DefRecompute.cs` (new) builds a `<Defs>` sub-doc
  of the dirty defs + inheritance ancestors (raw bodies from `Context.XmlAssets`), applies every
  running mod's patches via `patch.Apply(subDoc)`, resolves inheritance via `XmlInheritance`
  (Clear/TryRegister/Resolve, restored after), and `Massage`-extracts each resolved node
  (replicates `CachedDefHelper.Save`). Wired through `DirtySetGate.RunRecompute` →
  `UnifiedCacheSplice.Splice` → reparse → diff vs full rebuild over ALL defs. New flag
  `GagarinPrefs.DirtySetRecompute`.
  - **Result on the apparel-probe change:** `recomputed=210`, **`recomputeMismatches=12`**,
    `splicedDefs=27575` vs `rebuildDefs=27574` (one off — `Apparel_LocustCape` present in
    recompute, absent in rebuild). 198/210 defs are byte-perfect; the 12 failures are all
    **MISSING patch-applied values** (e.g. `Apparel_TribalHeaddress` lost
    `<researchPrerequisite>VFET_Tribalwear</researchPrerequisite>`; `Apparel_GravPack` lost its
    whole `CompProperties_ApparelOxygenProvider` block; a `GravForge` `<li>`).
  - **ROOT CAUSE (confirmed via decompile — validated the user's "something is nested" instinct):
    `PatchOperationSequence.ApplyWorker` aborts on the first failed sub-op** — `foreach (op) if
    (!op.Apply(xml)) { lastFailedOperation = op; return false; }`. A `PatchOperationAdd` returns
    **false when its xpath matches nothing.** Mods like **"Progression: Core"**
    (`3079786283/1.6/Patches/Core.xml`) wrap *dozens* of unrelated patches —
    StatDef Beauty, Campfire, Wall, …, and `Apparel_TribalHeaddress` (line 135) — in one
    `PatchOperationFindMod → PatchOperationSequence`. In my dirty-only sub-doc the sibling target
    defs are absent, so the **first** nested op targeting an absent def returns false → the whole
    sequence aborts → every later op (incl. the apparel `researchPrerequisite`/comp adds) **never
    runs**. That is exactly the 12 lost values.
  - **Why this is fundamental, not a bug:** patch application in RimWorld is inherently
    document-global, and all-or-nothing `PatchOperationSequence` bundles spanning many unrelated
    defs are common (progression mods, compatibility patches, VEF). **You cannot faithfully
    recompute a def in an isolated sub-doc when its patch is bundled in a sequence with
    other-def targets.** Expanding the sub-doc to include every sequence sibling approaches the
    full document → defeats incremental.
  - **The gate earned its keep:** it caught a subtle, silent correctness hole in the core
    recompute method on the very first real run. The dirty-set side is still proven
    (superset-safe, M2b-1 PASS); only *recompute-from-raw-in-a-sub-doc* is broken.

#### M2b-2b design fork — RESOLVED: Path 2 (bounded sibling expansion + fallback)
Three options were on the table; the chosen path is a **bounded** form of (B) plus an
(A)-style fallback. For the record:
  - **(A) Delta-from-baseline recompute.** Start from the baseline *resolved* dirty def and
    apply only the changed mod's delta. Clean for add/widen; no clean "un-apply" for
    removals/reorders → still needs a from-raw path or a forced full rebuild.
  - **(B) Expand the sub-doc to all sequence siblings.** Faithful; feared to approach the whole
    DB. **Rejected "unless closures turn out small in practice" — they ARE small** (see below).
  - **(C) Non-aborting sequences.** Rejected: changes Conditional/FindMod branch semantics, can
    introduce *different* silent errors.

**The fix (Path 2, branch `feat/subdoc-sibling-expansion`, in-game PASS 2026-06-21):**
`scripts/closure.py` measured the transitive closure offline on the apparel-probe change:
**245 dirty → 337 sub-doc total = 1.17% of 28,682 defs** (dominated by two large sequences in
`als.gravtech` and `oskarpotocki.vfe.tribals`). Option (B) is bounded in practice, so we take it
— with a conservative fallback for the one case it can't trust.
  - **`SubDocExpander.cs` (pure, offline-tested, mirrors `closure.py`):** for each dirty def,
    find the `PatchOperationSequence`s it belongs to (patch edges whose id ends `.operations[N]`,
    stripped to the parent key) and union in the defs the sequence's *sibling* child ops modify —
    the **context** set. Those defs populate the sub-doc so the sequence reaches all its targets
    and runs to completion; their recomputed values are discarded (the baseline cache already
    holds them). It also flags **`needsFullRebuild`** when the **changed** mod itself owns a
    container op (Sequence/Conditional): the baseline graph's execution path for that op is stale,
    so the caller falls back to the full rebuild (always faithful) rather than risk an unfaithful
    recompute.
  - **`DefRecompute.cs`** gains a `contextIds` parameter; the sub-doc is `dirty ∪ context`
    (+ inheritance ancestors of both); only dirty concrete defs are extracted.
  - **`DirtySetGate.RunRecompute`** loads the prior `DependencyGraph.json`, expands the sub-doc,
    falls back or recomputes → splices → diffs vs the full rebuild, and writes the machine-
    readable **`RecomputeReport.json`** (`pass`/`fallback`/`recomputeMismatches`/`contextCount`/
    `subDocSize`/…). `DirtySetDiagnostic` publishes `LastChangedMods` for the fallback check.
  - **Live result (test-harness mods, 2026-06-21):** dirty-set gate `nonDirtyMismatches=0`;
    recompute gate **`pass=true fallback=false recomputeMismatches=0`**, `dirtyCount=6
    contextCount=1 subDocSize=7` over 27,581 defs. `contextCount=1` is `TC_SeqSibling` pulled in
    by the expansion (CASE 3) — exactly the def whose absence caused the dead-end's sequence
    abort. The earlier dirty-only build produced `recomputeMismatches=12` here.
  - **Flags are now runtime-overridable** (`GagarinPrefs` static ctor reads `GAGARIN_*` env
    vars), so the live harness enables the pipeline at launch without a bespoke flag-edited build.
    See `TestMods/README.md` for the runner.

**Next steps (handoff — branch `feat/subdoc-sibling-expansion`, 14 commits ahead of `origin/main`,
NOT pushed):**
  1. **Scale-up proof — the important one.** The live PASS so far is on the tiny test-mod set
     (dirty=6, context=1, 27,581 defs). Re-run the recompute gate on the **real apparel-probe
     change** (134-mod modlist) where `closure.py` predicts dirty=245 → **context≈92, subDoc≈337**
     of 28,682. Expect `recomputeMismatches=0`; this is the load the dirty-only dead-end failed
     (12 mismatches). That probe (`PerformanceSearch/1.6/Patches/M2aProbe.xml`, widen to
     `Defs/ThingDef[apparel]`) is the existing real-change vehicle — the test mods don't reproduce
     the `als.gravtech`/`vfe.tribals` mega-sequences.
  2. **Fallback path live test.** Add a `PatchOperationSequence`/`Conditional` to `TestMod_Change`
     and assert `RecomputeReport.fallback==true` (the runner currently asserts `fallback==false`;
     gate that on a flag/arg). Confirms the changed-mod-container-op escape hatch fires.
  3. **Content-hash keying** (not packageId) for the changed-mod detection — the blind spot
     Gagarin + FGL share; required before the real cache path.
  4. **Error-logging metrics** (user-wanted, pre-share): auto-log recompute exceptions / gate
     FAILs / dirty-set or hash inconsistencies during normal play — see
     `[[project-incremental-cache]]` and the pushback-on-premature-sharing note.
  5. **Then Piece E / M2b-3:** wire the three-way decision (full hit / incremental / full miss)
     into the real cache path behind a default-OFF flag with a force-full-rebuild escape hatch.
- **M2b-3 / E:** wire the three-way decision (full hit / incremental / full miss) into the cache
  path behind a default-OFF setting with a force-full-rebuild escape hatch.

Approx/risks for M2b-2: RAW pre-patch candidate bodies miss cascade-only matches; container op
scoping (FindMod/Conditional/Test) and custom `PatchOperation`s that navigate XML without
`Select*` need care — the gate is exactly what catches these. computeMs to watch (M2a re-test
was already 2.5s; a columnar/indexed candidate doc is the optimization lever).

- **Serialization**: a format for the graph (+ the baseline unified doc for splicing) that
  supports in-place update. Open question: JSON likely too big at ~100k defs — measure
  against Piece A's `serializedBytes` metric, consider binary/columnar.
- **Mutation on change**: load persisted graph → identify changed mods by **content hash**
  (not packageId) → compute dirty set → recompute affected nodes → splice into baseline →
  **update the persisted graph in place**: insert/modify/remove nodes, re-match wildcard
  patches against changed nodes only, topological recompute for patch-on-patch ordering.
- **Reuse**: port Piece C's `DirtySet` + `ApplyModel` from the offline harness as the
  reference algorithm.
- **Cost guardrail**: persistence + load of the graph must stay well below the rebuild it
  saves. Piece A's overhead/size metrics are the go/no-go input here.
- **Open risks**: real `PatchOperation` semantics vs the minimal apply model; custom
  modded op types; wildcard re-match performance on big lists.

### Piece E — Live integration
Wire incremental recompute into Gagarin's real cache path.

- Hook `CombineIntoUnifiedXML` / `CachedDefHelper.Load`: replace the binary
  `IsUsingCache` with a **three-way** decision — full hit (use cache as-is), **incremental**
  (recompute dirty subset), full miss (rebuild + recapture).
- Recompute must run the **real** PatchOperation pipeline (not the minimal model) for
  fidelity. The Piece C zero-diff harness becomes the integration test: incremental result
  must byte-match a full rebuild.
- **Gating**: behind a setting, default OFF until proven, with a manual "force full
  rebuild" escape hatch.
- **Validation**: tonight's in-game capture + zero-diff harness + a rebuild-and-compare
  check.
- **Dependency**: D (persistence + mutate) and a way to drive RimWorld's apply over just
  the dirty patches/nodes.

### Prior-state sidecar + master toggle (2026-06-21, branch `feat/prior-state-sidecar`)

**Bug fixed:** the dirty-set diagnostic and zero-diff gate never fired on a real mod
add/remove/reorder — the exact loads we built them for. They read "prior load" inputs from
the author's LIVE cache files in a `LoadModXML` PREFIX, but the author tears those files
down FIRST, in `[Main.OnInitialization]` (`StartupHelper.StartUpStarted`), before any of our
patches run:
- On `ModListChanged`, `Context.IsUsingCache = false` → the `Context.cs` setter DELETES
  `ModList.xml` + `Unified.xml`, then `StartUpStarted` RE-DUMPS the CURRENT modlist to
  `ModList.xml`.
- So by our prefix: prior `Unified.xml` is gone (gate `Run()` returns at `!File.Exists`, no
  report) and `ModList.xml` already holds the NEW order (`DirtySetComputer` reorder/remove
  seed is blind → `seedReorder=0`).
- `ProvenanceRecorder.Save()` also OVERWRITES `DependencyGraph.json` mid-load before the
  gate's recompute reads it (clobber race).
The live harness missed all this because it only swaps a patch file with the modlist
UNCHANGED, so `IsUsingCache` stays true through OnInitialization.

**Fix (does NOT touch the author's deletion lifecycle):** give the incremental layer its own
prior-state SIDECAR the author never deletes. At the END of a successful cache-writing load
(`ParseAndProcessXML` postfix, right after `CachedDefHelper.Save()` + `ProvenanceRecorder.Save()`
+ `DirtySetGate.Run()`), `PriorStateSnapshot.Capture()` copies the just-written `ModList.xml`,
`Unified.xml`, `AssetsHash.xml`, `AssetsHashInt.xml`, `DependencyGraph.json` into
`…/MissileGirl/Incremental/prior/` — a SIBLING of `…/MissileGirl/Cache/`, outside the author's
delete scope (the author only ever deletes files INSIDE `Cache/`). So the sidecar always
reflects the PRIOR load and survives to next run. `DirtySetDiagnostic` (prior hashes/order +
graph) and `DirtySetGate` (prior `Unified.xml` + graph) read from the sidecar when it exists,
which fixes the gate-not-firing AND the `seedReorder=0` blindness AND removes the
`ProvenanceRecorder.Save()` clobber race (gate reads the sidecar graph, not the live one).
Graceful skip on first-ever load (no sidecar yet), exactly as today.

**Master toggle `GagarinPrefs.IncrementalCache`** (default OFF; env `GAGARIN_INCREMENTAL_CACHE`).
The runtime switch between "our graph / incremental representation" and the author's original
all-or-nothing cache. It gates the sidecar capture + the diagnostic/gate's redirect to it.
CONTRACT: every edit to the author's ORIGINAL code paths NO-OPs when OFF, so flipping it off
restores original behaviour byte-for-byte (the author's deletion lifecycle is untouched). This
flag is the future home of the Piece E three-way full-hit / incremental / full-miss decision.
Additive to the four `GAGARIN_DIRTYSET_*` / `CAPTURE` diagnostics: those still gate their own
report, but when ON they read priors from the sidecar this toggle's capture produced.

For the live re-test the owner exports the four existing diagnostic flags PLUS
`GAGARIN_INCREMENTAL_CACHE=1`. NB the sidecar bootstraps: the FIRST toggled-on changed load
still skips (no prior sidecar yet); it is populated that run and the gate fires from the next
changed load on.

---

## What the Piece A capture results are good for (it's a ubiquitous dataset)

`DependencyGraph.json` records, for a full load: every def **node** (id `DefType/DefName`,
source mod + file), every **patchEdge** (source mod, op type, xpath, matched + modified
node ids), every **inheritanceEdge**, and **metrics**. That's a general-purpose dataset
about the entire modded def-build — many tools fall out of the same artifact:

1. **Incremental recompute** (the original purpose) — dirty-set input for C→D→E.
2. **Patch conflict / order-sensitivity detection** — nodes in >1 patch's `modifiedNodeIds`
   from different mods are real conflicts or order-dependent merges. Standalone "why is my
   def weird" debugger.
3. **Dead / no-op patch detection** — patchEdges with empty `matchedNodeIds` = patches that
   matched nothing (doomed XPath probes). This is a precise, content-keyed version of FGL's
   failed-XPath skip-list → feeds the safe FGL hardening below directly.
4. **Load-order validation** — modified-by/depends-on sets imply required orderings (B's
   patch hits A's def ⇒ B loads after A); validate/correct `loadAfter` tags.
5. **Per-mod load-cost attribution** — node/edge counts + metrics show which mods dominate
   the merged database; a profiler/attribution view.
6. **Content-hash invalidation** — pair the graph with Gagarin's existing per-file
   `AssetHashingUtility` hashes to get the precise invalidation we keep wanting (used by
   both our cache and the FGL skip hardening).
7. **Classification input** — Piece B already consumes it (identity vs wildcard).
8. **Cross-update diffing** — snapshot graphs; diff after a mod/game update to see exactly
   what changed in the merged db. QA tool.
9. **Visualization** — render mod interactions to help users reason about their pack.

Takeaway: the capture is reusable infrastructure, not single-purpose. Worth keeping its
schema stable and the emitter clean.

---

## FGL safe-optimization brainstorm

Guiding principle: **prefer in-session pure memoization over cross-session persistence.**
FGL's danger comes from persisting across sessions and invalidating on coarse mod-order. A
pure memo of a deterministic function of in-process state can't go stale — no invalidation,
no in-place-update hole. Safest lever there is. Safety test each idea must pass: output
byte-identical to vanilla; skip provably-redundant work OR memoize a pure function; prefer
engine-agnostic (low-level) hooks; fail safe (fall back to real computation, never a wrong
result).

Community read: FGL's failure-skip is the one clearly-safe op; most other defaults change
timing/ordering/resolution and can break silently. So aim contributions at the safe lever.

### Tier 1 — pure in-session memoization (no invalidation needed; can't be wrong)
- **Compiled-XPath cache** — `SelectSingleNode(string)` re-parses the XPath every call;
  patches reuse path shapes constantly. Cache `xpath → XPathExpression.Compile(...)`. Same
  hot path as FGL's failure-skip but fully transparent (no skip, no assumption). Likely the
  single best safe win; composes with the failure-skip.
- **Reflection-metadata cache for `DirectXmlToObject`** — def deserialization does
  `type.GetField(name)`/`GetProperty` per field across the whole db. `(type,fieldName) →
  FieldInfo` is pure within a process. Big, hot, general.
- **Type-enumeration memoization** — `GenTypes.AllSubclasses`/`AllLeafSubclasses`/
  `AllTypesWithAttribute` walk all types, called repeatedly with same args; assembly set is
  fixed mid-load ⇒ deterministic. Audit which siblings FGL leaves uncached.
- **Pure string-transform memos** — `GetTypeNameWithoutIgnoredNamespaces` and similar
  `string→string` helpers in tight loops.

### Tier 2 — shrink an existing dangerous default into its safe subset
- **In-session-only type-by-name cache (drop the cross-session remap)** — FGL's type op is
  risky because it persists and *rewrites* the name across sessions (wrong-type resolution
  if the mod set shifts). The in-process cache with no persistence/remap keeps the speedup
  and removes the failure mode. Ship as a "safe mode" toggle.

### Tier 3 — subtractive (skip work with no game-state effect)
- **Skip dev-only diagnostic passes outside dev mode** — `ReportProbablyMissingAttributes`,
  obsolete-method checks, def cross-ref sanity warnings: produce warnings, not game state.
  Skip when `Prefs.DevMode` is off. Purely subtractive.
- **Harden the failure-skip** — keep FGL's safe op but key its skip-list on **content hash**
  instead of mod order, closing the in-place-update hole. (Direct user of capture insight #3
  / #6 above.)

### Tier 4 — allocation / GC reduction (safe, indirect)
- **String interning for repeated load strings** — defNames/labels/tags are massively
  duplicated; interning cuts allocations and GC pauses during load. No value change.

### First contribution to pursue
Compiled-XPath cache: transparent, engine-agnostic, attacks the known bottleneck, zero
invalidation reasoning. Then the failure-skip hardening (content-hash keyed), which reuses
our capture/hashing work.

Note: FGL deliberately does **not** cache a result document — it speeds the live pipeline.
So FGL contributions are about its memoization correctness; the dependency-graph/incremental
work belongs in the Gagarin/Missile Girl layer. Two layers, two homes.

### Caveat
These are candidates with rationale, not measured wins. Profile before investing — Piece A's
`captureOverheadMs` plumbing + a sampling profiler on a cold load will confirm which hot
paths are worth a PR. Best bets a priori: compiled-XPath and reflection-metadata caches.

---

## M2a in-game diagnostic — run & CLEANUP checklist (prepped 2026-06-21)

State going in: M2a build (both flags ON — `CaptureProvenance` + `DirtySetDiagnostic`) deployed
over the Workshop copy `3712928623` (Gagarin.dll 80896B; **stock 50176B `.bak` preserved**,
Cosmodrome `.bak` too). Source worktree flags are back OFF (clean). Probe patch staged at
`PerformanceSearch/1.6/Patches/M2aProbe.xml` with the **narrow** (zero-match) xpath. The fork
loaded by the game is the Workshop copy, NOT the dev worktree.

Vehicle = our own `joof.performancesearch` (reversible via git; no Workshop files edited). It is
symlinked into Mods but **not currently enabled**.

0. **Enable** `joof.performancesearch` in your mod list (in-game or RimSort), after Core/Missile
   Girl. (This + the probe file are the only mod-set/content changes.)
1. **Clear the MissileGirl cache** to force a clean cold baseline:
   ```bash
   rm -rf "/home/deck/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios/MissileGirl/Cache"
   ```
2. **Run 1 (baseline capture):** launch, reach main menu (slow cold rebuild). The narrow probe
   matches nothing, so PerformanceSearch contributes an empty baseline. ARCHIVE the graph before
   Run 2 (Run 2 may re-capture and overwrite it):
   ```bash
   C="/home/deck/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios/MissileGirl/Cache"
   cp "$C/DependencyGraph.json" "/home/deck/Developer/RimWorldMods/MissileGirl-metrics/M2a-baseline-$(date +%Y%m%d-%H%M).json"
   ```
3. **Widen the predicate:** in `PerformanceSearch/1.6/Patches/M2aProbe.xml` replace the narrow
   `<xpath>` line with `<xpath>Defs/ThingDef[apparel]</xpath>` (the file documents this). Do NOT
   clear the cache (the diagnostic needs Run 1's graph/hashes/ModList).
4. **Run 2 (diagnostic):** launch again. Watch the log for
   `GAGARIN: Dirty-set diagnostic ... seedWildcard=N` and read `$C/DirtySet.json`. **Success =
   `seedWildcard` > 0 and `changedMods` includes the probe** (the newly-matched apparel defs).
   `seedWildcard` ≈ number of apparel ThingDefs in the list. Archive `DirtySet.json`.
   - Negative control: with the OLD (baseline-xpath) logic this would read 0 — seedWildcard>0 is
     what proves M2a re-tests the CURRENT predicate.
5. **CLEANUP:**
   - `rm PerformanceSearch/1.6/Patches/M2aProbe.xml` (or `git -C PerformanceSearch clean -f 1.6/Patches`).
   - Disable `joof.performancesearch` again if you don't normally run it.
   - Restore stock Missile Girl DLLs from the `.bak` (same as the Piece A cleanup below).
   - Clear the MissileGirl cache once more so the next normal launch rebuilds stock.

Caveat: the re-test reads predicates by reflecting the live PatchOperation `xpath` field and does
not model `PatchOperationConditional`/`FindMod`/`Test` scoping, so contained ops may over-match
(superset-safe, never under). Fine for a diagnostic; the real gate is still M2b's zero-diff.

## Piece A in-game capture — run & CLEANUP checklist

State going in: capture-enabled DLL deployed over the Workshop copy (originals saved as
`.bak`); MissileGirl cache cleared (forces a cold miss); flag is ON only in the deployed
DLL (source in `MissileGirl-A` stays OFF / clean).

1. **Run**: launch RimWorld, let it fully reach the main menu (slow cold rebuild).
2. **Collect + ARCHIVE** (do this BEFORE any cache clear — a cache clear deletes the JSON):
   ```bash
   C="/home/deck/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios/MissileGirl/Cache"
   python3 -c "import json,sys;print(json.load(open(sys.argv[1]))['metrics'])" "$C/DependencyGraph.json"
   # archive it as evidence (modcount comes from the metrics' activeModCount)
   N=$(python3 -c "import json;print(json.load(open('$C/DependencyGraph.json'))['metrics'].get('activeModCount','x'))")
   cp "$C/DependencyGraph.json" "/home/deck/Developer/RimWorldMods/MissileGirl-metrics/${N}mods-$(date +%Y%m%d-%H%M).json"
   ```
   The whole `metrics` block is the source of truth (`captureOverheadMs`, `registerMs`,
   `recordMs`, `inheritanceResolvedCount`/`inheritanceEdgeCount`, `documentPathFallbacks`,
   `abstractNodeCount`, `activeModCount`). The archived file — not the chat — is the evidence.
3. **CLEANUP — restore the shipped DLLs** (back to stock Missile Girl):
   ```bash
   W="/home/deck/.local/share/Steam/steamapps/workshop/content/294100/3712928623"
   mv "$W/1.6/Plugins/Stable/Gagarin.dll.bak" "$W/1.6/Plugins/Stable/Gagarin.dll"
   mv "$W/1.6/Assemblies/Cosmodrome.dll.bak"  "$W/1.6/Assemblies/Cosmodrome.dll"
   ```
   Fallback if a `.bak` is ever lost: Steam → Missile Girl → Verify integrity of files.
4. No source cleanup needed (the `MissileGirl-A` worktree flag is already OFF; only the
   deployed Workshop DLL had it ON, and step 3 replaces that).

---

## Piece A — capture-overhead optimization (results 2026-06-20)

Full **~800-mod** list (production scale — an earlier "~60 mod" label was a mistaken
estimate; identical patchEdge/node/fallback counts across runs confirm the same full
list throughout), cold load, before vs after the optimization commits on
`feat/provenance-capture` (PR #3):

Progression of **emitted** metrics (the source of truth — earlier ad-hoc `docPathFallbacks`
values of "1" came from a flawed analysis script and are discarded):

| Build (all ~800 mods) | overheadMs | docPathFallbacks | nodes (abstract) | inheritanceEdges (resolved) |
|---|---|---|---|---|
| Baseline (pre-opt) | 78,451 | 1,674 | — | — |
| + capture/keying opt + memo | 138 | 744 | 15,755 (0) | 5,217 (broken) |
| + inheritance fix (66a939d) | 174 | 744 | 16,496 (741) | 5,664 (**99.6%**) |
| + key-at-selection A (bf09113) + metrics B | 128 | **1** ✓ | 19,499 (773) | 7,140 (**99.66%**) |

NB: the last row is a different/larger mod set (103 mods, 19,499 nodes — earlier rows were a
smaller list), so it is not a same-list before/after. But `docPathFallbacks → 1` is
structurally decisive: A removes post-detachment keying, so only the lone `Defs`-root add can
remain. First **archived** run: `MissileGirl-metrics/103mods-20260620-1527.json`.

Key reading: capture overhead collapsed ~570× (78,451 → 138ms) and held at ~174ms after
the inheritance fix. `docPathFallbacks` dropped from 744 to **1** once the key-at-selection
fix (A) landed (verified on the 103-mod archived run) — the lone remaining fallback is the
genuine `Defs`-root `PatchOperationAdd`.

**Artifact caveat:** the 138ms and 174ms run JSONs were **deleted** by cache-clears done to
stage later builds. The numbers above are transcript observations, not currently
re-verifiable from a saved file. Process fix going forward: **archive each run before
clearing the cache** — copy `DependencyGraph.json` to
`/home/deck/Developer/RimWorldMods/MissileGirl-metrics/<modcount>-<date>.json`. Treat that
archive (not the transcript) as the evidence base for the mod author.

What changed (three commits):
- **Capture from RimWorld's own selection.** The Apply prefix used to re-run
  `xml.SelectNodes(xpath)` over the whole document per PatchOperation — a second full
  XPath pass. Now a per-op sink collects whatever RimWorld's own
  `SelectNodes`/`SelectSingleNode` returns (hooks installed only when capture is on).
- **Abstract-def keying + `KeyForNode` memoization.** Abstract/`Name`-based parents now
  key as `ThingDef@BuildingBase` instead of an unstable positional `DocumentPath` whose
  computation paid an O(15,758) preceding-sibling walk at the `<Defs>` level. The 78s
  collapse was driven mostly by **memoization** (pre-memo, those expensive keyings ran
  once per match — many times per node; post-memo, once per distinct node). Net effect on
  the emitted metric: fallbacks halved 1,674 → 744 (not eliminated); the remaining 744 are
  the detached-node fallbacks that the key-at-selection fix (A) targets. The precise
  split between memoization and abstract-keying is unmeasured (the old build had no
  per-phase timing).
- **Per-phase timing** logged (not serialized), so cost is attributable.

Scale: validated at the full ~800-mod list. After adding the inheritance fix, a full
cold load reported `captureOverheadMs=174` (registerMs/recordMs dominate, all sub-50ms),
nodeCount 16,496 (incl. 741 abstract), inheritanceEdges 5,664 with 99.6% resolved,
docPathFallbacks 744. So overhead barely moved from the optimized baseline and nothing
quadratic resurfaced at scale — this is the production number to cite.

Caveats / not-yet-done:
- Magnitude was larger than the mechanistic prediction; the dominant lever turned out
  to be the keying fix (killed the O(n) sibling walks), not the duplicate-scan removal
  (which helps total wall-clock but sat outside the overhead metric).
- `patchEdgeCount` undercount (sequence-child collapse) — **FIXED** in PR #5
  (`feat/patch-id-recursion`): `IndexPatches` recurses into container ops via a generic
  field walk, assigning stable hierarchical ids. In-game validation (~104 mods): patchEdgeCount
  1,659 → 2,623 (+58%), `unindexed` edges hundreds → **3**, no regression on fallbacks (1) or
  inheritance (99.66%), overhead ~170-200ms.
  - **Residual: 3 unindexed ops (0.11%) — left as documented residual.** Broadening the field
    walk to all reference fields was tried and REVERTED (didn't catch them; same 3 persisted).
    They're standard leaf ops (Replace/Add/AttributeSet on apparel defs) with no static field
    holding them → almost certainly **generated dynamically during a parent op's Apply** (a
    generative/custom container), which no index-time walk can see. Real fix if ever wanted:
    **apply-time enclosing-op attribution** — track the currently-applying top-level op on the
    Apply-hook stack and have unindexed children inherit its id + sourceMod.
- The 744 `docPathFallbacks` above were on the inheritance-fix build, BEFORE the
  key-at-selection fix (commit bf09113); that fix should drive them toward ~0. Pending
  the A+B full-scale re-run to confirm.
- **Output correctness is NOT end-to-end verified** — see below.

### Open correctness gate (carry into D/E)
The pure keying/serialization logic is unit-tested (10/10), and the graph's shape +
keying look right, but we have **never** closed the loop: real captured graph →
incremental recompute → byte-diff vs a full RimWorld rebuild. Piece C proved the
*algorithm* on synthetic fixtures only. Until that loop runs on real capture output,
correctness is plausible, not proven. Also note the approach-5 tradeoff: for standard
pathed ops the captured match set is equal-or-better, but a custom `PatchOperation` that
navigates XML *without* `Select*` would now contribute no matched nodes (the old prefix
at least re-ran its declared xpath). Spot-checking known patches → expected edges is the
cheap interim confidence step; the real gate is D/E's recompute-and-diff.

### FIXED + VERIFIED — inheritance resolution (commit 66a939d; verified 2026-06-20)
Was ~96% broken; now **99.66%** resolved (7,116 / 7,140 on the 103-mod archived run; the
remaining ~24 are cross-mod / `MayRequire`-gated parents). Fixed by registering abstract
defs as `{DefType}@{Name}` nodes via an `XmlInheritance.TryRegister` postfix and resolving
`ParentName` against a `Name → nodeId` map. Original defect description, for the record:
On the earlier capture, only 228 of 5,217 `inheritanceEdges` resolved a `parentNodeId`;
4,989 were null because they point at abstract bases (`BuildingBase` ×237, `MoteBase`,
`FleckBase`, `AnimalKindBase`, `BaseBullet`, …). Cause: `ResolveInheritance` looks parents
up in `defNameToNodeId` (keyed by `defName`), but abstract bases have a `Name` attribute
and no `defName`, and are never registered as nodes (they never become `Def` objects).
Impact: inheritance fan-out — a change to `BuildingBase` must dirty its 237 descendants —
cannot work, so the graph is currently WRONG for incremental recompute. Fix (builds on the
new `@Name` keying): register abstract defs as `{DefType}@{Name}` nodes when encountered,
and resolve `ParentName` against a `Name → nodeId` map. This is the natural first task of D.

