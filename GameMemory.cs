﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LiveSplit.Skyrim
{
    class GameMemory
    {
        public enum SplitArea : int
        {
            None,
            Helgen,
            Whiterun,
            ThalmorEmbassy,
            Esbern,
            Riverwood,
            TheWall,
            Septimus,
            MzarkTower,
            ClearSky,
            HorseClimb,
            CutsceneStart,
            CutsceneEnd,
            Alduin1,
            HighHrothgar,
            Solitude,
            Windhelm,
            Council,
            Odahviing,
            EnterSovngarde,
            CollegeOfWinterholdQuestlineCompleted,
            CompanionsQuestlineCompleted,
            DarkBrotherhoodQuestlineCompleted,
            ThievesGuildQuestlineCompleted,
            AlduinDefeated
        }

        public event EventHandler OnFirstLevelLoading;
        public event EventHandler OnPlayerGainedControl;
        public event EventHandler OnLoadStarted;
        public event EventHandler OnLoadFinished;
        // public event EventHandler OnLoadScreenStarted;
        // public event EventHandler OnLoadScreenFinished;
        public delegate void SplitCompletedEventHandler(object sender, SplitArea type, uint frame);
        public event SplitCompletedEventHandler OnSplitCompleted;
        public event EventHandler OnBearCart;

        private Task _thread;
        private CancellationTokenSource _cancelSource;
        private SynchronizationContext _uiThread;
        private List<int> _ignorePIDs;
        private SkyrimSettings _settings;

        private DeepPointer _isLoadingPtr;
        private DeepPointer _isLoadingScreenPtr;
        private DeepPointer _isInFadeOutPtr;
        private DeepPointer _locationIDPtr;
        private DeepPointer _world_XPtr;
        private DeepPointer _world_YPtr;
        private DeepPointer _isAlduin2DefeatedPtr;
        private DeepPointer _questlinesCompletedPtr;
        private DeepPointer _collegeOfWinterholdQuestsCompletedPtr;
        private DeepPointer _companionsQuestsCompletedPtr;
        private DeepPointer _darkBrotherhoodQuestsCompletedPtr;
        private DeepPointer _thievesGuildQuestsCompletedPtr;
        private DeepPointer _isInEscapeMenuPtr;
        private DeepPointer _mainQuestsCompletedPtr;
        private DeepPointer _wordsOfPowerLearnedPtr;
        private DeepPointer _Alduin1HealthPtr;
        private DeepPointer _locationsDiscoveredPtr;
        private DeepPointer _arePlayerControlsDisablePtr;
        private DeepPointer _bearCartHealthPtr;

        private enum Locations
        {
            Tamriel = 0x0000003C,
            Sovngarde = 0x0002EE41,
            HelgenKeep01 = 0x0005DE24,
            WhiterunWorld = 0x0001A26F,
            ThalmorEmbassy02 = 0x0007DCFC,
            WhiterunDragonsreach = 0x000165A3,
            RiftenWorld = 0x00016BB4,
            RiftenRatway01 = 0x0003B698,
            RiverwoodSleepingGiantInn = 0x000133C6,
            KarthspireRedoubtWorld = 0x00035699,
            SkyHavenTemple = 0x000161EB,
            SeptimusSignusOutpost = 0x0002D4E4,
            TowerOfMzark = 0x0002D4E3,
            HighHrothgar = 0x00087764,
            SolitudeWorld = 0x00037EDF,
            SolitudeCastleDour = 0x000213A0,
            WindhelmWorld = 0x0001691D,
            WindhelmPalaceoftheKings = 0x0001677C,
            SkuldafnWorld = 0x000278DD,
        }

        private enum ExpectedDllSizes
        {
            SkyrimSteam = 27336704,
            SkyrimCracked = 26771456,
        }

        public bool[] splitStates { get; set; }
        bool isSkyHavenTempleVisited = false;
        bool isAlduin1Defeated = false;
        int leaveSleepingGiantInnCounter = 0;
        bool isCouncilDone = false;

        public void resetSplitStates()
        {
            for (int i = 0; i <= (int)SplitArea.AlduinDefeated; i++)
            {
                splitStates[i] = false;
            }
            isSkyHavenTempleVisited = false;
            isAlduin1Defeated = false;
            leaveSleepingGiantInnCounter = 0;
            isCouncilDone = false;
        }

        public GameMemory(SkyrimSettings componentSettings)
        {
            _settings = componentSettings;
            splitStates = new bool[(int)SplitArea.AlduinDefeated + 1];

            // Loads
            _isLoadingPtr = new DeepPointer(0x17337CC); // == 1 if a load is happening (any except loading screens in Helgen for some reason)
            _isLoadingScreenPtr = new DeepPointer(0xEE3561); // == 1 if in a loading screen
            _isInFadeOutPtr = new DeepPointer(0x172EE2E); // == 1 when in a fadeout, it goes back to 0 once control is gained

            // Position
            _locationIDPtr = new DeepPointer(0x01738308, 0x4, 0x78, 0x670, 0xEC); // ID of the current location (see http://steamcommunity.com/sharedfiles/filedetails/?id=148834641 or http://www.skyrimsearch.com/cells.php)
            _world_XPtr = new DeepPointer(0x0172E864, 0x64); // X world position (cell)
            _world_YPtr = new DeepPointer(0x0172E864, 0x68); // Y world position (cell)

            // Game state
            _isAlduin2DefeatedPtr = new DeepPointer(0x1711608); // == 1 when last blow is struck on alduin
            _questlinesCompletedPtr = new DeepPointer(0x00EE6C34, 0x3F0); // number of questlines completed (from ingame stats)
            _collegeOfWinterholdQuestsCompletedPtr = new DeepPointer(0x00EE6C34, 0x38c); // number of college of winterhold quests completed (from ingame stats)
            _companionsQuestsCompletedPtr = new DeepPointer(0x00EE6C34, 0x378); // number of companions quests completed (from ingame stats)
            _darkBrotherhoodQuestsCompletedPtr = new DeepPointer(0x00EE6C34, 0x3b4); // number of dark brotherhood quests completed (from ingame stats)
            _thievesGuildQuestsCompletedPtr = new DeepPointer(0x00EE6C34, 0x3a0); // number of thieves guild quests completed (from ingame stats)
            _isInEscapeMenuPtr = new DeepPointer(0x172E85E); // == 1 when in the pause menu or level up menu
            _mainQuestsCompletedPtr = new DeepPointer(0x00EE6C34, 0x350); // number of main quests completed (from ingame stats)
            _wordsOfPowerLearnedPtr = new DeepPointer(0x00EE6C34, 0x558); // "Words Of Power Learned" from ingame stats
            _Alduin1HealthPtr = new DeepPointer(0x00F41764, 0x74, 0x30, 0x30, 0x1c); // Alduin 1's health (if it's at 0 it's 99% of the time because it can't be found)
            _locationsDiscoveredPtr = new DeepPointer(0x00EE6C34, 0x170); // number of locations discovered (from ingame stats)
            _arePlayerControlsDisablePtr = new DeepPointer(0x172EF30, 0xf); // == 1 when player controls have been disabled (not necessarily all controls)
            _bearCartHealthPtr = new DeepPointer(0x00F354DC, 0x74, 0x30, 0x30, 0x1C);

            resetSplitStates();

            _ignorePIDs = new List<int>();
        }

        public void StartMonitoring()
        {
            if (_thread != null && _thread.Status == TaskStatus.Running)
            {
                throw new InvalidOperationException();
            }
            if (!(SynchronizationContext.Current is WindowsFormsSynchronizationContext))
            {
                throw new InvalidOperationException("SynchronizationContext.Current is not a UI thread.");
            }

            _uiThread = SynchronizationContext.Current;
            _cancelSource = new CancellationTokenSource();
            _thread = Task.Factory.StartNew(MemoryReadThread);
        }

        public void Stop()
        {
            if (_cancelSource == null || _thread == null || _thread.Status != TaskStatus.Running)
            {
                return;
            }

            _cancelSource.Cancel();
            _thread.Wait();
        }

        void MemoryReadThread()
        {
            Trace.WriteLine("[NoLoads] MemoryReadThread");

            while (!_cancelSource.IsCancellationRequested)
            {
                try
                {
                    Trace.WriteLine("[NoLoads] Waiting for TESV.exe...");

                    Process game;
                    while ((game = GetGameProcess()) == null)
                    {
                        Thread.Sleep(250);
                        if (_cancelSource.IsCancellationRequested)
                        {
                            return;
                        }
                    }

                    Trace.WriteLine("[NoLoads] Got TESV.exe!");

                    uint frameCounter = 0;
                    SkyrimData data = new SkyrimData(game);

                    bool loadingStarted = false;
                    bool loadingScreenStarted = false;
                    bool loadScreenFadeoutStarted = false;
                    bool isLoadingSaveFromMenu = false;
                    int loadScreenStartLocationID = 0;
                    int loadScreenStartWorld_X = 0;
                    int loadScreenStartWorld_Y = 0;
                    bool isWaitingLocationOrCoordsUpdate = false;
                    bool isWaitingLocationIDUpdate = false;

                    SplitArea lastQuestCompleted = SplitArea.None;
                    uint lastQuestframeCounter = 0;

                    while (!game.HasExited)
                    {
                        data.IsLoadingScreen.SetValue(_isLoadingScreenPtr);

                        //need to avoid doing more than one SetValue in the same iteration to not make the previous value invalid
                        if (data.IsLoadingScreen.Current)
                            data.IsLoading.SetValue(true);
                        else
                            data.IsLoading.SetValue(_isLoadingPtr);

                        data.IsInFadeOut.SetValue(_isInFadeOutPtr);
                        data.LocationID.SetValue(_locationIDPtr);
                        data.WorldX.SetValue(_world_XPtr);
                        data.WorldY.SetValue(_world_YPtr);
                        data.IsAlduin2Defeated.SetValue(_isAlduin2DefeatedPtr);
                        data.QuestlinesCompleted.SetValue(_questlinesCompletedPtr);
                        data.CollegeOfWinterholdQuestsCompleted.SetValue(_collegeOfWinterholdQuestsCompletedPtr);
                        data.CompanionsQuestsCompleted.SetValue(_companionsQuestsCompletedPtr);
                        data.DarkBrotherhoodQuestsCompleted.SetValue(_darkBrotherhoodQuestsCompletedPtr);
                        data.ThievesGuildQuestsCompleted.SetValue(_thievesGuildQuestsCompletedPtr);
                        data.IsInEscapeMenu.SetValue(_isInEscapeMenuPtr);
                        data.MainQuestsCompleted.SetValue(_mainQuestsCompletedPtr);
                        data.WordsOfPowerLearned.SetValue(_wordsOfPowerLearnedPtr);
                        data.Alduin1Health.SetValue(_Alduin1HealthPtr);
                        data.LocationsDiscovered.SetValue(_locationsDiscoveredPtr);
                        data.ArePlayerControlsDisabled.SetValue(_arePlayerControlsDisablePtr);
                        data.BearCartHealth.SetValue(_bearCartHealthPtr);


                        if (data.IsLoading.HasChanged)
                        {
                            if (data.IsLoading.Current)
                            {
                                Trace.WriteLine(String.Format("[NoLoads] Load Start - {0}", frameCounter));

                                loadingStarted = true;
                                // pause game timer
                                _uiThread.Post(d =>
                                {
                                    if (this.OnLoadStarted != null)
                                    {
                                        this.OnLoadStarted(this, EventArgs.Empty);
                                    }
                                }, null);
                            }
                            else
                            {
                                Trace.WriteLine(String.Format("[NoLoads] Load End - {0}", frameCounter));

                                if (loadingStarted)
                                {
                                    loadingStarted = false;

                                    if (!loadScreenFadeoutStarted)
                                    {
                                        if (data.LocationID.Current == (int)Locations.Tamriel && data.WorldX.Current == 13 && (data.WorldY.Current == -10 || data.WorldY.Current == -9) && data.WordsOfPowerLearned.Current == 3)
                                        {
                                            Split(SplitArea.ClearSky, frameCounter);
                                        }
                                    }

                                    // unpause game timer
                                    _uiThread.Post(d =>
                                    {
                                        if (this.OnLoadFinished != null)
                                        {
                                            this.OnLoadFinished(this, EventArgs.Empty);
                                        }
                                    }, null);
                                }
                                else
                                    Trace.WriteLine("what the fuck this should never be hit");
                            }
                        }

                        if (data.IsLoadingScreen.HasChanged)
                        {
                            if (data.IsLoadingScreen.Current)
                            {
                                Trace.WriteLine(String.Format("[NoLoads] LoadScreen Start at {0} X: {1} Y: {2} - {3}", data.LocationID.Current.ToString("X8"), data.WorldX.Current, data.WorldY.Current, frameCounter));

                                loadingScreenStarted = true;
                                loadScreenStartLocationID = data.LocationID.Current;
                                loadScreenStartWorld_X = data.WorldX.Current;
                                loadScreenStartWorld_Y = data.WorldY.Current;

                                if (data.IsInFadeOut.Current)
                                {
                                    loadScreenFadeoutStarted = true;
                                }

                                if (data.IsInEscapeMenu.Current)
                                {
                                    isLoadingSaveFromMenu = true;
                                }

                                // if it isn't a loadscreen from loading a save
                                if (!isLoadingSaveFromMenu)
                                {
                                    isWaitingLocationOrCoordsUpdate = true;
                                    isWaitingLocationIDUpdate = true;

                                    // if loadscreen starts while leaving helgen
                                    if (loadScreenStartLocationID == (int)Locations.HelgenKeep01 && loadScreenStartWorld_X == -2 && loadScreenStartWorld_Y == -5)
                                    {
                                        // Helgen split
                                        Split(SplitArea.Helgen, frameCounter);
                                    }
                                    // if loadscreen starts in around the carriage of Whiterun Stables
                                    else if (loadScreenStartLocationID == (int)Locations.Tamriel && loadScreenStartWorld_X == 4 && (loadScreenStartWorld_Y == -3 || loadScreenStartWorld_Y == -4) &&
                                            _settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_DRTCHOPS)
                                    {
                                        Split(SplitArea.Whiterun, frameCounter);
                                    }
                                    // if loadscreen starts in Karthspire and Sky Haven Temple has been entered at least once
                                    else if (loadScreenStartLocationID == (int)Locations.KarthspireRedoubtWorld && isSkyHavenTempleVisited &&
                                        (_settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_MRWALRUS || _settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_DRTCHOPS || _settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_DALLETH))
                                    {
                                        Split(SplitArea.TheWall, frameCounter);
                                    }
                                }
                                else
                                {
                                    isWaitingLocationOrCoordsUpdate = false;
                                    isWaitingLocationIDUpdate = false;
                                }
                            }
                            else
                            {
                                Trace.WriteLine(String.Format("[NoLoads] LoadScreen End at {0} X: {1} Y: {2} - {3}", data.LocationID.Current.ToString("X8"), data.WorldX.Current, data.WorldY.Current, frameCounter));

                                if (loadingScreenStarted)
                                {
                                    loadingScreenStarted = false;
                                    isLoadingSaveFromMenu = false;
                                }
                            }
                        }

                        if (data.IsInFadeOut.HasChanged)
                        {
                            if (data.IsInFadeOut.Current)
                            {
                                Debug.WriteLine(String.Format("[NoLoads] Fadeout started - {0}", frameCounter));
                                if (data.IsLoadingScreen.Current)
                                {
                                    loadScreenFadeoutStarted = true;
                                }
                            }
                            else
                            {
                                Debug.WriteLine(String.Format("[NoLoads] Fadeout ended - {0}", frameCounter));
                                // if loadscreen fadeout finishes in helgen
                                if (data.IsInFadeOut.Previous && loadScreenFadeoutStarted &&
                                    data.LocationID.Current == (int)Locations.Tamriel && data.WorldX.Current == 3 && data.WorldY.Current == -20)
                                {
                                    // reset
                                    Trace.WriteLine(String.Format("[NoLoads] Reset - {0}", frameCounter));
                                    _uiThread.Post(d =>
                                    {
                                        if (this.OnFirstLevelLoading != null)
                                        {
                                            this.OnFirstLevelLoading(this, EventArgs.Empty);
                                        }
                                    }, null);

                                    // start
                                    Trace.WriteLine(String.Format("[NoLoads] Start - {0}", frameCounter));
                                    _uiThread.Post(d =>
                                    {
                                        if (this.OnPlayerGainedControl != null)
                                        {
                                            this.OnPlayerGainedControl(this, EventArgs.Empty);
                                        }
                                    }, null);
                                }
                                loadScreenFadeoutStarted = false;
                            }
                        }

                        if ((data.LocationID.HasChanged || data.WorldX.HasChanged || data.WorldY.HasChanged) && isWaitingLocationOrCoordsUpdate)
                        {
                            isWaitingLocationOrCoordsUpdate = false;

                            // if loadscreen starts while in front of the door of Thalmor Embassy and doesn't end inside the Embassy
                            if (loadScreenStartLocationID == (int)Locations.Tamriel && loadScreenStartWorld_X == -20 && loadScreenStartWorld_Y == 28 &&
                                data.LocationID.Current != (int)Locations.ThalmorEmbassy02 &&
                                    (_settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_MRWALRUS || _settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_DRTCHOPS || _settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_DALLETH))
                            {
                                Split(SplitArea.ThalmorEmbassy, frameCounter);
                            }
                            // if loadscreen starts while in front of the Sleeping Giant Inn and doesn't end inside it
                            else if (loadScreenStartLocationID == (int)Locations.Tamriel && loadScreenStartWorld_X == 5 && loadScreenStartWorld_Y == -11 &&
                                data.LocationID.Current != (int)Locations.RiverwoodSleepingGiantInn &&
                                (_settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_MRWALRUS || _settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_DRTCHOPS || _settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_DALLETH))
                            {
                                Split(SplitArea.Riverwood, frameCounter);
                            }
                            // if loadscreen starts outside Septimus' Outpost and doesn't end inside it
                            else if (loadScreenStartLocationID == (int)Locations.Tamriel && loadScreenStartWorld_X == 28 && loadScreenStartWorld_Y == 34 &&
                                data.LocationID.Current != (int)Locations.SeptimusSignusOutpost &&
                                    (_settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_MRWALRUS || _settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_DRTCHOPS || _settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_DALLETH))
                            {
                                Split(SplitArea.Septimus, frameCounter);
                            }
                            // if loadscreen starts outside Mzark Tower and doesn't end inside it
                            else if (loadScreenStartLocationID == (int)Locations.Tamriel && loadScreenStartWorld_X == 6 && loadScreenStartWorld_Y == 11 &&
                                data.LocationID.Current != (int)Locations.TowerOfMzark &&
                                    (_settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_MRWALRUS || _settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_DRTCHOPS || _settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_DALLETH))
                            {
                                Split(SplitArea.MzarkTower, frameCounter);
                            }
                            // if loadscreen starts in High Hrothgar's whereabouts and doesn't end inside
                            else if (loadScreenStartLocationID == (int)Locations.Tamriel && loadScreenStartWorld_X == 13 &&
                                (loadScreenStartWorld_Y == -9 || loadScreenStartWorld_Y == -10) &&
                                    data.LocationID.Current != (int)Locations.HighHrothgar)
                            {
                                if (!splitStates[(int)SplitArea.HighHrothgar])
                                {
                                    Split(SplitArea.HighHrothgar, frameCounter);
                                }
                                else if (_settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_DRTCHOPS || _settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_DALLETH)
                                {
                                    Split(SplitArea.Council, frameCounter);
                                }
                            }
                        }

                        if (data.LocationID.HasChanged && isWaitingLocationIDUpdate)
                        {
                            isWaitingLocationIDUpdate = false;

                            if (data.LocationID.Current == (int)Locations.SkyHavenTemple)
                            {
                                isSkyHavenTempleVisited = true;
                            }

                            // if loadscreen starts in dragonsreach and ends in whiterun
                            if (loadScreenStartLocationID == (int)Locations.WhiterunDragonsreach &&
                                data.LocationID.Current == (int)Locations.WhiterunWorld && data.WorldX.Current == 6 && data.WorldY.Current == 0 &&
                                _settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_GR3YSCALE)
                            {
                                Split(SplitArea.Whiterun, frameCounter);
                            }
                            // if loadscreen starts in whiterun and doesn't end in dragonsreach
                            else if (loadScreenStartLocationID == (int)Locations.WhiterunWorld && loadScreenStartWorld_X == 6 && loadScreenStartWorld_Y == 0 &&
                                data.LocationID.Current != (int)Locations.WhiterunDragonsreach &&
                                (_settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_MRWALRUS || _settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_DALLETH))
                            {
                                Split(SplitArea.Whiterun, frameCounter);
                            }
                            // if loadscreen starts Thalmor Embassy and ends in front of its door
                            else if (loadScreenStartLocationID == (int)Locations.ThalmorEmbassy02 &&
                                data.LocationID.Current == (int)Locations.Tamriel && data.WorldX.Current == -20 && data.WorldY.Current == 28 &&
                                    _settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_GR3YSCALE)
                            {
                                Split(SplitArea.ThalmorEmbassy, frameCounter);
                            }
                            // if loadscreen starts while in front of the ratway door and doesn't end inside it
                            else if (loadScreenStartLocationID == (int)Locations.RiftenWorld && loadScreenStartWorld_X == 42 && loadScreenStartWorld_Y == -24 &&
                                data.LocationID.Current != (int)Locations.RiftenRatway01 &&
                                    (_settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_MRWALRUS || _settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_DRTCHOPS || _settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_DALLETH))
                            {
                                Split(SplitArea.Esbern, frameCounter);
                            }
                            // if loadscreen starts inside the ratway and ends in front of its door
                            else if (loadScreenStartLocationID == (int)Locations.RiftenRatway01 &&
                                data.LocationID.Current == (int)Locations.RiftenWorld && data.WorldX.Current == 42 && data.WorldY.Current == -24 &&
                                    _settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_GR3YSCALE)
                            {
                                Split(SplitArea.Esbern, frameCounter);
                            }
                            // if loadscreen starts while leaving the Sleeping Giant Inn and ends in front of its door
                            else if (loadScreenStartLocationID == (int)Locations.RiverwoodSleepingGiantInn &&
                                data.LocationID.Current == (int)Locations.Tamriel && data.WorldX.Current == 5 && data.WorldY.Current == -11 &&
                                _settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_GR3YSCALE)
                            {
                                leaveSleepingGiantInnCounter++;
                                if (leaveSleepingGiantInnCounter == 2)
                                {
                                    Split(SplitArea.Riverwood, frameCounter);
                                }
                            }
                            // if loadingscren starts in Sky Haven Temple and ends in Karthspire
                            else if (loadScreenStartLocationID == (int)Locations.SkyHavenTemple &&
                                data.LocationID.Current == (int)Locations.KarthspireRedoubtWorld &&
                                    _settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_GR3YSCALE)
                            {
                                Split(SplitArea.TheWall, frameCounter);
                            }
                            // if loadscreen starts inside Septimus' Outpost and ends in front of its door
                            else if (loadScreenStartLocationID == (int)Locations.SeptimusSignusOutpost &&
                                data.LocationID.Current == (int)Locations.Tamriel && data.WorldX.Current == 28 && data.WorldY.Current == 34 &&
                                    _settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_GR3YSCALE)
                            {
                                Split(SplitArea.Septimus, frameCounter);
                            }
                            // if loadscreen starts inside Mzark Tower and ends outside of it
                            else if (loadScreenStartLocationID == (int)Locations.TowerOfMzark &&
                                data.LocationID.Current == (int)Locations.Tamriel && data.WorldX.Current == 6 && data.WorldY.Current == 11 &&
                                    _settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_GR3YSCALE)
                            {
                                Split(SplitArea.MzarkTower, frameCounter);
                            }
                            // if Alduin1 has been defeated once and loadscreen starts in Paarthurnax' mountain whereabouts and ends in front of dragonsreach (fast travel)
                            else if (isAlduin1Defeated && loadScreenStartLocationID == (int)Locations.Tamriel && ((loadScreenStartWorld_X == 14 && loadScreenStartWorld_Y == -12) ||
                                (loadScreenStartWorld_X == 14 && loadScreenStartWorld_Y == -13) || (loadScreenStartWorld_X == 13 && loadScreenStartWorld_Y == -12) ||
                                    (loadScreenStartWorld_X == 13 && loadScreenStartWorld_Y == -13)) &&
                                        data.LocationID.Current == (int)Locations.WhiterunWorld && data.WorldX.Current == 6 && data.WorldY.Current == -1 &&
                                            (_settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_DRTCHOPS || _settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_GR3YSCALE || _settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_DALLETH))
                            {
                                Split(SplitArea.Alduin1, frameCounter);
                            }
                            // if loadscreen starts in high hrothgar and ends in front of one of its doors
                            else if (loadScreenStartLocationID == (int)Locations.HighHrothgar &&
                                data.LocationID.Current == (int)Locations.Tamriel && data.WorldX.Current == 13 && (data.WorldY.Current == -9 || data.WorldY.Current == -10) &&
                                    (_settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_DRTCHOPS || _settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_GR3YSCALE))
                            {
                                if (!isCouncilDone)
                                {
                                    Split(SplitArea.ClearSky, frameCounter);
                                }
                                else if (isCouncilDone && _settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_GR3YSCALE)
                                {
                                    Split(SplitArea.Council, frameCounter);
                                }
                            }
                            // if loadscreen starts in Solitude in front of the door of Castle Dour and doesn't end inside it
                            else if (loadScreenStartLocationID == (int)Locations.SolitudeWorld && loadScreenStartWorld_X == -16 && loadScreenStartWorld_Y == 26 &&
                                data.LocationID.Current != (int)Locations.SolitudeCastleDour &&
                                (_settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_MRWALRUS || _settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_DRTCHOPS || _settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_DALLETH))
                            {
                                Split(SplitArea.Solitude, frameCounter);
                            }
                            // if loadscreen starts in Solitude Castle Dour and ends outside in front of its door
                            else if (loadScreenStartLocationID == (int)Locations.SolitudeCastleDour &&
                                data.LocationID.Current == (int)Locations.SolitudeWorld && data.WorldX.Current == -16 && data.WorldY.Current == 26 &&
                                _settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_GR3YSCALE)
                            {
                                Split(SplitArea.Solitude, frameCounter);
                            }
                            // if loadscreen starts in Windhelm and doesn't end inside
                            else if (loadScreenStartLocationID == (int)Locations.WindhelmWorld && loadScreenStartWorld_X == 32 && loadScreenStartWorld_Y == 10 &&
                                data.LocationID.Current != (int)Locations.WindhelmPalaceoftheKings &&
                                (_settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_MRWALRUS || _settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_DRTCHOPS || _settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_DALLETH))
                            {
                                Split(SplitArea.Windhelm, frameCounter);
                            }
                            // if loadscreen starts in Windhelm's Palace of the Kings and ends outside
                            else if (loadScreenStartLocationID == (int)Locations.WindhelmPalaceoftheKings &&
                                data.LocationID.Current == (int)Locations.WindhelmWorld && data.WorldX.Current == 32 && data.WorldY.Current == 10 &&
                                _settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_GR3YSCALE)
                            {
                                Split(SplitArea.Windhelm, frameCounter);
                            }
                            // if loadscreen ends in Skuldafn.
                            else if (data.LocationID.Current == (int)Locations.SkuldafnWorld)
                            {
                                Split(SplitArea.Odahviing, frameCounter);
                            }
                            // if loadscreen ends in Sovngarde
                            else if (data.LocationID.Current == (int)Locations.Sovngarde)
                            {
                                Split(SplitArea.EnterSovngarde, frameCounter);
                            }
                        }

                        if (data.LocationsDiscovered.HasChanged)
                        {
                            if (data.LocationID.Current == (int)Locations.Tamriel && ((data.WorldX.Current == 14 && data.WorldY.Current == -12) || (data.WorldX.Current == 14 && data.WorldY.Current == -13) || (data.WorldX.Current == 13 && data.WorldY.Current == -12) ||
                                (data.WorldX.Current == 13 && data.WorldY.Current == -13)) &&
                                _settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_GR3YSCALE)
                            {
                                Split(SplitArea.HorseClimb, frameCounter);
                            }
                        }

                        if (data.ArePlayerControlsDisabled.HasChanged && !data.IsInEscapeMenu.Current)
                        {
                            if (data.ArePlayerControlsDisabled.Current)
                            {
                                if (data.LocationID.Current == (int)Locations.Tamriel && data.WorldX.Current == 13 && data.WorldY.Current == -12 &&
                                    (_settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_DRTCHOPS || _settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_DALLETH))
                                {
                                    Split(SplitArea.CutsceneStart, frameCounter);
                                }
                            }
                            else
                            {
                                if (data.LocationID.Current == (int)Locations.Tamriel && data.WorldX.Current == 13 && data.WorldY.Current == -12 &&
                                    (_settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_GR3YSCALE || _settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_DALLETH))
                                {
                                    Split(SplitArea.CutsceneEnd, frameCounter);
                                }
                            }
                        }

                        if (data.Alduin1Health.Current < 0 && !isAlduin1Defeated)
                        {
                            Debug.WriteLine(String.Format("[NoLoads] Alduin 1 has been defeated. HP: {1} - {0}", frameCounter, data.Alduin1Health.Current));
                            isAlduin1Defeated = true;

                            if (_settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_MRWALRUS)
                            {
                                Split(SplitArea.Alduin1, frameCounter);
                            }
                        }

                        if (data.LocationID.Current == (int)Locations.HelgenKeep01 && data.BearCartHealth.Current < 0 && data.BearCartHealth.Previous >= 0 && data.BearCartHealth.PrevDerefSuccess)
                        {
                            Debug.WriteLine(String.Format("[NoLoads] BEAR CART! HP: {1} - {0}", frameCounter, data.BearCartHealth.Current));

                            _uiThread.Post(d =>
                            {
                                if (this.OnBearCart != null)
                                {
                                    this.OnBearCart(this, EventArgs.Empty);
                                }
                            }, null);
                        }

                        // the only mainquest you can complete here is the council so when a quest completes, walrus' council split
                        if (data.MainQuestsCompleted.Current == data.MainQuestsCompleted.Previous + 1 && data.LocationID.Current == (int)Locations.HighHrothgar)
                        {
                            isCouncilDone = true;

                            if (_settings.AnyPercentTemplate == SkyrimSettings.TEMPLATE_MRWALRUS)
                            {
                                Split(SplitArea.Council, frameCounter);
                            }
                        }

                        // if alduin is defeated in sovngarde
                        if (data.IsAlduin2Defeated.HasChanged && data.IsAlduin2Defeated.Current && data.LocationID.Current == (int)Locations.Sovngarde)
                        {
                            // AlduinDefeated split
                            Split(SplitArea.AlduinDefeated, frameCounter);
                        }

                        // reset lastQuest 100 frames (1.5 seconds) after a completion to avoid splitting on a wrong questline.
                        if (frameCounter >= lastQuestframeCounter + 100 && lastQuestCompleted != SplitArea.None)
                        {
                            lastQuestCompleted = SplitArea.None;
                        }

                        if (data.CollegeOfWinterholdQuestsCompleted.Current > data.CollegeOfWinterholdQuestsCompleted.Previous)
                        {
                            Debug.WriteLine(String.Format("[NoLoads] A College of Winterhold quest has been completed - {0}", frameCounter));
                            lastQuestCompleted = SplitArea.CollegeOfWinterholdQuestlineCompleted;
                            lastQuestframeCounter = frameCounter;
                        }
                        else if (data.CompanionsQuestsCompleted.Current > data.CompanionsQuestsCompleted.Previous)
                        {
                            Debug.WriteLine(String.Format("[NoLoads] A Companions quest has been completed - {0}", frameCounter));
                            lastQuestCompleted = SplitArea.CompanionsQuestlineCompleted;
                            lastQuestframeCounter = frameCounter;
                        }
                        else if (data.DarkBrotherhoodQuestsCompleted.Current > data.DarkBrotherhoodQuestsCompleted.Previous)
                        {
                            Debug.WriteLine(String.Format("[NoLoads] A Dark Brotherhood quest has been completed - {0}", frameCounter));
                            lastQuestCompleted = SplitArea.DarkBrotherhoodQuestlineCompleted;
                            lastQuestframeCounter = frameCounter;
                        }
                        else if (data.ThievesGuildQuestsCompleted.Current > data.ThievesGuildQuestsCompleted.Previous)
                        {
                            Debug.WriteLine(String.Format("[NoLoads] A Thieves' Guild quest has been completed - {0}", frameCounter));
                            lastQuestCompleted = SplitArea.ThievesGuildQuestlineCompleted;
                            lastQuestframeCounter = frameCounter;
                        }

                        // if a questline is completed
                        if (data.QuestlinesCompleted.Current > data.QuestlinesCompleted.Previous)
                        {
                            Debug.WriteLineIf(lastQuestCompleted == SplitArea.None, String.Format("[NoLoads] A questline has been completed. - {0}", frameCounter));
                            Split(lastQuestCompleted, frameCounter);
                        }


                        Debug.WriteLineIf(data.LocationID.HasChanged, String.Format("[NoLoads] Location changed to {0} - {1}", data.LocationID.Current.ToString("X8"), frameCounter));
                        Debug.WriteLineIf(data.WorldX.HasChanged || data.WorldY.HasChanged, String.Format("[NoLoads] Coords changed to X: {0} Y: {1} - {2}", data.WorldX.Current, data.WorldY.Current, frameCounter));
                        Debug.WriteLineIf(data.IsInEscapeMenu.HasChanged, String.Format("[NoLoads] isInEscapeMenu changed to {0} - {1}", data.IsInEscapeMenu.Current, frameCounter));

                        frameCounter++;

                        Thread.Sleep(15);

                        if (_cancelSource.IsCancellationRequested)
                        {
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine(ex.ToString());
                    Thread.Sleep(1000);
                }
            }
        }

        private void Split(SplitArea split, uint frame)
        {
            _uiThread.Post(d =>
            {
                if (this.OnSplitCompleted != null)
                {
                    this.OnSplitCompleted(this, split, frame);
                }
            }, null);
        }

        Process GetGameProcess()
        {
            Process game = Process.GetProcesses().FirstOrDefault(p => p.ProcessName.ToLower() == "tesv"
                && !p.HasExited && !_ignorePIDs.Contains(p.Id));
            if (game == null)
            {
                return null;
            }

            if (game.MainModule.ModuleMemorySize != (int)ExpectedDllSizes.SkyrimSteam && game.MainModule.ModuleMemorySize != (int)ExpectedDllSizes.SkyrimCracked)
            {
                _ignorePIDs.Add(game.Id);
                _uiThread.Send(d => MessageBox.Show("Unexpected game version. Skyrim 1.9.32.0.8 is required.", "LiveSplit.Skyrim",
                    MessageBoxButtons.OK, MessageBoxIcon.Error), null);
                return null;
            }

            return game;
        }
    }
}
