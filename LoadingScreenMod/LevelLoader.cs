using System;
using System.Collections;
using System.Collections.Generic;
using ColossalFramework;
using ColossalFramework.Packaging;
using ColossalFramework.Steamworks;
using ColossalFramework.UI;
using UnityEngine;

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
            instance.activated = ngs.m_updateMode == SimulationManager.UpdateMode.LoadGame || ngs.m_updateMode == SimulationManager.UpdateMode.NewGame;
            instance.simulationFailed = false;

            if (!lm.m_currentlyLoading && !lm.m_applicationQuitting)
            {
                if (lm.m_LoadingWrapper != null)
                    lm.m_LoadingWrapper.OnLevelUnloading();

                if (instance.activated)
                {
                    instance.cityName = asset?.name ?? "NewGame";
                    Profiling.Init();
                    new Sharing().Deploy();
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
                yield return Application.LoadLevelAdditiveAsync("LoadingAnimation");
                LoadingManager.instance.m_loadingProfilerScenes.EndLoading();
            }

            if (LoadingManager.instance.m_loadedEnvironment == null) // loading from main menu
            {
                Util.DebugPrint("Loading from main menu", Profiling.Millis);
                knownToFail.Clear();
                fastLoad = false;
            }
            else // loading from in-game
            {
                Util.DebugPrint("Loading from in-game", Profiling.Millis);

                while (!LoadingManager.instance.m_metaDataLoaded && !task.completedOrFailed) // IL_158
                    yield return null;

                if (SimulationManager.instance.m_metaData == null)
                {
                    SimulationManager.instance.m_metaData = new SimulationMetaData();
                    SimulationManager.instance.m_metaData.m_environment = "Sunny";
                    SimulationManager.instance.m_metaData.Merge(ngs);
                }

                string mapThemeName = SimulationManager.instance.m_metaData.m_MapThemeMetaData?.name;
                fastLoad = SimulationManager.instance.m_metaData.m_environment == LoadingManager.instance.m_loadedEnvironment && mapThemeName == LoadingManager.instance.m_loadedMapTheme;

                // The game is nicely optimized when loading from in-game. We must specifically address the following situation:
                // - loading from in-game
                // - environment (biome) stays the same
                // - map theme stays the same
                // - 'load used assets' is enabled
                // - not all assets used in the save being loaded are currently in memory.

                if (fastLoad)
                {
                    if (Settings.settings.loadUsed)
                    {
                        int startMillis = Profiling.Millis;

                        while (Profiling.Millis - startMillis < 3000 && !IsSaveDeserialized())
                        {
                            Util.DebugPrint("Waiting for simulation", Profiling.Millis);
                            yield return null;
                        }

                        fastLoad = !AnyMissingAssets();
                    }

                    if (fastLoad) // optimized load
                    {
                        Util.DebugPrint("Optimized load", Profiling.Millis);
                        LoadingManager.instance.QueueLoadingAction((IEnumerator) Util.Invoke(LoadingManager.instance, "EssentialScenesLoaded"));
                        LoadingManager.instance.QueueLoadingAction((IEnumerator) Util.Invoke(LoadingManager.instance, "RenderDataReady"));
                    }
                    else // fallback to full load
                    {
                        Util.DebugPrint("Fallback to full", Profiling.Millis);
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
                    Util.DebugPrint("DestroyAllPrefabs", Profiling.Millis);
                }
            }

            if (LoadingManager.instance.m_loadedEnvironment == null) // IL_290
            {
                Util.DebugPrint("Starting full load", Profiling.Millis);
                AsyncOperation op;

                if (!string.IsNullOrEmpty(playerScene))
                {
                    LoadingManager.instance.m_loadingProfilerScenes.BeginLoading(playerScene);
                    op = Application.LoadLevelAsync(playerScene);

                    while (!op.isDone) // IL_312
                    {
                        LoadingManager.instance.SetSceneProgress(op.progress * 0.1f);
                        yield return null;
                    }

                    LoadingManager.instance.m_loadingProfilerScenes.EndLoading();
                }

                while (!LoadingManager.instance.m_metaDataLoaded && !task.completedOrFailed) // IL_34F
                    yield return null;

                if (SimulationManager.instance.m_metaData == null)
                {
                    SimulationManager.instance.m_metaData = new SimulationMetaData();
                    SimulationManager.instance.m_metaData.m_environment = "Sunny";
                    SimulationManager.instance.m_metaData.Merge(ngs);
                }

                LoadingManager.instance.m_supportsExpansion[0] = (bool) Util.Invoke(LoadingManager.instance, "DLC", 369150u);
                LoadingManager.instance.m_supportsExpansion[1] = (bool) Util.Invoke(LoadingManager.instance, "DLC", 420610u);
                bool isWinter = SimulationManager.instance.m_metaData.m_environment == "Winter";

                if (isWinter && !LoadingManager.instance.m_supportsExpansion[1])
                {
                    SimulationManager.instance.m_metaData.m_environment = "Sunny";
                    isWinter = false;
                }

                scene = (string) Util.Invoke(LoadingManager.instance, "GetLoadingScene");

                if (!string.IsNullOrEmpty(scene))
                {
                    LoadingManager.instance.m_loadingProfilerScenes.BeginLoading(scene);
                    op = Application.LoadLevelAdditiveAsync(scene);

                    while (!op.isDone) // IL_4D0
                    {
                        LoadingManager.instance.SetSceneProgress(0.1f + op.progress * 0.03f);
                        yield return null;
                    }

                    LoadingManager.instance.m_loadingProfilerScenes.EndLoading();
                }

                scene = SimulationManager.instance.m_metaData.m_environment + "Prefabs"; // IL_4F0

                if (!string.IsNullOrEmpty(scene))
                {
                    LoadingManager.instance.m_loadingProfilerScenes.BeginLoading(scene);
                    op = Application.LoadLevelAdditiveAsync(scene);

                    while (!op.isDone) // IL_585
                    {
                        LoadingManager.instance.SetSceneProgress(0.13f + op.progress * 0.5f);
                        yield return null;
                    }

                    LoadingManager.instance.m_loadingProfilerScenes.EndLoading();
                }

                if ((bool) Util.Invoke(LoadingManager.instance, "DLC", 1u)) // IL_5A5
                {
                    scene = isWinter ? "WinterLoginPackPrefabs" : "LoginPackPrefabs";
                    LoadingManager.instance.m_loadingProfilerScenes.BeginLoading(scene);
                    op = Application.LoadLevelAdditiveAsync(scene);

                    while (!op.isDone) // IL_63C
                    {
                        LoadingManager.instance.SetSceneProgress(0.63f + op.progress * 0.02f);
                        yield return null;
                    }

                    LoadingManager.instance.m_loadingProfilerScenes.EndLoading();
                }

                if ((bool) Util.Invoke(LoadingManager.instance, "DLC", 340160u)) // IL_65C
                {
                    scene = isWinter ? "WinterPreorderPackPrefabs" : "PreorderPackPrefabs";
                    LoadingManager.instance.m_loadingProfilerScenes.BeginLoading(scene);
                    op = Application.LoadLevelAdditiveAsync(scene);

                    while (!op.isDone) // IL_6F8
                    {
                        LoadingManager.instance.SetSceneProgress(0.65f + op.progress * 0.02f);
                        yield return null;
                    }

                    LoadingManager.instance.m_loadingProfilerScenes.EndLoading();
                }

                scene = isWinter ? "WinterSignupPackPrefabs" : "SignupPackPrefabs"; // IL_718
                LoadingManager.instance.m_loadingProfilerScenes.BeginLoading(scene);
                op = Application.LoadLevelAdditiveAsync(scene);

                while (!op.isDone) // IL_79F
                {
                    LoadingManager.instance.SetSceneProgress(0.67f + op.progress * 0.01f);
                    yield return null;
                }

                LoadingManager.instance.m_loadingProfilerScenes.EndLoading();

                if ((bool) Util.Invoke(LoadingManager.instance, "DLC", 346791u))
                {
                    scene = "DeluxePackPrefabs";
                    LoadingManager.instance.m_loadingProfilerScenes.BeginLoading(scene);
                    op = Application.LoadLevelAdditiveAsync(scene);

                    while (!op.isDone) // IL_846
                    {
                        LoadingManager.instance.SetSceneProgress(0.68f + op.progress * 0.02f);
                        yield return null;
                    }

                    LoadingManager.instance.m_loadingProfilerScenes.EndLoading();
                }

                if (Steam.IsAppOwned(238370u)) // IL_866
                {
                    scene = "MagickaPackPrefabs";
                    LoadingManager.instance.m_loadingProfilerScenes.BeginLoading(scene);
                    op = Application.LoadLevelAdditiveAsync(scene);

                    while (!op.isDone) // IL_8ED
                    {
                        LoadingManager.instance.SetSceneProgress(0.7f + op.progress * 0.01f);
                        yield return null;
                    }

                    LoadingManager.instance.m_loadingProfilerScenes.EndLoading();
                }

                if (LoadingManager.instance.m_supportsExpansion[0]) // IL_90D
                {
                    scene = isWinter ? "WinterExpansion1Prefabs" : "Expansion1Prefabs";
                    LoadingManager.instance.m_loadingProfilerScenes.BeginLoading(scene);
                    op = Application.LoadLevelAdditiveAsync(scene);

                    while (!op.isDone) // IL_9A6
                    {
                        LoadingManager.instance.SetSceneProgress(0.71f + op.progress * 0.02f);
                        yield return null;
                    }

                    LoadingManager.instance.m_loadingProfilerScenes.EndLoading();
                }

                if (LoadingManager.instance.m_supportsExpansion[1]) // IL_9C6
                {
                    scene = "Expansion2Prefabs";
                    LoadingManager.instance.m_loadingProfilerScenes.BeginLoading(scene);
                    op = Application.LoadLevelAdditiveAsync(scene);

                    while (!op.isDone) // IL_A4A
                    {
                        LoadingManager.instance.SetSceneProgress(0.73f + op.progress * 0.02f);
                        yield return null;
                    }

                    LoadingManager.instance.m_loadingProfilerScenes.EndLoading();
                }

                Package.Asset europeanStyles = PackageManager.FindAssetByName("System." + DistrictStyle.kEuropeanStyleName); // IL_A6A

                if (europeanStyles != null && europeanStyles.isEnabled)
                {
                    if (SimulationManager.instance.m_metaData.m_environment.Equals("Europe"))
                        scene = "EuropeNormalPrefabs";
                    else
                        scene = "EuropeStylePrefabs";

                    LoadingManager.instance.m_loadingProfilerScenes.BeginLoading(scene);
                    op = Application.LoadLevelAdditiveAsync(scene);

                    while (!op.isDone) // IL_B45
                    {
                        LoadingManager.instance.SetSceneProgress(0.75f + op.progress * 0.02f);
                        yield return null;
                    }

                    LoadingManager.instance.m_loadingProfilerScenes.EndLoading();
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
                    op = Application.LoadLevelAdditiveAsync(scene);

                    while (!op.isDone) // IL_C47
                    {
                        LoadingManager.instance.SetSceneProgress(0.77f + op.progress * 0.11f);
                        yield return null;
                    }

                    LoadingManager.instance.m_loadingProfilerScenes.EndLoading();
                }

                if (!simulationFailed)
                    simulationFailed = HasFailed(task);

                if (!string.IsNullOrEmpty(uiScene)) // IL_C67
                {
                    LoadingManager.instance.m_loadingProfilerScenes.BeginLoading(uiScene);
                    op = Application.LoadLevelAdditiveAsync(uiScene);

                    while (!op.isDone) // IL_CDE
                    {
                        LoadingManager.instance.SetSceneProgress(0.88f + op.progress * 0.11f);
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
                    yield return Application.LoadLevelAdditiveAsync(scene);
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
                catch(Exception e)
                {
                    UnityEngine.Debug.LogException(e);
                }
            }

            return false;
        }

        /// <summary>
        /// Checks if buildings, props, trees, and vehicles have been deserialized from the savegame.
        /// Note: two threads at play here, old values of m_size might be cached for quite some time.
        /// </summary>
        static bool IsSaveDeserialized()
        {
            FastList<LoadingProfiler.Event> events = ProfilerSource.GetEvents(LoadingManager.instance.m_loadingProfilerSimulation);
            return events.m_size > 55;
        }

        /// <summary>
        /// Checks if the savegame needs any assets not currently in memory.
        /// </summary>
        bool AnyMissingAssets()
        {
            try
            {
                new UsedAssets().LookupUsed();
                bool ret = UsedAssets.instance.AnyMissing(knownToFail);
                UsedAssets.instance.Dispose();
                return ret;
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
                Util.DebugPrint("DestroyLoaded", typeof(P).Name, n, prefabs.Count, Profiling.Millis);

                // This has not been necessary yet. However, it is quite fatal if prefabs are left behind so better be sure.
                if (n != prefabs.Count)
                {
                    object fastList = Util.GetStatic(typeof(PrefabCollection<P>), "m_scenePrefabs");
                    Util.Set(fastList, "m_size", 0);
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
            }
        }

    }
}
