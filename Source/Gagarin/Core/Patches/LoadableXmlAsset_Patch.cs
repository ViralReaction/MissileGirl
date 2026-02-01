using System;
using System.IO;
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

            Finder.Harmony.Patch(AccessTools.Constructor(typeof(LoadableXmlAsset), [typeof(string), typeof(string)]), postfix: new HarmonyMethod(AccessTools.Method(typeof(LoadableXmlAsset_Constructor_Patch), nameof(Postfix_String))));

            // Is this one even needed? Game launches fine. Need more testing.
            Finder.Harmony.Patch(AccessTools.Constructor(typeof(LoadableXmlAsset), [typeof(FileInfo), typeof(ModContentPack)]), postfix: new HarmonyMethod(AccessTools.Method(typeof(LoadableXmlAsset_Constructor_Patch), nameof(Postfix_FileInfo))));
        }

        public static void Postfix_String(LoadableXmlAsset __instance, string name, string text)
        {
            Process(__instance, text);
        }

        // Is this one even needed? Game launches fine. Need more testing.
        public static void Postfix_FileInfo(LoadableXmlAsset __instance, FileInfo file, ModContentPack mod)
        {
            string contents = __instance.xmlDoc.OuterXml;
            Process(__instance, contents);
        }

        private static void Process(LoadableXmlAsset __instance, string text)
        {
            if (!Context.IsLoadingModXML && !Context.IsLoadingPatchXML) return;
            try
            {
                string current = AssetHashingUtility.CalculateHashMd5(text);
                UInt64 currentInt = AssetHashingUtility.CalculateHash(text);
                string id = __instance.GetLoadableId();

                lock (Context.AssetsHashes)
                {
                    if (!Context.AssetsHashes.TryGetValue(id, out string old) || !Context.AssetsHashesInt.TryGetValue(id, out ulong oldInt) || current != old || oldInt != currentInt)
                    {
                        try
                        {
                            if (GagarinEnvironmentInfo.CacheExists && Context.IsUsingCache)
                            {
                                string message = Context.IsLoadingPatchXML ? "Patches changed!" : "Asset changed!";

                                Log.Warning($"GAGARIN: {message}" + $"<color=red>{__instance.name}</color>:<color=red>{Context.CurrentLoadingMod?.PackageId ?? "Unknown"}</color> " + $"in {__instance.fullFolderPath}");
                            }
                        }
                        finally
                        {
                            Context.IsUsingCache = false;
                        }
                    }
                    Context.Assets.Add(id);
                    Context.AssetsHashes[id] = current;
                    Context.AssetsHashesInt[id] = currentInt;
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