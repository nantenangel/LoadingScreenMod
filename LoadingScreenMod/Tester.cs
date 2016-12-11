using System.Collections.Generic;
using System.IO;
using ColossalFramework.Packaging;
using UnityEngine;

namespace LoadingScreenMod
{
    internal sealed class Tester : MonoBehaviour
    {
        const string dir = @"g:\testassets1\";
        Package[] packages;

        public void Awake()
        {
            DontDestroyOnLoad(this);
        }

        public void Update()
        {
            if (Time.time > 20f)
            {
                Test();
                Destroy(this);
            }
        }

        void Test()
        {
            packages = CreatePackages(dir);
        }

        Package[] CreatePackages(string path)
        {
            string[] files = Directory.GetFiles(path);
            List<Package> list = new List<Package>(files.Length);

            foreach (string crp in files)
                list.Add(new Package(null, crp, true));

            return list.ToArray();
        }

        void PrintPackages()
        {
            foreach(Package p in packages)
            {
                Trace.Pr(p.packageName, "\t\t", p.packagePath);

                foreach(Package.Asset a in p)
                    Trace.Pr(a.isMainAsset ? " *" : "  ", a.fullName.PadRight(90), a.checksum, a.type, a.size);
            }

            Trace.Newline();
        }
    }
}
