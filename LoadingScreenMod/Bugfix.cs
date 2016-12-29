using System;
using ColossalFramework.Importers;

namespace LoadingScreenMod
{
    public sealed class Bugfix : DetourUtility
    {
        public static Bugfix instance;

        // Delegates can be used to call non-public methods. Delegates have about the same performance as regular method calls.
        static readonly Action<Image> Dizpose = Util.CreateAction<Image>("Dispose");

        public Bugfix()
        {
            instance = this;
            // init(typeof(PackageManager), "FindAssetByName");
            init(typeof(Image), "Finalize", "Fnalize");
        }

        internal override void Dispose()
        {
            Revert();
            base.Dispose();
            instance = null;
        }

        static void Fnalize(Image image)
        {
            Dizpose(image);
            Trace.imgFinalizes++;
        }

        /// <summary>
        /// Fix for the bug that is still in the base game: asset search stops when a package with the correct name is found.
        /// In reality, package names are *not* unique. Think of workshop submissions that contain multiple crp files.
        /// </summary>
        //public static Package.Asset FindAssetByName(string fullName)
        //{
        //    int i = fullName.IndexOf(".");

        //    if (i >= 0)
        //    {
        //        string packageName = fullName.Substring(0, i);
        //        string assetName = fullName.Substring(i + 1);
        //        Package.Asset a;

        //        foreach (Package p in PackageManager.allPackages)
        //            if (p.packageName == packageName && (a = p.Find(assetName)) != null)
        //                return a;
        //    }

        //    return null;
        //}
    }
}
