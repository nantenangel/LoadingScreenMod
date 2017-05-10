using ICities;

namespace LoadingScreenModTest
{
    public sealed class Mod : IUserMod, ILoadingExtension
    {
        static bool created = false;
        public string Name => "Loading Screen Mod [Test]";
        public string Description => "New loading options";

        public void OnEnabled() => Create();
        public void OnDisabled() => Stopping();
        public void OnSettingsUI(UIHelperBase helper) => Settings.settings.OnSettingsUI(helper);
        public void OnCreated(ILoading loading) { }
        public void OnReleased() { }
        public void OnLevelLoaded(LoadMode mode) { Util.DebugPrint("OnLevelLoaded at", Profiling.Millis); }
        public void OnLevelUnloading() { }

        void Create()
        {
            if (!created)
            {
                LevelLoader.Create().Deploy();
                //new PackageManagerFix().Deploy();
                created = true;
            }
        }

        void Stopping()
        {
            LevelLoader.instance?.Dispose();
            //PackageManagerFix.instance?.Dispose();
            created = false;
        }
    }
}
