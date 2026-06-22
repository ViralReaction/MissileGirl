// // Copyright (c) 2026 ViralReaction
// //
// // This program and the accompanying materials are made available under the
// // terms of the Eclipse Public License 2.0 which is available at
// // http://www.eclipse.org/legal/epl-2.0.
// //
// // SPDX-License-Identifier: EPL-2.0

// DirtySetGate.cs (Piece D — Milestone 2b: real-engine zero-diff gate)
//
// Contains: the RimWorld-facing driver that proves, against the REAL engine, that the dirty
// set (M1 structural + M2a wildcard flips) is a true SUPERSET of what actually changed on a
// given load. It snapshots the prior Unified.xml before the load deletes it, lets Gagarin's
// normal full rebuild write the new Unified.xml, then diffs the two: every def NOT in the
// dirty set must be byte-identical between prior cache and rebuild. A non-dirty mismatch is a
// subset error — the silent-staleness failure the whole incremental effort guards against.
//
// Used for: closing the long-open correctness gate (the dirty-set algorithm was only ever
// proven on synthetic fixtures). This is the harness the M2b recompute will plug into: once it
// reports zero non-dirty mismatches we can trust the dirty set, then build the recompute and
// re-use the same diff to require the recomputed defs byte-match the rebuild too.
//
// Timing: the prior Unified.xml is deleted in the LoadModXML postfix (cache miss), so we copy
// it aside in a LoadModXML PREFIX. The new Unified.xml is written by CachedDefHelper.Save() in
// the ParseAndProcessXML postfix; Run() is called explicitly right after that Save so ordering
// is deterministic rather than relying on Harmony postfix priority. Gated behind
// GagarinPrefs.DirtySetGate; consumes DirtySetDiagnostic.LastDirtySet. Changes no cache
// behaviour.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using MissileGirl;
using Verse;

namespace Gagarin
{
    [GagarinPatch(typeof(LoadedModManager), nameof(LoadedModManager.LoadModXML))]
    public static class DirtySetGate
    {
        private const string SnapshotFileName = "PriorUnified.snapshot.xml";
        private const string ReportFileName = "GateReport.json";

        // Where the gate reads the PRIOR Unified.xml from. With the master toggle ON, the sidecar
        // already holds the prior Unified (PriorStateSnapshot captured it at the end of the last
        // load, outside the author's delete scope), so we read it there and skip the in-cache
        // snapshot entirely — this is what makes the gate fire on a modlist change, where the
        // live Unified.xml is deleted by OnInitialization before our Prefix could copy it. When
        // the toggle is OFF we fall back to the legacy in-cache snapshot the Prefix makes.
        private static string SnapshotPath =>
            PriorStateSnapshot.Available
                ? PriorStateSnapshot.PriorUnifiedPath
                : Path.Combine(GagarinEnvironmentInfo.CacheFolderPath, SnapshotFileName);

        // Whether SnapshotPath points at the surviving sidecar copy (toggle ON) rather than the
        // Prefix's in-cache snapshot. The sidecar is read-only prior state we must NOT delete in
        // Run's finally; the legacy in-cache snapshot is ours to clean up.
        private static bool UsingSidecar => PriorStateSnapshot.Available;

        // Recompute verdict for this load, set by RunRecompute and read by the combined metrics
        // load_summary in Run. Null recomputePass means the recompute did not run (flag off /
        // skipped); fallback true means it declined and the full rebuild stands. Reset each Run.
        private static bool? s_recomputePass;
        private static bool s_recomputeFallback;
        private static int s_recomputeMismatches;
        private static int s_subDocSize;
        private static long s_recomputeMs;

        // The shared metrics envelope for this load (cold == full rebuild). Mirrors
        // DirtySetDiagnostic.CurrentEnvelope; rebuilt per record (cheap).
        private static MetricsLog.Envelope CurrentEnvelope()
        {
            return MetricsLog.NewEnvelope(
                cold: !Context.IsUsingCache,
                packageIds: LoadedModManager.RunningMods.Select(m => m.PackageId));
        }

        // Copy the prior Unified.xml aside before the load deletes it (cache miss) or overwrites
        // it. Runs as a LoadModXML prefix, so it is guaranteed ahead of the deleting postfix.
        // NO-OP when the sidecar is in use: the prior Unified already survives in the sidecar, so
        // there is nothing to rescue here (and on a modlist change the live Unified.xml is
        // already gone by the time this prefix runs — the whole reason the sidecar exists).
        public static void Prefix()
        {
            if (!GagarinPrefs.DirtySetGate)
                return;
            if (UsingSidecar)
                return;
            try
            {
                string prior = GagarinEnvironmentInfo.UnifiedXmlFilePath;
                string snapshot = Path.Combine(GagarinEnvironmentInfo.CacheFolderPath, SnapshotFileName);
                if (File.Exists(prior))
                    File.Copy(prior, snapshot, overwrite: true);
                else if (File.Exists(snapshot))
                    File.Delete(snapshot); // no prior cache this run; clear a stale snapshot
            }
            catch (Exception e)
            {
                Logger.Debug("GAGARIN: dirty-set gate snapshot failed", exception: e);
                if (GagarinPrefs.Metrics)
                    MetricsLog.Append(MetricsLog.BuildError(
                        CurrentEnvelope(), "gate.snapshot", e.GetType().Name, e.Message));
            }
        }

        // Called from the ParseAndProcessXML postfix, immediately after CachedDefHelper.Save()
        // has written the rebuilt Unified.xml. Compares the prior snapshot to the rebuild and
        // reports the non-dirty mismatches (must be zero for a superset-safe dirty set).
        public static void Run()
        {
            if (!GagarinPrefs.DirtySetGate)
                return;

            string snapshot = SnapshotPath;
            string rebuilt = GagarinEnvironmentInfo.UnifiedXmlFilePath;
            if (!File.Exists(snapshot) || !File.Exists(rebuilt))
                return; // no prior cache to gate against (e.g. first cold load)

            HashSet<string> dirty = DirtySetDiagnostic.LastDirtySet;
            if (dirty == null)
            {
                Log.Warning("GAGARIN: <color=yellow>Dirty-set gate</color> skipped — no dirty set " +
                    "(enable DirtySetDiagnostic alongside DirtySetGate).");
                return;
            }

            // Reset this load's recompute verdict; RunRecompute fills it in if it runs.
            s_recomputePass = null;
            s_recomputeFallback = false;
            s_recomputeMismatches = 0;
            s_subDocSize = 0;
            s_recomputeMs = 0;

            try
            {
                var sw = Stopwatch.StartNew();
                XmlDocument baselineDoc = LoadCache(snapshot);
                Dictionary<string, string> baseline = UnifiedCacheDiff.IndexById(baselineDoc);
                Dictionary<string, string> rebuild = UnifiedCacheDiff.IndexById(LoadCache(rebuilt));
                List<string> mismatches = UnifiedCacheDiff.NonDirtyMismatches(baseline, rebuild, dirty);
                sw.Stop();

                Emit(baseline.Count, rebuild.Count, dirty.Count, mismatches, sw.ElapsedMilliseconds);

                // M2b-2b — real-engine recompute proof: recompute the dirty defs, splice them
                // onto the prior cache, and require the spliced doc to byte-match the full
                // rebuild over EVERY def (empty dirty set => all defs compared). Non-dirty defs
                // are reused verbatim and already proven equal by the gate above, so any mismatch
                // here is a recompute fidelity gap.
                if (GagarinPrefs.DirtySetRecompute)
                    RunRecompute(baselineDoc, rebuild, dirty);

                // Metrics: one combined load_summary for this load (diagnostic figures + this
                // gate's verdict + any recompute verdict), plus inconsistency records for the
                // high-value anomalies (gate FAIL = silent staleness; recompute FAIL = fidelity
                // gap). Errors are logged at the catch below regardless of these flags.
                if (GagarinPrefs.Metrics)
                {
                    EmitLoadSummary(dirty.Count, mismatches.Count, sw.ElapsedMilliseconds);
                    if (mismatches.Count > 0)
                        MetricsLog.Append(MetricsLog.BuildInconsistency(
                            CurrentEnvelope(), "gate", mismatches.Count,
                            mismatches.GetRange(0, Math.Min(20, mismatches.Count))));
                }
            }
            catch (Exception e)
            {
                Logger.Debug("GAGARIN: dirty-set gate failed", exception: e);
                if (GagarinPrefs.Metrics)
                    MetricsLog.Append(MetricsLog.BuildError(
                        CurrentEnvelope(), "gate", e.GetType().Name, e.Message));
            }
            finally
            {
                // Only clean up our own in-cache snapshot. When reading from the sidecar, the
                // file is the prior-state copy PriorStateSnapshot owns (and re-writes later this
                // load) — deleting it would destroy next run's prior, so leave it be.
                if (!UsingSidecar)
                    try { if (File.Exists(snapshot)) File.Delete(snapshot); } catch { /* best effort */ }
            }
        }

        // Emits the combined per-load metrics row: the diagnostic's seed/dirty figures (published
        // on DirtySetDiagnostic) joined with this gate's verdict and any recompute verdict. The
        // diagnostic always runs before the gate (the gate consumes its dirty set), so its fields
        // are normally valid; if they aren't (e.g. an exception in the diagnostic), the seed
        // counts fall back to zero rather than skip the row, so the gate verdict is still recorded.
        private static void EmitLoadSummary(int dirtyCount, int nonDirtyMismatches, long gateMs)
        {
            DirtyResult r = DirtySetDiagnostic.LastResult;
            bool haveDiag = DirtySetDiagnostic.LastDiagnosticValid && r != null;
            MetricsLog.Append(MetricsLog.BuildLoadSummary(
                CurrentEnvelope(),
                haveDiag ? DirtySetDiagnostic.LastChangedAssets : 0,
                DirtySetDiagnostic.LastChangedMods?.Count ?? 0,
                haveDiag ? r.Nodes.Count : dirtyCount,
                haveDiag ? DirtySetDiagnostic.LastTotalNodes : 0,
                haveDiag ? r.SeedChangedDefs : 0,
                haveDiag ? r.SeedPatchModified : 0,
                haveDiag ? r.SeedReorder : 0,
                haveDiag ? r.SeedWildcardFlip : 0,
                haveDiag ? r.InheritanceAdded : 0,
                haveDiag ? DirtySetDiagnostic.LastComputeMs : 0,
                gatePass: nonDirtyMismatches == 0,
                nonDirtyMismatches: nonDirtyMismatches,
                gateMs: gateMs,
                recomputePass: s_recomputePass,
                recomputeFallback: s_recomputeFallback,
                recomputeMismatches: s_recomputeMismatches,
                subDocSize: s_subDocSize,
                recomputeMs: s_recomputeMs));
        }

        private const string GraphFileName = "DependencyGraph.json";
        private const string RecomputeReportFileName = "RecomputeReport.json";

        // Recompute the dirty defs through the real engine, splice onto the prior cache, and diff
        // the spliced result against the full rebuild over ALL defs. Mismatches here = recompute
        // fidelity gaps (the splice is offline-proven and non-dirty reuse is gate-proven).
        //
        // The sub-doc the recompute runs over is the dirty set PLUS the CONTEXT set
        // SubDocExpander derives (sibling defs of any PatchOperationSequence a dirty def is part
        // of), so a sequence does not abort on absent targets and silently leave the dirty def
        // un-patched. When the CHANGED mod itself owns a container op the baseline execution path
        // is stale, so we fall back to the full rebuild for this load rather than risk an
        // unfaithful recompute.
        private static void RunRecompute(XmlDocument baselineDoc,
            Dictionary<string, string> rebuild, HashSet<string> dirty)
        {
            // The graph is the PRIOR run's DependencyGraph.json — snapshotted execution paths we
            // trust for UNCHANGED mods' sequences. (Same file DirtySetDiagnostic just consumed.)
            // From the sidecar when the toggle is ON, so we get the PRIOR graph rather than the
            // one ProvenanceRecorder.Save() overwrote in-cache earlier this load (the clobber
            // race); else the live cache copy, as before.
            string graphPath = PriorStateSnapshot.Available
                ? PriorStateSnapshot.PriorGraphPath
                : Path.Combine(GagarinEnvironmentInfo.CacheFolderPath, GraphFileName);
            if (!File.Exists(graphPath))
            {
                Log.Warning("GAGARIN: <color=white>Recompute gate</color> skipped — no " +
                    GraphFileName + " to expand the sub-doc against.");
                return;
            }
            DependencyGraphData graph = DependencyGraphData.Load(graphPath);
            ICollection<string> changedMods = DirtySetDiagnostic.LastChangedMods ?? new HashSet<string>();

            HashSet<string> context = SubDocExpander.Expand(
                graph, dirty, changedMods, out bool needsFullRebuild, out string fallbackReason);
            if (needsFullRebuild)
            {
                // Not a failure: the full rebuild already ran and is authoritative. The sub-doc
                // recompute simply cannot faithfully reproduce a changed container op, so we
                // decline to recompute this load. No mismatches to report.
                Log.Warning($"GAGARIN: <color=white>Recompute gate</color> " +
                    $"<color=yellow>FALLBACK</color> — {fallbackReason}");
                EmitRecomputeReport(fallback: true, fallbackReason: fallbackReason, miss: NoEmptyList,
                    contextCount: 0, dirtyCount: dirty.Count, recomputed: 0, removed: 0,
                    splicedDefs: 0, rebuildDefs: rebuild.Count, recomputeMs: 0);
                // Publish a fallback verdict for the metrics summary: pass (the authoritative full
                // rebuild stands) but flagged fallback so it is not counted as a real recompute.
                s_recomputePass = true;
                s_recomputeFallback = true;
                s_recomputeMismatches = 0;
                s_subDocSize = 0;
                s_recomputeMs = 0;
                return;
            }

            var sw = Stopwatch.StartNew();
            Dictionary<string, string> recomputed =
                DefRecompute.Recompute(dirty, context, out List<string> removed);
            XmlDocument spliced = UnifiedCacheSplice.Splice(baselineDoc, recomputed, removed);
            // Normalize through the same whitespace-insensitive parse the rebuild went through, so
            // OuterXml comparison is not tripped by formatting differences.
            Dictionary<string, string> splicedIdx = UnifiedCacheDiff.IndexById(Reparse(spliced));
            // Empty dirty set => every def is compared (nothing exempted).
            List<string> miss = UnifiedCacheDiff.NonDirtyMismatches(splicedIdx, rebuild, NoneDirty);
            sw.Stop();

            string verdict = miss.Count == 0 ? "<color=green>PASS</color>" : "<color=red>FAIL</color>";
            Log.Warning($"GAGARIN: <color=white>Recompute gate</color> {verdict} " +
                $"recomputeMismatches={miss.Count} contextCount={context.Count} " +
                $"subDocSize={dirty.Count + context.Count} " +
                $"(recomputed={recomputed.Count} removed={removed.Count} " +
                $"splicedDefs={splicedIdx.Count} rebuildDefs={rebuild.Count}) recomputeMs={sw.ElapsedMilliseconds}");
            EmitRecomputeReport(fallback: false, fallbackReason: null, miss: miss,
                contextCount: context.Count, dirtyCount: dirty.Count, recomputed: recomputed.Count,
                removed: removed.Count, splicedDefs: splicedIdx.Count, rebuildDefs: rebuild.Count,
                recomputeMs: sw.ElapsedMilliseconds);
            if (miss.Count > 0)
            {
                Log.Warning("GAGARIN: recompute mismatches (recompute != rebuild): " +
                    string.Join(", ", miss.GetRange(0, Math.Min(20, miss.Count))));
                DumpMismatches(miss, splicedIdx, rebuild);
            }

            // Publish this recompute's verdict for the metrics summary, and record a high-value
            // inconsistency on FAIL (recomputed defs diverged from the full rebuild). The full
            // mismatch list is in RecomputeReport.json (which a cache clear may wipe); the sample
            // here survives in the rolling log.
            s_recomputePass = miss.Count == 0;
            s_recomputeFallback = false;
            s_recomputeMismatches = miss.Count;
            s_subDocSize = dirty.Count + context.Count;
            s_recomputeMs = sw.ElapsedMilliseconds;
            if (GagarinPrefs.Metrics && miss.Count > 0)
                MetricsLog.Append(MetricsLog.BuildInconsistency(
                    CurrentEnvelope(), "recompute", miss.Count,
                    miss.GetRange(0, Math.Min(20, miss.Count))));
        }

        private static readonly List<string> NoEmptyList = new List<string>();

        // Writes RecomputeReport.json — the machine-readable verdict the live test harness asserts
        // on (it greps the "Recompute gate" log line, then parses this for the exact numbers).
        // A FALLBACK is reported as pass=true (the authoritative full rebuild ran) with
        // fallback=true so the harness can distinguish a real recompute from a declined one.
        private static void EmitRecomputeReport(bool fallback, string fallbackReason,
            List<string> miss, int contextCount, int dirtyCount, int recomputed, int removed,
            int splicedDefs, int rebuildDefs, long recomputeMs)
        {
            try
            {
                bool pass = fallback || miss.Count == 0;
                var sb = new StringBuilder();
                sb.Append('{');
                sb.Append($"\"pass\":{(pass ? "true" : "false")},");
                sb.Append($"\"fallback\":{(fallback ? "true" : "false")},");
                sb.Append("\"fallbackReason\":");
                if (fallbackReason == null) sb.Append("null"); else AppendQuoted(sb, fallbackReason);
                sb.Append(',');
                sb.Append($"\"recomputeMismatches\":{miss.Count},");
                sb.Append($"\"contextCount\":{contextCount},");
                sb.Append($"\"subDocSize\":{dirtyCount + contextCount},");
                sb.Append($"\"dirtyCount\":{dirtyCount},");
                sb.Append($"\"recomputed\":{recomputed},");
                sb.Append($"\"removed\":{removed},");
                sb.Append($"\"splicedDefs\":{splicedDefs},");
                sb.Append($"\"rebuildDefs\":{rebuildDefs},");
                sb.Append($"\"recomputeMs\":{recomputeMs},");
                sb.Append("\"mismatchIds\":[");
                for (int i = 0; i < miss.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    AppendQuoted(sb, miss[i]);
                }
                sb.Append("]}");
                File.WriteAllText(
                    Path.Combine(GagarinEnvironmentInfo.CacheFolderPath, RecomputeReportFileName),
                    sb.ToString());
            }
            catch (Exception e)
            {
                Logger.Debug("GAGARIN: failed writing RecomputeReport.json", exception: e);
                if (GagarinPrefs.Metrics)
                    MetricsLog.Append(MetricsLog.BuildError(
                        CurrentEnvelope(), "recompute.write", e.GetType().Name, e.Message));
            }
        }

        // Writes the recomputed-vs-rebuild XML for each mismatch to RecomputeMismatch.json so the
        // exact byte difference can be diagnosed offline. Temporary debug aid for M2b bring-up.
        private static void DumpMismatches(List<string> miss,
            Dictionary<string, string> spliced, Dictionary<string, string> rebuild)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append('{');
                for (int i = 0; i < miss.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    spliced.TryGetValue(miss[i], out string got);
                    rebuild.TryGetValue(miss[i], out string want);
                    AppendQuoted(sb, miss[i]); sb.Append(":{");
                    sb.Append("\"recomputed\":"); AppendQuoted(sb, got ?? "<absent>"); sb.Append(',');
                    sb.Append("\"rebuild\":"); AppendQuoted(sb, want ?? "<absent>"); sb.Append('}');
                }
                sb.Append('}');
                File.WriteAllText(Path.Combine(GagarinEnvironmentInfo.CacheFolderPath, "RecomputeMismatch.json"),
                    sb.ToString());
            }
            catch (Exception e)
            {
                Logger.Debug("GAGARIN: failed writing RecomputeMismatch.json", exception: e);
                if (GagarinPrefs.Metrics)
                    MetricsLog.Append(MetricsLog.BuildError(
                        CurrentEnvelope(), "recompute.dumpMismatches", e.GetType().Name, e.Message));
            }
        }

        private static readonly HashSet<string> NoneDirty = new HashSet<string>();

        // Round-trips a document through a whitespace-insensitive parse so its node OuterXml
        // matches the cache loaded by LoadCache (which drops insignificant whitespace).
        private static XmlDocument Reparse(XmlDocument doc)
        {
            var settings = new XmlReaderSettings
            {
                IgnoreComments = true,
                IgnoreWhitespace = true,
                CheckCharacters = false
            };
            var result = new XmlDocument();
            using StringReader input = new StringReader(doc.OuterXml);
            using XmlReader reader = XmlReader.Create(input, settings);
            result.Load(reader);
            return result;
        }

        // Loads a DefXmlStorage cache the same way CachedDefHelper does (whitespace-insensitive,
        // characters unchecked) so identical content yields identical OuterXml on both sides.
        private static XmlDocument LoadCache(string path)
        {
            var settings = new XmlReaderSettings
            {
                IgnoreComments = true,
                IgnoreWhitespace = true,
                CheckCharacters = false
            };
            var doc = new XmlDocument();
            using StringReader input = new StringReader(File.ReadAllText(path));
            using XmlReader reader = XmlReader.Create(input, settings);
            doc.Load(reader);
            return doc;
        }

        private static void Emit(int baselineDefs, int rebuildDefs, int dirtyCount,
            List<string> mismatches, long gateMs)
        {
            string verdict = mismatches.Count == 0 ? "<color=green>PASS</color>" : "<color=red>FAIL</color>";
            Log.Warning($"GAGARIN: <color=white>Dirty-set gate</color> {verdict} " +
                $"nonDirtyMismatches={mismatches.Count} " +
                $"(baselineDefs={baselineDefs} rebuildDefs={rebuildDefs} dirty={dirtyCount}) gateMs={gateMs}");
            if (mismatches.Count > 0)
                Log.Warning("GAGARIN: gate mismatches (dirty set missed these): " +
                    string.Join(", ", mismatches.GetRange(0, Math.Min(20, mismatches.Count))));

            try
            {
                var sb = new StringBuilder();
                sb.Append('{');
                sb.Append($"\"pass\":{(mismatches.Count == 0 ? "true" : "false")},");
                sb.Append($"\"nonDirtyMismatches\":{mismatches.Count},");
                sb.Append($"\"baselineDefs\":{baselineDefs},");
                sb.Append($"\"rebuildDefs\":{rebuildDefs},");
                sb.Append($"\"dirtyCount\":{dirtyCount},");
                sb.Append($"\"gateMs\":{gateMs},");
                sb.Append("\"mismatchIds\":[");
                for (int i = 0; i < mismatches.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    AppendQuoted(sb, mismatches[i]);
                }
                sb.Append("]}");
                File.WriteAllText(Path.Combine(GagarinEnvironmentInfo.CacheFolderPath, ReportFileName),
                    sb.ToString());
            }
            catch (Exception e)
            {
                Logger.Debug("GAGARIN: failed writing GateReport.json", exception: e);
                if (GagarinPrefs.Metrics)
                    MetricsLog.Append(MetricsLog.BuildError(
                        CurrentEnvelope(), "gate.write", e.GetType().Name, e.Message));
            }
        }

        private static void AppendQuoted(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else sb.Append(c);
            }
            sb.Append('"');
        }
    }
}
