// // Copyright (c) 2026 ViralReaction
// //
// // This program and the accompanying materials are made available under the
// // terms of the Eclipse Public License 2.0 which is available at
// // http://www.eclipse.org/legal/epl-2.0.
// //
// // SPDX-License-Identifier: EPL-2.0

using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Proton
{
    public static class Context
    {
        public static readonly object settingsLock = new object();
        public static ProtonSettings settingsInt;
        public static ProtonSettings Settings
        {
            get
            {
                if (settingsInt == null)
                {
                    lock (settingsLock)
                    {
                        if (settingsInt == null)
                            settingsInt = new ProtonSettings();
                    }
                }
                return settingsInt;
            }
            set => settingsInt = value;
        }

        public static Dictionary<string, AlertSettings> TypeIdToSettings = new Dictionary<string, AlertSettings>();
        public static Dictionary<Alert, AlertSettings> AlertToSettings = new Dictionary<Alert, AlertSettings>();

        public static AlertsReadout ReadoutInstance;

        public static AlertSettings[] AlertSettingsByIndex;
        public static Alert[] Alerts;
    }
}
