// // Copyright (c) 2026 ViralReaction
// //
// // This program and the accompanying materials are made available under the
// // terms of the Eclipse Public License 2.0 which is available at
// // http://www.eclipse.org/legal/epl-2.0.
// //
// // SPDX-License-Identifier: EPL-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MissileGirl.Patches
{
    public class BeautyUtility_Patch
    {
        static Pawn curPawn;

        [RocketPatch(typeof(BeautyUtility), nameof(BeautyUtility.FillBeautyRelevantCells))]
        public class BeautyUtility_FillBeautyRelevantCells_Patch
        {
            static FieldInfo fSampleNumCells_Beauty = AccessTools.Field(typeof(BeautyUtility), nameof(BeautyUtility.SampleNumCells_Beauty));
            static MethodInfo mGetSampleNumCells = AccessTools.Method(typeof(BeautyUtility_FillBeautyRelevantCells_Patch), nameof(GetSampleNumCells));

            static int MinSampleNumCells = GenRadial.NumCellsInRadius(2.6f);
            static int MaxSampleNumCells = GenRadial.NumCellsInRadius(6.9f);

            static int GetSampleNumCells()
            {
                if (!RocketPrefs.Enabled || !RocketPrefs.FixBeauty || curPawn == null || curPawn.pather == null)
                {
                    return BeautyUtility.SampleNumCells_Beauty;
                }
                if (curPawn.InBed() || curPawn.Downed)
                {
                    return MinSampleNumCells;
                }
                return Mathf.CeilToInt(Mathf.Lerp(MinSampleNumCells, MaxSampleNumCells, (GenTicks.TicksGame - curPawn.pather.lastMovedTick) / 60));
            }

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                List<CodeInstruction> codes = instructions.ToList();
                bool finished = false;

                for (int i = 0; i < codes.Count; i++)
                {
                    if (!finished)
                    {
                        if (codes[i].opcode == OpCodes.Ldsfld && codes[i].OperandIs(fSampleNumCells_Beauty))
                        {
                            finished = true;
                            yield return new CodeInstruction(OpCodes.Call, mGetSampleNumCells).MoveLabelsFrom(codes[i]).MoveBlocksFrom(codes[i]);
                            continue;
                        }
                    }
                    yield return codes[i];
                }
                if (!finished)
                {
                    throw new InvalidOperationException("MissileGirl: Failed to patch BeautyUtility.FillBeautyRelevantCells; BeautyUtility.SampleNumCells_Beauty load was not found.");
                }
            }
        }

        [RocketPatch(typeof(Pawn_NeedsTracker), nameof(Pawn_NeedsTracker.NeedsTrackerTickInterval))]
        public class Pawn_NeedsTracker_Patch
        {
            public static void Prefix(Pawn_NeedsTracker __instance)
            {
                curPawn = __instance.pawn;
            }

            public static void Finalizer(Exception __exception)
            {
                curPawn = null;
            }
        }
    }
}
