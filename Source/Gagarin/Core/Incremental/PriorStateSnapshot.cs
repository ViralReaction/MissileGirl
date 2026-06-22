// // Copyright (c) 2026 ViralReaction
// //
// // This program and the accompanying materials are made available under the
// // terms of the Eclipse Public License 2.0 which is available at
// // http://www.eclipse.org/legal/epl-2.0.
// //
// // SPDX-License-Identifier: EPL-2.0

// PriorStateSnapshot.cs (Piece D — prior-state sidecar)
//
// Contains: the sidecar store that gives the incremental layer its OWN copy of the previous
// load's cache inputs (ModList.xml, Unified.xml, AssetsHash.xml, AssetsHashInt.xml,
// DependencyGraph.json), plus accessors the dirty-set diagnostic and gate read their priors
// from. Gated behind the master toggle GagarinPrefs.IncrementalCache.
//
// Why this exists (the bug it fixes): the dirty-set diagnostic and the zero-diff gate need the
// PRIOR load's cache files to seed their reorder/remove diff, snapshot the prior Unified.xml,
// and load the prior dependency graph. They captured those priors in a LoadModXML PREFIX. But
// on any modlist add/remove/reorder the author's startup runs FIRST and tears the live cache
// down before our prefix ever runs:
//   - StartupHelper.StartUpStarted ([Main.OnInitialization]) sees ModListChanged, sets
//     Context.IsUsingCache = false, which (Context.cs setter) DELETES ModList.xml and
//     Unified.xml, then RE-DUMPS the CURRENT modlist to ModList.xml.
//   - So by the time our LoadModXML prefix runs: the prior Unified.xml is already gone (the
//     gate's snapshot silently fails -> Run() returns at !File.Exists(snapshot), no report),
//     and ModList.xml already holds the CURRENT order (so DirtySetComputer's reorder/remove
//     seed is blind -> seedReorder=0).
//   - LoadedModManager_Patch then calls ProvenanceRecorder.Save() which OVERWRITES
//     DependencyGraph.json mid-load, before the gate's recompute reads it for its baseline.
// The live harness never caught this because it only swaps a patch file with the modlist
// UNCHANGED, so IsUsingCache stays true through OnInitialization and flips false only mid-
// LoadModXML, after our prefixes already snapshotted the priors.
//
// The fix (this file): do NOT touch the author's deletion lifecycle. Instead, at the END of a
// successful cache-writing load, copy the just-written cache files into a sidecar directory the
// author's code never deletes — under CustomConfigFolderPath (the .../MissileGirl/ config dir),
// which is the PARENT of CacheFolderPath (.../MissileGirl/Cache/). The author only ever deletes
// files INSIDE Cache/, so the sidecar always survives to the next run and always reflects the
// PRIOR load. The diagnostic and gate then read their priors from the sidecar, which both fixes
// the gate-not-firing / seedReorder=0 blindness and removes the ProvenanceRecorder.Save() clobber
// race (the gate reads the sidecar graph, not the just-overwritten live one).
//
// Capture timing: the natural call site is the ParseAndProcessXML postfix, right after
// CachedDefHelper.Save() + ProvenanceRecorder.Save() have written the live cache (and after the
// gate has read the PRIOR sidecar). At that point the live cache reflects THIS load, so copying
// it makes the sidecar the prior for NEXT run.

using System;
using System.Collections.Generic;
using System.IO;
using MissileGirl;

namespace Gagarin
{
    public static class PriorStateSnapshot
    {
        // The cache files we mirror. These are exactly the inputs the diagnostic + gate read as
        // their "prior load" state. Kept here (rather than re-deriving GagarinEnvironmentInfo's
        // private file-name constants) so capture and read agree on one list.
        private const string ModListFileName = "ModList.xml";
        private const string UnifiedFileName = "Unified.xml";
        private const string HashFileName = "AssetsHash.xml";
        private const string HashIntFileName = "AssetsHashInt.xml";
        private const string GraphFileName = "DependencyGraph.json";

        // The directory the sidecar copies live in: .../MissileGirl/Incremental/prior/.
        // RocketEnvironmentInfo.CustomConfigFolderPath is .../MissileGirl, and CacheFolderPath is
        // that + /Cache, so this is a SIBLING of Cache and outside the author's delete scope. The
        // path arithmetic (and its outside-Cache property) is verified offline via
        // PriorStateSnapshotPaths.ResolvePriorDir.
        public static string PriorDir =>
            PriorStateSnapshotPaths.ResolvePriorDir(RocketEnvironmentInfo.CustomConfigFolderPath);

        // Prior-copy accessors the diagnostic + gate read instead of the live cache files.
        public static string PriorModListPath => Path.Combine(PriorDir, ModListFileName);
        public static string PriorUnifiedPath => Path.Combine(PriorDir, UnifiedFileName);
        public static string PriorHashPath => Path.Combine(PriorDir, HashFileName);
        public static string PriorGraphPath => Path.Combine(PriorDir, GraphFileName);

        // True when the master toggle is ON and a usable sidecar exists. Callers use this to
        // decide whether to redirect their prior reads to the sidecar (toggle ON + sidecar
        // present) or fall back to their original behaviour (skip / live files). Cheap existence
        // probe of the prior dir; the individual readers still File.Exists each file they need.
        public static bool Available =>
            GagarinPrefs.IncrementalCache && Directory.Exists(PriorDir);

        // Copy the just-written live cache files into the sidecar so they become NEXT run's
        // prior. Toggle-gated: a complete NO-OP when IncrementalCache is OFF, so the author's
        // original load behaviour is unchanged byte-for-byte. Best-effort: a copy failure is
        // logged and swallowed (the live cache is authoritative; a missing sidecar simply makes
        // next run's diagnostic/gate skip, exactly as today).
        public static void Capture()
        {
            if (!GagarinPrefs.IncrementalCache)
                return;
            try
            {
                string priorDir = PriorDir;
                Directory.CreateDirectory(priorDir);

                PriorStateSnapshotPaths.CopyIfExists(GagarinEnvironmentInfo.ModListFilePath, Path.Combine(priorDir, ModListFileName));
                PriorStateSnapshotPaths.CopyIfExists(GagarinEnvironmentInfo.UnifiedXmlFilePath, Path.Combine(priorDir, UnifiedFileName));
                PriorStateSnapshotPaths.CopyIfExists(GagarinEnvironmentInfo.HashFilePath, Path.Combine(priorDir, HashFileName));
                PriorStateSnapshotPaths.CopyIfExists(GagarinEnvironmentInfo.HashFilePathInt, Path.Combine(priorDir, HashIntFileName));
                PriorStateSnapshotPaths.CopyIfExists(
                    Path.Combine(GagarinEnvironmentInfo.CacheFolderPath, GraphFileName),
                    Path.Combine(priorDir, GraphFileName));

                Logger.Debug("GAGARIN: prior-state sidecar captured to " + priorDir);
            }
            catch (Exception e)
            {
                Logger.Debug("GAGARIN: prior-state sidecar capture failed", exception: e);
            }
        }
    }
}
