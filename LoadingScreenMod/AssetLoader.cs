using System;
using System.Collections;
using System.Collections.Generic;
using ColossalFramework;
using ColossalFramework.Packaging;
using ColossalFramework.Steamworks;
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
        internal string currentFullName;
        SteamHelper.DLC_BitMask notMask;
        int propCount, treeCount, buildingCount, vehicleCount, lastMillis;
        readonly bool loadEnabled = Settings.settings.loadEnabled, loadUsed = Settings.settings.loadUsed, reportAssets = Settings.settings.reportAssets;
        public bool hasStarted, hasFinished;
        internal const int yieldInterval = 200;
        internal HashSet<string> Props => loadedProps;
        internal HashSet<string> Trees => loadedTrees;
        internal HashSet<string> Buildings => loadedBuildings;
        internal HashSet<string> Vehicles => loadedVehicles;
        internal bool IsIntersection(string fullName) => loadedIntersections.Contains(fullName);

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

            LoadingManager.instance.m_loadingProfilerCustomContent.BeginLoading("Loading Assets First Pass");
            notMask = ~SteamHelper.GetOwnedDLCMask();
            lastMillis = Profiling.Millis;
            Package.Asset[] assets = FilterAssets(UserAssetType.CustomAssetMetaData);
            Sharing.instance?.Start();

            // Load custom assets: props, trees, trailers
            for (i = 0; i < assets.Length; i++)
                if (PropTreeTrailer(assets[i]) && Profiling.Millis - lastMillis > yieldInterval)
                {
                    lastMillis = Profiling.Millis;
                    yield return null;
                }

            LoadingManager.instance.m_loadingProfilerCustomContent.EndLoading();
            LoadingManager.instance.m_loadingProfilerCustomContent.BeginLoading("Loading Assets Second Pass");

            // Load custom assets: buildings and vehicles
            for (i = 0; i < assets.Length; i++)
                if (BuildingVehicle(assets[i], styleBuildings.Contains(assets[i].fullName)) && Profiling.Millis - lastMillis > yieldInterval)
                {
                    lastMillis = Profiling.Millis;
                    yield return null;
                }

            assets = null;
            Report();

            LoadingManager.instance.m_loadingProfilerCustomContent.EndLoading();
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

        bool PropTreeTrailer(Package.Asset asset)
        {
            CustomAssetMetaData assetMetaData = null;

            try
            {
                bool wantBecauseEnabled = loadEnabled && IsEnabled(asset);

                if (!wantBecauseEnabled && !(loadUsed && UsedAssets.instance.GotPropTreeTrailerPackage(asset.package.packageName)))
                    return false;

                assetMetaData = asset.Instantiate<CustomAssetMetaData>();
                CustomAssetMetaData.Type type = assetMetaData.type;

                if (type == CustomAssetMetaData.Type.Building || type == CustomAssetMetaData.Type.Vehicle || type == CustomAssetMetaData.Type.Unknown ||
                    (AssetImporterAssetTemplate.GetAssetDLCMask(assetMetaData) & notMask) != 0)
                    return false;

                // Always remember: assetRef may point to another package because the deserialization method accepts any asset with a matching checksum.
                string fullName = asset.package.packageName + "." + assetMetaData.assetRef.name;
                HashSet<string> alreadyLoaded;
                bool wanted;

                switch (type)
                {
                    case CustomAssetMetaData.Type.Prop:
                        wanted = wantBecauseEnabled || loadUsed && UsedAssets.instance.GotProp(fullName);
                        alreadyLoaded = loadedProps;
                        break;

                    case CustomAssetMetaData.Type.Tree:
                        wanted = wantBecauseEnabled || loadUsed && UsedAssets.instance.GotTree(fullName);
                        alreadyLoaded = loadedTrees;
                        break;

                    case CustomAssetMetaData.Type.Trailer:
                        wanted = wantBecauseEnabled || loadUsed && UsedAssets.instance.GotVehicle(fullName);
                        alreadyLoaded = loadedVehicles;
                        break;

                    default:
                        return false;
                }

                if (wanted && !IsDuplicate(fullName, alreadyLoaded, asset.package))
                    PropTreeTrailerImpl(fullName, assetMetaData.assetRef);
            }
            catch (Exception ex)
            {
                Failed(assetMetaData?.assetRef, ex);
                // CODebugBase<LogChannel>.Warn(LogChannel.Modding, string.Concat(new object[] { ex.GetType(), ": Loading custom asset failed[", asset, "]\n", ex.Message }));
            }

            return true;
        }

        internal void PropTreeTrailerImpl(string fullName, Package.Asset data)
        {
            try
            {
                LoadingManager.instance.m_loadingProfilerCustomAsset.BeginLoading(AssetName(data.name));
                // CODebugBase<LogChannel>.Log(LogChannel.Modding, string.Concat("Loading custom asset ", assetMetaData.name, " from ", asset));

                GameObject go = data.Instantiate<GameObject>();
                go.name = fullName;
                go.SetActive(false);
                PrefabInfo info = go.GetComponent<PrefabInfo>();
                info.m_isCustomContent = true;

                if (info.m_Atlas != null && info.m_InfoTooltipThumbnail != null && info.m_InfoTooltipThumbnail != string.Empty && info.m_Atlas[info.m_InfoTooltipThumbnail] != null)
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

                // Trailers, this way.
                VehicleInfo vi = go.GetComponent<VehicleInfo>();

                if (vi != null)
                {
                    if (loadedVehicles.Add(fullName))
                        PrefabCollection<VehicleInfo>.InitializePrefabs("Custom Assets", vi, null);

                    if (vi.m_lodObject != null)
                        vi.m_lodObject.SetActive(false);
                }
            }
            finally
            {
                LoadingManager.instance.m_loadingProfilerCustomAsset.EndLoading();
            }
        }

        bool BuildingVehicle(Package.Asset asset, bool includedInStyle)
        {
            CustomAssetMetaData assetMetaData = null;

            try
            {
                bool wantBecauseEnabled = loadEnabled && IsEnabled(asset);

                if (!includedInStyle && !wantBecauseEnabled && !(loadUsed && UsedAssets.instance.GotBuildingVehiclePackage(asset.package.packageName)))
                    return false;

                assetMetaData = asset.Instantiate<CustomAssetMetaData>();
                CustomAssetMetaData.Type type = assetMetaData.type;

                if (type != CustomAssetMetaData.Type.Building && type != CustomAssetMetaData.Type.Vehicle && type != CustomAssetMetaData.Type.Unknown ||
                    (AssetImporterAssetTemplate.GetAssetDLCMask(assetMetaData) & notMask) != 0)
                    return false;

                // Always remember: assetRef may point to another package because the deserialization method accepts any asset with a matching checksum.
                string fullName = asset.package.packageName + "." + assetMetaData.assetRef.name;
                HashSet<string> alreadyLoaded;
                bool wanted;

                switch (type)
                {
                    case CustomAssetMetaData.Type.Building:
                        wanted = wantBecauseEnabled || loadUsed && UsedAssets.instance.GotBuilding(fullName);
                        alreadyLoaded = loadedBuildings;
                        break;

                    case CustomAssetMetaData.Type.Vehicle:
                        wanted = wantBecauseEnabled || loadUsed && UsedAssets.instance.GotVehicle(fullName);
                        alreadyLoaded = loadedVehicles;
                        break;

                    case CustomAssetMetaData.Type.Unknown:
                        wanted = wantBecauseEnabled;
                        alreadyLoaded = new HashSet<string>();
                        break;

                    default:
                        return false;
                }

                if ((includedInStyle || wanted) && !IsDuplicate(fullName, alreadyLoaded, asset.package))
                    BuildingVehicleImpl(fullName, assetMetaData.assetRef, wanted);
            }
            catch (Exception ex)
            {
                Failed(assetMetaData?.assetRef, ex);
                // CODebugBase<LogChannel>.Warn(LogChannel.Modding, string.Concat(new object[] { ex.GetType(), ": Loading custom asset failed:[", asset, "]\n", ex.Message }));
            }

            return true;
        }

        void BuildingVehicleImpl(string fullName, Package.Asset data, bool wanted)
        {
            try
            {
                LoadingManager.instance.m_loadingProfilerCustomAsset.BeginLoading(AssetName(data.name));
                // CODebugBase<LogChannel>.Log(LogChannel.Modding, string.Concat("Loading custom asset ", assetMetaData.name, " from ", asset));

                currentFullName = fullName;
                GameObject go = data.Instantiate<GameObject>();
                go.name = fullName;
                go.SetActive(false);
                PrefabInfo info = go.GetComponent<PrefabInfo>();
                info.m_isCustomContent = true;

                if (info.m_Atlas != null && info.m_InfoTooltipThumbnail != null && info.m_InfoTooltipThumbnail != string.Empty && info.m_Atlas[info.m_InfoTooltipThumbnail] != null)
                    info.m_InfoTooltipAtlas = info.m_Atlas;

                BuildingInfo bi = go.GetComponent<BuildingInfo>();

                if (bi != null)
                {
                    if (bi.m_lodObject != null)
                        bi.m_lodObject.SetActive(false);

                    if (loadedBuildings.Add(fullName))
                    {
                        PrefabCollection<BuildingInfo>.InitializePrefabs("Custom Assets", bi, null);
                        bi.m_dontSpawnNormally = !wanted;
                        buildingCount++;

                        if (bi.GetAI() is IntersectionAI)
                            loadedIntersections.Add(fullName);
                    }
                }

                VehicleInfo vi = go.GetComponent<VehicleInfo>();

                if (vi != null)
                {
                    if (loadedVehicles.Add(fullName))
                    {
                        PrefabCollection<VehicleInfo>.InitializePrefabs("Custom Assets", vi, null);
                        vehicleCount++;
                    }

                    if (vi.m_lodObject != null)
                        vi.m_lodObject.SetActive(false);
                }
            }
            finally
            {
                currentFullName = null;
                LoadingManager.instance.m_loadingProfilerCustomAsset.EndLoading();
            }
        }

        internal static Package.Asset[] FilterAssets(Package.AssetType assetType)
        {
            List<Package.Asset> enabled = new List<Package.Asset>(32), notEnabled = new List<Package.Asset>(32);

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

        static string ShortenAssetName(string fullName_Data)
        {
            int j = fullName_Data.IndexOf('.');

            if (j >= 0 && j < fullName_Data.Length - 1)
                fullName_Data = fullName_Data.Substring(j + 1);

            return AssetName(fullName_Data);
        }

        internal void Failed(Package.Asset data, Exception e)
        {
            string name = data?.name;

            if (name != null && failedAssets.Add(data.fullName))
            {
                Util.DebugPrint("Asset failed:", data.fullName);

                if (reportAssets)
                    AssetReport.instance.Failed(data.fullName);

                Profiling.CustomAssetFailed(AssetName(name));
                DualProfilerSource profiler = LoadingScreen.instance.DualSource;
                profiler?.SomeFailed();
            }

            if (e != null)
                UnityEngine.Debug.LogException(e);
        }

        internal void Duplicate(string name, Package package)
        {
            string path = package.packagePath ?? "Path unknown";

            if (reportAssets)
                AssetReport.instance.Duplicate(name, path);

            Util.DebugPrint("Duplicate asset", name, "in", path);
            name = ShortenAssetName(name);
            LoadingManager.instance.m_loadingProfilerCustomAsset.BeginLoading(name);
            Profiling.CustomAssetDuplicate(name);
            LoadingManager.instance.m_loadingProfilerCustomAsset.EndLoading();
            DualProfilerSource profiler = LoadingScreen.instance.DualSource;
            profiler?.SomeDuplicate();
        }

        internal void NotFound(string name)
        {
            if (name != null)
            {
                if (reportAssets)
                {
                    if (currentFullName != null)
                        AssetReport.instance.NotFound(name, currentFullName);
                    else
                        AssetReport.instance.NotFound(name);
                }

                if (failedAssets.Add(name))
                {
                    Util.DebugPrint("Asset not found:", name);
                    name = ShortenAssetName(name);
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
