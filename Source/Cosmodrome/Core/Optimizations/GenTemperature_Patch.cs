using System.Collections.Generic;
using Verse;
namespace RocketMan.Optimizations
{
    [RocketPatch(typeof(GenTemperature), nameof(GenTemperature.ComfortableTemperatureRange), parameters = [typeof(Pawn)])]
    internal class GenTemperature_Patch
    {
        // ReSharper disable once FieldCanBeMadeReadOnly.Local
        private static Dictionary<int, FloatRange> tempCache = new Dictionary<int, FloatRange>();
        private static int LastTick = 0;

        public static bool Prefix(Pawn p, ref FloatRange __result)
        {

            if (LastTick != Find.TickManager.TicksGame)
            {
                LastTick = Find.TickManager.TicksGame;
                tempCache.Clear();
            }

            if (!tempCache.TryGetValue(p.thingIDNumber, out FloatRange result)) return true;
            __result = result;
            return false;

        }

        public static void Postfix(Pawn p, FloatRange __result)
        {
            // ReSharper disable once CanSimplifyDictionaryLookupWithTryAdd
            if (!tempCache.ContainsKey(p.thingIDNumber))
            {
                tempCache.Add(p.thingIDNumber, __result);
            }
        }
    }
}
