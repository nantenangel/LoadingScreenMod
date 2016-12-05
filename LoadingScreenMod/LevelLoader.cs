using System;
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

namespace LoadingScreenMod
{
    /// <summary>
    /// LoadLevelCoroutine from LoadingManager.
    /// </summary>
    public sealed class LevelLoader : DetourUtility
    {
        public static LevelLoader instance;
        public string cityName;
        readonly HashSet<string> knownToFail = new HashSet<string>(); // assets that failed or are missing
        public bool activated, simulationFailed, fastLoad;

        internal LevelLoader()
        {
            instance = this;
            init(Singleton<LoadingManager>.instance.GetType(), "LoadLevel", 4, 0, typeof(Package.Asset));
        }

        internal void AddFailedAssets(HashSet<string> assets)
        {
            foreach (string fullName in assets)
                knownToFail.Add(fullName);
        }

        internal override void Dispose()
        {
            Revert();
            base.Dispose();
            knownToFail.Clear();
            instance = null;
        }

        public Coroutine LoadLevel(Package.Asset asset, string playerScene, string uiScene, SimulationMetaData ngs)
        {
            LoadingManager lm = Singleton<LoadingManager>.instance;
            instance.activated = ngs.m_updateMode == SimulationManager.UpdateMode.LoadGame || ngs.m_updateMode == SimulationManager.UpdateMode.NewGameFromMap || Input.GetKey(KeyCode.LeftControl);
            instance.simulationFailed = false;
            Util.DebugPrint("LoadLevel", playerScene, uiScene, ngs.m_updateMode);

            if (!lm.m_currentlyLoading && !lm.m_applicationQuitting)
            {
                if (lm.m_LoadingWrapper != null)
                    lm.m_LoadingWrapper.OnLevelUnloading();

                if (instance.activated)
                {
                    instance.cityName = asset?.name ?? "NewGame";
                    Profiling.Init();
                    new AssetLoader().Setup();
                    new LoadingScreen().Setup();
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

                if (instance.activated)
                    Profiling.stopWatch.Start();

                lm.m_loadingProfilerMain.Reset();
                lm.m_loadingProfilerSimulation.Reset();
                lm.m_loadingProfilerScenes.Reset();

                IEnumerator iter = instance.activated ? instance.LoadLevelCoroutine(asset, playerScene, uiScene, ngs) :
                    (IEnumerator) Util.Invoke(lm, "LoadLevelCoroutine", asset, playerScene, uiScene, ngs);

                return lm.StartCoroutine(iter);
            }

            return null;
        }

        public IEnumerator LoadLevelCoroutine(Package.Asset asset, string playerScene, string uiScene, SimulationMetaData ngs)
        {
            string scene;
            yield return null;

            LoadingManager.instance.SetSceneProgress(0f);
            Util.InvokeVoid(LoadingManager.instance, "PreLoadLevel");
            AsyncTask task = Singleton<SimulationManager>.instance.AddAction("Loading", (IEnumerator) Util.Invoke(LoadingManager.instance, "LoadSimulationData", asset, ngs));
            LoadSaveStatus.activeTask = task;

            if (!LoadingManager.instance.LoadingAnimationComponent.AnimationLoaded)
            {
                LoadingManager.instance.m_loadingProfilerScenes.BeginLoading("LoadingAnimation");
                yield return SceneManager.LoadSceneAsync("LoadingAnimation", LoadSceneMode.Additive);
                LoadingManager.instance.m_loadingProfilerScenes.EndLoading();
            }

            if (LoadingManager.instance.m_loadedEnvironment == null) // loading from main menu
            {
                knownToFail.Clear();
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

                Util.InvokeVoid(LoadingManager.instance, "MetaDataLoaded");
                string mapThemeName = SimulationManager.instance.m_metaData.m_MapThemeMetaData?.name;
                fastLoad = SimulationManager.instance.m_metaData.m_environment == LoadingManager.instance.m_loadedEnvironment && mapThemeName == LoadingManager.instance.m_loadedMapTheme;

                // The game is nicely optimized when loading from the pause menu. We must specifically address the following situation:
                // - environment (biome) stays the same
                // - map theme stays the same
                // - 'load used assets' is enabled
                // - not all assets and prefabs used in the save being loaded are currently in memory.

                if (fastLoad)
                {
                    if (Settings.settings.loadUsed)
                    {
                        int startMillis = Profiling.Millis;

                        while (Profiling.Millis - startMillis < 5000 && !IsSaveDeserialized())
                            yield return null;

                        fastLoad = !AnyMissingAssets();
                    }

                    if (fastLoad) // optimized load
                    {
                        LoadingManager.instance.QueueLoadingAction((IEnumerator) Util.Invoke(LoadingManager.instance, "EssentialScenesLoaded"));
                        LoadingManager.instance.QueueLoadingAction((IEnumerator) Util.Invoke(LoadingManager.instance, "RenderDataReady"));
                    }
                    else // fallback to full load
                    {
                        DestroyLoadedPrefabs();
                        LoadingManager.instance.m_loadedEnvironment = null;
                        LoadingManager.instance.m_loadedMapTheme = null;
                    }
                }
                else // full load
                {
                    // Notice that there is a race condition in the base game at this point: DestroyAllPrefabs ruins the simulation
                    // if its deserialization has progressed far enough. Typically there is no problem.
                    Util.InvokeVoid(LoadingManager.instance, "DestroyAllPrefabs");
                    LoadingManager.instance.m_loadedEnvironment = null;
                    LoadingManager.instance.m_loadedMapTheme = null;
                }
            }

            if (LoadingManager.instance.m_loadedEnvironment == null) // IL_27C
            {
                AsyncOperation op;

                if (!string.IsNullOrEmpty(playerScene))
                {
                    LoadingManager.instance.m_loadingProfilerScenes.BeginLoading(playerScene);
                    op = SceneManager.LoadSceneAsync(playerScene, LoadSceneMode.Single);

                    while (!op.isDone) // IL_2FF
                    {
                        LoadingManager.instance.SetSceneProgress(op.progress * 0.1f);
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

                Util.InvokeVoid(LoadingManager.instance, "MetaDataLoaded");
                KeyValuePair<string, float>[] levels = SetLevels();
                float currentProgress = 0.10f;

                for (int i = 0; i < levels.Length; i++)
                {
                    scene = levels[i].Key;

                    if (string.IsNullOrEmpty(scene)) // just a marker to stop prefab skipping
                    {
                        PrefabLoader.instance?.Revert();
                        continue;
                    }

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

                if (Settings.settings.SkipAny)
                {
                    yield return null;
                    PrefabLoader.instance?.DestroySkipped();
                    yield return null;

                    try
                    {
                        Resources.UnloadUnusedAssets();
                    }
                    catch (Exception e)
                    {
                        UnityEngine.Debug.LogException(e);
                    }

                    yield return null;
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    yield return null;
                }

                // LoadingManager.instance.QueueLoadingAction((IEnumerator) Util.Invoke(LoadingManager.instance, "LoadCustomContent")); // IL_B65
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
                        LoadingManager.instance.SetSceneProgress(0.74f + op.progress * 0.05f);
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
                        LoadingManager.instance.SetSceneProgress(0.79f + op.progress * 0.2f);
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

            LoadingManager.instance.QueueLoadingAction((IEnumerator) Util.Invoke(LoadingManager.instance, "LoadLevelComplete", mode));

            if (Singleton<TelemetryManager>.exists)
                Singleton<TelemetryManager>.instance.StartSession(asset?.name, playerScene, mode, SimulationManager.instance.m_metaData);

            PrefabLoader.instance?.Dispose();
        }

        /// <summary>
        /// Creates the list of standard prefab levels to load.
        /// </summary>
        KeyValuePair<string, float>[] SetLevels()
        {
            bool skipAny = Settings.settings.SkipAny;

            if (skipAny)
                new PrefabLoader().Deploy();

            MethodInfo dlcMethod = typeof(LoadingManager).GetMethod("DLC", BindingFlags.Instance | BindingFlags.NonPublic);
            LoadingManager.instance.m_supportsExpansion[0] = (bool) dlcMethod.Invoke(LoadingManager.instance, new object[] { 369150u });
            LoadingManager.instance.m_supportsExpansion[1] = (bool) dlcMethod.Invoke(LoadingManager.instance, new object[] { 420610u });
            LoadingManager.instance.m_supportsExpansion[2] = (bool) dlcMethod.Invoke(LoadingManager.instance, new object[] { 515191u });

            bool isWinter = SimulationManager.instance.m_metaData.m_environment == "Winter";

            if (isWinter && !LoadingManager.instance.m_supportsExpansion[1])
            {
                SimulationManager.instance.m_metaData.m_environment = "Sunny";
                isWinter = false;
            }

            List<KeyValuePair<string, float>> levels = new List<KeyValuePair<string, float>>(14);
            string scene = (string) Util.Invoke(LoadingManager.instance, "GetLoadingScene");

            if (!string.IsNullOrEmpty(scene))
                levels.Add(new KeyValuePair<string, float>(scene, 0.11f));

            levels.Add(new KeyValuePair<string, float>(SimulationManager.instance.m_metaData.m_environment + "Prefabs", 0.55f));

            if ((bool) dlcMethod.Invoke(LoadingManager.instance, new object[] { 1u }))
                levels.Add(new KeyValuePair<string, float>(isWinter ? "WinterLoginPackPrefabs" : "LoginPackPrefabs", 0.56f));

            levels.Add(new KeyValuePair<string, float>(isWinter ? "WinterPreorderPackPrefabs" : "PreorderPackPrefabs", 0.57f));
            levels.Add(new KeyValuePair<string, float>(isWinter ? "WinterSignupPackPrefabs" : "SignupPackPrefabs", 0.58f));

            if ((bool) dlcMethod.Invoke(LoadingManager.instance, new object[] { 346791u }))
                levels.Add(new KeyValuePair<string, float>("DeluxePackPrefabs", 0.59f));

            if (PlatformService.IsAppOwned(238370u))
                levels.Add(new KeyValuePair<string, float>("MagickaPackPrefabs", 0.60f));

            if (LoadingManager.instance.m_supportsExpansion[0])
                levels.Add(new KeyValuePair<string, float>(isWinter ? "WinterExpansion1Prefabs" : "Expansion1Prefabs", 0.63f));

            if (LoadingManager.instance.m_supportsExpansion[1])
                levels.Add(new KeyValuePair<string, float>("Expansion2Prefabs", 0.64f));

            if (LoadingManager.instance.m_supportsExpansion[2])
                levels.Add(new KeyValuePair<string, float>("Expansion3Prefabs", 0.65f));

            if ((bool) dlcMethod.Invoke(LoadingManager.instance, new object[] { 456200u }))
                levels.Add(new KeyValuePair<string, float>("FootballPrefabs", 0.655f));

            if ((bool) dlcMethod.Invoke(LoadingManager.instance, new object[] { 525940u }))
                levels.Add(new KeyValuePair<string, float>("Football2Prefabs", 0.66f));

            if ((bool) dlcMethod.Invoke(LoadingManager.instance, new object[] { 526610u }))
                levels.Add(new KeyValuePair<string, float>("Football3Prefabs", 0.665f));

            if ((bool) dlcMethod.Invoke(LoadingManager.instance, new object[] { 526611u }))
                levels.Add(new KeyValuePair<string, float>("Football4Prefabs", 0.67f));

            if ((bool) dlcMethod.Invoke(LoadingManager.instance, new object[] { 526612u }))
                levels.Add(new KeyValuePair<string, float>("Football5Prefabs", 0.675f));

            if ((bool) dlcMethod.Invoke(LoadingManager.instance, new object[] { 547501u }))
                levels.Add(new KeyValuePair<string, float>("Station1Prefabs", 0.68f));

            if ((bool) dlcMethod.Invoke(LoadingManager.instance, new object[] { 515190u }))
                levels.Add(new KeyValuePair<string, float>("ModderPack1Prefabs", 0.69f));

            if ((bool) dlcMethod.Invoke(LoadingManager.instance, new object[] { 547500u }))
                levels.Add(new KeyValuePair<string, float>("ModderPack2Prefabs", 0.70f));

            if (skipAny && !Settings.settings.applyToEuropean)
                levels.Add(new KeyValuePair<string, float>(string.Empty, 0f));

            Package.Asset europeanStyles = PackageManager.FindAssetByName("System." + DistrictStyle.kEuropeanStyleName);

            if (europeanStyles != null && europeanStyles.isEnabled)
                levels.Add(new KeyValuePair<string, float>(SimulationManager.instance.m_metaData.m_environment.Equals("Europe") ? "EuropeNormalPrefabs" : "EuropeStylePrefabs", 0.74f));

            if (skipAny && Settings.settings.applyToEuropean)
                levels.Add(new KeyValuePair<string, float>(string.Empty, 0f));

            return levels.ToArray();
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
        public static bool IsSaveDeserialized() => GetSimProgress() > 55;

        /// <summary>
        /// Returns the progress of simulation deserialization.
        /// Note: two threads at play here, old values of m_size might be cached for quite some time.
        /// </summary>
        public static int GetSimProgress()
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
        /// Checks if the savegame needs any assets or prefabs not currently in memory.
        /// </summary>
        bool AnyMissingAssets()
        {
            try
            {
                return UsedAssets.Create().AnyMissing(knownToFail);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
            }

            return true;
        }

        public static void DestroyLoadedPrefabs()
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
        public static void DestroyLoaded<P>() where P : PrefabInfo
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
