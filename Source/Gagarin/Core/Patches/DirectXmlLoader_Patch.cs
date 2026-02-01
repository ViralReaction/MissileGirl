using System;
using System.Xml;
using RocketMan;
using Verse;

namespace Gagarin
{
    public static class DirectXmlToObjectNew_Patch
    {
        [GagarinPatch(typeof(DirectXmlToObjectNew), nameof(DirectXmlToObjectNew.DefFromNodeNew))]
        public static class DirectXmlToObjectNew_DefFromNodeNew_Patch
        {
            public static void Postfix(XmlNode node, LoadableXmlAsset loadingAsset, Def __result)
            {
                if (!Context.IsUsingCache && __result != null)
                {
                    try
                    {
                        CachedDefHelper.Register(__result, node, loadingAsset);
                    }
                    catch (Exception er)
                    {
                        Logger.Debug("GAGARIN: Failed in LoadableXmlAsset", exception: er);
                        Context.IsUsingCache = false;
                    }
                }
            }
        }
    }

}
