using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
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
                Util.DebugPrint("Main", Settings.settings.skip.asString(), Settings.settings.applyToEuropean);
                knownToFail.Clear();
                fastLoad = false;
            }
            else // loading from in-game (the pause menu)
            {
                Util.DebugPrint("Game", Settings.settings.skip.asString(), Settings.settings.applyToEuropean);

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
                    Util.DebugPrint("Simulation progress:", GetSimProgress(), "at", Profiling.Millis);
                    Util.InvokeVoid(LoadingManager.instance, "DestroyAllPrefabs");
                    LoadingManager.instance.m_loadedEnvironment = null;
                    LoadingManager.instance.m_loadedMapTheme = null;
                }
            }

            if (LoadingManager.instance.m_loadedEnvironment == null) // IL_290
            {
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

                KeyValuePair<string, float>[] levels = SetLevels();
                float currentProgress = 0.10f;

                for (int i = 0; i < levels.Length; i++)
                {
                    scene = levels[i].Key;

                    if (string.IsNullOrEmpty(scene)) // just a marker to stop prefab skipping
                    {
                        PrefabLoader.instance?.Revert();
                        Sc("Reverted");
                        continue;
                    }

                    LoadingManager.instance.m_loadingProfilerScenes.BeginLoading(scene);
                    Sc(scene);
                    op = Application.LoadLevelAdditiveAsync(scene);

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
                    Sc(scene);
                    op = Application.LoadLevelAdditiveAsync(scene);

                    while (!op.isDone) // IL_C47
                    {
                        LoadingManager.instance.SetSceneProgress(0.73f + op.progress * 0.06f);
                        yield return null;
                    }

                    LoadingManager.instance.m_loadingProfilerScenes.EndLoading();
                }

                if (!simulationFailed)
                    simulationFailed = HasFailed(task);

                if (!string.IsNullOrEmpty(uiScene)) // IL_C67
                {
                    LoadingManager.instance.m_loadingProfilerScenes.BeginLoading(uiScene);
                    Sc(uiScene);
                    op = Application.LoadLevelAdditiveAsync(uiScene);

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

            int n = PrefabCollection<BuildingInfo>.LoadedCount();
            Sc("Scene buildings - " + n);

            for (int i = 0; i < n; i++)
                PrefabLoader.w?.WriteLine("  " + (PrefabCollection<BuildingInfo>.GetLoaded((uint) i)?.gameObject.name ?? "null"));

            PrefabLoader.instance?.Dispose();
        }

        void Sc(string s)
        {
            s += " - " + Profiling.Millis;
            PrefabLoader.w?.WriteLine("\n" + s);
            PrefabLoader.w?.WriteLine(new string('-', 8));
        }

        /// <summary>
        /// Creates the list of standard prefab levels to load.
        /// </summary>
        KeyValuePair<string, float>[] SetLevels()
        {
            bool skipAny = Settings.settings.SkipAny;

            if (skipAny)
            {
                new PrefabLoader().Deploy();
                PrefabLoader.w.WriteLine("\nEnv: " + SimulationManager.instance.m_metaData.m_environment + "  Theme: " + (SimulationManager.instance.m_metaData.m_MapThemeMetaData?.name ?? "null") + "  City: " + instance.cityName);
            }

            LoadingManager.instance.m_supportsExpansion[0] = (bool) Util.Invoke(LoadingManager.instance, "DLC", 369150u);
            LoadingManager.instance.m_supportsExpansion[1] = (bool) Util.Invoke(LoadingManager.instance, "DLC", 420610u);
            bool isWinter = SimulationManager.instance.m_metaData.m_environment == "Winter";

            if (isWinter && !LoadingManager.instance.m_supportsExpansion[1])
            {
                SimulationManager.instance.m_metaData.m_environment = "Sunny";
                isWinter = false;
            }

            List<KeyValuePair<string, float>> levels = new List<KeyValuePair<string, float>>(12);
            string scene = (string) Util.Invoke(LoadingManager.instance, "GetLoadingScene");

            if (!string.IsNullOrEmpty(scene))
                levels.Add(new KeyValuePair<string, float>(scene, 0.11f));

            levels.Add(new KeyValuePair<string, float>(SimulationManager.instance.m_metaData.m_environment + "Prefabs", 0.57f));

            if ((bool) Util.Invoke(LoadingManager.instance, "DLC", 1u))
                levels.Add(new KeyValuePair<string, float>(isWinter ? "WinterLoginPackPrefabs" : "LoginPackPrefabs", 0.58f));

            levels.Add(new KeyValuePair<string, float>(isWinter ? "WinterPreorderPackPrefabs" : "PreorderPackPrefabs", 0.59f));
            levels.Add(new KeyValuePair<string, float>(isWinter ? "WinterSignupPackPrefabs" : "SignupPackPrefabs", 0.60f));

            if ((bool) Util.Invoke(LoadingManager.instance, "DLC", 346791u))
                levels.Add(new KeyValuePair<string, float>("DeluxePackPrefabs", 0.61f));

            if (Steam.IsAppOwned(238370u))
                levels.Add(new KeyValuePair<string, float>("MagickaPackPrefabs", 0.62f));

            if (LoadingManager.instance.m_supportsExpansion[0])
                levels.Add(new KeyValuePair<string, float>(isWinter ? "WinterExpansion1Prefabs" : "Expansion1Prefabs", 0.65f));

            if (LoadingManager.instance.m_supportsExpansion[1])
                levels.Add(new KeyValuePair<string, float>("Expansion2Prefabs", 0.66f));

            if ((bool) Util.Invoke(LoadingManager.instance, "DLC", 456200u))
                levels.Add(new KeyValuePair<string, float>("FootballPrefabs", 0.67f));

            if ((bool) Util.Invoke(LoadingManager.instance, "DLC", 525940u))
                levels.Add(new KeyValuePair<string, float>("Football2Prefabs", 0.68f));

            if ((bool) Util.Invoke(LoadingManager.instance, "DLC", 526610u))
                levels.Add(new KeyValuePair<string, float>("Football3Prefabs", 0.685f));

            if ((bool) Util.Invoke(LoadingManager.instance, "DLC", 526611u))
                levels.Add(new KeyValuePair<string, float>("Football4Prefabs", 0.69f));

            if ((bool) Util.Invoke(LoadingManager.instance, "DLC", 526612u))
                levels.Add(new KeyValuePair<string, float>("Football5Prefabs", 0.695f));

            if ((bool) Util.Invoke(LoadingManager.instance, "DLC", 515190u))
                levels.Add(new KeyValuePair<string, float>("ModderPack1Prefabs", 0.70f));

            if (skipAny && !Settings.settings.applyToEuropean)
                levels.Add(new KeyValuePair<string, float>(string.Empty, 0f));

            Package.Asset europeanStyles = PackageManager.FindAssetByName("System." + DistrictStyle.kEuropeanStyleName);

            if (europeanStyles != null && europeanStyles.isEnabled)
                levels.Add(new KeyValuePair<string, float>(SimulationManager.instance.m_metaData.m_environment.Equals("Europe") ? "EuropeNormalPrefabs" : "EuropeStylePrefabs", 0.73f));

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
                int cnt = (int) dict.GetType().GetMethod("get_Count", BindingFlags.Instance | BindingFlags.Public).Invoke(dict, null);
                Util.DebugPrint("DestroyLoaded", typeof(P).Name, "left behind:", cnt);
                prefabs.Clear(); prefabs.Capacity = 0;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
            }
        }
    }
}
