using RimWorld;

namespace RocketMan.Optimizations
{
    [RocketPatch(typeof(CompDeepDrill), nameof(CompDeepDrill.CanDrillNow))]
    internal class CompDeepDrill_Patch
    {

        public static bool Prefix(CompDeepDrill __instance, ref bool __result)
        {
            __result = (__instance.powerComp == null || __instance.powerComp.PowerOn) && (__instance.parent.Map.Biome.hasBedrock || __instance.ValuableResourcesPresent());
            return false;
        }
    }
}
