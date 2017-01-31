using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using UnityEngine;

namespace LoadingScreenModTest
{
    public sealed class PrefabLoader : DetourUtility<PrefabLoader>
    {
        readonly FieldInfo hasQueuedActionsField = typeof(LoadingManager).GetField("m_hasQueuedActions", BindingFlags.NonPublic | BindingFlags.Instance);
        readonly FieldInfo nameField, prefabsField, prefabsField2, replacesField, replacesField2;
        internal HashSet<string> skippedPrefabs = new HashSet<string>();
        bool saveDeserialized;
        const string ROUTINE = "<InitializePrefabs>c__Iterator6"; // TODO make robust

        private PrefabLoader()
        {
            Type coroutine = typeof(BuildingCollection).GetNestedType(ROUTINE, BindingFlags.NonPublic);
            nameField = coroutine?.GetField("name", BindingFlags.NonPublic | BindingFlags.Instance);
            prefabsField = coroutine?.GetField("prefabs", BindingFlags.NonPublic | BindingFlags.Instance);
            prefabsField2 = coroutine?.GetField("<$>prefabs", BindingFlags.NonPublic | BindingFlags.Instance);
            replacesField = coroutine?.GetField("replaces", BindingFlags.NonPublic | BindingFlags.Instance);
            replacesField2 = coroutine?.GetField("<$>replaces", BindingFlags.NonPublic | BindingFlags.Instance);

            if (nameField != null && prefabsField != null && prefabsField2 != null && replacesField != null && replacesField2 != null)
                init(typeof(LoadingManager), "QueueLoadingAction");
        }

        internal override void Dispose()
        {
            Util.DebugPrint("Skipped", skippedPrefabs.Count, "prefabs");
            base.Dispose();
            skippedPrefabs.Clear(); skippedPrefabs = null;
        }

        public static void QueueLoadingAction(LoadingManager lm, IEnumerator action)
        {
            bool isBuildingCollection = action.GetType().Name == ROUTINE;

            // This race condition with the simulation thread must be watched. It never occurs in my game, though.
            if (isBuildingCollection && !instance.saveDeserialized)
            {
                int startMillis = Profiling.Millis;

                while (!LevelLoader.IsSaveDeserialized() && Profiling.Millis - startMillis < 2000)
                    Thread.Sleep(60);

                instance.saveDeserialized = true;
            }

            while (!Monitor.TryEnter(LevelLoader.instance.loadingLock, SimulationManager.SYNCHRONIZE_TIMEOUT))
                ;

            try
            {
                if (isBuildingCollection)
                    action = instance.Skip(action);

                if (action != null)
                {
                    Queue<IEnumerator> mainThreadQueue = (Queue<IEnumerator>) LevelLoader.instance.queueField.GetValue(lm);
                    mainThreadQueue.Enqueue(action);

                    if (mainThreadQueue.Count < 2)
                        instance.hasQueuedActionsField.SetValue(lm, true);
                }
            }
            finally
            {
                Monitor.Exit(LevelLoader.instance.loadingLock);
            }
        }

        IEnumerator Skip(IEnumerator action)
        {
            try
            {
                string name = nameField.GetValue(action) as string;
                BuildingInfo[] prefabs = prefabsField.GetValue(action) as BuildingInfo[];
                string[] replaces = replacesField.GetValue(action) as string[];

                if (replaces == null)
                    replaces = new string[0];

                List<BuildingInfo> keptPrefabs = null; List<string> keptReplaces = null;
                UsedAssets.Create();

                for (int i = 0; i < prefabs.Length; i++)
                {
                    BuildingInfo info = prefabs[i];
                    string replace = i < replaces.Length ? replaces[i] : null;

                    if (Skip(info, replace))
                    {
                        skippedPrefabs.Add(info.gameObject.name);

                        if (keptPrefabs == null)
                        {
                            keptPrefabs = prefabs.ToList(i);

                            if (i < replaces.Length)
                                keptReplaces = replaces.ToList(i);
                        }
                    }
                    else if (keptPrefabs != null)
                    {
                        keptPrefabs.Add(info);

                        if (keptReplaces != null)
                            keptReplaces.Add(replace);
                    }
                }

                if (keptPrefabs != null)
                {
                    BuildingInfo[] p = keptPrefabs.ToArray();
                    prefabsField.SetValue(action, p);
                    prefabsField2.SetValue(action, p);
                    BuildingCollection bc = GameObject.Find(name)?.GetComponent<BuildingCollection>();

                    // In the presence of the European Buildings Unlocker mod, bc is usually null.
                    // Obviously caused by the Destroy() invokes in that mod.
                    if (bc != null)
                        bc.m_prefabs = p;

                    if (keptReplaces != null)
                    {
                        string[] r = keptReplaces.ToArray();
                        replacesField.SetValue(action, r);
                        replacesField2.SetValue(action, r);

                        if (bc != null)
                            bc.m_replacedNames = r;
                    }

                    if (p.Length == 0)
                        return null;
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
            }

            return action;
        }

        static bool Skip(BuildingInfo info, string replace)
        {
            string name = info.gameObject.name;
            int index = GetIndex(info.GetService(), info.GetSubService());
            bool skipClass = Settings.settings.skip[index];
            return (skipClass || Settings.settings.SkipThis(name)) && !UsedAssets.instance.GotPrefab(name, replace);
        }

        public static int GetIndex(ItemClass.Service service, ItemClass.SubService sub)
        {
            switch (service)
            {
                case ItemClass.Service.Residential:
                case ItemClass.Service.Commercial:
                case ItemClass.Service.Industrial:
                    int index = (int) sub;

                    if (index <= 6)
                        return index;

                    if (index <= 9)
                        return 6; // specialized industry

                    if (index == 18 || index == 19)
                        return 7; // specialized commerce

                    return 0;

                case ItemClass.Service.Office:
                    return 8;

                default:
                    return 0;
            }
        }

        internal static IEnumerator DestroySkipped()
        {
            if (instance?.skippedPrefabs == null || instance.skippedPrefabs.Count == 0)
                yield break;

            BuildingInfo[] all = Resources.FindObjectsOfTypeAll<BuildingInfo>();
            HashSet<string> skippedPrefabs = instance.skippedPrefabs;

            for (int i = 0; i < all.Length; i++)
            {
                BuildingInfo info = all[i];
                bool skipped = skippedPrefabs.Contains(info.gameObject.name);

                if (skipped)
                {
                    DestroyBuilding(info);
                    yield return null;
                }
            }

            try
            {
                Resources.UnloadUnusedAssets();
                Util.DebugPrint("Skipped some at", Profiling.Millis);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
            }
        }

        static void DestroyBuilding(BuildingInfo info)
        {
            info.DestroyPrefabInstance();
            info.DestroyPrefab();
        }
    }
}
