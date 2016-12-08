using ICities;
using ColossalFramework;
using System.IO;

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
                new LevelLoader().Deploy();
                new PackageManagerFix().Deploy();
                created = true;
            }
        }

        void Stopping()
        {
            LevelLoader.instance?.Dispose();
            PackageManagerFix.instance?.Dispose();
            created = false;
        }
    }
}
