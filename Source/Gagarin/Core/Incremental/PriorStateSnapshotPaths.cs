// // Copyright (c) 2026 ViralReaction
// //
// // This program and the accompanying materials are made available under the
// // terms of the Eclipse Public License 2.0 which is available at
// // http://www.eclipse.org/legal/epl-2.0.
// //
// // SPDX-License-Identifier: EPL-2.0

// PriorStateSnapshotPaths.cs (Piece D — prior-state sidecar, pure helpers)
//
// Contains: the dependency-free pieces of the prior-state sidecar — the path arithmetic that
// places the sidecar OUTSIDE the author's delete scope, and the copy-if-exists primitive.
//
// Why split from PriorStateSnapshot: that class touches RimWorld-coupled types
// (RocketEnvironmentInfo, GagarinEnvironmentInfo, GagarinPrefs, MissileGirl.Logger) so it cannot
// be compiled into the offline net8.0 NUnit project. These two helpers depend only on
// System.IO, so they live here and are unit-tested directly (PriorStateSnapshotPathsTests),
// mirroring how the rest of the incremental layer keeps its load-bearing logic dependency-free.

using System.IO;

namespace Gagarin
{
    public static class PriorStateSnapshotPaths
    {
        // The sidecar subtree, relative to the surviving config dir. A dedicated subtree so it
        // cannot collide with anything the author or the metrics workstream writes there.
        public const string SidecarRootName = "Incremental";
        public const string SidecarPriorName = "prior";

        // Given the surviving config dir (RocketEnvironmentInfo.CustomConfigFolderPath, i.e.
        // .../MissileGirl), return the sidecar prior directory: .../MissileGirl/Incremental/prior.
        // The author's teardown only ever deletes files INSIDE .../MissileGirl/Cache, so this
        // sibling of Cache survives — the property the offline test pins.
        public static string ResolvePriorDir(string customConfigFolderPath)
        {
            return Path.Combine(customConfigFolderPath, SidecarRootName, SidecarPriorName);
        }

        // Copy src -> dst (overwriting) when src exists; returns whether it copied. Throws on IO
        // error so the caller's try/catch records it. The only file-IO bit exercisable offline.
        public static bool CopyIfExists(string src, string dst)
        {
            if (!File.Exists(src))
                return false;
            File.Copy(src, dst, overwrite: true);
            return true;
        }
    }
}
