using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using RocketMan;
using Verse;

namespace Gagarin
{
    public static class LoadableXmlAsset_Constructor_Patch
    {
        [Main.OnInitialization]
        public static void Start()
        {
            Finder.Harmony.Patch(AccessTools.Constructor(typeof(LoadableXmlAsset), new[] { typeof(string), typeof(string), typeof(string) }),
                postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(LoadableXmlAsset_Constructor_Patch), nameof(LoadableXmlAsset_Constructor_Patch.Postfix))));
        }

        public static void Postfix(LoadableXmlAsset __instance, string contents)
        {
            if (Context.IsLoadingModXML || Context.IsLoadingPatchXML)
            {
                try
                {
                    string current    = AssetHashingUtility.CalculateHashMd5(contents);
                    UInt64 currentInt = AssetHashingUtility.CalculateHash(contents);
                    string id         = __instance.GetLoadableId();

                    lock (Context.AssetsHashes)
                    {
                        if (!Context.AssetsHashes.TryGetValue(id, out string old) || !Context.AssetsHashesInt.TryGetValue(id, out ulong oldInt) || current != old || oldInt != currentInt)
                        {
                            try
                            {
                                if (GagarinEnvironmentInfo.CacheExists && Context.IsUsingCache)
                                {
                                    string message = Context.IsLoadingPatchXML ? "Patches changed!" : "Asset changed!";

                                    Log.Warning($"GAGARIN: {message}" +
                                        $"<color=red>{__instance.name}</color>:<color=red>{Context.CurrentLoadingMod?.PackageId ?? "Unknown"}</color> " +
                                        $"in {__instance.fullFolderPath}");
                                }
                            }
                            finally
                            {
                                Context.IsUsingCache = false;
                            }                            
                        }
                        Context.Assets.Add(id);
                        Context.AssetsHashes[id] = current;
                        Context.AssetsHashesInt[id] = (ulong)currentInt;
                    }
                }
                catch (Exception er)
                {
                    Context.IsUsingCache = false;
                    Logger.Debug("GAGARIN: Failed in LoadableXmlAsset", exception: er);
                }
            }
        }
    }
}
