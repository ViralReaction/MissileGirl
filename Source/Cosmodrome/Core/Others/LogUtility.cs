// // Copyright (c) 2026 ViralReaction
// //
// // This program and the accompanying materials are made available under the
// // terms of the Eclipse Public License 2.0 which is available at
// // http://www.eclipse.org/legal/epl-2.0.
// //
// // SPDX-License-Identifier: EPL-2.0

using System;
namespace MissileGirl
{
    public static class LogUtility
    {
        private static readonly int _MissileGirl_HEADER_LENGTH = "MissileGirl:".Length;
        private static readonly int _SOYUZ_HEADER_LENGTH = "SOYUZ:".Length;
        private static readonly int _ROCKETERR_HEADER_LENGTH = "ROCKETEER:".Length;
        private static readonly int _PROTON_HEADER_LENGTH = "PROTON:".Length;
        private static readonly int _GAGARIN_HEADER_LENGTH = "GAGARIN:".Length;
        private static string rocketColor = "orange";

        public static string StylizeRocketLog(this string text)
        {
            int startIndex;
            string replacement;
            try
            {
                if (text.StartsWith("MissileGirl:"))
                {
                    replacement = $"<color={rocketColor}>MissileGirl:</color> ";
                    startIndex = _MissileGirl_HEADER_LENGTH;
                }
                else if (text.StartsWith("SOYUZ:"))
                {
                    replacement = $"<color={rocketColor}>MissileGirl</color>+<color=red>SOYUZ:</color> ";
                    startIndex = _SOYUZ_HEADER_LENGTH;
                }
                else if (text.StartsWith("ROCKETEER:"))
                {
                    replacement = $"<color={rocketColor}>MissileGirl</color>+<color=yellow>ROCKETEER:</color> ";
                    startIndex = _ROCKETERR_HEADER_LENGTH;
                }
                else if (text.StartsWith("PROTON:"))
                {
                    replacement = $"<color={rocketColor}>MissileGirl</color>+<color=green>PROTON:</color> ";
                    startIndex = _PROTON_HEADER_LENGTH;
                }
                else if (text.StartsWith("GAGARIN:"))
                {
                    replacement = $"<color={rocketColor}>MissileGirl</color>+<color=blue>GAGARIN:</color>[<color=red>EXPERIMENTAL</color>] ";
                    startIndex = _GAGARIN_HEADER_LENGTH;
                }
                else return text;
                return replacement + text.Substring(startIndex).Trim();
            }
            catch { }
            return text;
        }
    }
}
