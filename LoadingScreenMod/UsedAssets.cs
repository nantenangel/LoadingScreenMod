using System;
using System.Collections.Generic;
using ColossalFramework.Packaging;

namespace LoadingScreenModTest
{
    internal sealed class UsedAssets : Instance<UsedAssets>
    {
        HashSet<string> allPackages = new HashSet<string>();
        HashSet<string>[] allAssets;
        HashSet<string> buildingAssets = new HashSet<string>(), propAssets = new HashSet<string>(), treeAssets = new HashSet<string>(),
            vehicleAssets = new HashSet<string>(), citizenAssets = new HashSet<string>(), netAssets = new HashSet<string>();
        HashSet<string> indirectProps = new HashSet<string>(), indirectTrees = new HashSet<string>();

        internal HashSet<string> Buildings => buildingAssets;
        internal HashSet<string> Props => propAssets;
        internal HashSet<string> Trees => treeAssets;
        internal HashSet<string> Vehicles => vehicleAssets;
        internal HashSet<string> Citizens => citizenAssets;
        internal HashSet<string> Nets => netAssets;
        internal HashSet<string> IndirectProps => indirectProps;
        internal HashSet<string> IndirectTrees => indirectTrees;

        private UsedAssets()
        {
            allAssets = new HashSet<string>[] { buildingAssets, propAssets, treeAssets, vehicleAssets, vehicleAssets, buildingAssets, buildingAssets,
                propAssets, citizenAssets, netAssets, netAssets, buildingAssets };
            LookupUsed();
        }

        void LookupUsed()
        {
            LookupSimulationBuildings(allPackages, buildingAssets);
            LookupSimulationNets(allPackages, netAssets);
            LookupSimulationAssets<CitizenInfo>(allPackages, citizenAssets);
            LookupSimulationAssets<PropInfo>(allPackages, propAssets);
            LookupSimulationAssets<TreeInfo>(allPackages, treeAssets);
            LookupSimulationAssets<VehicleInfo>(allPackages, vehicleAssets);
        }

        internal void Dispose()
        {
            allPackages.Clear(); buildingAssets.Clear(); propAssets.Clear(); treeAssets.Clear(); vehicleAssets.Clear(); citizenAssets.Clear();
            netAssets.Clear(); indirectProps.Clear(); indirectTrees.Clear();
            allPackages = null; buildingAssets = null; propAssets = null; treeAssets = null; vehicleAssets = null; citizenAssets = null;
            netAssets = null; indirectProps = null; indirectTrees = null;
            allAssets = null; instance = null;
        }

        /// <summary>
        /// False positives are possible at this stage.
        /// </summary>
        internal bool GotPackage(string packageName) => allPackages.Contains(packageName) || packageName.IndexOf('.') >= 0;

        /// <summary>
        /// Is the asset used in the city?
        /// </summary>
        internal bool IsUsed(CustomAssetMetaData meta)
        {
            Package.Asset assetRef = meta.assetRef;
            return assetRef != null ? allAssets[(int) meta.type].Contains(assetRef.fullName) : false;
        }

        /// <summary>
        /// Dynamic check to find out if at least one asset in the current load chain is used in the city. At this time, only buildings are considered containers.
        /// </summary>
        internal bool GotAnyContainer()
        {
            foreach (Package.Asset assetRef in AssetLoader.instance.stack)
                if (buildingAssets.Contains(assetRef.fullName))
                    return true;

            return false;
        }

        internal void ReportMissingAssets()
        {
            ReportMissingAssets<BuildingInfo>(buildingAssets);
            ReportMissingAssets<PropInfo>(propAssets);
            ReportMissingAssets<TreeInfo>(treeAssets);
            ReportMissingAssets<VehicleInfo>(vehicleAssets);
            ReportMissingAssets<CitizenInfo>(citizenAssets);
            ReportMissingAssets<NetInfo>(netAssets);
        }

        static void ReportMissingAssets<P>(HashSet<string> customAssets) where P : PrefabInfo
        {
            try
            {
                foreach (string fullName in customAssets)
                    if (CustomDeserializer.FindLoaded<P>(fullName) == null)
                        AssetLoader.instance.NotFound(fullName);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
            }
        }

        internal bool AllAssetsAvailable(HashSet<string> ignore)
        {
            return (!Settings.settings.loadUsed || AllAvailable<BuildingInfo>(buildingAssets, ignore) &&
                    AllAvailable<PropInfo>(propAssets, ignore) && AllAvailable<TreeInfo>(treeAssets, ignore) &&
                    AllAvailable<VehicleInfo>(vehicleAssets, ignore) && AllAvailable<CitizenInfo>(citizenAssets, ignore) &&
                    AllAvailable<NetInfo>(netAssets, ignore));
        }

        static bool AllAvailable<P>(HashSet<string> fullNames, HashSet<string> ignore) where P : PrefabInfo
        {
            foreach (string name in fullNames)
                if (!ignore.Contains(name) && CustomDeserializer.FindLoaded<P>(name) == null)
                {
                    Util.DebugPrint("Not available:", name);
                    return false;
                }

            return true;
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

                for (int i = 1; i < n; i++)
                    if (buffer[i].m_flags != Building.Flags.None)
                        Add(PrefabCollection<BuildingInfo>.PrefabName(buffer[i].m_infoIndex), packages, assets);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
            }
        }

        /// <summary>
        /// NetInfos require more effort because the NotUsedGuide/UnlockMilestone stuff gets into way.
        /// </summary>
        void LookupSimulationNets(HashSet<string> packages, HashSet<string> assets)
        {
            try
            {
                NetNode[] buffer1 = NetManager.instance.m_nodes.m_buffer;
                int n = buffer1.Length;

                for (int i = 1; i < n; i++)
                    if (buffer1[i].m_flags != NetNode.Flags.None)
                    {
                        string fullName = PrefabCollection<NetInfo>.PrefabName(buffer1[i].m_infoIndex);
                        Add(fullName, packages, assets);
                    }

                NetSegment[] buffer2 = NetManager.instance.m_segments.m_buffer;
                n = buffer2.Length;

                for (int i = 1; i < n; i++)
                    if (buffer2[i].m_flags != NetSegment.Flags.None)
                    {
                        string fullName = PrefabCollection<NetInfo>.PrefabName(buffer2[i].m_infoIndex);
                        Add(fullName, packages, assets);
                    }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
            }
        }

        static void Add(string fullName, HashSet<string> packages, HashSet<string> assets)
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
            }
        }
    }
}
