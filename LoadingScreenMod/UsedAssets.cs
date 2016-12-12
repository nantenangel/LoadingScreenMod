using System;
using System.Collections.Generic;
using ColossalFramework.Packaging;
using ColossalFramework.PlatformServices;
using UnityEngine;

namespace LoadingScreenMod
{
    internal sealed class UsedAssets
    {
        internal static UsedAssets instance;
        static PackageDeserializer.CustomDeserializeHandler defaultHandler;
        HashSet<string> allPackages = new HashSet<string>();
        HashSet<string> buildingAssets = new HashSet<string>(), propAssets = new HashSet<string>(), treeAssets = new HashSet<string>(), vehicleAssets = new HashSet<string>();
        HashSet<string> indirectProps = new HashSet<string>(), indirectTrees = new HashSet<string>(), buildingPrefabs = new HashSet<string>();
        Package.Asset[] assets;
        Dictionary<PublishedFileId, HashSet<string>> packagesToPaths;

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

        internal void Hook()
        {
            defaultHandler = PackageDeserializer.customDeserializer;
            PackageDeserializer.SetCustomDeserializer(CustomDeserialize);
        }

        internal void Unhook()
        {
            if (PackageDeserializer.customDeserializer == CustomDeserialize)
                PackageDeserializer.SetCustomDeserializer(defaultHandler);
        }

        internal void Dispose()
        {
            allPackages.Clear(); buildingAssets.Clear(); propAssets.Clear(); treeAssets.Clear(); vehicleAssets.Clear(); indirectProps.Clear(); indirectTrees.Clear(); buildingPrefabs.Clear();
            allPackages = null; buildingAssets = null; propAssets = null; treeAssets = null; vehicleAssets = null; indirectProps = null; indirectTrees = null; buildingPrefabs = null;
            instance = null; assets = null; defaultHandler = null;
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
        /// Dynamic check to find out if at least one asset in the current load chain is used in the city. At this time, only buildings are considered containers.
        /// </summary>
        bool GotAnyContainer()
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
                    if (PrefabCollection<P>.FindLoaded(name) == null)
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
                if (!ignore.Contains(name) && PrefabCollection<P>.FindLoaded(name) == null)
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

        static object CustomDeserialize(Package p, Type t, PackageReader r)
        {
            // First, make the common case fast.
            if (t == typeof(float))
                return r.ReadSingle();
            if (t == typeof(Vector2))
                return r.ReadVector2();

            // Props and trees in buildings and parks.
            if (t == typeof(BuildingInfo.Prop))
            {
                PropInfo pi = Get<PropInfo>(r.ReadString()); // old name format (without package name) is possible
                TreeInfo ti = Get<TreeInfo>(r.ReadString()); // old name format (without package name) is possible

                if (Settings.settings.reportAssets && UsedAssets.instance.GotAnyContainer())
                {
                    if (pi != null)
                    {
                        string n = pi.gameObject.name;

                        if (!string.IsNullOrEmpty(n) && n.Contains("."))
                            UsedAssets.instance.indirectProps.Add(n);
                    }

                    if (ti != null)
                    {
                        string n = ti.gameObject.name;

                        if (!string.IsNullOrEmpty(n) && n.Contains("."))
                            UsedAssets.instance.indirectTrees.Add(n);
                    }
                }

                return new BuildingInfo.Prop
                {
                    m_prop = pi,
                    m_tree = ti,
                    m_position = r.ReadVector3(),
                    m_angle = r.ReadSingle(),
                    m_probability = r.ReadInt32(),
                    m_fixedHeight = r.ReadBoolean()
                };
            }

            // Prop variations in props.
            if (t == typeof(PropInfo.Variation))
            {
                string name = r.ReadString();
                string fullName = p.packageName + "." + name;
                PropInfo pi = null;

                if (fullName == AssetLoader.instance.Current)
                    Util.DebugPrint("Warning:", fullName, "wants to be a prop variation for itself.");
                else
                    pi = Get<PropInfo>(p, fullName, name, false);

                return new PropInfo.Variation
                {
                    m_prop = pi,
                    m_probability = r.ReadInt32()
                };
            }

            // Tree variations in trees.
            if (t == typeof(TreeInfo.Variation))
            {
                string name = r.ReadString();
                string fullName = p.packageName + "." + name;
                TreeInfo ti = null;

                if (fullName == AssetLoader.instance.Current)
                    Util.DebugPrint("Warning:", fullName, "wants to be a tree variation for itself.");
                else
                    ti = Get<TreeInfo>(p, fullName, name, false);

                return new TreeInfo.Variation
                {
                    m_tree = ti,
                    m_probability = r.ReadInt32()
                };
            }

            // It seems that trailers are listed in the save game so this is not necessary. Better to be safe however
            // because a missing trailer reference is fatal for the simulation thread.
            if (t == typeof(VehicleInfo.VehicleTrailer))
            {
                string name = r.ReadString();
                string fullName = p.packageName + "." + name;
                VehicleInfo vi = Get<VehicleInfo>(p, fullName, name, false);

                VehicleInfo.VehicleTrailer trailer;
                trailer.m_info = vi;
                trailer.m_probability = r.ReadInt32();
                trailer.m_invertProbability = r.ReadInt32();
                return trailer;
            }

            // Sub-buildings in buildings.
            if (t == typeof(BuildingInfo.SubInfo))
            {
                string name = r.ReadString();
                string fullName = p.packageName + "." + name;
                BuildingInfo bi = null;

                if (fullName == AssetLoader.instance.Current || name == AssetLoader.instance.Current)
                    Util.DebugPrint("Warning:", fullName, "wants to be a sub-building for itself.");
                else
                    bi = Get<BuildingInfo>(p, fullName, name, true);

                BuildingInfo.SubInfo subInfo = new BuildingInfo.SubInfo();
                subInfo.m_buildingInfo = bi;
                subInfo.m_position = r.ReadVector3();
                subInfo.m_angle = r.ReadSingle();
                subInfo.m_fixedHeight = r.ReadBoolean();
                return subInfo;
            }

            return defaultHandler(p, t, r);
        }

        // Works with (fullName = asset name), too.
        static T Get<T>(string fullName) where T : PrefabInfo
        {
            if (string.IsNullOrEmpty(fullName))
                return null;

            T info = PrefabCollection<T>.FindLoaded(fullName);

            if (info == null && Load(ref fullName, FindAsset(fullName)))
                info = PrefabCollection<T>.FindLoaded(fullName);

            return info;
        }

        // For sub-buildings, name may be package.assetname.
        static T Get<T>(Package package, string fullName, string name, bool tryName) where T : PrefabInfo
        {
            T info = PrefabCollection<T>.FindLoaded(fullName);

            if (info == null && tryName)
                info = PrefabCollection<T>.FindLoaded(name);

            if (info == null)
            {
                Package.Asset data = package.Find(name);

                if (data == null && tryName)
                    data = FindAsset(name); // yes, name

                if (data != null)
                    fullName = data.fullName;
                else if (name.Contains("."))
                    fullName = name;

                if (Load(ref fullName, data))
                    info = PrefabCollection<T>.FindLoaded(fullName);
            }

            return info;
        }

        /// <summary>
        /// Given packagename.assetname, find the asset. Works with (fullName = asset name), too.
        /// </summary>
        static Package.Asset FindAsset(string fullName)
        {
            try
            {
                int j = fullName.IndexOf('.');

                if (j > 0 && j < fullName.Length - 1)
                {
                    // The fast path.
                    Package.Asset asset = instance.FindByName(fullName.Substring(0, j), fullName.Substring(j + 1));

                    if (asset != null)
                        return asset;
                }

                // Fast fail.
                if (AssetLoader.instance.HasFailed(fullName))
                    return null;

                Package.Asset[] a = UsedAssets.instance.assets;

                if (a == null)
                    a = UsedAssets.instance.assets = AssetLoader.FilterAssets(Package.AssetType.Object);

                // We also try the old (early 2015) naming that does not contain the package name. FindLoaded does this, too.
                for (int i = 0; i < a.Length; i++)
                    if (fullName == a[i].fullName || fullName == a[i].name)
                        return a[i];
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
            }

            return null;
        }

        Package.Asset FindByName(string packageName, string assetName)
        {
            PublishedFileId id = Util.GetPackageId(packageName);

            if (id != PublishedFileId.invalid)
            {
                if (packagesToPaths == null || packagesToPaths.Count == 0)
                    packagesToPaths = (Dictionary<PublishedFileId, HashSet<string>>) Util.GetStatic(typeof(PackageManager), "m_PackagesSteamToPathsMap");

                HashSet<string> paths;

                if (packagesToPaths.TryGetValue(id, out paths))
                {
                    Package package; Package.Asset asset;

                    foreach (string path in paths)
                        if ((package = PackageManager.FindPackageAt(path)) != null && (asset = package.Find(assetName)) != null)
                            return asset;
                }
            }

            return null;
        }

        static bool Load(ref string fullName, Package.Asset data)
        {
            if (data != null)
                try
                {
                    fullName = data.fullName;
                    AssetLoader.instance.LoadImpl(fullName, data);
                    return true;
                }
                catch (Exception e)
                {
                    AssetLoader.instance.Failed(fullName, e);
                }
            else
                AssetLoader.instance.NotFound(fullName);

            return false;
        }
    }
}
