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

        internal void Test()
        {
            instance = this;
            packages = CreatePackages(dir);
            PrintPackages();

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
            List<Package.Asset>[] queues = GetLoadQueues();

            LoadPackages();
            Trace.Newline();
            Trace.Pr("Assets:");

            for(int i = 0; i < queues.Length; i++)
                for (int j = 0; j < queues[i].Count; j++)
                {
                    Package.Asset asset = queues[i][j];
                    GameObject go = AssetDeserializer.Instantiate<GameObject>(asset);
                    go.name = asset.fullName;
                    Initialize(go);
                }

            Trace.Ind(0, "Done");
            packages = null; instance = null;
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
                Sharing.instance.LoadPackage(p);

            Trace.Ind(0, "Loading finished");
        }

        List<Package.Asset>[] GetLoadQueues()
        {
            List<Package.Asset>[] queues = { new List<Package.Asset>(4), new List<Package.Asset>(64), new List<Package.Asset>(4), new List<Package.Asset>(64) };

            foreach (Package p in packages)
                foreach (Package.Asset a in p.FilterAssets(UserAssetType.CustomAssetMetaData))
                {
                    CustomAssetMetaData assetMetaData = AssetDeserializer.Instantiate<CustomAssetMetaData>(a);
                    Package.Asset assetRef = assetMetaData.assetRef;

                    switch (assetMetaData.type)
                    {
                        case CustomAssetMetaData.Type.Building:
                        case CustomAssetMetaData.Type.Vehicle:
                        case CustomAssetMetaData.Type.Unknown:
                            queues[3].Add(assetRef);
                            break;

                        case CustomAssetMetaData.Type.Prop:
                        case CustomAssetMetaData.Type.Tree:
                        case CustomAssetMetaData.Type.Trailer:
                            queues[1].Add(assetRef);
                            break;

                        case CustomAssetMetaData.Type.SubBuilding:
                            queues[2].Add(assetRef);
                            break;

                        case CustomAssetMetaData.Type.PropVariation:
                            queues[0].Add(assetRef);
                            break;
                    }
                }

            return queues;
        }

        void PrintPackages()
        {
            foreach(Package p in packages)
            {
                Trace.Pr(p.packageName, "\t\t", p.packagePath);

                foreach(Package.Asset a in p)
                    Trace.Pr(a.isMainAsset ? " *" : "  ", a.fullName.PadRight(90), a.checksum, a.type.ToString().PadRight(10), a.offset.ToString().PadLeft(7), a.size.ToString().PadLeft(7));
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
