using ICities;
using ColossalFramework;

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
            Util.DebugPrint("OnLevelLoaded at", Profiling.Millis);
        }

        void Create()
        {
            if (!created)
            {
                Stopping();
                Trace.Start();
                new LevelLoader().Deploy();
                // new PackageManagerFix().Deploy();
                created = true;
            }
        }

        void Stopping()
        {
            if (created)
                Trace.Stop();

            LevelLoader.instance?.Dispose();
            // PackageManagerFix.instance?.Dispose();
            created = false;
        }
    }
}
