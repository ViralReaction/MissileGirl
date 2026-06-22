// // Copyright (c) 2026 ViralReaction
// //
// // This program and the accompanying materials are made available under the
// // terms of the Eclipse Public License 2.0 which is available at
// // http://www.eclipse.org/legal/epl-2.0.
// //
// // SPDX-License-Identifier: EPL-2.0

// PriorStateSnapshotPathsTests.cs (Piece D — prior-state sidecar)
//
// Contains: offline coverage for the dependency-free sidecar helpers. The load-bearing
// invariant is that the sidecar lands OUTSIDE the author's Cache delete scope (so it survives
// OnInitialization's modlist-change teardown), plus the copy-if-exists primitive Capture relies
// on. The RimWorld-coupled bits (toggle gating, the real config-dir resolution, the gate/
// diagnostic redirect) cannot be exercised offline — the owner re-runs the live removal scale-up
// to confirm the gate now emits.

using System.IO;
using Gagarin;

namespace Gagarin.Tests
{
    [TestFixture]
    public class PriorStateSnapshotPathsTests
    {
        [Test]
        public void ResolvePriorDir_IsUnderConfigDir_ButOutsideCache()
        {
            // .../MissileGirl is the surviving config dir; .../MissileGirl/Cache is the only place
            // the author deletes. The sidecar must be inside the config dir yet NOT inside Cache.
            string configDir = Path.Combine("root", "MissileGirl");
            string cacheDir = Path.Combine(configDir, "Cache");

            string priorDir = PriorStateSnapshotPaths.ResolvePriorDir(configDir);

            Assert.That(priorDir, Does.StartWith(configDir),
                "sidecar must live under the surviving config dir");
            Assert.That(priorDir, Does.Not.StartWith(cacheDir),
                "sidecar must NOT live under Cache, which the author tears down on a cache miss");
        }

        [Test]
        public void ResolvePriorDir_UsesTheDocumentedSubtree()
        {
            string configDir = Path.Combine("root", "MissileGirl");

            string priorDir = PriorStateSnapshotPaths.ResolvePriorDir(configDir);

            string expected = Path.Combine(configDir,
                PriorStateSnapshotPaths.SidecarRootName, PriorStateSnapshotPaths.SidecarPriorName);
            Assert.That(priorDir, Is.EqualTo(expected));
        }

        [Test]
        public void CopyIfExists_ReturnsFalse_WhenSourceMissing()
        {
            string tmp = NewTempDir();
            try
            {
                string src = Path.Combine(tmp, "absent.xml");
                string dst = Path.Combine(tmp, "out.xml");

                bool copied = PriorStateSnapshotPaths.CopyIfExists(src, dst);

                Assert.That(copied, Is.False);
                Assert.That(File.Exists(dst), Is.False,
                    "no destination should be created when the source is absent");
            }
            finally { Directory.Delete(tmp, recursive: true); }
        }

        [Test]
        public void CopyIfExists_CopiesAndOverwrites_WhenSourcePresent()
        {
            string tmp = NewTempDir();
            try
            {
                string src = Path.Combine(tmp, "src.xml");
                string dst = Path.Combine(tmp, "dst.xml");
                File.WriteAllText(src, "new");
                File.WriteAllText(dst, "stale"); // pre-existing prior copy must be overwritten

                bool copied = PriorStateSnapshotPaths.CopyIfExists(src, dst);

                Assert.That(copied, Is.True);
                Assert.That(File.ReadAllText(dst), Is.EqualTo("new"));
            }
            finally { Directory.Delete(tmp, recursive: true); }
        }

        private static string NewTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "gagarin-sidecar-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
