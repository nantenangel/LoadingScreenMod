using System;
using System.Collections.Generic;
using System.IO;
using ColossalFramework.Packaging;
using UnityEngine;

namespace LoadingScreenMod
{
    internal sealed class Tester
    {
        const string dir = @"g:\testassets1\";
        internal static Tester instance;
        Package[] packages;
        Dictionary<string, byte[]> data = new Dictionary<string, byte[]>();

        internal void Test()
        {
            instance = this;
            packages = CreatePackages(dir);
            PrintPackages();
            LoadPackages();

            Trace.Newline();
            Trace.Ind(0, "GC");
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Trace.Ind(0, "GC finished");

            Trace.Newline();
            Trace.Pr("CustomAssetMetaData:");
            Profiling.Start();
            List<Package.Asset> assets = new List<Package.Asset>();
            AddAssets(assets, CustomAssetMetaData.Type.PropVariation);
            AddAssets(assets, CustomAssetMetaData.Type.Prop, CustomAssetMetaData.Type.Tree, CustomAssetMetaData.Type.Trailer);
            AddAssets(assets, CustomAssetMetaData.Type.SubBuilding);
            AddAssets(assets, CustomAssetMetaData.Type.Building, CustomAssetMetaData.Type.Vehicle);

            Trace.Newline();
            Trace.Pr("Assets:");

            foreach (Package.Asset asset in assets)
            {
                GameObject go = AssetDeserializer.Instantiate<GameObject>(asset);
                go.name = asset.fullName;
                Initialize(go);
            }

            Trace.Ind(0, "Done");
            data.Clear(); packages = null; instance = null;
        }

        Package[] CreatePackages(string path)
        {
            string[] files = Directory.GetFiles(path);
            List<Package> list = new List<Package>(files.Length);

            foreach (string crp in files)
                list.Add(new Package(null, crp, true));

            return list.ToArray();
        }

        void LoadPackages()
        {
            Trace.Newline();
            Trace.Ind(0, "Loading packages");

            foreach (Package p in packages)
                data[p.packagePath] = File.ReadAllBytes(p.packagePath);

            Trace.Ind(0, "Loading finished");
        }

        void AddAssets(List<Package.Asset> assets, params CustomAssetMetaData.Type[] types)
        {
            foreach (Package p in packages)
                foreach (Package.Asset a in p.FilterAssets(UserAssetType.CustomAssetMetaData))
                {
                    CustomAssetMetaData assetMetaData = AssetDeserializer.Instantiate<CustomAssetMetaData>(a);

                    if (((IList<CustomAssetMetaData.Type>) types).Contains(assetMetaData.type))
                        assets.Add(assetMetaData.assetRef);
                }
        }

        internal MemStream GetStream(Package.Asset asset)
        {
            byte[] mem;

            if (data.TryGetValue(asset.package.packagePath, out mem))
                return new MemStream(mem, (int) asset.offset);

            Trace.Pr("NOT IN MEMORY:", asset.fullName, asset.package.packagePath);
            return null; // TODO
        }

        void PrintPackages()
        {
            foreach(Package p in packages)
            {
                Trace.Pr(p.packageName, "\t\t", p.packagePath);

                foreach(Package.Asset a in p)
                    Trace.Pr(a.isMainAsset ? " *" : "  ", a.fullName.PadRight(90), a.checksum, a.type, a.size);
            }
        }

        void Initialize(GameObject go)
        {
            Trace.Ind(0, "Initialize", go.name);
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

                PrefabCollection<PropInfo>.InitializePrefabs("Custom Assets", pi, null);
            }

            TreeInfo ti = go.GetComponent<TreeInfo>();

            if (ti != null)
                PrefabCollection<TreeInfo>.InitializePrefabs("Custom Assets", ti, null);

            BuildingInfo bi = go.GetComponent<BuildingInfo>();

            if (bi != null)
            {
                if (bi.m_lodObject != null)
                    bi.m_lodObject.SetActive(false);

                PrefabCollection<BuildingInfo>.InitializePrefabs("Custom Assets", bi, null);
                bi.m_dontSpawnNormally = false;
            }

            VehicleInfo vi = go.GetComponent<VehicleInfo>();

            if (vi != null)
            {
                if (vi.m_lodObject != null)
                    vi.m_lodObject.SetActive(false);

                PrefabCollection<VehicleInfo>.InitializePrefabs("Custom Assets", vi, null);
            }
        }
    }
}
