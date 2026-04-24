// // Copyright (c) 2026 ViralReaction
// //
// // This program and the accompanying materials are made available under the
// // terms of the Eclipse Public License 2.0 which is available at
// // http://www.eclipse.org/legal/epl-2.0.
// //
// // SPDX-License-Identifier: EPL-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Verse;

namespace MissileGirl
{
    public partial class RocketMod : Mod
    {
        public void LoadSettings()
        {
            bool settingsFound = false;
            try
            {
                if (File.Exists(RocketEnvironmentInfo.RocketSettingsFilePath))
                {
                    Scribe.loader.InitLoading(RocketEnvironmentInfo.RocketSettingsFilePath);
                    try
                    {
                        Scribe_Deep.Look(ref RocketMod.Settings, "ModSettings");
                        settingsFound = RocketMod.Settings != null;
                        if (RocketMod.Settings == null)
                            RocketMod.Settings = new RocketSettings();
                    }
                    catch (Exception er)
                    {
                        Log.Error($"MissileGirl: Error while scribing settings {er}");
                        Logger.Debug("Error while scribing settings", exception: er);
                    }
                    finally
                    {
                        Scribe.loader.FinalizeLoading();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"MissileGirl: Caught exception while loading mod settings data for {Content.FolderName}. Generating fresh settings. The exception was: {ex.ToString()}");
                RocketMod.Settings = null;
            }
            if (RocketMod.Settings == null)
            {
                RocketMod.Settings = new RocketSettings();
            }
            if (!settingsFound)
            {
                WriteSettings();
            }
            foreach (var action in Main.onSettingsScribedLoaded)
            {
                try
                {
                    action.Invoke();
                }
                catch (Exception er)
                {
                    Log.Error($"MissileGirl: Error in post scribe {action} with error {er}");
                    Logger.Debug("Error in post scribe", exception: er);
                }
            }
        }

        public override void WriteSettings()
        {
            if (RocketPrefs.WarmingUp && !(WarmUpMapComponent.current?.Finished ?? true))
            {
                WarmUpMapComponent.current.AbortWarmUp();
            }
            Scribe.saver.InitSaving(RocketEnvironmentInfo.RocketSettingsFilePath, "SettingsBlock");
            try
            {
                Scribe_Deep.Look(ref RocketMod.Settings, "ModSettings");
            }
            catch (Exception er)
            {
                Log.Error($"MissileGirl: Error while scribing settings {er}");
                Logger.Debug("Error while scribing settings", exception: er);
            }
            finally
            {
                Scribe.saver.FinalizeSaving();
            }
            base.WriteSettings();
        }

        public class RocketSettings : IExposable
        {
            public void ExposeData()
            {
                ScribeRocketPrefs();
                ScribeExtras();
                if (Scribe.mode == LoadSaveMode.LoadingVars)
                {
                    UpdateExceptions();
                }
            }

            private void ScribeRocketPrefs()
            {
                string version = RocketAssembliesInfo.Version;
                bool upgrade = false;
                Scribe_Values.Look(ref version, "version", null, forceSave: true);
                if (version != RocketAssembliesInfo.Version && !RocketEnvironmentInfo.IsDevEnv)
                {
                    upgrade = true;
                    version = RocketAssembliesInfo.Version;
                }

                Scribe_Values.Look(ref RocketDebugPrefs.Debug, "debug", false);
                Scribe_Values.Look(ref RocketPrefs.Enabled, "enabled", true);
                Scribe_Values.Look(ref RocketPrefs.Learning, "learning", true);
                Scribe_Values.Look(ref RocketPrefs.FixBeauty, "FixBeauty", true);

                // Optimizations
                Scribe_Values.Look(ref RocketPrefs.DeepDrillOptimize, "DeepDrillOptimize", true);
                Scribe_Values.Look(ref RocketPrefs.TemperatureTickCheck, "TemperatureTickCheck", true);
                Scribe_Values.Look(ref RocketPrefs.BuildingRepairCheck, "BuildingRepairCheck", true);
                Scribe_Values.Look(ref RocketPrefs.NotifyPawnDamage, "NotifyPawnDamage", true);

                Scribe_Values.Look(ref RocketPrefs.StatGearCachingEnabled, "statGearCachingEnabled", true);
                Scribe_Values.Look(ref RocketPrefs.ShowWarmUpPopup, "showWarmUpPopup", true);
                Scribe_Values.Look(ref RocketPrefs.PauseAfterWarmup, "pauseAfterWarmup", false);
                Scribe_Values.Look(ref RocketPrefs.AlertThrottling, "alertThrottling", true);
                Scribe_Values.Look(ref RocketPrefs.DisableAllAlert, "disableAllAlert", false);
                Scribe_Values.Look(ref RocketPrefs.LearningAlertEnabled, "learningAlertEnabled", true);
                if (upgrade)
                {
                    RocketPrefs.FixBeauty = true;
                }
                if (!upgrade)
                {

                    Scribe_Values.Look(ref RocketPrefs.CorpsesRemovalEnabled, "corpsesRemovalEnabled", false);
                }

                Scribe_Values.Look(ref RocketPrefs.MainButtonToggle, "mainButtonToggle", true);
                Scribe_Values.Look(ref RocketPrefs.DisableForcedSlowdowns, "disableForcedSlowdowns", false);
                Scribe_Values.Look(ref RocketPrefs.TranslationCaching, "translationCaching", false);
            }

            private void ScribeExtras()
            {
                foreach (var action in Main.onScribe)
                {
                    try
                    {
                        action.Invoke();
                    }
                    catch (Exception er)
                    {
                        Log.Error($"MissileGirl: Error scribing settings with mod {Scribe.mode} in action {action} with error {er}");
                        Logger.Debug($"Error scribing settings with mod {Scribe.mode}", exception: er);
                    }
                }
            }
        }
    }
}
