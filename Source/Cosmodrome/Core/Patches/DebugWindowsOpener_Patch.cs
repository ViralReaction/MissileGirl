// // Copyright (c) 2026 ViralReaction
// //
// // This program and the accompanying materials are made available under the
// // terms of the Eclipse Public License 2.0 which is available at
// // http://www.eclipse.org/legal/epl-2.0.
// //
// // SPDX-License-Identifier: EPL-2.0

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace MissileGirl
{

    [RocketPatch(typeof(DebugWindowsOpener), methodType: MethodType.Constructor)]
    [StaticConstructorOnStartup]
    public static class DebugWindowsOpener_Patch
    {
        public static Action drawButtonsCached;

        public static FieldInfo mDrawButtonCache = AccessTools.Field(typeof(DebugWindowsOpener_Patch), nameof(DebugWindowsOpener_Patch.drawButtonsCached));

        static DebugWindowsOpener_Patch()
        {
            Finder.Harmony.Patch(AccessTools.Constructor(typeof(DebugWindowsOpener)), postfix: new HarmonyMethod(AccessTools.Method(typeof(DebugWindowsOpener_Patch), nameof(DebugWindowsOpener_Patch.Postfix))));

            Finder.Harmony.Patch(AccessTools.Method(typeof(DebugWindowsOpener), nameof(DebugWindowsOpener.DevToolStarterOnGUI)), transpiler: new HarmonyMethod(AccessTools.Method(typeof(DebugWindowsOpener_Patch), nameof(DebugWindowsOpener_Patch.Transpiler))));
        }

        public static void Postfix(DebugWindowsOpener __instance)
        {
            drawButtonsCached = () =>
            {
                __instance.DrawButtons();

                if (__instance.widgetRow.ButtonIcon(ContentFinder<Texture2D>.Get("MissileGirl/UI/missilegirl_debug_options_button", true), "Open " + "<color=orange>MissileGirls</color> hidden debug options."))
                {
                    if (Find.WindowStack.WindowOfType<Window_HiddenDebugMenu>() != null)
                    {
                        Find.WindowStack.RemoveWindowsOfType(typeof(Window_HiddenDebugMenu));
                    }
                    else
                    {
                        Find.WindowStack.Add(new Window_HiddenDebugMenu());
                    }
                }
            };
            __instance.drawButtonsCached = drawButtonsCached;
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            bool patched = false;
            bool replacedDrawButtons = false;
            foreach (CodeInstruction code in instructions)
            {
                if (!patched && code.opcode == OpCodes.Ldc_R4 && 28f.Equals(code.operand))
                {
                    yield return new CodeInstruction(OpCodes.Ldc_R4, 2f);
                    yield return new CodeInstruction(OpCodes.Add);
                    patched = true;
                }
                if (code.opcode == OpCodes.Ldfld && code.OperandIs(mDrawButtonCache))
                {
                    yield return new CodeInstruction(OpCodes.Ldsfld, mDrawButtonCache)
                    {
                        labels = code.labels
                    };
                    replacedDrawButtons = true;
                    continue;
                }
                yield return code;
            }
            if (!patched || !replacedDrawButtons)
            {
                Logger.Debug("MissileGirl: DebugWindowsOpener.DevToolStarterOnGUI patch did not find all expected IL anchors.");
            }
        }
    }
}
