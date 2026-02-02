using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RocketMan.Optimizations
{
    [RocketPatch(typeof(ListerBuildingsRepairable), nameof(ListerBuildingsRepairable.UpdateBuilding))]
    internal class ListerBuildingsRepairable_Patch
    {
        public static bool Prefix(Building b)
        {
            return b.def.building.repairable && b.def.useHitPoints;
        }
    }
}
