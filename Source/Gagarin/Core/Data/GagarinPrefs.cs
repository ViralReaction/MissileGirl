// // Copyright (c) 2026 ViralReaction
// //
// // This program and the accompanying materials are made available under the
// // terms of the Eclipse Public License 2.0 which is available at
// // http://www.eclipse.org/legal/epl-2.0.
// //
// // SPDX-License-Identifier: EPL-2.0

using System;
namespace Gagarin
{
    public static class GagarinPrefs
    {
        public static bool Enabled = true;

        public static int FilterMode = (int)UnityEngine.FilterMode.Trilinear;

        public static bool TextureCachingEnabled = false;

        public static float MipMapBias = float.MinValue;

        public static DateTime CacheCreationTime;

        public static bool CacheExpires = true;
        public static int CacheRetentionTime = 3;

        // Dev-only flag for Piece A of the incremental-cache prototype. When ON
        // (and only on a cold/cache-miss load) Gagarin captures the def
        // dependency graph to DependencyGraph.json. Default OFF so normal users
        // and the shipped cache are unaffected. Not scribed to settings.
        public static bool CaptureProvenance = false;

        // Dev-only flag for Piece D Milestone 1. When ON, and a prior DependencyGraph.json
        // (from a CaptureProvenance run) + asset hashes exist, Gagarin computes — alongside
        // its normal rebuild — which defs WOULD need recomputing given what changed, and
        // writes DirtySet.json. Pure diagnostic: it changes no cache behaviour. Default OFF.
        public static bool DirtySetDiagnostic = false;

        // Dev-only flag for Piece D Milestone 2b. When ON (alongside DirtySetDiagnostic, whose
        // dirty set it consumes), Gagarin snapshots the prior Unified.xml before the load
        // deletes it, lets the normal rebuild produce the new one, then proves against the REAL
        // engine that every def NOT in the dirty set is byte-identical between the two —
        // writing GateReport.json. A non-dirty mismatch is a subset error (silent staleness).
        // Heavier than the diagnostic (re-reads two full unified caches), so it is its own flag.
        // Pure diagnostic: changes no cache behaviour. Default OFF.
        public static bool DirtySetGate = false;

        // Dev-only flag for Piece D Milestone 2b. When ON (alongside DirtySetGate), Gagarin
        // RECOMPUTES the dirty defs through the real engine (DefRecompute), splices them onto the
        // prior cache (UnifiedCacheSplice), and proves the spliced result byte-matches the full
        // rebuild over EVERY def — the real incremental zero-diff proof. Still a diagnostic: it
        // builds the spliced doc in memory and diffs it, but does not replace the real cache.
        // Default OFF.
        public static bool DirtySetRecompute = false;

        // Dev-only flag for the week-long real-world evidence base. When ON, the incremental-cache
        // pipeline PERSISTS metrics to an append-only JSON-Lines log (MetricsLog) that survives
        // cache clears, so a human can run the cache during normal play for a week and accumulate
        // a real failure record without a live harness. ERROR records are the priority signal.
        //
        // Scope note — this flag governs PERSISTENCE, not what each stage computes. ERROR records
        // are captured whenever Metrics is ON and the corresponding stage runs (so a crash in the
        // diagnostic/gate/recompute is always logged). But load_summary and inconsistency records
        // only have data to write when the stages that produce them actually run, which is gated
        // by the other diagnostic flags. To populate a FULL evidence base, the human should enable
        // GAGARIN_DIRTYSET_DIAGNOSTIC + _GATE + _RECOMPUTE alongside GAGARIN_METRICS. Default OFF
        // (production never sets it, so the log is never written). Not scribed to settings.
        public static bool Metrics = false;

        // MASTER TOGGLE for the incremental-cache representation (default OFF). This is the
        // runtime switch between "our graph / incremental representation" and the author's
        // original all-or-nothing cache. It governs the prior-state SIDECAR: when ON, a
        // successful cache-writing load copies the just-written cache files (ModList.xml,
        // Unified.xml, AssetsHash.xml, AssetsHashInt.xml, DependencyGraph.json) into a sidecar
        // directory the author's startup teardown never touches (see PriorStateSnapshot), and
        // the dirty-set diagnostic + gate read their PRIOR inputs from that sidecar instead of
        // the live cache files (which OnInitialization deletes/overwrites on a modlist change
        // BEFORE our LoadModXML prefixes run — the bug this fixes).
        //
        // CONTRACT: every edit to the author's ORIGINAL code paths must NO-OP when this is OFF,
        // so flipping it off restores the original behaviour byte-for-byte. Only the additive
        // incremental layer (the sidecar capture/read, and later the Piece E full-hit /
        // incremental / full-miss decision that will live here) is governed by this flag; the
        // author's deletion lifecycle (Context.IsUsingCache setter, StartupHelper) is untouched.
        //
        // This flag is additive to the four dev diagnostics above: those still gate their own
        // capture/report, but when ON they read their priors from the sidecar this toggle's
        // capture produced — a clean composition rather than a replacement.
        public static bool IncrementalCache = false;

        // Dev-only: let the live test harness flip the four incremental-cache diagnostic flags
        // at launch via environment variables, instead of requiring a bespoke build with the
        // defaults edited. The static ctor runs on first access to any field (i.e. before the
        // load-time patches read these flags), applying any GAGARIN_* override present. Field
        // initializers above run first, so an absent env var leaves the shipped default (OFF)
        // untouched — production never sets these, so behaviour is unchanged. These four flags
        // are the only ones overridable, and each changes no cache behaviour (they are pure
        // diagnostics; see each flag's header). Accepted truthy values: "1" or "true".
        //
        //   GAGARIN_CAPTURE_PROVENANCE   -> CaptureProvenance
        //   GAGARIN_DIRTYSET_DIAGNOSTIC  -> DirtySetDiagnostic
        //   GAGARIN_DIRTYSET_GATE        -> DirtySetGate
        //   GAGARIN_DIRTYSET_RECOMPUTE   -> DirtySetRecompute
        //   GAGARIN_METRICS              -> Metrics
        //   GAGARIN_INCREMENTAL_CACHE    -> IncrementalCache (the master toggle)
        static GagarinPrefs()
        {
            CaptureProvenance  = EnvFlag("GAGARIN_CAPTURE_PROVENANCE",  CaptureProvenance);
            DirtySetDiagnostic = EnvFlag("GAGARIN_DIRTYSET_DIAGNOSTIC", DirtySetDiagnostic);
            DirtySetGate       = EnvFlag("GAGARIN_DIRTYSET_GATE",       DirtySetGate);
            DirtySetRecompute  = EnvFlag("GAGARIN_DIRTYSET_RECOMPUTE",  DirtySetRecompute);
            Metrics            = EnvFlag("GAGARIN_METRICS",             Metrics);
            IncrementalCache   = EnvFlag("GAGARIN_INCREMENTAL_CACHE",   IncrementalCache);
        }

        // Returns the env-var override for a flag, or the current value when the var is unset.
        private static bool EnvFlag(string name, bool current)
        {
            string v = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(v))
                return current;
            return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
    }
}
