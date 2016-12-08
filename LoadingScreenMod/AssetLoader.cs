using System;
using System.Collections;
using System.Collections.Generic;
using ColossalFramework;
using ColossalFramework.Packaging;
using ColossalFramework.PlatformServices;
using UnityEngine;

namespace LoadingScreenMod
{
    /// <summary>
    /// LoadCustomContent coroutine from LoadingManager.
    /// </summary>
    public sealed class AssetLoader
    {
        public static AssetLoader instance;
        HashSet<string> failedAssets = new HashSet<string>(), loadedProps = new HashSet<string>(), loadedTrees = new HashSet<string>(),
            loadedBuildings = new HashSet<string>(), loadedVehicles = new HashSet<string>(), loadedIntersections = new HashSet<string>();
        internal Stack<string> current = new Stack<string>(4);
        int propCount, treeCount, buildingCount, vehicleCount, lastMillis;
        readonly bool loadEnabled = Settings.settings.loadEnabled, loadUsed = Settings.settings.loadUsed, reportAssets = Settings.settings.reportAssets;
        public bool hasStarted, hasFinished;
        internal const int yieldInterval = 200;
        internal HashSet<string> Props => loadedProps;
        internal HashSet<string> Trees => loadedTrees;
        internal HashSet<string> Buildings => loadedBuildings;
        internal HashSet<string> Vehicles => loadedVehicles;
        internal bool IsIntersection(string fullName) => loadedIntersections.Contains(fullName);
        internal bool HasFailed(string fullName) => failedAssets.Contains(fullName);

        public AssetLoader()
        {
            instance = this;
            hasStarted = hasFinished = false;
        }

        public void Setup()
        {
            new Sharing().Deploy();

            if (reportAssets)
                new AssetReport();
        }

        public void Dispose()
        {
            UsedAssets.instance?.Dispose();
            Sharing.instance?.Dispose();
            LevelLoader.instance.AddFailedAssets(failedAssets);
            failedAssets.Clear(); loadedProps.Clear(); loadedTrees.Clear(); loadedBuildings.Clear(); loadedVehicles.Clear(); loadedIntersections.Clear();
            instance = null; failedAssets = null; loadedProps = null; loadedTrees = null; loadedBuildings = null; loadedVehicles = null; loadedIntersections = null;
        }

        void Report()
        {
            if (loadUsed)
            {
                UsedAssets.instance.ReportMissingAssets();
                UsedAssets.instance.Unhook();
            }

            if (reportAssets)
            {
                AssetReport.instance.Save();
                AssetReport.instance.Dispose();
            }

            Sharing.instance?.Dispose();
        }

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

                // If skipping of standard prefabs is enabled, we must ensure that there are no skipped prefabs in the default district syle.
                if (Settings.settings.SkipAny)
                    RemoveSkipped(districtStyle);

                districtStyles.Add(districtStyle);
            }

            foreach(Package.Asset asset in PackageManager.FilterAssets(UserAssetType.DistrictStyleMetaData))
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

            if (loadUsed)
                UsedAssets.Create().Hook();

            LoadingManager.instance.m_loadingProfilerCustomContent.BeginLoading("Calculating asset load order");
            List<Package.Asset>[] queues = GetLoadQueues(styleBuildings);
            LoadingManager.instance.m_loadingProfilerCustomContent.EndLoading();
            lastMillis = Profiling.Millis;
            Sharing.instance?.Start();

            // Load custom assets.
            for (i = 0; i < queues.Length; i++)
            {
                LoadingManager.instance.m_loadingProfilerCustomContent.BeginLoading("Loading Custom Assets Pass " + i);

                foreach (Package.Asset asset in queues[i])
                {
                    Load(asset);

                    if (Profiling.Millis - lastMillis > yieldInterval)
                    {
                        lastMillis = Profiling.Millis;
                        yield return null;
                    }
                }

                LoadingManager.instance.m_loadingProfilerCustomContent.EndLoading();
            }

            current.Clear();
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
                            BuildingInfo bi = PrefabCollection<BuildingInfo>.FindLoaded(districtStyleMetaData.assets[j] + "_Data");

                            if (bi != null)
                            {
                                districtStyle.Add(bi);

                                if (districtStyleMetaData.builtin) // this is always false
                                    bi.m_dontSpawnNormally = !districtStyleMetaData.assetRef.isEnabled;
                            }
                            else
                                CODebugBase<LogChannel>.Warn(LogChannel.Modding, "Warning: Missing asset (" + districtStyleMetaData.assets[i] + ") in style " + districtStyleMetaData.name);
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

            if (Singleton<TelemetryManager>.exists)
                Singleton<TelemetryManager>.instance.CustomContentInfo(buildingCount, propCount, treeCount, vehicleCount);

            LoadingManager.instance.m_loadingProfilerMain.EndLoading();
            hasFinished = true;
        }

        void Load(Package.Asset asset)
        {
            string fullName = null;

            try
            {
                CustomAssetMetaData assetMetaData = asset.Instantiate<CustomAssetMetaData>();

                // Always remember: assetRef may point to another package because the deserialization method accepts any asset with a matching checksum.
                // There is a bug in the 1.6.0 game update in this.
                fullName = asset.package.packageName + "." + assetMetaData.assetRef.name;

                CustomAssetMetaData.Type type = assetMetaData.type;
                bool spawnNormally = (type == CustomAssetMetaData.Type.Building || type == CustomAssetMetaData.Type.SubBuilding) ?
                    loadEnabled && IsEnabled(asset) || loadUsed && UsedAssets.instance.GotBuilding(fullName) : true;
                current.Clear();
                LoadImpl(fullName, assetMetaData.assetRef, spawnNormally);
            }
            catch (Exception e)
            {
                Failed(fullName ?? asset.fullName, e);
            }
        }

        internal void LoadImpl(string fullName, Package.Asset assetRef, bool spawnNormally = true)
        {
            try
            {
                Trace.Ind(current.Count * 2, fullName);

                current.Push(fullName);
                LoadingManager.instance.m_loadingProfilerCustomAsset.BeginLoading(AssetName(assetRef.name));
                GameObject go = assetRef.Instantiate<GameObject>();
                go.name = fullName;
                go.SetActive(false);
                PrefabInfo info = go.GetComponent<PrefabInfo>();
                info.m_isCustomContent = true;

                if (info.m_Atlas != null && !string.IsNullOrEmpty(info.m_InfoTooltipThumbnail) && info.m_Atlas[info.m_InfoTooltipThumbnail] != null)
                    info.m_InfoTooltipAtlas = info.m_Atlas;

                PropInfo pi = go.GetComponent<PropInfo>();

                if (pi != null)
                {
                    if (pi.m_lodObject != null)
                        pi.m_lodObject.SetActive(false);

                    if (loadedProps.Add(fullName))
                    {
                        PrefabCollection<PropInfo>.InitializePrefabs("Custom Assets", pi, null);
                        propCount++;
                    }
                }

                TreeInfo ti = go.GetComponent<TreeInfo>();

                if (ti != null && loadedTrees.Add(fullName))
                {
                    PrefabCollection<TreeInfo>.InitializePrefabs("Custom Assets", ti, null);
                    treeCount++;
                }

                BuildingInfo bi = go.GetComponent<BuildingInfo>();

                if (bi != null)
                {
                    if (bi.m_lodObject != null)
                        bi.m_lodObject.SetActive(false);

                    if (loadedBuildings.Add(fullName))
                    {
                        PrefabCollection<BuildingInfo>.InitializePrefabs("Custom Assets", bi, null);
                        bi.m_dontSpawnNormally = !spawnNormally;
                        buildingCount++;

                        if (bi.GetAI() is IntersectionAI)
                            loadedIntersections.Add(fullName);
                    }
                }

                VehicleInfo vi = go.GetComponent<VehicleInfo>();

                if (vi != null)
                {
                    if (vi.m_lodObject != null)
                        vi.m_lodObject.SetActive(false);

                    if (loadedVehicles.Add(fullName))
                    {
                        PrefabCollection<VehicleInfo>.InitializePrefabs("Custom Assets", vi, null);
                        vehicleCount++;
                    }
                }
            }
            finally
            {
                current.Pop();
                LoadingManager.instance.m_loadingProfilerCustomAsset.EndLoading();
            }
        }

        static void RemoveSkipped(DistrictStyle style)
        {
            HashSet<string> skippedPrefabs = PrefabLoader.instance?.skippedPrefabs;

            if (skippedPrefabs == null || skippedPrefabs.Count == 0)
                return;

            try
            {
                BuildingInfo[] inStyle = style.GetBuildingInfos();
                ((HashSet<BuildingInfo>) Util.Get(style, "m_Infos")).Clear();
                ((HashSet<int>) Util.Get(style, "m_AffectedServices")).Clear();

                foreach (BuildingInfo info in inStyle)
                    if (info != null)
                    {
                        GameObject go = info.gameObject;

                        if (go != null && !skippedPrefabs.Contains(go.name))
                            style.Add(info);
                    }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
            }
        }

        List<Package.Asset>[] GetLoadQueues(HashSet<string> styleBuildings)
        {
            Package.Asset[] assets = FilterAssets(UserAssetType.CustomAssetMetaData);
            List<Package.Asset>[] queues = { new List<Package.Asset>(4), new List<Package.Asset>(64), new List<Package.Asset>(4), new List<Package.Asset>(64) };
            SteamHelper.DLC_BitMask notMask = ~SteamHelper.GetOwnedDLCMask();

            for (int i = 0; i < assets.Length; i++)
            {
                Package.Asset asset = assets[i];
                string fullName = null;

                try
                {
                    bool wantThis = loadEnabled && IsEnabled(asset) || styleBuildings.Contains(asset.fullName);

                    // Make the first check fast.
                    if (wantThis || loadUsed && UsedAssets.instance.GotPackage(asset.package.packageName))
                    {
                        CustomAssetMetaData assetMetaData = asset.Instantiate<CustomAssetMetaData>();

                        // Always remember: assetRef may point to another package because the deserialization method accepts any asset with the same checksum.
                        // Think of identical vehicle trailers in different crp's.
                        // There is a bug in the 1.6.0 game update in this.
                        fullName = asset.package.packageName + "." + assetMetaData.assetRef.name;

                        if ((AssetImporterAssetTemplate.GetAssetDLCMask(assetMetaData) & notMask) == 0)
                            switch (assetMetaData.type)
                            {
                                case CustomAssetMetaData.Type.Building:
                                    if ((wantThis || loadUsed && UsedAssets.instance.GotBuilding(fullName)) && !IsDuplicate(fullName, loadedBuildings, asset.package))
                                        queues[3].Add(asset);
                                    break;

                                case CustomAssetMetaData.Type.Prop:
                                    if ((wantThis || loadUsed && UsedAssets.instance.GotProp(fullName)) && !IsDuplicate(fullName, loadedProps, asset.package))
                                        queues[1].Add(asset);
                                    break;

                                case CustomAssetMetaData.Type.Tree:
                                    if ((wantThis || loadUsed && UsedAssets.instance.GotTree(fullName)) && !IsDuplicate(fullName, loadedTrees, asset.package))
                                        queues[1].Add(asset);
                                    break;

                                case CustomAssetMetaData.Type.Vehicle:
                                    if ((wantThis || loadUsed && UsedAssets.instance.GotVehicle(fullName)) && !IsDuplicate(fullName, loadedVehicles, asset.package))
                                        queues[3].Add(asset);
                                    break;

                                case CustomAssetMetaData.Type.Trailer:
                                    if ((wantThis || loadUsed && UsedAssets.instance.GotVehicle(fullName)) && !IsDuplicate(fullName, loadedVehicles, asset.package))
                                        queues[1].Add(asset);
                                    break;

                                case CustomAssetMetaData.Type.Unknown:
                                    if (wantThis)
                                        queues[3].Add(asset);
                                    break;

                                case CustomAssetMetaData.Type.SubBuilding:
                                    if ((wantThis || loadUsed && UsedAssets.instance.GotBuilding(fullName)) && !IsDuplicate(fullName, loadedBuildings, asset.package))
                                        queues[2].Add(asset);
                                    break;

                                case CustomAssetMetaData.Type.PropVariation:
                                    if ((wantThis || loadUsed && UsedAssets.instance.GotProp(fullName)) && !IsDuplicate(fullName, loadedProps, asset.package))
                                        queues[0].Add(asset);
                                    break;
                            }
                    }
                }
                catch (Exception e)
                {
                    Failed(fullName ?? asset.fullName, e);
                }
            }

            Trace.Ind(0, "Queues", queues[0].Count, queues[1].Count, queues[2].Count, queues[3].Count);
            return queues;
        }

        internal static Package.Asset[] FilterAssets(Package.AssetType assetType)
        {
            List<Package.Asset> enabled = new List<Package.Asset>(64), notEnabled = new List<Package.Asset>(64);

            foreach (Package.Asset asset in PackageManager.FilterAssets(assetType))
                if (asset != null)
                    if (IsEnabled(asset))
                        enabled.Add(asset);
                    else
                        notEnabled.Add(asset);

            // Why enabled assets first? Because in duplicate prefab name situations, I want the enabled one to get through.
            Package.Asset[] ret = new Package.Asset[enabled.Count + notEnabled.Count];
            enabled.CopyTo(ret);
            notEnabled.CopyTo(ret, enabled.Count);
            enabled.Clear(); notEnabled.Clear();
            return ret;
        }

        // There is an interesting bug in the package manager: secondary CustomAssetMetaDatas in a crp are considered always enabled.
        // As a result, the game loads all vehicle trailers, no matter if they are enabled or not. This is the fix.
        static bool IsEnabled(Package.Asset asset)
        {
            if (asset.isMainAsset)
                return asset.isEnabled;

            Package.Asset main = asset.package.Find(asset.package.packageMainAsset);
            return main?.isEnabled ?? false;
        }

        internal static string AssetName(string name_Data) => name_Data.Length > 5 && name_Data.EndsWith("_Data") ? name_Data.Substring(0, name_Data.Length - 5) : name_Data;

        static string ShorterAssetName(string fullName_Data)
        {
            int j = fullName_Data.IndexOf('.');

            if (j >= 0 && j < fullName_Data.Length - 1)
                fullName_Data = fullName_Data.Substring(j + 1);

            return AssetName(fullName_Data);
        }

        internal void Failed(string fullName, Exception e)
        {
            if (fullName != null && failedAssets.Add(fullName))
            {
                Util.DebugPrint("Asset failed:", fullName);

                if (reportAssets)
                    AssetReport.instance.Failed(fullName);

                Profiling.CustomAssetFailed(ShorterAssetName(fullName));
                DualProfilerSource profiler = LoadingScreen.instance.DualSource;
                profiler?.SomeFailed();
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
            string name = ShorterAssetName(fullName);
            LoadingManager.instance.m_loadingProfilerCustomAsset.BeginLoading(name);
            Profiling.CustomAssetDuplicate(name);
            LoadingManager.instance.m_loadingProfilerCustomAsset.EndLoading();
            DualProfilerSource profiler = LoadingScreen.instance.DualSource;
            profiler?.SomeDuplicate();
        }

        internal void NotFound(string fullName)
        {
            if (fullName != null)
            {
                if (reportAssets)
                {
                    if (current.Count > 0)
                        AssetReport.instance.NotFound(fullName, current.Peek());
                    else
                        AssetReport.instance.NotFound(fullName);
                }

                if (failedAssets.Add(fullName))
                {
                    Util.DebugPrint("Asset not found:", fullName);
                    string name = ShorterAssetName(fullName);
                    LoadingManager.instance.m_loadingProfilerCustomAsset.BeginLoading(name);
                    Profiling.CustomAssetNotFound(name);
                    LoadingManager.instance.m_loadingProfilerCustomAsset.EndLoading();
                    DualProfilerSource profiler = LoadingScreen.instance.DualSource;
                    profiler?.SomeNotFound();
                }
            }
        }

        bool IsDuplicate(string fullName, HashSet<string> alreadyLoaded, Package package)
        {
            if (alreadyLoaded.Contains(fullName))
            {
                Duplicate(fullName, package);
                return true;
            }
            else
                return false;
        }

        internal static bool IsWorkshopPackage(string fullName, out ulong id)
        {
            int j = fullName.IndexOf('.');

            if (j <= 0 || j >= fullName.Length - 1)
            {
                id = 0;
                return false;
            }

            string p = fullName.Substring(0, j);
            return ulong.TryParse(p, out id) && id > 999999;
        }

        internal static bool IsPrivatePackage(string fullName)
        {
            ulong id;

            // Private: a local asset created by the player (not from the workshop).
            // My rationale is the following:
            // 43453453.Name -> Workshop
            // Name.Name     -> Private
            // Name          -> Either an old-format (early 2015) reference, or something from DLC/Deluxe packs.
            //                  If loading is not successful then cannot tell for sure, assumed DLC/Deluxe when reported as not found.

            if (IsWorkshopPackage(fullName, out id))
                return false;
            else
                return fullName.IndexOf('.') >= 0;
        }
    }
}
