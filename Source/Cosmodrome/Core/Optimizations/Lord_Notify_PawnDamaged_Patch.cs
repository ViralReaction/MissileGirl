using RimWorld;
using Verse;
using Verse.AI.Group;
namespace RocketMan.Optimizations
{
    [RocketPatch(typeof(Lord), nameof(Lord.Notify_PawnDamaged))]
    internal class Lord_Notify_PawnDamaged_Patch
    {

        public static bool Prefix(Pawn victim)
        {
            LordToil L = victim.GetLord().CurLordToil;
            return L is not LordToil_AssaultColony && L is not LordToil_AssaultColonySappers;

        }
    }
}
