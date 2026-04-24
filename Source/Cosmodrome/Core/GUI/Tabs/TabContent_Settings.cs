// // Copyright (c) 2026 ViralReaction
// //
// // This program and the accompanying materials are made available under the
// // terms of the Eclipse Public License 2.0 which is available at
// // http://www.eclipse.org/legal/epl-2.0.
// //
// // SPDX-License-Identifier: EPL-2.0

using System;
using System.IO;
using RimWorld;
using UnityEngine;
using Verse;

namespace MissileGirl.Tabs
{
    public class TabContent_Settings : ITabContent
    {
        public override Texture2D Icon => TexTab.Settings;
        public override string Label => KeyedResources.MissileGirl_Tab;
        public override bool ShouldShow => true;

        private Texture2D _graphic;
        private Texture2D Graphic => _graphic ??= ContentFinder<Texture2D>.Get("MissileGirl/UI/missilegirl_main_nobackground", true);

        private const float BannerHeight = 200f;
        private const float BannerGap = 15f;

        private static readonly Listing_Collapsible.Group_Collapsible _group = new Listing_Collapsible.Group_Collapsible();
        private static readonly Listing_Collapsible _general = new Listing_Collapsible(expanded: true);
        private static readonly Listing_Collapsible _genMap = new Listing_Collapsible(_group);
        private static readonly Listing_Collapsible _junk = new Listing_Collapsible(_group);
        private static readonly Listing_Collapsible _statCache = new Listing_Collapsible(_group);
        private static readonly Listing_Collapsible _optimization = new Listing_Collapsible(_group);
        private static readonly Listing_Collapsible _experimental = new Listing_Collapsible(_group);
        private static readonly Listing_Collapsible _debug = new Listing_Collapsible(_group);

        public override void DoContent(Rect rect)
        {
            GUIUtility.ExecuteSafeGUIAction(() =>
            {
                if (RocketPrefs.WarmingUp)
                {
                    GUIUtility.ExecuteSafeGUIAction(() =>
                    {
                        GUIFont.Font = GUIFontSize.Medium;
                        GUIFont.Anchor = TextAnchor.MiddleCenter;
                        if (Find.TickManager.Paused)
                        {
                            Widgets.Label(rect, KeyedResources.MissileGirl_Settings_PleaseWait);
                        }
                        else
                        {
                            Widgets.Label(rect, KeyedResources.MissileGirl_Settings_PleaseUnpause);
                        }
                    });
                    return;
                }

                // Banner
                Widgets.DrawTextureFitted(new Rect(rect.x, rect.y, rect.width - 180f, BannerHeight), Graphic, 1.0f);
                rect.yMin += BannerHeight + BannerGap;

                // Scroll view
                DrawSections(rect);
            });
        }

        private static void DrawSections(Rect inRect)
        {
            GUIUtility.ExecuteSafeGUIAction(() =>
            {
                _general.Expanded = true;
                _general.Begin(inRect, KeyedResources.MissileGirl_Settings, drawIcon: false, drawInfo: false);

                if (_general.CheckboxLabeled(KeyedResources.MissileGirl_Enable, ref RocketPrefs.Enabled))
                {
                    RocketMod.ResetRocketDebugPrefs();
                }

                if (_general.CheckboxLabeled("MissileGirl.ShowIcon".Translate(), ref RocketPrefs.MainButtonToggle, "MissileGirl.ShowIcon.Description".Translate()))
                {
                    MainButtonDef btn = DefDatabase<MainButtonDef>.GetNamed("RocketWindow", errorOnFail: false);
                    if (btn != null)
                    {
                        btn.buttonVisible = RocketPrefs.MainButtonToggle;
                        Logger.Message($"MissileGirl: <color=red>MainButton</color> is now " + $"{(RocketPrefs.MainButtonToggle ? "shown" : "hidden")}!");
                    }
                }

                _general.CheckboxLabeled("MissileGirl.ProgressBar".Translate(), ref RocketPrefs.ShowWarmUpPopup, "MissileGirl.ProgressBar.Description".Translate());

                _general.End(ref inRect);
                inRect.yMin += 5;

                if (Find.World != null)
                {
                    WorldInfoComponent info = Find.World.GetComponent<WorldInfoComponent>();
                    _genMap.Begin(inRect, KeyedResources.MissileGirl_GenMapSize);
                    _genMap.Label(KeyedResources.MissileGirl_GenMapSize_Text);
                    _genMap.Line(1);
                    _genMap.Label(KeyedResources.MissileGirl_GenMapSize_Note);
                    _genMap.Columns(18, [
                        rect =>
                        {
                            GUIFont.Anchor = TextAnchor.MiddleLeft;
                            float a = info.InitialMapWidth;
                            string buf = $"{a}";
                            Widgets.Label(rect, KeyedResources.MissileGirl_GenMapSize_Width);
                            Widgets.TextFieldNumeric(rect.RightHalf(), ref a, ref buf, 0, 1000);
                            if ((int)a != info.InitialMapWidth)
                            {
                                info.InitialMapWidth = (int)a;
                                info.useCustomMapSizes = true;
                            }
                        },
                        rect =>
                        {
                            GUIFont.Anchor = TextAnchor.MiddleLeft;
                            float a = info.InitialMapHeight;
                            string buf = $"{a}";
                            Widgets.Label(rect.MoveTopLeftCorner(25f, 0), KeyedResources.MissileGirl_GenMapSize_Height);
                            Widgets.TextFieldNumeric(rect.RightHalf(), ref a, ref buf, 0, 1000);
                            if ((int)a == info.InitialMapHeight) return;
                            info.InitialMapHeight = (int)a;
                            info.useCustomMapSizes = true;
                        }
                    ], useMargins: true);
                    _genMap.End(ref inRect);
                    inRect.yMin += 5;
                }

                if (!RocketPrefs.Enabled) return;



                _statCache.Begin(inRect, "MissileGirl.StatCacheSettings".Translate());
                _statCache.CheckboxLabeled("MissileGirl.Adaptive".Translate(), ref RocketPrefs.Learning, "MissileGirl.Adaptive.Description".Translate());
                _statCache.CheckboxLabeled("MissileGirl.AdaptiveAlert.Label".Translate(), ref RocketPrefs.LearningAlertEnabled, "MissileGirl.AdaptiveAlert.Description".Translate());
                _statCache.CheckboxLabeled("MissileGirl.EnableGearStatCaching".Translate(), ref RocketPrefs.StatGearCachingEnabled);
                _statCache.End(ref inRect);
                inRect.yMin += 5;

                _optimization.Begin(inRect, "MissileGirl.OptimizationSettings".Translate());
                _optimization.CheckboxLabeled(KeyedResources.MissileGirl_FixBeauty, ref RocketPrefs.FixBeauty, KeyedResources.MissileGirl_FixBeauty_Tip);
                _optimization.CheckboxLabeled(KeyedResources.MissileGirl_DeepDrillOptimize, ref RocketPrefs.DeepDrillOptimize, KeyedResources.MissileGirl_DeepDrillOptimize_Tip);
                _optimization.CheckboxLabeled(KeyedResources.MissileGirl_TemperatureTickCheck, ref RocketPrefs.TemperatureTickCheck, KeyedResources.MissileGirl_TemperatureTickCheck_Tip);
                _optimization.CheckboxLabeled(KeyedResources.MissileGirl_BuildingRepairCheck, ref RocketPrefs.BuildingRepairCheck, KeyedResources.MissileGirl_BuildingRepairCheck_Tip);
                _optimization.CheckboxLabeled(KeyedResources.MissileGirl_NotifyPawnDamage, ref RocketPrefs.NotifyPawnDamage, KeyedResources.MissileGirl_NotifyPawnDamage_Tip);
                _optimization.End(ref inRect);
                inRect.yMin += 5;

                if (Prefs.DevMode || RocketEnvironmentInfo.IsDevEnv)
                {
                    _experimental.Begin(inRect, KeyedResources.MissileGirl_Experimental);
                    bool devKeyEnabled = File.Exists(RocketEnvironmentInfo.DevKeyFilePath);
                    if (_experimental.CheckboxLabeled(KeyedResources.MissileGirl_Experimental_OptInBeta, ref devKeyEnabled))
                    {
                        switch (devKeyEnabled)
                        {
                            case false when File.Exists(RocketEnvironmentInfo.DevKeyFilePath):
                                File.Delete(RocketEnvironmentInfo.DevKeyFilePath);
                                break;
                            case true when !File.Exists(RocketEnvironmentInfo.DevKeyFilePath):
                                File.WriteAllText(RocketEnvironmentInfo.DevKeyFilePath, "enabled");
                                break;
                        }
                    }
                    _experimental.End(ref inRect);
                    inRect.yMin += 5;
                }
                if (RocketEnvironmentInfo.IsDevEnv)
                {
                    _junk.Begin(inRect, "MissileGirl.Junk".Translate());
                    _junk.CheckboxLabeled("MissileGirl.CorpseRemoval".Translate(), ref RocketPrefs.CorpsesRemovalEnabled, "MissileGirl.CorpseRemoval.Description".Translate());
                    _junk.End(ref inRect);
                    inRect.yMin += 5;
                }

                _debug.Begin(inRect, "Debugging options");
                if (_debug.CheckboxLabeled("MissileGirl.Debugging".Translate(), ref RocketDebugPrefs.Debug, "MissileGirl.Debugging.Description".Translate()) && !RocketDebugPrefs.Debug)
                {
                    RocketMod.ResetRocketDebugPrefs();
                }
                if (RocketDebugPrefs.Debug)
                {
                    _debug.Line(1);
                    _debug.CheckboxLabeled("Enable Stat Logging (Will kill performance)", ref RocketDebugPrefs.StatLogging);
                    _debug.Gap();
                }
                _debug.End(ref inRect);
            });
        }

        public override void OnSelect() => base.OnSelect();
        public override void OnDeselect() => base.OnDeselect();

        [Main.YieldTabContent]
        [Main.YieldModMenuTab]
        public static ITabContent YieldTab() => new TabContent_Settings();
    }
}
