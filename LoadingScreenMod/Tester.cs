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
        Package[] packages;

        internal void Test()
        {
            packages = CreatePackages(dir);
            PrintPackages();
            LoadPackages();

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
                AssetDeserializer.Instantiate<GameObject>(asset);

            Trace.Ind(0, "Done");
            packages = null;
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
        }

        void AddAssets(List<Package.Asset> assets, params CustomAssetMetaData.Type[] types)
        {
            foreach (Package p in packages)
            {
                byte[] bytes = File.ReadAllBytes(p.packagePath);

                foreach (Package.Asset a in p.FilterAssets(UserAssetType.CustomAssetMetaData))
                {
                    CustomAssetMetaData assetMetaData = AssetDeserializer.Instantiate<CustomAssetMetaData>(a);

                    if (((IList<CustomAssetMetaData.Type>) types).Contains(assetMetaData.type))
                        assets.Add(assetMetaData.assetRef);
                }
            }
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
    }
}
