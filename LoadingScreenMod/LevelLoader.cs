﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using ColossalFramework;
using ColossalFramework.Packaging;
using ColossalFramework.PlatformServices;
using ColossalFramework.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LoadingScreenModTest
{
    /// <summary>
    /// LoadLevelCoroutine from LoadingManager.
    /// </summary>
    public sealed class LevelLoader : DetourUtility<LevelLoader>
    {
        public string cityName;
        readonly HashSet<string> knownFailedAssets = new HashSet<string>(); // assets that failed or are missing
        readonly Dictionary<string, bool> knownFastLoads = new Dictionary<string, bool>(2); // savegames that can be fastloaded
        internal object loadingLock;
        internal Queue<IEnumerator> mainThreadQueue;
        DateTime fullLoadTime, savedSkipStamp;
        bool simulationFailed, fastLoad;

        private LevelLoader()
        {
            init(typeof(LoadingManager), "LoadLevel", 5, 0, typeof(Package.Asset));
        }

        internal void AddFailedAssets(HashSet<string> assets)
        {
            foreach (string fullName in assets)
                knownFailedAssets.Add(fullName);
        }

        internal override void Dispose()
        {
            base.Dispose();
            knownFailedAssets.Clear();
            knownFastLoads.Clear();
        }

        public Coroutine LoadLevel(Package.Asset asset, string playerScene, string uiScene, SimulationMetaData ngs, bool forceEnvironmentReload = false)
        {
            LoadingManager lm = Singleton<LoadingManager>.instance;
            bool activated = ngs.m_updateMode == SimulationManager.UpdateMode.LoadGame || ngs.m_updateMode == SimulationManager.UpdateMode.NewGameFromMap ||
                ngs.m_updateMode == SimulationManager.UpdateMode.NewGameFromScenario || Input.GetKey(KeyCode.LeftControl);
            instance.simulationFailed = false;

            if (!lm.m_currentlyLoading && !lm.m_applicationQuitting)
            {
                if (lm.m_LoadingWrapper != null)
                    lm.m_LoadingWrapper.OnLevelUnloading(); // OnLevelUnloading

                if (activated)
                {
                    Settings s = Settings.settings;
                    Util.DebugPrint("Options: 710", s.loadEnabled, s.loadUsed, s.shareTextures, s.shareMaterials, s.shareMeshes, s.reportAssets, s.skipPrefabs);

                    LoadingManager.instance.SetSceneProgress(0f);
                    instance.cityName = asset?.name ?? "NewGame";
                    Profiling.Init();
                    CustomDeserializer.Create();
                    Fixes.Create().Deploy();
                    AssetLoader.Create().Setup();
                    LoadingScreen.Create().Setup();
                }

                lm.LoadingAnimationComponent.enabled = true;
                lm.m_currentlyLoading = true;
                lm.m_metaDataLoaded = false;
                lm.m_simulationDataLoaded = false;
                lm.m_loadingComplete = false;
                lm.m_renderDataReady = false;
                lm.m_essentialScenesLoaded = false;
                lm.m_brokenAssets = string.Empty;
                Util.Set(lm, "m_sceneProgress", 0f);
                Util.Set(lm, "m_simulationProgress", 0f);

                if (activated)
                    Profiling.Start();

                lm.m_loadingProfilerMain.Reset();
                lm.m_loadingProfilerSimulation.Reset();
                lm.m_loadingProfilerScenes.Reset();

                IEnumerator iter = activated ? instance.LoadLevelCoroutine(asset, playerScene, uiScene, ngs, forceEnvironmentReload) :
                    (IEnumerator) Util.Invoke(lm, "LoadLevelCoroutine", asset, playerScene, uiScene, ngs, forceEnvironmentReload);

                return lm.StartCoroutine(iter);
            }

            return null;
        }

        public IEnumerator LoadLevelCoroutine(Package.Asset asset, string playerScene, string uiScene, SimulationMetaData ngs, bool forceEnvironmentReload)
        {
            string scene;
            int i;
            yield return null;

            try
            {
                Util.InvokeVoid(LoadingManager.instance, "PreLoadLevel");
            }
            catch (Exception e)
            {
                Util.DebugPrint("PreLoadLevel: exception from some mod.");
                UnityEngine.Debug.LogException(e);
            }

            if (!LoadingManager.instance.LoadingAnimationComponent.AnimationLoaded)
            {
                LoadingManager.instance.m_loadingProfilerScenes.BeginLoading("LoadingAnimation");
                yield return SceneManager.LoadSceneAsync("LoadingAnimation", LoadSceneMode.Additive);
                LoadingManager.instance.m_loadingProfilerScenes.EndLoading();
            }

            AsyncTask task = Singleton<SimulationManager>.instance.AddAction("Loading", (IEnumerator) Util.Invoke(LoadingManager.instance, "LoadSimulationData", asset, ngs));
            LoadSaveStatus.activeTask = task;
            DateTime skipStamp = Settings.settings.LoadSkipFile();

            if (LoadingManager.instance.m_loadedEnvironment == null) // loading from main menu
            {
                knownFailedAssets.Clear();
                fastLoad = false;
            }
            else // loading from in-game (the pause menu)
            {
                while (!LoadingManager.instance.m_metaDataLoaded && !task.completedOrFailed) // IL_139
                    yield return null;

                if (SimulationManager.instance.m_metaData == null)
                {
                    SimulationManager.instance.m_metaData = new SimulationMetaData();
                    SimulationManager.instance.m_metaData.m_environment = "Sunny";
                    SimulationManager.instance.m_metaData.Merge(ngs);
                }

                Util.InvokeVoid(LoadingManager.instance, "MetaDataLoaded"); // No OnCreated
                string mapThemeName = SimulationManager.instance.m_metaData.m_MapThemeMetaData?.name;
                fastLoad = SimulationManager.instance.m_metaData.m_environment == LoadingManager.instance.m_loadedEnvironment &&
                    mapThemeName == LoadingManager.instance.m_loadedMapTheme && !forceEnvironmentReload;

                // The game is nicely optimized when loading from the pause menu. We must specifically address the following situations:
                // - environment (biome) stays the same
                // - map theme stays the same
                // - forceEnvironmentReload is false
                // - 'load used assets' is enabled
                // - not all assets and prefabs used in the save being loaded are currently in memory
                // - prefab skipping has changed.

                if (fastLoad)
                {
                    i = Profiling.Millis;

                    // Check custom asset availability.
                    if (Settings.settings.loadUsed && !IsKnownFastLoad(asset))
                    {
                        while (Profiling.Millis - i < 5000 && !IsSaveDeserialized())
                            yield return null;

                        fastLoad = AllAssetsAvailable();
                    }

                    // Check building prefab availability.
                    if (fastLoad)
                    {
                        if (skipStamp != savedSkipStamp)
                            fastLoad = false;
                        else if (Settings.settings.SkipPrefabs && !IsKnownFastLoad(asset))
                        {
                            while (Profiling.Millis - i < 5000 && !IsSaveDeserialized())
                                yield return null;

                            fastLoad = AllPrefabsAvailable();
                        }
                    }

                    if (fastLoad) // optimized load
                    {
                        LoadingManager.instance.QueueLoadingAction((IEnumerator) Util.Invoke(LoadingManager.instance, "EssentialScenesLoaded"));
                        LoadingManager.instance.QueueLoadingAction((IEnumerator) Util.Invoke(LoadingManager.instance, "RenderDataReady"));
                        Util.DebugPrint("fast load at", Profiling.Millis);
                    }
                    else // fallback to full load
                    {
                        DestroyLoadedPrefabs();
                        LoadingManager.instance.m_loadedEnvironment = null;
                        LoadingManager.instance.m_loadedMapTheme = null;
                        Util.DebugPrint("fallback to full load at", Profiling.Millis);
                    }
                }
                else // full load
                {
                    // Notice that there is a race condition in the base game at this point: DestroyAllPrefabs ruins the simulation
                    // if its deserialization has progressed far enough. Typically there is no problem.
                    Util.InvokeVoid(LoadingManager.instance, "DestroyAllPrefabs");
                    LoadingManager.instance.m_loadedEnvironment = null;
                    LoadingManager.instance.m_loadedMapTheme = null;
                    Util.DebugPrint("full load at", Profiling.Millis);
                }
            }

            // Full load.
            if (LoadingManager.instance.m_loadedEnvironment == null) // IL_27C
            {
                AsyncOperation op;
                knownFastLoads.Clear();
                fullLoadTime = DateTime.Now;
                savedSkipStamp = skipStamp;
                loadingLock = Util.Get(LoadingManager.instance, "m_loadingLock");
                mainThreadQueue = (Queue<IEnumerator>) Util.Get(LoadingManager.instance, "m_mainThreadQueue");


                if (!string.IsNullOrEmpty(playerScene))
                {
                    LoadingManager.instance.m_loadingProfilerScenes.BeginLoading(playerScene);
                    op = SceneManager.LoadSceneAsync(playerScene, LoadSceneMode.Single);

                    while (!op.isDone) // IL_2FF
                    {
                        LoadingManager.instance.SetSceneProgress(op.progress * 0.01f);
                        yield return null;
                    }

                    LoadingManager.instance.m_loadingProfilerScenes.EndLoading();
                }

                while (!LoadingManager.instance.m_metaDataLoaded && !task.completedOrFailed) // IL_33C
                    yield return null;

                if (SimulationManager.instance.m_metaData == null)
                {
                    SimulationManager.instance.m_metaData = new SimulationMetaData();
                    SimulationManager.instance.m_metaData.m_environment = "Sunny";
                    SimulationManager.instance.m_metaData.Merge(ngs);
                }

                try
                {
                    Util.InvokeVoid(LoadingManager.instance, "MetaDataLoaded"); // OnCreated if loading from the main manu
                }
                catch (Exception e)
                {
                    Util.DebugPrint("MetaDataLoaded: exception from some mod.");
                    UnityEngine.Debug.LogException(e);
                }

                KeyValuePair<string, float>[] levels = SetLevels();
                float currentProgress = 0.10f;

                for (i = 0; i < levels.Length; i++)
                {
                    scene = levels[i].Key;
                    LoadingManager.instance.m_loadingProfilerScenes.BeginLoading(scene);
                    op = SceneManager.LoadSceneAsync(scene, LoadSceneMode.Additive);

                    while (!op.isDone)
                    {
                        LoadingManager.instance.SetSceneProgress(currentProgress + op.progress * (levels[i].Value - currentProgress));
                        yield return null;
                    }

                    LoadingManager.instance.m_loadingProfilerScenes.EndLoading();
                    currentProgress = levels[i].Value;
                }

                PrefabLoader.instance?.Revert();
                Util.DebugPrint("PrefabLoader Revert at", Profiling.Millis);

                if (Settings.settings.SkipPrefabs)
                    LoadingManager.instance.QueueLoadingAction(PrefabLoader.DestroySkipped());

                Util.DebugPrint("mainThreadQueue len", mainThreadQueue.Count, "at", Profiling.Millis);

                // Some major mods (Network Extensions 1 & 2, Single Train Track, Metro Overhaul) have a race condition issue
                // in their NetInfo Installer. Everything goes fine if LoadCustomContent() below is NOT queued before the
                // said Installers have finished. This is just a workaround for the issue. The actual fix should be in
                // the Installers. Notice that the built-in loader of the game is also affected.

                do
                {
                    yield return null;
                    yield return null;

                    lock(loadingLock)
                    {
                        i = mainThreadQueue.Count;
                    }
                }
                while (i > 0);

                Util.DebugPrint("mainThreadQueue len 0 at", Profiling.Millis);

                LoadingManager.instance.QueueLoadingAction(AssetLoader.instance.LoadCustomContent());
                RenderManager.Managers_CheckReferences();
                LoadingManager.instance.QueueLoadingAction((IEnumerator) Util.Invoke(LoadingManager.instance, "EssentialScenesLoaded"));
                RenderManager.Managers_InitRenderData();
                LoadingManager.instance.QueueLoadingAction((IEnumerator) Util.Invoke(LoadingManager.instance, "RenderDataReady"));
                simulationFailed = HasFailed(task);

                // Performance optimization: do not load scenes while custom assets are loading.
                while (!AssetLoader.instance.hasFinished)
                    yield return null;

                scene = SimulationManager.instance.m_metaData.m_environment + "Properties";

                if (!string.IsNullOrEmpty(scene))
                {
                    LoadingManager.instance.m_loadingProfilerScenes.BeginLoading(scene);
                    op = SceneManager.LoadSceneAsync(scene, LoadSceneMode.Additive);

                    while (!op.isDone) // IL_C47
                    {
                        LoadingManager.instance.SetSceneProgress(0.85f + op.progress * 0.05f);
                        yield return null;
                    }

                    LoadingManager.instance.m_loadingProfilerScenes.EndLoading();
                }

                if (!simulationFailed)
                    simulationFailed = HasFailed(task);

                if (!string.IsNullOrEmpty(uiScene)) // IL_C67
                {
                    LoadingManager.instance.m_loadingProfilerScenes.BeginLoading(uiScene);
                    op = SceneManager.LoadSceneAsync(uiScene, LoadSceneMode.Additive);

                    while (!op.isDone) // IL_CDE
                    {
                        LoadingManager.instance.SetSceneProgress(0.90f + op.progress * 0.08f);
                        yield return null;
                    }

                    LoadingManager.instance.m_loadingProfilerScenes.EndLoading();
                }

                LoadingManager.instance.m_loadedEnvironment = SimulationManager.instance.m_metaData.m_environment; // IL_CFE
                LoadingManager.instance.m_loadedMapTheme = SimulationManager.instance.m_metaData.m_MapThemeMetaData?.name;
            }
            else
            {
                scene = (string) Util.Invoke(LoadingManager.instance, "GetLoadingScene");

                if (!string.IsNullOrEmpty(scene))
                {
                    LoadingManager.instance.m_loadingProfilerScenes.BeginLoading(scene);
                    yield return SceneManager.LoadSceneAsync(scene, LoadSceneMode.Additive);
                    LoadingManager.instance.m_loadingProfilerScenes.EndLoading();
                }
            }

            LoadingManager.instance.SetSceneProgress(1f); // IL_DBF

            if (!simulationFailed)
                simulationFailed = HasFailed(task);

            while (!task.completedOrFailed) // IL_DED
                yield return null;

            LoadingManager.instance.m_simulationDataLoaded = LoadingManager.instance.m_metaDataLoaded;
            LoadingManager.SimulationDataReadyHandler SimDataReady = Util.Get(LoadingManager.instance, "m_simulationDataReady") as LoadingManager.SimulationDataReadyHandler;
            SimDataReady?.Invoke();
            SimulationManager.UpdateMode mode = SimulationManager.UpdateMode.Undefined;

            if (ngs != null)
                mode = ngs.m_updateMode;

            LoadingManager.instance.QueueLoadingAction((IEnumerator) Util.Invoke(LoadingManager.instance, "LoadLevelComplete", mode)); // OnLevelLoaded
            PrefabLoader.instance?.Dispose();
            LoadingManager.instance.QueueLoadingAction(LoadingComplete());
            knownFastLoads[asset.checksum] = true;
            Util.DebugPrint("Waiting at", Profiling.Millis);
            AssetLoader.instance.PrintMem();
        }

        // Loading complete.
        public IEnumerator LoadingComplete()
        {
            Util.DebugPrint("All completed at", Profiling.Millis);
            AssetLoader.instance.PrintMem();
            Singleton<LoadingManager>.instance.LoadingAnimationComponent.enabled = false;
            AssetLoader.instance.Dispose();
            Fixes.instance?.Dispose();
            CustomDeserializer.instance?.Dispose();
            Profiling.Stop();
            yield break;
        }

        /// <summary>
        /// Creates the list of standard prefab levels to load.
        /// </summary>
        KeyValuePair<string, float>[] SetLevels()
        {
            if (Settings.settings.SkipPrefabs)
                PrefabLoader.Create().Deploy();

            MethodInfo dlcMethod = typeof(LoadingManager).GetMethod("DLC", BindingFlags.Instance | BindingFlags.NonPublic);
            LoadingManager.instance.m_supportsExpansion[0] = (bool) dlcMethod.Invoke(LoadingManager.instance, new object[] { 369150u });
            LoadingManager.instance.m_supportsExpansion[1] = (bool) dlcMethod.Invoke(LoadingManager.instance, new object[] { 420610u });
            LoadingManager.instance.m_supportsExpansion[2] = (bool) dlcMethod.Invoke(LoadingManager.instance, new object[] { 515191u });
            LoadingManager.instance.m_supportsExpansion[3] = (bool) dlcMethod.Invoke(LoadingManager.instance, new object[] { 547502u });
            LoadingManager.instance.m_supportsExpansion[4] = (bool) dlcMethod.Invoke(LoadingManager.instance, new object[] { 614580u });
            LoadingManager.instance.m_supportsExpansion[5] = (bool) dlcMethod.Invoke(LoadingManager.instance, new object[] { 715191u });
            bool isWinter = SimulationManager.instance.m_metaData.m_environment == "Winter";

            if (isWinter && !LoadingManager.instance.m_supportsExpansion[1])
            {
                SimulationManager.instance.m_metaData.m_environment = "Sunny";
                isWinter = false;
            }

            List<KeyValuePair<string, float>> levels = new List<KeyValuePair<string, float>>(18);
            string scene = (string) Util.Invoke(LoadingManager.instance, "GetLoadingScene");

            if (!string.IsNullOrEmpty(scene))
                levels.Add(new KeyValuePair<string, float>(scene, 0.015f));

            levels.Add(new KeyValuePair<string, float>(SimulationManager.instance.m_metaData.m_environment + "Prefabs", 0.12f));

            if ((bool) dlcMethod.Invoke(LoadingManager.instance, new object[] { 1u }))
                levels.Add(new KeyValuePair<string, float>(isWinter ? "WinterLoginPackPrefabs" : "LoginPackPrefabs", 0.121f));

            levels.Add(new KeyValuePair<string, float>(isWinter ? "WinterPreorderPackPrefabs" : "PreorderPackPrefabs", 0.122f));
            levels.Add(new KeyValuePair<string, float>(isWinter ? "WinterSignupPackPrefabs" : "SignupPackPrefabs", 0.123f));

            if ((bool) dlcMethod.Invoke(LoadingManager.instance, new object[] { 346791u }))
                levels.Add(new KeyValuePair<string, float>("DeluxePackPrefabs", 0.124f));

            if (PlatformService.IsAppOwned(238370u))
                levels.Add(new KeyValuePair<string, float>("MagickaPackPrefabs", 0.125f));

            if (LoadingManager.instance.m_supportsExpansion[0])
                levels.Add(new KeyValuePair<string, float>(isWinter ? "WinterExpansion1Prefabs" : "Expansion1Prefabs", 0.126f));

            if (LoadingManager.instance.m_supportsExpansion[1])
                levels.Add(new KeyValuePair<string, float>("Expansion2Prefabs", 0.127f));

            if (LoadingManager.instance.m_supportsExpansion[2])
                levels.Add(new KeyValuePair<string, float>("Expansion3Prefabs", 0.128f));

            if (LoadingManager.instance.m_supportsExpansion[3])
                levels.Add(new KeyValuePair<string, float>("Expansion4Prefabs", 0.13f));

            if (LoadingManager.instance.m_supportsExpansion[4])
                levels.Add(new KeyValuePair<string, float>(isWinter ? "WinterExpansion5Prefabs" : "Expansion5Prefabs", 0.132f));

            if (LoadingManager.instance.m_supportsExpansion[5])
                levels.Add(new KeyValuePair<string, float>(SimulationManager.instance.m_metaData.m_environment + "Expansion6Prefabs", 0.133f));

            for (int i = 0; i < levelStrings.Length; i++)
                if ((bool) dlcMethod.Invoke(LoadingManager.instance, new object[] { levelStrings[i].Value }))
                    levels.Add(new KeyValuePair<string, float>(levelStrings[i].Key, 0.134f + i * 0.01f / levelStrings.Length));

            if ((bool) dlcMethod.Invoke(LoadingManager.instance, new object[] { 715190u }))
            {
                Package.Asset asset = PackageManager.FindAssetByName("System." + DistrictStyle.kEuropeanSuburbiaStyleName);

                if (asset != null && asset.isEnabled)
                    levels.Add(new KeyValuePair<string, float>("ModderPack3Prefabs", 0.144f));
            }

            if ((bool) dlcMethod.Invoke(LoadingManager.instance, new object[] { 563850u }))
                levels.Add(new KeyValuePair<string, float>("ChinaPackPrefabs", 0.145f));

            Package.Asset europeanStyles = PackageManager.FindAssetByName("System." + DistrictStyle.kEuropeanStyleName);

            if (europeanStyles != null && europeanStyles.isEnabled)
                levels.Add(new KeyValuePair<string, float>(SimulationManager.instance.m_metaData.m_environment.Equals("Europe") ? "EuropeNormalPrefabs" : "EuropeStylePrefabs", 0.15f));

            return levels.ToArray();
        }

        KeyValuePair<string, uint>[] levelStrings =
            { new KeyValuePair<string, uint>("FootballPrefabs",     456200u),
              new KeyValuePair<string, uint>("Football2Prefabs",    525940u),
              new KeyValuePair<string, uint>("Football3Prefabs",    526610u),
              new KeyValuePair<string, uint>("Football4Prefabs",    526611u),
              new KeyValuePair<string, uint>("Football5Prefabs",    526612u),
              new KeyValuePair<string, uint>("Station1Prefabs",     547501u),
              new KeyValuePair<string, uint>("Station2Prefabs",     614582u),
              new KeyValuePair<string, uint>("Station3Prefabs",     715193u),
              new KeyValuePair<string, uint>("Station4Prefabs",     815380u),
              new KeyValuePair<string, uint>("FestivalPrefabs",     614581u),
              new KeyValuePair<string, uint>("ChristmasPrefabs",    715192u),
              new KeyValuePair<string, uint>("ModderPack1Prefabs",  515190u),
              new KeyValuePair<string, uint>("ModderPack2Prefabs",  547500u) };

        /// <summary>
        /// The savegame is a fast load if it is pre-known or its time stamp is newer than the full load time stamp.
        /// </summary>
        bool IsKnownFastLoad(Package.Asset asset)
        {
            if (knownFastLoads.TryGetValue(asset.checksum, out bool v))
            {
                Util.DebugPrint(asset.fullName + " -> knownFastLoads", v);
                return v;
            }

            try
            {
                v = fullLoadTime < asset.package.Find(asset.package.packageMainAsset).Instantiate<SaveGameMetaData>().timeStamp;
                knownFastLoads[asset.checksum] = v;
                return v;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
            }

            return false;
        }

        /// <summary>
        /// Checks (and reports) if the simulation thread has failed.
        /// </summary>
        bool HasFailed(AsyncTask simulationTask)
        {
            if (simulationTask.failed)
            {
                try
                {
                    Exception[] exceptions = ((Queue<Exception>) Util.GetStatic(typeof(UIView), "sLastException")).ToArray();
                    string msg = null;

                    if (exceptions.Length > 0)
                        msg = exceptions[exceptions.Length - 1].Message;

                    SimpleProfilerSource profiler = LoadingScreen.instance.SimulationSource;
                    profiler?.Failed(msg);
                    return true;
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogException(e);
                }
            }

            return false;
        }

        /// <summary>
        /// Checks if buildings, props, trees, and vehicles have been deserialized from the savegame.
        /// </summary>
        internal static bool IsSaveDeserialized() => GetSimProgress() > 54;

        /// <summary>
        /// Returns the progress of simulation deserialization.
        /// Note: two threads at play here, old values of m_size might be cached for quite some time.
        /// </summary>
        static int GetSimProgress()
        {
            try
            {
                FastList<LoadingProfiler.Event> events = ProfilerSource.GetEvents(LoadingManager.instance.m_loadingProfilerSimulation);
                return Thread.VolatileRead(ref events.m_size);
            }
            catch (Exception) { }

            return -1;
        }

        /// <summary>
        /// Checks if the game has all required custom assets currently in memory.
        /// </summary>
        bool AllAssetsAvailable()
        {
            try
            {
                return UsedAssets.Create().AllAssetsAvailable(knownFailedAssets);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
            }

            return true;
        }

        /// <summary>
        /// Checks if the game has all required building prefabs currently in memory.
        /// </summary>
        bool AllPrefabsAvailable()
        {
            try
            {
                PrefabLoader.Create().LookupSimulationPrefabs();
                return PrefabLoader.instance.AllPrefabsAvailable();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
            }

            return true;
        }

        static void DestroyLoadedPrefabs()
        {
            DestroyLoaded<NetInfo>();
            DestroyLoaded<BuildingInfo>();
            DestroyLoaded<PropInfo>();
            DestroyLoaded<TreeInfo>();
            DestroyLoaded<TransportInfo>();
            DestroyLoaded<VehicleInfo>();
            DestroyLoaded<CitizenInfo>();
            DestroyLoaded<EventInfo>();
            DestroyLoaded<DisasterInfo>();
            DestroyLoaded<RadioContentInfo>();
            DestroyLoaded<RadioChannelInfo>();
        }

        /// <summary>
        /// Destroys scene prefabs. Unlike DestroyAll(), simulation prefabs are not affected.
        /// </summary>
        static void DestroyLoaded<P>() where P : PrefabInfo
        {
            try
            {
                int n = PrefabCollection<P>.LoadedCount();
                List<P> prefabs = new List<P>(n);

                for (int i = 0; i < n; i++)
                {
                    P info = PrefabCollection<P>.GetLoaded((uint) i);

                    if (info != null)
                    {
                        info.m_prefabDataIndex = -1; // leave simulation prefabs as they are
                        prefabs.Add(info);
                    }
                }

                PrefabCollection<P>.DestroyPrefabs(string.Empty, prefabs.ToArray(), null);

                // This has not been necessary yet. However, it is quite fatal if prefabs are left behind so better be sure.
                if (n != prefabs.Count)
                {
                    object fastList = Util.GetStatic(typeof(PrefabCollection<P>), "m_scenePrefabs");
                    Util.Set(fastList, "m_size", 0, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                }

                object dict = Util.GetStatic(typeof(PrefabCollection<P>), "m_prefabDict");
                dict.GetType().GetMethod("Clear", BindingFlags.Instance | BindingFlags.Public).Invoke(dict, null);
                prefabs.Clear(); prefabs.Capacity = 0;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
            }
        }
    }
}
