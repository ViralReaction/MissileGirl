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
using LudeonTK;
using Verse;

namespace MissileGirl
{
    public static class EditWindow_Log_DoMessagesListing_Patch
    {
        private static FieldInfo fLogMessageText = AccessTools.Field(typeof(LogMessage), nameof(LogMessage.text));

        public static void PatchEditWindow_Log()
        {
            try
            {
                Finder.Harmony.Patch(AccessTools.Method(typeof(EditWindow_Log), nameof(EditWindow_Log.DoMessagesListing)),
                                     transpiler: new HarmonyMethod(typeof(EditWindow_Log_DoMessagesListing_Patch), nameof(EditWindow_Log_DoMessagesListing_Patch.Transpiler)));
            }
            catch (Exception er) { Log.Warning($"<color=orange>MissileGirl:</color>[<color=red>NOTANERROR</color>] Unable to stylize logs: {er}"); }
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = instructions.ToList();
            bool finished = false;
            for (int i = 0; i < codes.Count; i++)
            {
                if (!finished)
                {
                    if (codes[i].opcode == OpCodes.Ldfld && codes[i].OperandIs(fLogMessageText))
                    {
                        yield return codes[i];
                        yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(LogUtility), nameof(LogUtility.StylizeRocketLog)));
                        finished = true;
                        continue;
                    }
                }
                yield return codes[i];
            }
            if (!finished)
            {
                throw new InvalidOperationException("MissileGirl: Failed to patch EditWindow_Log.DoMessagesListing; LogMessage.text load was not found.");
            }
        }
    }
}
