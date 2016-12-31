using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ColossalFramework.Packaging;
using UnityEngine;

namespace LoadingScreenMod
{
    internal sealed class Tester
    {
        const string dir = @"g:\testassets4\";
        internal static Tester instance;
        Package[] packages;
        internal int index;

        internal void Test()
        {
            instance = this;
            Trace.Pr("Loading from", dir);
            Trace.Newline();
            packages = CreatePackages(dir);
            PrintPackages();

            //Trace.Newline();
            //Trace.Ind(0, "GC");
            //GC.Collect();
            //GC.WaitForPendingFinalizers();
            //GC.Collect();
            //GC.WaitForPendingFinalizers();
            //Trace.Ind(0, "GC finished");

            Trace.Newline();
            Trace.Pr("CustomAssetMetaData:");
            Profiling.Start();
            Package.Asset[] queue = GetLoadQueue();

            Sharing.instance.Start(queue);
            Trace.Newline();
            Trace.Pr("Assets:");

            for (index = 0; index < queue.Length; index++)
            {
                Sharing.instance.WaitForWorkers();
                Package.Asset asset = queue[index];
                Trace.Seq("starts asset   ", index, asset.fullName);
                GameObject go = AssetDeserializer.Instantiate(asset) as GameObject;
                go.name = asset.fullName;
                Initialize(go);
                Trace.Seq("completed asset", index, asset.fullName);
            }

            Trace.Seq("done");
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

        //void LoadPackages()
        //{
        //    Trace.Newline();
        //    Trace.Ind(0, "Loading packages");

        //    foreach (Package p in packages)
        //        Sharing.instance.LoadPackage(p);

        //    Trace.Ind(0, "Loading finished");
        //}

        Package.Asset[] GetLoadQueue()
        {
            // Package[] packages = PackageManager.allPackages.ToArray();
            Array.Sort(packages, (a, b) => string.Compare(a.packageName, b.packageName));
            List<CustomAssetMetaData> list = new List<CustomAssetMetaData>(6);

            // [0] propvar - prop  [1] prop & tree  [2] sub-building - building  [3] building  [4] trailer - vehicle  [5] vehicle
            List<Package.Asset>[] queues = { new List<Package.Asset>(4), new List<Package.Asset>(64), new List<Package.Asset>(4),
                                             new List<Package.Asset>(64), new List<Package.Asset>(32), new List<Package.Asset>(16) };

            foreach (Package p in packages)
            {
                list.Clear();

                foreach (Package.Asset a in p.FilterAssets(UserAssetType.CustomAssetMetaData))
                    list.Add(AssetDeserializer.Instantiate(a) as CustomAssetMetaData);

                if (list.Count == 1) // the common case
                {
                    CustomAssetMetaData meta = list[0];
                    CustomAssetMetaData.Type type = meta.type;
                    int offset = type == CustomAssetMetaData.Type.Trailer || type == CustomAssetMetaData.Type.SubBuilding || type == CustomAssetMetaData.Type.PropVariation ? -1 : 0;
                    AddToQueue(queues, meta, offset);
                }
                else if (list.Count > 1)
                {
                    list.Sort((a, b) => b.type - a.type); // prop variation, sub-building, trailer before main asset
                    CustomAssetMetaData.Type type = list[0].type;
                    int offset = type == CustomAssetMetaData.Type.Trailer || type == CustomAssetMetaData.Type.SubBuilding || type == CustomAssetMetaData.Type.PropVariation ? -1 : 0;

                    for(int i = 0; i < list.Count; i++)
                        AddToQueue(queues, list[i], offset);
                }
            }

            Package.Asset[] queue = new Package.Asset[queues.Select(lst => lst.Count).Sum()];

            for (int i = 0, k = 0; i < queues.Length; k += queues[i].Count, i++)
                queues[i].CopyTo(queue, k);

            return queue;
        }

        void AddToQueue(List<Package.Asset>[] queues, CustomAssetMetaData meta, int offset)
        {
            Package.Asset assetRef = meta.assetRef;

            switch (meta.type)
            {
                case CustomAssetMetaData.Type.Prop:
                case CustomAssetMetaData.Type.Tree:
                case CustomAssetMetaData.Type.PropVariation:
                    queues[1 + offset].Add(assetRef);
                    break;

                case CustomAssetMetaData.Type.Building:
                case CustomAssetMetaData.Type.SubBuilding:
                    queues[3 + offset].Add(assetRef);
                    break;

                //case CustomAssetMetaData.Type.Vehicle:
                //case CustomAssetMetaData.Type.Trailer:
                //case CustomAssetMetaData.Type.Unknown:
                default:
                    queues[5 + offset].Add(assetRef);
                    break;
            }
        }

        static bool IsEnabled(Package package)
        {
            Package.Asset mainAsset = package.Find(package.packageMainAsset);
            return mainAsset?.isEnabled ?? true;
        }

        void PrintPackages()
        {
            foreach(Package p in packages)
            {
                Trace.Pr(p.packageName, "\t\t", p.packagePath, "   ", p.version);

                foreach(Package.Asset a in p)
                    Trace.Pr(a.isMainAsset ? " *" : "  ", a.fullName.PadRight(90), a.checksum, a.type.ToString().PadRight(10), a.offset.ToString().PadLeft(7), a.size.ToString().PadLeft(7));
            }
        }

        void Initialize(GameObject go)
        {
            // Trace.Ind(0, "Initialize", go.name);
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
