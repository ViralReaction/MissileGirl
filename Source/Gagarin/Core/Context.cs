using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Xml;
using Microsoft.Build.Utilities;
using Verse;

namespace Gagarin
{
    public static class Context
    {
        private static bool _isUsingCache = false;

        public static bool IsUsingCache
        {
            get => _isUsingCache;
            set
            {
                if (!value && value != _isUsingCache)
                {
                    if (File.Exists(GagarinEnvironmentInfo.ModListFilePath))
                        File.Delete(GagarinEnvironmentInfo.ModListFilePath);
                    if (File.Exists(GagarinEnvironmentInfo.UnifiedXmlFilePath))
                        File.Delete(GagarinEnvironmentInfo.UnifiedXmlFilePath);
                    StackTrace trace = new StackTrace(1);
                    StringBuilder builder = new StringBuilder();
                    builder.Append("GAGARIN: <color=yello>Cache disabled from</color>");
                    for (int i = 0; i < trace.FrameCount; i++)
                    {
                        var frame = trace.GetFrame(i);
                        builder.AppendInNewLine($"{frame.GetMethod().DeclaringType?.FullName}:{frame.GetMethod().Name}():{frame.GetFileLineNumber()}");
                    }
                    Log.Warning(builder.ToString());
                    RocketMan.Logger.Debug(builder.ToString());
                }
                _isUsingCache = value;
            }
        }

        private static bool _isLoadingModXML = false;

        public static bool IsLoadingModXML
        {
            get => _isLoadingModXML;
            set
            {
                if (!value && value != _isLoadingModXML)
                {
                    CurrentLoadingMod = null;
                }
                _isLoadingModXML = value;
            }
        }

        private static bool _isLoadingPatchXML = false;

        public static bool IsLoadingPatchXML
        {
            get => _isLoadingPatchXML;
            set
            {
                if (!value && value != _isLoadingPatchXML)
                {
                    CurrentLoadingMod = null;
                }
                _isLoadingPatchXML = value;
            }
        }

        public static bool IsRecovering = false;

        public static bool LoadingFinished = false;

        public static ModContentPack Core;        

        public static GagarinSettings Settings;

        public static Dictionary<XmlNode, LoadableXmlAsset> DefsXmlAssets = new Dictionary<XmlNode, LoadableXmlAsset>();

        public static Dictionary<string, LoadableXmlAsset> XmlAssets = new Dictionary<string, LoadableXmlAsset>();

        public static List<ModContentPack> RunningMods = new List<ModContentPack>();

        public static HashSet<string> Assets = new HashSet<string>();

        public static Dictionary<string, string> AssetsHashes = new Dictionary<string, string>();

        public static Dictionary<string, UInt64> AssetsHashesInt = new Dictionary<string, UInt64>();

        public static ModContentPack CurrentLoadingMod;
    }
}
