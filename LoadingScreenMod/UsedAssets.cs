using System;
using System.Collections.Generic;

namespace LoadingScreenModTest
{
    internal sealed class UsedAssets
    {
        internal static UsedAssets instance;
        HashSet<string> allPackages = new HashSet<string>();
        HashSet<string>[] allAssets;
        HashSet<string> buildingAssets = new HashSet<string>(), propAssets = new HashSet<string>(), treeAssets = new HashSet<string>(), vehicleAssets = new HashSet<string>();
        HashSet<string> indirectProps = new HashSet<string>(), indirectTrees = new HashSet<string>(), buildingPrefabs = new HashSet<string>();

        internal HashSet<string> Buildings => buildingAssets;
        internal HashSet<string> Props => propAssets;
        internal HashSet<string> Trees => treeAssets;
        internal HashSet<string> Vehicles => vehicleAssets;
        internal HashSet<string> IndirectProps => indirectProps;
        internal HashSet<string> IndirectTrees => indirectTrees;

        internal static UsedAssets Create()
        {
            if (instance == null)
            {
                instance = new UsedAssets();
                instance.allAssets = new HashSet<string>[] { instance.buildingAssets, instance.propAssets, instance.treeAssets, instance.vehicleAssets,
                                                             instance.vehicleAssets, instance.buildingAssets, instance.buildingAssets, instance.propAssets };
                instance.LookupUsed();
            }

            return instance;
        }

        void LookupUsed()
        {
            LookupSimulationBuildings(allPackages, buildingAssets);
            LookupSimulationAssets<PropInfo>(allPackages, propAssets);
            LookupSimulationAssets<TreeInfo>(allPackages, treeAssets);
            LookupSimulationAssets<VehicleInfo>(allPackages, vehicleAssets);
        }

        internal void Dispose()
        {
            allPackages.Clear(); buildingAssets.Clear(); propAssets.Clear(); treeAssets.Clear(); vehicleAssets.Clear(); indirectProps.Clear(); indirectTrees.Clear(); buildingPrefabs.Clear();
            allPackages = null; buildingAssets = null; propAssets = null; treeAssets = null; vehicleAssets = null; indirectProps = null; indirectTrees = null; buildingPrefabs = null;
            allAssets = null; instance = null;
        }

        /// <summary>
        /// False positives are possible at this stage.
        /// </summary>
        internal bool GotPackage(string packageName) => allPackages.Contains(packageName) || packageName.IndexOf('.') >= 0;

        internal bool GotProp(string fullName) => propAssets.Contains(fullName);
        internal bool GotTree(string fullName) => treeAssets.Contains(fullName);
        internal bool GotBuilding(string fullName) => buildingAssets.Contains(fullName);
        internal bool GotVehicle(string fullName) => vehicleAssets.Contains(fullName);
        internal bool GotIndirectProp(string fullName) => indirectProps.Contains(fullName);
        internal bool GotIndirectTree(string fullName) => indirectTrees.Contains(fullName);

        /// <summary>
        /// Is the asset used in the city?
        /// </summary>
        internal bool IsUsed(CustomAssetMetaData meta) => allAssets[(int) meta.type].Contains(meta.assetRef.fullName);

        /// <summary>
        /// Dynamic check to find out if at least one asset in the current load chain is used in the city. At this time, only buildings are considered containers.
        /// </summary>
        internal bool GotAnyContainer()
        {
            foreach (string fullName in AssetLoader.instance.stack)
                if (GotBuilding(fullName))
                    return true;

            return false;
        }

        internal bool GotPrefab(string fullName, string replace)
        {
            if (buildingPrefabs.Contains(fullName))
                return true;

            replace = replace?.Trim();

            if (string.IsNullOrEmpty(replace))
                return false;

            if (replace.IndexOf(',') != -1)
            {
                string[] array = replace.Split(',');

                for (int i = 0; i < array.Length; i++)
                    if (buildingPrefabs.Contains(array[i].Trim()))
                        return true;

                return false;
            }
            else
                return buildingPrefabs.Contains(replace);
        }

        internal void ReportMissingAssets()
        {
            ReportMissingAssets<BuildingInfo>(buildingAssets);
            ReportMissingAssets<PropInfo>(propAssets);
            ReportMissingAssets<TreeInfo>(treeAssets);
            ReportMissingAssets<VehicleInfo>(vehicleAssets);
        }

        static void ReportMissingAssets<P>(HashSet<string> customAssets) where P : PrefabInfo
        {
            try
            {
                foreach (string name in customAssets)
                    if (CustomDeserializer.FindLoaded<P>(name) == null)
                        AssetLoader.instance.NotFound(name);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
            }
        }

        internal bool AnyMissing(HashSet<string> ignore)
        {
            return AnyMissing<BuildingInfo>(buildingAssets, ignore) || AnyMissing<PropInfo>(propAssets, ignore) ||
                   AnyMissing<TreeInfo>(treeAssets, ignore) || AnyMissing<VehicleInfo>(vehicleAssets, ignore) ||
                   Settings.settings.SkipAny && AnyMissing<BuildingInfo>(buildingPrefabs, new HashSet<string>());
        }

        static bool AnyMissing<P>(HashSet<string> fullNames, HashSet<string> ignore) where P : PrefabInfo
        {
            foreach (string name in fullNames)
                if (!ignore.Contains(name) && CustomDeserializer.FindLoaded<P>(name) == null)
                    return true;

            return false;
        }

        /// <summary>
        /// Looks up the custom assets placed in the city.
        /// </summary>
        void LookupSimulationAssets<P>(HashSet<string> packages, HashSet<string> assets) where P : PrefabInfo
        {
            try
            {
                int n = PrefabCollection<P>.PrefabCount();

                for (int i = 0; i < n; i++)
                    Add(PrefabCollection<P>.PrefabName((uint) i), packages, assets);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
            }
        }

        /// <summary>
        /// BuildingInfos require more effort because the NotUsedGuide/UnlockMilestone stuff gets into way.
        /// </summary>
        void LookupSimulationBuildings(HashSet<string> packages, HashSet<string> assets)
        {
            try
            {
                Building[] buffer = BuildingManager.instance.m_buildings.m_buffer;
                int n = buffer.Length;
                HashSet<string> prefabs = Settings.settings.SkipAny ? buildingPrefabs : null;

                for (int i = 1; i < n; i++)
                    if (buffer[i].m_flags != Building.Flags.None)
                        Add(PrefabCollection<BuildingInfo>.PrefabName(buffer[i].m_infoIndex), packages, assets, prefabs);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
            }
        }

        static void Add(string fullName, HashSet<string> packages, HashSet<string> assets, HashSet<string> prefabs = null)
        {
            if (!string.IsNullOrEmpty(fullName))
            {
                int j = fullName.IndexOf('.');

                // Recognize custom assets:
                if (j >= 0 && j < fullName.Length - 1)
                {
                    packages.Add(fullName.Substring(0, j)); // packagename (or pac in case the full name is pac.kagename.assetname)
                    assets.Add(fullName); // packagename.assetname
                }
                else if (prefabs != null)
                    prefabs.Add(fullName);
            }
        }
    }
}
