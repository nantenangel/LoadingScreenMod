using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using UnityEngine;

namespace LoadingScreenMod
{
    public sealed class PrefabLoader : DetourUtility
    {
        internal static PrefabLoader instance;
        readonly object loadingLock = Util.Get(LoadingManager.instance, "m_loadingLock");
        readonly FieldInfo queueField = typeof(LoadingManager).GetField("m_mainThreadQueue", BindingFlags.NonPublic | BindingFlags.Instance);
        readonly FieldInfo hasQueuedActionsField = typeof(LoadingManager).GetField("m_hasQueuedActions", BindingFlags.NonPublic | BindingFlags.Instance);
        readonly FieldInfo nameField, prefabsField, prefabsField2, replacesField, replacesField2;
        internal HashSet<string> skippedPrefabs = new HashSet<string>();
        bool saveDeserialized;
        const string ROUTINE = "<InitializePrefabs>c__Iterator2";
        internal static StreamWriter w;

        internal PrefabLoader()
        {
            instance = this;
            w = new StreamWriter(Util.GetFileName("LoadingQueue", "txt"));

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
            Revert();
            base.Dispose();
            skippedPrefabs.Clear(); skippedPrefabs = null;
            w?.Dispose();
            w = null; instance = null;
        }

        public static void QueueLoadingAction(LoadingManager lm, IEnumerator action)
        {
            bool isBuildingCollection = action.GetType().Name == ROUTINE;

            // This race condition with the simulation thread must be checked. It never occurs in my game, though.
            if (isBuildingCollection && !instance.saveDeserialized)
            {
                int startMillis = Profiling.Millis;

                while (!LevelLoader.IsSaveDeserialized() && Profiling.Millis - startMillis < 2000)
                {
                    w.WriteLine("\nSleeping - " + LevelLoader.GetSimProgress() + " - " + Profiling.Millis);
                    Thread.Sleep(60);
                }

                instance.saveDeserialized = true;
            }

            while (!Monitor.TryEnter(instance.loadingLock, SimulationManager.SYNCHRONIZE_TIMEOUT))
                ;

            try
            {
                if (isBuildingCollection)
                    action = instance.Skip(action);

                if (action != null)
                {
                    Queue<IEnumerator> mainThreadQueue = (Queue<IEnumerator>) instance.queueField.GetValue(lm);
                    instance.Desc(action, mainThreadQueue, isBuildingCollection);
                    mainThreadQueue.Enqueue(action);

                    if (mainThreadQueue.Count < 2)
                        instance.hasQueuedActionsField.SetValue(lm, true);
                }
            }
            finally
            {
                Monitor.Exit(instance.loadingLock);
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
                    BuildingCollection bc = GameObject.Find(name)?.GetComponent<BuildingCollection>();
                    w.WriteLine("\nKept " + p.Length + " prefabs out of " + prefabs.Length + " (" + name + ")");
                    prefabsField.SetValue(action, p);
                    prefabsField2.SetValue(action, p);

                    // In the presence of the European Buildings Unlocker mod, bc is usually null.
                    // Obviously caused by the Destroy() invokes in that mod.
                    if (bc != null)
                        bc.m_prefabs = p;

                    if (keptReplaces != null)
                    {
                        string[] r = keptReplaces.ToArray();
                        w.WriteLine("Kept " + r.Length + " replaces out of " + replaces.Length);
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

            if (info.name != name)
                Util.DebugPrint("Difference:", info.name, name);

            int index = GetIndex(info.GetService(), info.GetSubService());
            bool skipClass = Settings.settings.skip[index];
            return (skipClass || Settings.settings.SkipThis(name)) && !UsedAssets.instance.GotPrefab(name, replace);
        }

        void Desc(IEnumerator action, Queue<IEnumerator> mainThreadQueue, bool isBuildingCollection)
        {
            string s = string.Empty;

            try
            {
                var method = new StackFrame(2).GetMethod();
                s = "\n" + method.DeclaringType.FullName + "." + method.Name + " - " + Profiling.Millis + " - " + LevelLoader.GetSimProgress() + " - " + mainThreadQueue.Count;
                string name = Util.Get(action, "name") as string;
                GameObject go = GameObject.Find(name);

                if (go != null)
                    s = string.Concat(s, " - ", go.activeSelf, " - ", go.activeInHierarchy);

                if (!string.IsNullOrEmpty(name))
                    s = string.Concat(s, " - ", name);

                s = string.Concat(s, " - ", go?.transform?.parent?.gameObject?.name ?? "null parent");

                BuildingInfo[] prefabs = (BuildingInfo[]) Util.Get(action, "prefabs");
                string[] replaces = (string[]) Util.Get(action, "replaces");
                s = string.Concat(s, " - (", prefabs.Length, ", ");

                if (replaces != null)
                    s = string.Concat(s, replaces.Length, ")");
                else
                    s = string.Concat(s, "null)");
            }
            catch (Exception) { }

            try
            {
                BuildingCommonCollection common = Util.Get(action, "collection") as BuildingCommonCollection;

                if (common != null)
                {
                    BuildingInfoSub burned = common.m_burned;
                    BuildingInfoSub constr = common.m_construction;

                    if (burned != null)
                        s = string.Concat(s, " + ", burned.name, " ", burned.GetService(), " ", burned.GetSubService());
                    if (constr != null)
                        s = string.Concat(s, " + ", constr.name, " ", constr.GetService(), " ", constr.GetSubService());
                }
            }
            catch (Exception) { }

            w.WriteLine(s + "  \t" + action.GetType().Name);

            try
            {
                string name = Util.Get(action, "name") as string;
                GameObject go = GameObject.Find(name);

                if (go != null)
                    foreach (BuildingCollection c in go.GetComponentsInChildren<BuildingCollection>(true))
                        w.WriteLine(" " + c.GetType().Name + "   " + c.name + "   " + c.isActiveAndEnabled);

                // if (name == "Residential High")
                //    SpeedTest(name, action, go);
            }
            catch (Exception) { }

            try
            {
                Array prefabs = Util.Get(action, "prefabs") as Array;
                string[] replaces = Util.Get(action, "replaces") as string[];
                int i = 0;

                if (prefabs != null && prefabs.Rank == 1)
                    foreach (object o in prefabs)
                    {
                        if (o != null && o is PrefabInfo)
                        {
                            PrefabInfo info = (PrefabInfo) o;
                            s = "  " + info.gameObject.name.PadRight(38);
                            int level = (int) info.GetClassLevel() + 1;
                            s = string.Concat(s, info.GetType().Name + " " + info.GetService() + " " + info.GetSubService() + " L" + level);

                            if (info.GetWidth() > 0 || info.GetLength() > 0)
                                s = string.Concat(s, " " + info.GetWidth() + "x" + info.GetLength());

                            if (info is BuildingInfo)
                                s = string.Concat(s, " " + ((BuildingInfo) info).m_zoningMode);

                            if (replaces != null && replaces.Length > i)
                            {
                                string r = replaces[i];

                                if (!string.IsNullOrEmpty(r))
                                    s = string.Concat(s.PadRight(105), " replaces ", r);
                            }

                            w.WriteLine(s);
                        }

                        i++;
                    }
            }
            catch (Exception) { }
        }

        void SpeedTest(string name, IEnumerator action, GameObject go)
        {
            if (go == null)
                Util.DebugPrint("GO is null");

            int LEN = 100000;
            long t = Profiling.stopWatch.ElapsedTicks, n = 0;

            for (int i = 0; i < LEN; i++)
            {
                BuildingInfo[] prefabs = prefabsField.GetValue(action) as BuildingInfo[];
                string[] replaces = replacesField.GetValue(action) as string[];
                n += prefabs.Length;
                prefabsField.SetValue(action, prefabs);
                replacesField.SetValue(action, replaces);
            }

            Util.DebugPrint("Speed test 1", Profiling.stopWatch.ElapsedTicks - t, Stopwatch.Frequency, n);
            t = Profiling.stopWatch.ElapsedTicks; n = 0;

            for (int i = 0; i < LEN; i++)
            {
                BuildingCollection bc = go?.GetComponent<BuildingCollection>();

                if (bc != null)
                    n += bc.m_prefabs.Length;
            }

            Util.DebugPrint("Speed test 2", Profiling.stopWatch.ElapsedTicks - t, Stopwatch.Frequency, n);

            GameObject pp = go?.transform?.parent?.gameObject;
            BuildingCollection[] aa = pp?.GetComponentsInChildren<BuildingCollection>(true);

            if (pp == null)
                Util.DebugPrint("Parent is null");
            else
                Util.DebugPrint("Parent is", pp.name);

            if (aa == null)
                Util.DebugPrint("All is null");
            else
                Util.DebugPrint("All length is", aa.Length);

            for (int j = 0; j < aa.Length; j++)
                Util.DebugPrint(" in all:", aa[j]?.name);

            t = Profiling.stopWatch.ElapsedTicks; n = 0;

            for (int i = 0; i < LEN; i++)
            {
                BuildingCollection[] all = go?.transform?.parent?.gameObject?.GetComponentsInChildren<BuildingCollection>(true);

                if (all != null)
                    for (int j = 0; j < all.Length; j++)
                        if (name == all[j]?.name)
                        {
                            n += all[j].m_prefabs.Length;
                            break;
                        }
            }

            Util.DebugPrint("Speed test 3", Profiling.stopWatch.ElapsedTicks - t, Stopwatch.Frequency, n);
            t = Profiling.stopWatch.ElapsedTicks; n = 0;
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

        void Desc2(IEnumerator action, Queue<IEnumerator> mainThreadQueue, bool isBuildingCollection)
        {
            if (!isBuildingCollection)
                return;

            string s = "\n";

            try
            {
                string name = Util.Get(action, "name") as string;

                if (!string.IsNullOrEmpty(name))
                    s = string.Concat(s, name);
            }
            catch (Exception) { }

            w.WriteLine(s);

            try
            {
                Array prefabs = Util.Get(action, "prefabs") as Array;

                if (prefabs != null && prefabs.Rank == 1)
                    foreach (object o in prefabs)
                    {
                        if (o != null && o is PrefabInfo)
                        {
                            PrefabInfo info = (PrefabInfo) o;
                            s = "  " + info.gameObject.name.PadRight(38);
                            int level = (int) info.GetClassLevel() + 1;
                            string sub = info.GetSubService() == ItemClass.SubService.None ? "" : info.GetSubService().ToString() + " ";
                            s = string.Concat(s, info.GetService() + " " + sub + "L" + level);

                            if (info.GetWidth() > 0 || info.GetLength() > 0)
                                s = string.Concat(s, " " + info.GetWidth() + "x" + info.GetLength());

                            if (info is BuildingInfo)
                                s = string.Concat(s, " " + ((BuildingInfo) info).m_zoningMode);

                            w.WriteLine(s);
                        }
                    }
            }
            catch (Exception) { }
        }

        internal void DestroySkipped()
        {
            if (skippedPrefabs == null || skippedPrefabs.Count == 0)
                return;

            BuildingInfo[] all = Resources.FindObjectsOfTypeAll<BuildingInfo>();
            w.WriteLine("\nAll BuildingInfos - " + all.Length);

            for (int i = 0; i < all.Length; i++)
            {
                BuildingInfo info = all[i];
                GameObject go = info.gameObject;
                bool skipped = skippedPrefabs.Contains(go.name);
                string s = "  " + (go.name ?? "null") + " " + go.activeInHierarchy + " " + info.isActiveAndEnabled;
                s += " " + (go.transform?.parent?.gameObject?.name ?? "null parent") + " " + (skipped ? "  skipped" : "");
                w?.WriteLine(s);

                if (skipped)
                    DestroyBuilding(info);
            }
        }

        void DestroyBuilding(BuildingInfo info)
        {
            info.DestroyPrefabInstance();
            info.DestroyPrefab();
        }
    }
}
