using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RocketMan.Optimizations
{
    [RocketPatch(typeof(StatWorker), "GetValueUnfinalized", parameters: new[] { typeof(StatRequest), typeof(bool) })]
    internal static class StatWorker_GetValueUnfinalized_Interrupt_Patch
    {
        public static HashSet<MethodBase> callingMethods = new HashSet<MethodBase>();

        public static MethodBase m_Interrupt =
            AccessTools.Method(typeof(StatWorker_GetValueUnfinalized_Interrupt_Patch), "Interrupt");

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Ldarg_1);
            yield return new CodeInstruction(OpCodes.Ldarg_2);
            yield return new CodeInstruction(OpCodes.Call, m_Interrupt);

            foreach (var code in instructions)
                yield return code;
        }

        public static void Interrupt(StatWorker statWorker, StatRequest req, bool applyPostProcess)
        {
            if (RocketPrefs.Learning && RocketDebugPrefs.StatLogging)
            {
                StackTrace trace = new StackTrace();
                StackFrame frame = trace.GetFrame(2);
                MethodBase method = frame.GetMethod();
                string handler = method.GetMethodPath();
                if (RocketDebugPrefs.Debug) Log.Message(string.Format("ROCKETMAN: called stats.GetUnfinalizedValue from {0}", handler));
                callingMethods.Add(method);
            }
        }
    }

    [RocketPatch]
    internal static class StatWorker_GetValueUnfinalized_Hijacked_Patch
    {
        private static bool shouldCache = false;

        private static bool unitExists = false;

        private static CachedUnit unit;

        private static int key;

        private static int signature;

        private static Dictionary<int, CachedUnit> cache = new Dictionary<int, CachedUnit>();

        private static MethodBase replacemant =
            AccessTools.Method(typeof(StatWorker_GetValueUnfinalized_Hijacked_Patch), nameof(Replacemant));

        [StructLayout(LayoutKind.Sequential, Size = 12)]
        private struct CachedUnit
        {
            public readonly float value;
            public readonly int signature;
            public readonly int tick;

            public CachedUnit(float value, int signature)
            {
                this.value = value;
                this.signature = signature;
                this.tick = GenTicks.TicksGame;
            }
        }

        public static void Dirty(Pawn pawn)
        {
            int signature = pawn.GetSignature(true);
            if (RocketDebugPrefs.Debug && RocketDebugPrefs.StatLogging)
                Log.Message(string.Format("ROCKETMAN: changed signature for pawn {0} to {1}", pawn, signature));
        }

        internal static IEnumerable<MethodBase> TargetMethodsUnfinalized()
        {
            foreach (Type t in typeof(StatWorker).AllLeafSubclasses())
            {
                if (t.IsAbstract)
                    continue;
                yield return AccessTools.Method(t, nameof(StatWorker.GetValueUnfinalized));
            }
            yield return AccessTools.Method(typeof(StatWorker), nameof(StatWorker.GetValueUnfinalized));
        }

        public static IEnumerable<MethodBase> TargetMethods()
        {
            return TargetMethodsUnfinalized()
                .Where(m => true && m != null && m.IsValidTarget())
                .ToHashSet();
        }

        public static float Replacemant(StatRequest req, bool applyPostProcess)
        {
            return 0f;
        }

        [Main.OnWorldLoaded]
        public static void PatchAll()
        {
            foreach (MethodBase method in TargetMethods())
            {
                Patch(method);
                Log.Message($"ROCKETMAN: Replaced {method.DeclaringType.FullName}:{method.Name}");
            }
        }

        private static void Patch(MethodBase original)
        {
            HarmonyLib.Patches info = Harmony.GetPatchInfo(original);

            IEnumerable<MethodBase> prefixes = info.Prefixes
                .OrderBy(p => -1 * p.priority)
                .Select(p => (MethodBase)p.PatchMethod);
            IEnumerable<MethodBase> postfixes = info.Postfixes
                .OrderBy(p => -1 * p.priority)
                .Select(p => (MethodBase)p.PatchMethod);
            IEnumerable<MethodBase> transpilers = info.Transpilers
                .OrderBy(p => -1 * p.priority)
                .Select(p => (MethodBase)p.PatchMethod);
            IEnumerable<MethodBase> finalizers = info.Finalizers
                .OrderBy(p => -1 * p.priority)
                .Select(p => (MethodBase)p.PatchMethod);
        }

        [HarmonyPriority(int.MaxValue)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Prefix(StatWorker __instance, ref float __result, StatRequest req, bool applyPostProcess)
        {
            var tick = GenTicks.TicksGame;
            if (true
                && RocketPrefs.Enabled
                && Current.Game != null
                && tick >= 600
                && !IgnoreMeDatabase.ShouldIgnore(__instance.stat))
            {
                key = Tools.GetKey(__instance, req, applyPostProcess);
                signature = req.thingInt?.GetSignature() ?? -1;

                if (!cache.TryGetValue(key, out unit))
                {
                    shouldCache = true;
                    unitExists = false;
                    return true;
                }

                if (tick - unit.tick - 1 > RocketStates.StatExpiry[__instance.stat.index] || signature != unit.signature)
                {
                    cache.Remove(key);
                    shouldCache = true;
                    unitExists = true;
                    return true;
                }
                __result = unit.value;
                return shouldCache = false;
            }
            shouldCache = false;
            return true;
        }

        [HarmonyPriority(int.MinValue)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Postfix(StatWorker __instance, ref float __result, StatRequest req)
        {
            if (shouldCache)
            {
                if (RocketPrefs.Learning && unitExists)
                {
                    float t = RocketStates.StatExpiry[__instance.stat.index];
                    float T = GenTicks.TicksGame - unit.tick;
                    float a = Mathf.Abs(__result - unit.value) / Mathf.Max(__result, unit.value, 1f);
                    RocketStates.StatExpiry[__instance.stat.index] =
                        Mathf.Clamp(
                            t - Mathf.Clamp(RocketPrefs.LearningRate * (T * a - t), -0.1f, 0.25f),
                            0f, 1024f);
                }
                cache[key] = new CachedUnit(__result, req.thingInt?.GetSignature() ?? -1);
                shouldCache = false;
            }
        }

        [Main.OnTickLonger]
        public static void OnTickLonger() => cache.Clear();
    }
}