using ICities;
using ColossalFramework;
using UnityEngine;

namespace LoadingScreenMod
{
    public sealed class Mod : IUserMod, ILoadingExtension
    {
        static bool created = false;
        public string Name => "Loading Screen Mod";
        public string Description => "New loading options for custom assets and standard prefabs";

        public void OnEnabled() => Create();
        public void OnCreated(ILoading loading) { Create(); Util.DebugPrint("OnCreated", Profiling.Millis); }
        public void OnDisabled() => Stopping();
        public void OnSettingsUI(UIHelperBase helper) => Settings.OnSettingsUI(helper);
        public void OnLevelUnloading() { Util.DebugPrint("OnLevelUnloading", Profiling.Millis); }
        public void OnReleased() { Util.DebugPrint("OnReleased", Profiling.Millis); }

        public void OnLevelLoaded(LoadMode mode)
        {
            Util.DebugPrint("OnLevelLoaded", TimeScript.start = Profiling.Millis);
            // PrefabLoader.w?.WriteLine("\nOnLevelLoaded at " + TimeScript.start);
            // GameObject go = new GameObject("My go for timing");
            // go.AddComponent<TimeScript>();

            if (LevelLoader.instance.activated)
                Singleton<LoadingManager>.instance.LoadingAnimationComponent.enabled = false;
        }

        void Create()
        {
            if (!created)
            {
                Stopping();
                new LevelLoader().Deploy();
                created = true;
            }
        }

        void Stopping()
        {
            LevelLoader.instance?.Dispose();
            created = false;
        }
    }

    public class TimeScript : MonoBehaviour
    {
        internal static int start;
        int target = 0;

        public void Update()
        {
            int t = Profiling.Millis - start;

            if (t >= target)
            {
                ulong pagefileUsage, workingSetSize;
                MemoryAPI.GetUsage(out pagefileUsage, out workingSetSize);
                PrefabLoader.w?.WriteLine("WSS at " + target + ": " + workingSetSize / 1048576 + " MB");

                if (target < 10000)
                    target += 2500;
                else if (target < 60000)
                    target += 10000;
                else
                {
                    PrefabLoader.instance?.Dispose();
                    Destroy(gameObject);
                }
            }
        }

        public void OnDestroy() => PrefabLoader.instance?.Dispose();
    }
}
