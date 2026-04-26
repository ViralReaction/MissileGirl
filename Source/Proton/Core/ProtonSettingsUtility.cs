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
using MissileGirl;
using Verse;

namespace Proton
{
    public static class ProtonSettingsUtility
    {
        [Main.OnScribe]
        public static void OnScribe()
        {
            ProtonSettings settings = Context.Settings;
            Scribe_Deep.Look(ref settings, "protonSettings");
            Context.Settings = settings;
            RocketEnvironmentInfo.ProtonLoaded = true;
        }
    }
}
