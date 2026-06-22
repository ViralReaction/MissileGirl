// // Copyright (c) 2026 ViralReaction
// //
// // This program and the accompanying materials are made available under the
// // terms of the Eclipse Public License 2.0 which is available at
// // http://www.eclipse.org/legal/epl-2.0.
// //
// // SPDX-License-Identifier: EPL-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using HarmonyLib;
using MissileGirl;
using Verse;

namespace Gagarin
{
    public static class LoadedModManager_Patch
    {
        [GagarinPatch(typeof(LoadedModManager), nameof(LoadedModManager.LoadModXML))]
        public static class LoadModXML_Patch
        {
            public static void Prefix()
            {
                try
                {
                    Context.IsLoadingModXML = true;
                    if (File.Exists(GagarinEnvironmentInfo.HashFilePath))
                    {
                        Context.AssetsHashes = AssetHashingUtility.Load(GagarinEnvironmentInfo.HashFilePath);
                    }
                    if (File.Exists(GagarinEnvironmentInfo.HashFilePathInt))
                    {
                        Context.AssetsHashesInt = AssetHashingUtility.LoadInt(GagarinEnvironmentInfo.HashFilePathInt);
                    }
                }
                catch (Exception er)
                {
                    Context.IsUsingCache = false;
                    Logger.Debug("GAGARIN: Loading error", er);
                    throw;
                }
            }

            public static void Postfix(IEnumerable<LoadableXmlAsset> __result)
            {
                try
                {
                    Context.XmlAssets = new Dictionary<string, LoadableXmlAsset>();
                    foreach (KeyValuePair<string, LoadableXmlAsset> pair in __result.Select(a => new KeyValuePair<string, LoadableXmlAsset>(a.FullFilePath, a)))
                        Context.XmlAssets.Add(pair.Key, pair.Value);

                    if (Context.IsUsingCache && Context.Assets.Count != Context.AssetsHashes.Count)
                    {
                        Context.IsUsingCache = false;
                        Context.AssetsHashes.RemoveAll(a => !Context.Assets.Contains(a.Key));
                        Context.AssetsHashesInt.RemoveAll(a => !Context.Assets.Contains(a.Key));
                        Log.Warning("GAGARIN: Total number of files changed. Reseting cache");
                    }
                    if (!Context.IsUsingCache)
                    {
                        AssetHashingUtility.Dump(Context.AssetsHashes, GagarinEnvironmentInfo.HashFilePath);
                        AssetHashingUtility.Dump(Context.AssetsHashesInt, GagarinEnvironmentInfo.HashFilePathInt);
                        if (File.Exists(GagarinEnvironmentInfo.UnifiedXmlFilePath))
                            File.Delete(GagarinEnvironmentInfo.UnifiedXmlFilePath);
                    }
                    Context.IsLoadingModXML = false;
                }
                catch (Exception er)
                {
                    Logger.Debug("GAGARIN: Loading error", er);
                    throw;
                }
            }
        }

        [GagarinPatch(typeof(TKeySystem), nameof(TKeySystem.Parse))]
        public static class TKeySystem_Parse_Patch
        {
            public static void Postfix()
            {
                DuplicateHelper.QueueReportProcessing();
            }
        }

        [GagarinPatch(typeof(LoadedModManager), nameof(LoadedModManager.ClearCachedPatches))]
        public static class ClearCachedPatches_Patch
        {
            public static bool Prefix()
            {
                if (Context.IsUsingCache)
                {
                    foreach (var mod in Context.RunningMods)
                    {
                        if (mod.patches != null)
                        {
                            foreach (var patch in mod.patches)
                                patch.neverSucceeded = false;
                        }
                        mod.loadedAnyPatches = true;
                    }
                    return false;
                }
                return true;
            }
        }

        [GagarinPatch(typeof(LoadedModManager), nameof(LoadedModManager.ApplyPatches))]
        public static class ApplyPatches_Patch
        {
            [HarmonyPriority(Priority.Last)]
            public static bool Prefix()
            {
                try
                {
                    CachedDefHelper.Prepare();

                    // Provenance capture (dev-flag gated, cold load only): start a
                    // fresh graph and assign deterministic patchIds in the same
                    // order ApplyPatches enumerates the operations.
                    if (ProvenanceRecorder.Active)
                    {
                        ProvenanceRecorder.Reset();
                        ProvenanceRecorder.IndexPatches(Context.RunningMods);
                    }

                    return !Context.IsUsingCache;
                }
                catch (Exception er)
                {
                    Logger.Debug("GAGARIN: Loading error", er);
                    throw;
                }
            }

            public static void Postfix(XmlDocument xmlDoc)
            {
                if (!Context.IsUsingCache)
                {
                    try
                    {
                        if (File.Exists(GagarinEnvironmentInfo.UnifiedPatchedOriginalXmlPath))
                            File.Delete(GagarinEnvironmentInfo.UnifiedPatchedOriginalXmlPath);
                        XmlWriterSettings settings = new XmlWriterSettings
                        {
                            CheckCharacters = false,
                            Indent = true,
                            NewLineChars = "\n"
                        };
                        using (XmlWriter writer = XmlWriter.Create(GagarinEnvironmentInfo.UnifiedPatchedOriginalXmlPath, settings))
                        {
                            xmlDoc.Save(writer);
                        }
                    }
                    catch (Exception er)
                    {
                        Logger.Debug("GAGARIN: Loading error", er);
                        throw;
                    }
                }
            }
        }

        [GagarinPatch(typeof(LoadedModManager), nameof(LoadedModManager.ParseAndProcessXML))]
        public static class ParseAndProcessXML_Patch
        {
            [HarmonyPriority(Priority.Last)]
            public static void Postfix()
            {
                if (!Context.IsUsingCache)
                {
                    try
                    {
                        GagarinPrefs.CacheCreationTime = DateTime.Now;
                        GagarinSettings.WriteSettings();

                        CachedDefHelper.Save();

                        // Defs are now registered and inheritance resolved: flush
                        // the captured dependency graph (dev-flag gated, no-op
                        // otherwise).
                        ProvenanceRecorder.Save();

                        // M2b real-engine gate (dev-flag gated, no-op otherwise): the
                        // rebuilt Unified.xml now exists on disk, so prove the dirty set
                        // is a superset against it. Called here, right after Save, so the
                        // ordering is deterministic. Runs BEFORE the sidecar capture below,
                        // so it still reads the PRIOR sidecar (this run's files only become
                        // the prior once Capture overwrites them).
                        DirtySetGate.Run();

                        // Prior-state sidecar capture (master-toggle gated, no-op when
                        // GAGARIN_INCREMENTAL_CACHE is off). Copy the cache files just written
                        // above into a sidecar the author's startup teardown never deletes, so
                        // they are available as the PRIOR load's state on the next run —
                        // regardless of OnInitialization deleting the live cache on a modlist
                        // change. This is what lets the diagnostic/gate read true priors.
                        PriorStateSnapshot.Capture();
                    }
                    catch (Exception er)
                    {
                        Logger.Debug("GAGARIN: Loading error", er);
                        throw;
                    }
                }
            }
        }

        [GagarinPatch(typeof(LoadedModManager), nameof(LoadedModManager.CombineIntoUnifiedXML))]
        public static class CombineIntoUnifiedXML_Patch
        {
            private static bool usedCache = false;

            [HarmonyPriority(Priority.Last)]
            public static bool Prefix(List<LoadableXmlAsset> xmls, ref XmlDocument __result, Dictionary<XmlNode, LoadableXmlAsset> assetlookup)
            {
                try
                {
                    Context.DefsXmlAssets = assetlookup;
                    if (Prefs.LogVerbose)
                    {
                        Log.Warning($"GAGARIN: CombineIntoUnifiedXML has <color=red>Context.IsUsingCache={Context.IsUsingCache}</color>");
                    }
                    if (Context.IsUsingCache)
                    {
                        usedCache = true;
                        CachedDefHelper.Load(__result = new XmlDocument(), assetlookup);
                        return false;
                    }
                }
                catch (Exception er)
                {
                    Logger.Debug("GAGARIN: Loading error", er);
                    throw;
                }
                return true;
            }

            [HarmonyPriority(Priority.First)]
            public static void Postfix(XmlDocument __result, Dictionary<XmlNode, LoadableXmlAsset> assetlookup)
            {
                if (!usedCache && __result != null && !assetlookup.EnumerableNullOrEmpty())
                {
                    try
                    {
                        DuplicateHelper.ParseCreateReports(__result, assetlookup);
                    }
                    catch (Exception er)
                    {
                        Logger.Debug("GAGARIN: Loading error", er);
                        throw;
                    }
                }
            }
        }
    }
}
