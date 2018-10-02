using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using UnityEngine;

namespace LoadingScreenModTest
{
    public sealed class PrefabLoader : DetourUtility<PrefabLoader>
    {
        readonly FieldInfo hasQueuedActionsField = typeof(LoadingManager).GetField("m_hasQueuedActions", BindingFlags.NonPublic | BindingFlags.Instance);
        readonly FieldInfo nameField, prefabsField, replacesField;
        Matcher skipMatcher = Settings.settings.SkipMatcher, exceptMatcher = Settings.settings.ExceptMatcher;
        internal HashSet<string> skippedPrefabs = new HashSet<string>();
        HashSet<string> simulationPrefabs;
        bool saveDeserialized;
        const string ROUTINE = "<InitializePrefabs>c__Iterator0";
        StreamWriter w;

        private PrefabLoader()
        {
            w = new StreamWriter(Util.GetFileName("LoadingQueue", "txt"));
            Type coroutine = typeof(BuildingCollection).GetNestedType(ROUTINE, BindingFlags.NonPublic);
            nameField = coroutine?.GetField("name", BindingFlags.NonPublic | BindingFlags.Instance);
            prefabsField = coroutine?.GetField("prefabs", BindingFlags.NonPublic | BindingFlags.Instance);
            replacesField = coroutine?.GetField("replaces", BindingFlags.NonPublic | BindingFlags.Instance);

            if (nameField != null && prefabsField != null && replacesField != null)
                init(typeof(LoadingManager), "QueueLoadingAction");
        }

        internal override void Dispose()
        {
            w.Dispose();
            w = null;
            Util.DebugPrint("Skipped", skippedPrefabs.Count, "prefabs");
            base.Dispose();
            skippedPrefabs.Clear(); skippedPrefabs = null;
            skipMatcher = exceptMatcher = null;
        }

        public static void QueueLoadingAction(LoadingManager lm, IEnumerator action)
        {
            bool isBuildingCollection = action.GetType().DeclaringType == typeof(BuildingCollection);

            // This race condition with the simulation thread must be watched. It never occurs in my game, though.
            if (isBuildingCollection && !instance.saveDeserialized)
            {
                int startMillis = Profiling.Millis;

                while (!LevelLoader.IsSaveDeserialized() && Profiling.Millis - startMillis < 5000)
                    Thread.Sleep(60);

                instance.saveDeserialized = true;
            }

            while (!Monitor.TryEnter(LevelLoader.instance.loadingLock, SimulationManager.SYNCHRONIZE_TIMEOUT))
                ;

            try
            {
                if (isBuildingCollection)
                {
                    instance.Desc(action);
                    action = instance.Skip(action);
                }

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
                Util.DebugPrint("xxx", action.GetType().FullName, "at", Profiling.Millis);
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

                Util.DebugPrint("-->", name, "at", Profiling.Millis, "prefabs:", prefabs.Length, "rep:", Util.OnJoin("; ", replaces));

                LookupSimulationPrefabs();
                List<BuildingInfo> keptPrefabs = null; List<string> keptReplaces = null;

                for (int i = 0; i < prefabs.Length; i++)
                {
                    BuildingInfo info = prefabs[i];
                    string replace = i < replaces.Length ? replaces[i] : string.Empty;

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
                    BuildingCollection bc = GameObject.Find(name)?.GetComponent<BuildingCollection>();

                    // In the presence of the European Buildings Unlocker mod, bc is usually null.
                    // Obviously caused by the Destroy() invokes in that mod.
                    if (bc != null)
                        bc.m_prefabs = p;

                    if (keptReplaces != null)
                    {
                        string[] r = keptReplaces.ToArray();
                        replacesField.SetValue(action, r);

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

        bool Skip(BuildingInfo info, string replace)
        {
            if (!string.IsNullOrEmpty(replace))
                Util.DebugPrint(info.gameObject.name, "  replaces", replace);
            else
                Util.DebugPrint(info.gameObject.name);

            if (skipMatcher.Matches(info))
            {
                string name = info.gameObject.name;

                if (IsSimulationPrefab(name, replace))
                {
                    Util.DebugPrint(name + " -> not skipped (used in city)");
                    return false;
                }

                if (exceptMatcher.Matches(info))
                {
                    Util.DebugPrint(name + " -> not skipped (excepted)");
                    return false;
                }

                Util.DebugPrint(name + " -> skipped");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Looks up the building prefabs used in the simulation.
        /// </summary>
        internal void LookupSimulationPrefabs()
        {
            if (simulationPrefabs == null)
            {
                simulationPrefabs = new HashSet<string>();

                try
                {
                    Building[] buffer = BuildingManager.instance.m_buildings.m_buffer;
                    int n = buffer.Length;

                    for (int i = 1; i < n; i++)
                        if (buffer[i].m_flags != Building.Flags.None)
                        {
                            string fullName = PrefabCollection<BuildingInfo>.PrefabName(buffer[i].m_infoIndex);

                            // Recognize prefabs.
                            if (!string.IsNullOrEmpty(fullName) && fullName.IndexOf('.') < 0)
                                simulationPrefabs.Add(fullName);
                        }
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogException(e);
                }

                Console.WriteLine("LookupSimulationPrefabs:");

                foreach (var name in simulationPrefabs)
                    Console.WriteLine(name);
            }
        }

        internal bool AllPrefabsAvailable() => CustomDeserializer.AllAvailable<BuildingInfo>(simulationPrefabs, new HashSet<string>());

        bool IsSimulationPrefab(string name, string replace)
        {
            if (simulationPrefabs.Contains(name))
                return true;

            replace = replace?.Trim();

            if (string.IsNullOrEmpty(replace))
                return false;

            if (replace.IndexOf(',') != -1)
            {
                string[] array = replace.Split(',');

                for (int i = 0; i < array.Length; i++)
                    if (simulationPrefabs.Contains(array[i].Trim()))
                        return true;

                return false;
            }
            else
                return simulationPrefabs.Contains(replace);
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

        void Desc(IEnumerator action)
        {
            string s = "\n";
            string name = Util.Get(action, "name") as string;

            if (!string.IsNullOrEmpty(name))
                s = string.Concat(s, name);

            w.WriteLine(s);

            if (Util.Get(action, "prefabs") is Array prefabs && prefabs.Rank == 1)
                foreach (object o in prefabs)
                    if (o is PrefabInfo info)
                    {
                        s = "  " + info.gameObject.name.PadRight(64);
                        int level = (int) info.GetClassLevel() + 1;
                        string sub = info.GetSubService() == ItemClass.SubService.None ? "" : info.GetSubService().ToString() + " ";
                        s = string.Concat(s, info.GetService() + " " + sub + "L" + level);

                        if (info.GetWidth() > 0 || info.GetLength() > 0)
                            s = string.Concat(s, " " + info.GetWidth() + "x" + info.GetLength());

                        if (info is BuildingInfo bi)
                            s = string.Concat(s, " " + bi.m_zoningMode);

                        w.WriteLine(s);
                    }
        }
    }
}
