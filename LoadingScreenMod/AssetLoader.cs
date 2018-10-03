using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ColossalFramework;
using ColossalFramework.Packaging;
using ColossalFramework.PlatformServices;
using ColossalFramework.Plugins;
using ColossalFramework.UI;
using ICities;
using UnityEngine;
using static AssetDataWrapper;
using static ColossalFramework.Plugins.PluginManager;

namespace LoadingScreenModTest
{
    /// <summary>
    /// LoadCustomContent coroutine from LoadingManager.
    /// </summary>
    public sealed class AssetLoader : Instance<AssetLoader>
    {
        HashSet<string> failedAssets = new HashSet<string>(), loadedProps = new HashSet<string>(), loadedTrees = new HashSet<string>(),
            loadedBuildings = new HashSet<string>(), loadedVehicles = new HashSet<string>(), loadedCitizens = new HashSet<string>(),
            loadedNets = new HashSet<string>(), loadedIntersections = new HashSet<string>(), dontSpawnNormally = new HashSet<string>();
        HashSet<string>[] allLoads;
        int[] loadQueueIndex;
        Dictionary<string, SomeMetaData> metaDatas = new Dictionary<string, SomeMetaData>(128);
        Dictionary<string, CustomAssetMetaData> citizenMetaDatas = new Dictionary<string, CustomAssetMetaData>();
        Dictionary<string, PluginInfo> plugins;

        internal readonly Stack<Package.Asset> stack = new Stack<Package.Asset>(4); // the asset loading stack
        int beginMillis, lastMillis, assetCount;
        public bool hasStarted, hasFinished;
        readonly bool reportAssets = Settings.settings.reportAssets,
            isWin = Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor;

        internal const int yieldInterval = 350;
        float progress;
        internal HashSet<string> Props => loadedProps;
        internal HashSet<string> Trees => loadedTrees;
        internal HashSet<string> Buildings => loadedBuildings;
        internal HashSet<string> Vehicles => loadedVehicles;
        internal HashSet<string> Nets => loadedNets;
        internal bool IsIntersection(string fullName) => loadedIntersections.Contains(fullName);
        internal bool HasFailed(string fullName) => failedAssets.Contains(fullName);
        internal Package.Asset Current => stack.Count > 0 ? stack.Peek() : null;

        private AssetLoader()
        {
            allLoads = new HashSet<string>[] { loadedBuildings, loadedProps, loadedTrees, loadedVehicles, loadedVehicles, loadedBuildings, loadedBuildings,
                loadedProps, loadedCitizens, loadedNets, loadedNets, loadedBuildings };

            loadQueueIndex = new int[] { 5, 1, 1, 7, 7, 5, 5, 1, 0, 3, 3, 3 };
        }

        public void Setup()
        {
            Sharing.Create();

            if (reportAssets)
                AssetReport.Create();
        }

        public void Dispose()
        {
            UsedAssets.instance?.Dispose();
            Sharing.instance?.Dispose();
            LevelLoader.instance.AddFailedAssets(failedAssets);
            failedAssets.Clear(); loadedProps.Clear(); loadedTrees.Clear(); loadedBuildings.Clear(); loadedVehicles.Clear(); loadedCitizens.Clear();
            loadedNets.Clear(); loadedIntersections.Clear(); dontSpawnNormally.Clear(); metaDatas.Clear(); citizenMetaDatas.Clear();
            failedAssets = null; loadedProps = null; loadedTrees = null; loadedBuildings = null; loadedVehicles = null; loadedCitizens = null;
            loadedNets = null;  loadedIntersections = null; dontSpawnNormally = null; metaDatas = null; citizenMetaDatas = null;
            plugins = null; allLoads = null; loadQueueIndex = null; instance = null;
        }

        void Report()
        {
            if (Settings.settings.loadUsed)
                UsedAssets.instance.ReportMissingAssets();

            if (reportAssets)
            {
                AssetReport.instance.Save();
                AssetReport.instance.Dispose();
            }

            Sharing.instance?.Dispose();
        }

        internal static bool IsActive() => instance != null && instance.hasStarted && !instance.hasFinished;

        public IEnumerator LoadCustomContent()
        {
            LoadingManager.instance.m_loadingProfilerMain.BeginLoading("LoadCustomContent");
            LoadingManager.instance.m_loadingProfilerCustomContent.Reset();
            LoadingManager.instance.m_loadingProfilerCustomAsset.Reset();
            LoadingManager.instance.m_loadingProfilerCustomContent.BeginLoading("District Styles");
            LoadingManager.instance.m_loadingProfilerCustomAsset.PauseLoading();
            hasStarted = true;

            int i, j;
            DistrictStyle districtStyle;
            DistrictStyleMetaData districtStyleMetaData;
            List<DistrictStyle> districtStyles = new List<DistrictStyle>();
            HashSet<string> styleBuildings = new HashSet<string>();
            FastList<DistrictStyleMetaData> districtStyleMetaDatas = new FastList<DistrictStyleMetaData>();
            FastList<Package> districtStylePackages = new FastList<Package>();
            Package.Asset europeanStyles = PackageManager.FindAssetByName("System." + DistrictStyle.kEuropeanStyleName);

            if (europeanStyles != null && europeanStyles.isEnabled)
            {
                districtStyle = new DistrictStyle(DistrictStyle.kEuropeanStyleName, true);
                Util.InvokeVoid(LoadingManager.instance, "AddChildrenToBuiltinStyle", GameObject.Find("European Style new"), districtStyle, false);
                Util.InvokeVoid(LoadingManager.instance, "AddChildrenToBuiltinStyle", GameObject.Find("European Style others"), districtStyle, true);

                if (Settings.settings.SkipPrefabs)
                    PrefabLoader.RemoveSkipped(districtStyle);

                districtStyles.Add(districtStyle);
            }

            if ((bool) typeof(LoadingManager).GetMethod("DLC", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(LoadingManager.instance, new object[] { 715190u }))
            {
                Package.Asset asset = PackageManager.FindAssetByName("System." + DistrictStyle.kEuropeanSuburbiaStyleName);

                if (asset != null && asset.isEnabled)
                {
                    districtStyle = new DistrictStyle(DistrictStyle.kEuropeanSuburbiaStyleName, true);
                    Util.InvokeVoid(LoadingManager.instance, "AddChildrenToBuiltinStyle", GameObject.Find("Modder Pack 3"), districtStyle, false);

                    if (Settings.settings.SkipPrefabs)
                        PrefabLoader.RemoveSkipped(districtStyle);

                    districtStyles.Add(districtStyle);
                }
            }

            foreach (Package.Asset asset in PackageManager.FilterAssets(UserAssetType.DistrictStyleMetaData))
            {
                try
                {
                    if (asset != null && asset.isEnabled)
                    {
                        districtStyleMetaData = asset.Instantiate<DistrictStyleMetaData>();

                        if (districtStyleMetaData != null && !districtStyleMetaData.builtin)
                        {
                            districtStyleMetaDatas.Add(districtStyleMetaData);
                            districtStylePackages.Add(asset.package);

                            if (districtStyleMetaData.assets != null)
                                for (i = 0; i < districtStyleMetaData.assets.Length; i++)
                                    styleBuildings.Add(districtStyleMetaData.assets[i]);
                        }
                    }
                }
                catch (Exception ex)
                {
                    CODebugBase<LogChannel>.Warn(LogChannel.Modding, string.Concat(new object[] {ex.GetType(), ": Loading custom district style failed[", asset, "]\n", ex.Message}));
                }
            }

            LoadingManager.instance.m_loadingProfilerCustomAsset.ContinueLoading();
            LoadingManager.instance.m_loadingProfilerCustomContent.EndLoading();

            if (Settings.settings.loadUsed)
                UsedAssets.Create();

            lastMillis = Profiling.Millis;
            LoadingScreen.instance.DualSource.Add("Custom Assets");
            LoadingManager.instance.m_loadingProfilerCustomContent.BeginLoading("Calculating asset load order");
            Util.DebugPrint("GetLoadQueue", Profiling.Millis);
            Package.Asset[] queue = GetLoadQueue(styleBuildings);
            Util.DebugPrint("LoadQueue", queue.Length, Profiling.Millis);
            LoadingManager.instance.m_loadingProfilerCustomContent.EndLoading();
            plugins = (Dictionary<string, PluginInfo>) Util.Get(Singleton<PluginManager>.instance, "m_Plugins");
            //PrintPlugins();

            LoadingManager.instance.m_loadingProfilerCustomContent.BeginLoading("Loading Custom Assets");
            Sharing.instance.Start(queue);
            beginMillis = Profiling.Millis;

            for (i = 0; i < queue.Length; i++)
            {
                Package.Asset assetRef = queue[i];
                Console.WriteLine(string.Concat("[LSMT] ", i, ": ", Profiling.Millis, " ", assetCount, " ", Sharing.instance.currentCount, " ",
                    assetRef.fullName, Sharing.instance.ThreadStatus));

                if ((i & 31) == 0)
                    PrintMem();

                Sharing.instance.WaitForWorkers();

                try
                {
                    stack.Clear();
                    LoadImpl(assetRef);
                }
                catch (Exception e)
                {
                    AssetFailed(assetRef.fullName, e);
                }

                Sharing.instance.ManageLoadQueue(i);

                if (Profiling.Millis - lastMillis > yieldInterval)
                {
                    lastMillis = Profiling.Millis;
                    progress = 0.15f + (i + 1) * 0.7f / queue.Length;
                    LoadingScreen.instance.SetProgress(progress, progress, assetCount, assetCount - i - 1 + queue.Length, beginMillis, lastMillis);
                    yield return null;
                }
            }

            lastMillis = Profiling.Millis;
            LoadingScreen.instance.SetProgress(0.85f, 1f, assetCount, assetCount, beginMillis, lastMillis);
            LoadingManager.instance.m_loadingProfilerCustomContent.EndLoading();
            Util.DebugPrint("Custom assets loaded in", lastMillis - beginMillis);
            PrintMem();
            queue = null;
            stack.Clear();
            Report();

            LoadingManager.instance.m_loadingProfilerCustomContent.BeginLoading("Finalizing District Styles");
            LoadingManager.instance.m_loadingProfilerCustomAsset.PauseLoading();

            for (i = 0; i < districtStyleMetaDatas.m_size; i++)
            {
                try
                {
                    districtStyleMetaData = districtStyleMetaDatas.m_buffer[i];
                    districtStyle = new DistrictStyle(districtStyleMetaData.name, false);

                    if (districtStylePackages.m_buffer[i].GetPublishedFileID() != PublishedFileId.invalid)
                        districtStyle.PackageName = districtStylePackages.m_buffer[i].packageName;

                    if (districtStyleMetaData.assets != null)
                    {
                        for(j = 0; j < districtStyleMetaData.assets.Length; j++)
                        {
                            BuildingInfo bi = CustomDeserializer.FindLoaded<BuildingInfo>(districtStyleMetaData.assets[j] + "_Data");

                            if (bi != null)
                            {
                                districtStyle.Add(bi);

                                if (districtStyleMetaData.builtin) // this is always false
                                    bi.m_dontSpawnNormally = !districtStyleMetaData.assetRef.isEnabled;
                            }
                            else
                                CODebugBase<LogChannel>.Warn(LogChannel.Modding, "Warning: Missing asset (" + districtStyleMetaData.assets[j] + ") in style " + districtStyleMetaData.name);
                        }

                        districtStyles.Add(districtStyle);
                    }
                }
                catch (Exception ex)
                {
                    CODebugBase<LogChannel>.Warn(LogChannel.Modding, ex.GetType() + ": Loading district style failed\n" + ex.Message);
                }
            }

            Singleton<DistrictManager>.instance.m_Styles = districtStyles.ToArray();

            if (Singleton<BuildingManager>.exists)
                Singleton<BuildingManager>.instance.InitializeStyleArray(districtStyles.Count);

            LoadingManager.instance.m_loadingProfilerCustomAsset.ContinueLoading();
            LoadingManager.instance.m_loadingProfilerCustomContent.EndLoading();
            LoadingManager.instance.m_loadingProfilerMain.EndLoading();
            hasFinished = true;
        }

        internal void PrintMem()
        {
            string s = "[LSMT] Mem ";

            try
            {
                if (isWin)
                {
                    MemoryAPI.GetUsage(out ulong pagefileUsage, out ulong workingSetSize);
                    int wsMegas = (int) (workingSetSize >> 20), pfMegas = (int) (pagefileUsage>> 20);
                    s += string.Concat(wsMegas.ToString(), " ", pfMegas.ToString(), " ");
                }

                s = string.Concat(s, GC.CollectionCount(0).ToString());

                if (Sharing.HasInstance)
                    s += string.Concat(" ", Sharing.instance.Misses.ToString(), " ", Sharing.instance.WorkersAhead.ToString());
            }
            catch (Exception)
            {
            }

            Console.WriteLine(s);
        }

        internal void LoadImpl(Package.Asset assetRef)
        {
            try
            {
                stack.Push(assetRef);
                LoadingManager.instance.m_loadingProfilerCustomAsset.BeginLoading(AssetName(assetRef.name));
                GameObject go = AssetDeserializer.Instantiate(assetRef) as GameObject;
                CustomAssetMetaData.Type type = GetMetaType(assetRef);
                string packageName = assetRef.package.packageName;
                string fullName = type < CustomAssetMetaData.Type.RoadElevation ? packageName + "." + go.name : PillarOrElevationName(packageName, go.name);
                go.name = fullName;
                go.SetActive(false);
                PrefabInfo info = go.GetComponent<PrefabInfo>();
                info.m_isCustomContent = true;

                if (info.m_Atlas != null && !string.IsNullOrEmpty(info.m_InfoTooltipThumbnail) && info.m_Atlas[info.m_InfoTooltipThumbnail] != null)
                    info.m_InfoTooltipAtlas = info.m_Atlas;

                PropInfo pi;
                TreeInfo ti;
                BuildingInfo bi;
                VehicleInfo vi;
                CitizenInfo ci;
                NetInfo ni;

                if ((pi = go.GetComponent<PropInfo>()) != null)
                {
                    if (pi.m_lodObject != null)
                        pi.m_lodObject.SetActive(false);

                    Initialize(pi);
                    loadedProps.Add(fullName);
                }
                else if ((ti = go.GetComponent<TreeInfo>()) != null)
                {
                    Initialize(ti);
                    loadedTrees.Add(fullName);
                }
                else if ((bi = go.GetComponent<BuildingInfo>()) != null)
                {
                    if (bi.m_lodObject != null)
                        bi.m_lodObject.SetActive(false);

                    bi.m_dontSpawnNormally = dontSpawnNormally.Remove(fullName);
                    Initialize(bi);
                    loadedBuildings.Add(fullName);

                    if (bi.GetAI() is IntersectionAI)
                        loadedIntersections.Add(fullName);
                }
                else if ((vi = go.GetComponent<VehicleInfo>()) != null)
                {
                    if (vi.m_lodObject != null)
                        vi.m_lodObject.SetActive(false);

                    Initialize(vi);
                    loadedVehicles.Add(fullName);
                }
                else if ((ci = go.GetComponent<CitizenInfo>()) != null)
                {
                    if (ci.m_lodObject != null)
                        ci.m_lodObject.SetActive(false);

                    if (ci.InitializeCustomPrefab(citizenMetaDatas[assetRef.fullName]))
                    {
                        citizenMetaDatas.Remove(assetRef.fullName);
                        ci.gameObject.SetActive(true);
                        Initialize(ci);
                        loadedCitizens.Add(fullName);
                    }
                    else
                    {
                        info = null;
                        CODebugBase<LogChannel>.Warn(LogChannel.Modding, "Custom citizen [" + assetRef.fullName + "] template not available in selected theme. Asset not added in game.");
                    }
                }
                else if ((ni = go.GetComponent<NetInfo>()) != null)
                {
                    loadedNets.Add(fullName);
                    Initialize(ni);
                }
                else
                    info = null;

                if (info != null)
                {
                    string path = Path.GetDirectoryName(assetRef.package.packagePath);

                    if (!string.IsNullOrEmpty(path) && plugins.TryGetValue(path, out PluginInfo plugin))
                    {
                        IAssetDataExtension[] extensions = plugin.GetInstances<IAssetDataExtension>();

                        if (extensions.Length > 0)
                        {
                            UserAssetData uad = GetUserAssetData(assetRef, out string name);

                            if (uad != null)
                            {
                                for (int i = 0; i < extensions.Length; i++)
                                {
                                    try
                                    {
                                        extensions[i].OnAssetLoaded(name, info, uad.Data);
                                    }
                                    catch (Exception e)
                                    {
                                        ModException ex = new ModException("The Mod " + plugin.ToString() + " has caused an error when loading " + fullName, e);
                                        UIView.ForwardException(ex);
                                        Debug.LogException(ex);
                                    }
                                }
                            }
                            else
                                Util.DebugPrint("UserAssetData is null for", fullName);
                        }
                    }
                }
            }
            finally
            {
                stack.Pop();
                assetCount++;
                LoadingManager.instance.m_loadingProfilerCustomAsset.EndLoading();
            }
        }

        void Initialize<T>(T info) where T : PrefabInfo
        {
            string fullName = info.gameObject.name;
            string brokenAssets = LoadingManager.instance.m_brokenAssets;
            PrefabCollection<T>.InitializePrefabs("Custom Assets", info, null);
            LoadingManager.instance.m_brokenAssets = brokenAssets;

            if (CustomDeserializer.FindLoaded<T>(fullName) == null)
                throw new Exception(string.Concat(typeof(T).Name, " ", fullName, " failed"));
        }

        Package.Asset[] GetLoadQueue(HashSet<string> styleBuildings)
        {
            Package[] packages = PackageManager.allPackages.ToArray();
            Array.Sort(packages, (a, b) => string.Compare(a.packageName, b.packageName));
            List<Package.Asset> assets = new List<Package.Asset>(8);
            List<CustomAssetMetaData> metas = new List<CustomAssetMetaData>(8);

            // Why this load order? By having related and identical assets close to each other, we get more loader cache hits (of meshes and textures)
            // in Sharing. We also get faster disk reads.
            // [0] propvar and prop, citizen  [1] prop, tree  [2] pillar and elevation and road  [3] road
            // [4] sub-building and building  [5] building    [6] trailer and vehicle            [7] vehicle
            List<Package.Asset>[] queues = { new List<Package.Asset>(4), new List<Package.Asset>(64), new List<Package.Asset>(4),  new List<Package.Asset>(4),
                                             new List<Package.Asset>(4), new List<Package.Asset>(64), new List<Package.Asset>(32), new List<Package.Asset>(32) };

            Util.DebugPrint("Sorted at", Profiling.Millis);
            SteamHelper.DLC_BitMask notMask = ~SteamHelper.GetOwnedDLCMask();
            bool loadEnabled = Settings.settings.loadEnabled, loadUsed = Settings.settings.loadUsed, report = loadUsed && Settings.settings.reportAssets;
            //PrintPackages(packages);

            foreach (Package p in packages)
            {
                CustomAssetMetaData meta = null;

                try
                {
                    assets.Clear();
                    assets.AddRange(p.FilterAssets(UserAssetType.CustomAssetMetaData));

                    if (assets.Count == 0)
                        continue;

                    if (report)
                        AssetReport.instance.AddPackage(p);

                    bool enabled = loadEnabled && IsEnabled(p);

                    if (assets.Count == 1) // the common case
                    {
                        bool want = enabled || styleBuildings.Contains(assets[0].fullName);

                        // Fast exit.
                        if (!want && !(loadUsed && UsedAssets.instance.GotPackage(p.packageName)))
                            continue;

                        meta = AssetDeserializer.Instantiate(assets[0]) as CustomAssetMetaData;
                        bool used = loadUsed && UsedAssets.instance.IsUsed(meta);

                        if (used || want && (AssetImporterAssetTemplate.GetAssetDLCMask(meta) & notMask) == 0)
                        {
                            CustomAssetMetaData.Type type = meta.type;
                            int offset = type == CustomAssetMetaData.Type.Trailer || type == CustomAssetMetaData.Type.SubBuilding ||
                                type == CustomAssetMetaData.Type.PropVariation || type >= CustomAssetMetaData.Type.RoadElevation ? -1 : 0;
                            AddToQueue(queues, meta, offset, !(enabled | used));
                        }
                    }
                    else
                    {
                        bool want = enabled;

                        // Fast exit.
                        if (!want)
                        {
                            for (int i = 0; i < assets.Count; i++)
                                want = want || styleBuildings.Contains(assets[i].fullName);

                            if (!want && !(loadUsed && UsedAssets.instance.GotPackage(p.packageName)))
                                continue;
                        }

                        metas.Clear();
                        bool used = false;

                        for (int i = 0; i < assets.Count; i++)
                        {
                            meta = AssetDeserializer.Instantiate(assets[i]) as CustomAssetMetaData;
                            metas.Add(meta);
                            want = want && (AssetImporterAssetTemplate.GetAssetDLCMask(meta) & notMask) == 0;
                            used = used || loadUsed && UsedAssets.instance.IsUsed(meta);
                        }

                        if (want | used)
                        {
                            metas.Sort((a, b) => b.type - a.type); // prop variation, sub-building, trailer, elevation, pillar before main asset
                            CustomAssetMetaData.Type type = metas[0].type;
                            int offset = type == CustomAssetMetaData.Type.Trailer || type == CustomAssetMetaData.Type.SubBuilding ||
                                type == CustomAssetMetaData.Type.PropVariation || type >= CustomAssetMetaData.Type.RoadElevation ? -1 : 0;
                            bool dontSpawn = !(enabled | used);

                            for (int i = 0; i < metas.Count; i++)
                                AddToQueue(queues, metas[i], offset, dontSpawn);
                        }
                    }
                }
                catch (Exception e)
                {
                    AssetFailed(meta?.assetRef?.fullName ?? p.packageName + "." + p.packageMainAsset, e);
                }
            }

            Package.Asset[] queue = new Package.Asset[queues.Select(lst => lst.Count).Sum()];

            for (int i = 0, k = 0; i < queues.Length; k += queues[i].Count, i++)
                queues[i].CopyTo(queue, k);

            return queue;
        }

        void AddToQueue(List<Package.Asset>[] queues, CustomAssetMetaData meta, int offset, bool dontSpawn)
        {
            Package.Asset assetRef = meta.assetRef;

            if (assetRef == null)
            {
                Util.DebugPrint(meta.name, " Warning : NULL asset");
                return;
            }

            CustomAssetMetaData.Type type = meta.type;
            Package package = assetRef.package;
            string fullName = type < CustomAssetMetaData.Type.RoadElevation ? assetRef.fullName : PillarOrElevationName(package.packageName, assetRef.name);

            if (!IsDuplicate(fullName, allLoads[(int) type], package))
            {
                queues[loadQueueIndex[(int) type] + offset].Add(assetRef);
                metaDatas[assetRef.fullName] = new SomeMetaData(meta.userDataRef, meta.name, type);

                if (dontSpawn)
                    dontSpawnNormally.Add(fullName);

                if (type == CustomAssetMetaData.Type.Citizen)
                    citizenMetaDatas[fullName] = meta;
            }
        }

        static bool IsEnabled(Package package)
        {
            Package.Asset mainAsset = package.Find(package.packageMainAsset);
            return mainAsset?.isEnabled ?? true;
        }

        internal CustomAssetMetaData.Type GetMetaType(Package.Asset assetRef)
        {
            SomeMetaData some;

            if (metaDatas.TryGetValue(assetRef.fullName, out some))
                return some.type;

            ReadMetaData(assetRef.package);

            if (metaDatas.TryGetValue(assetRef.fullName, out some))
                return some.type;

            Util.DebugPrint("Cannot resolve metatype for", assetRef.fullName);
            return CustomAssetMetaData.Type.Unknown;
        }

        UserAssetData GetUserAssetData(Package.Asset assetRef, out string name)
        {
            SomeMetaData some;

            if (!metaDatas.TryGetValue(assetRef.fullName, out some))
            {
                ReadMetaData(assetRef.package);
                metaDatas.TryGetValue(assetRef.fullName, out some);
            }

            if (some.userDataRef != null)
            {
                try
                {
                    UserAssetData uad = AssetDeserializer.Instantiate(some.userDataRef) as UserAssetData;

                    if (uad == null)
                        uad = new UserAssetData();

                    name = some.name;
                    return uad;
                }
                catch (Exception)
                {
                    Util.DebugPrint("Cannot resolve UserAssetData for", assetRef.fullName);
                }
            }

            name = string.Empty;
            return null;
        }

        void ReadMetaData(Package package)
        {
            foreach (Package.Asset asset in package.FilterAssets(UserAssetType.CustomAssetMetaData))
            {
                try
                {
                    CustomAssetMetaData meta = AssetDeserializer.Instantiate(asset) as CustomAssetMetaData;
                    metaDatas[meta.assetRef.fullName] = new SomeMetaData(meta.userDataRef, meta.name, meta.type);
                }
                catch (Exception)
                {
                    Util.DebugPrint("Cannot read metadata from", package.packageName);
                }
            }
        }

        internal static string PillarOrElevationName(string packageName, string fullName) => packageName + "." + PackageHelper.StripName(fullName);
        internal static string AssetName(string name_Data) => name_Data.Length > 5 && name_Data.EndsWith("_Data") ? name_Data.Substring(0, name_Data.Length - 5) : name_Data;

        static string ShorterAssetName(string fullName_Data)
        {
            int j = fullName_Data.IndexOf('.');

            if (j >= 0 && j < fullName_Data.Length - 1)
                fullName_Data = fullName_Data.Substring(j + 1);

            return AssetName(fullName_Data);
        }

        internal void AssetFailed(string fullName, Exception e)
        {
            if (fullName != null && failedAssets.Add(fullName))
            {
                if (reportAssets)
                    AssetReport.instance.AssetFailed(fullName);

                Util.DebugPrint("Asset failed:", fullName);
                DualProfilerSource profiler = LoadingScreen.instance.DualSource;
                profiler?.CustomAssetFailed(ShorterAssetName(fullName));
            }

            if (e != null)
                UnityEngine.Debug.LogException(e);
        }

        internal void Duplicate(string fullName, Package package)
        {
            string path = package.packagePath ?? "Path unknown";

            if (reportAssets)
                AssetReport.instance.Duplicate(fullName, path);

            Util.DebugPrint("Duplicate asset", fullName, "in", path);
            DualProfilerSource profiler = LoadingScreen.instance.DualSource;
            profiler?.CustomAssetDuplicate(ShorterAssetName(fullName));
        }

        internal void NotFound(string fullName)
        {
            if (fullName != null)
            {
                if (reportAssets)
                {
                    Package.Asset refBy = Current;

                    if (refBy != null)
                        AssetReport.instance.NotFound(fullName, refBy.fullName);
                    else
                        AssetReport.instance.NotFound(fullName);
                }

                if (failedAssets.Add(fullName))
                {
                    Util.DebugPrint("Asset not found:", fullName);
                    DualProfilerSource profiler = LoadingScreen.instance.DualSource;
                    profiler?.CustomAssetNotFound(ShorterAssetName(fullName));
                }
            }
        }

        bool IsDuplicate(string fullName, HashSet<string> set, Package package)
        {
            if (!set.Add(fullName))
            {
                Duplicate(fullName, package);
                return true;
            }
            else
                return false;
        }

        //void PrintPlugins()
        //{
        //    foreach (KeyValuePair<string, PluginInfo> plugin in plugins)
        //    {
        //        Util.DebugPrint("Plugin ", plugin.Value.name);
        //        Util.DebugPrint("  path", plugin.Key);
        //        Util.DebugPrint("  id", plugin.Value.publishedFileID);
        //        Util.DebugPrint("  assemblies", plugin.Value.assemblyCount);
        //        Util.DebugPrint("  asset data extensions", plugin.Value.GetInstances<IAssetDataExtension>().Length);
        //    }
        //}

        //static void PrintPackages(Package[] packages)
        //{
        //    foreach (Package p in packages)
        //    {
        //        Trace.Pr(p.packageName, "\t\t", p.packagePath, "   ", p.version);

        //        foreach (Package.Asset a in p)
        //            Trace.Pr(a.isMainAsset ? " *" : "  ", a.fullName.PadRight(116), a.checksum, a.type.ToString().PadRight(19),
        //                a.offset.ToString().PadLeft(8), a.size.ToString().PadLeft(8));
        //    }
        //}
    }

    struct SomeMetaData
    {
        internal Package.Asset userDataRef;
        internal string name;
        internal CustomAssetMetaData.Type type;

        internal SomeMetaData(Package.Asset u, string n, CustomAssetMetaData.Type t)
        {
            userDataRef = u;
            name = n;
            type = t;
        }
    }
}
