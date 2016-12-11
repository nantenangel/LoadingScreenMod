using ICities;
using ColossalFramework;
using System.IO;
using UnityEngine;
using ColossalFramework.Packaging;

namespace LoadingScreenMod
{
    public sealed class Mod : IUserMod, ILoadingExtension
    {
        static bool created = false;
        public string Name => "Loading Screen Mod";
        public string Description => "New loading options";

        public void OnEnabled() => Create();
        public void OnCreated(ILoading loading) => Create();
        public void OnDisabled() => Stopping();
        public void OnSettingsUI(UIHelperBase helper) => Settings.OnSettingsUI(helper);
        public void OnLevelUnloading() { }
        public void OnReleased() { }

        public void OnLevelLoaded(LoadMode mode)
        {
            if (LevelLoader.instance.activated)
                Singleton<LoadingManager>.instance.LoadingAnimationComponent.enabled = false;

            Settings.helper = null;
        }

        void Create()
        {
            if (!created)
            {
                Stopping();
                Trace.Start();
                new LevelLoader().Deploy();
                // new PackageManagerFix().Deploy();

                // GameObject go = new GameObject("My tester GO");
                // go.AddComponent<Tester>();

                created = true;
            }
        }

        void Stopping()
        {
            if (created)
            {
                PrintPackages();
                Trace.Stop();
            }

            LevelLoader.instance?.Dispose();
            // PackageManagerFix.instance?.Dispose();
            created = false;
        }

        void PrintPackages()
        {
            foreach (Package p in PackageManager.allPackages)
            {
                Trace.Pr(p.packageName, "\t\t", p.packagePath);

                foreach (Package.Asset a in p)
                    Trace.Pr(a.isMainAsset ? " *" : "  ", a.fullName.PadRight(90), a.checksum, a.type, a.size);
            }

            Trace.Newline();
        }
    }
}
