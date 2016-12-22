using System;
using UnityEngine;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Collections.Generic;
using ColossalFramework.PlatformServices;

namespace LoadingScreenMod
{
    public static class Util
    {
        public static void DebugPrint(params object[] args)
        {
            string s = string.Format("[LoadingScreen] {0}", " ".OnJoin(args));
            Debug.Log(s);
        }

        public static string OnJoin(this string delim, IEnumerable<object> args) => string.Join(delim, args.Select(o => o?.ToString() ?? "null").ToArray());
        public static string asString(this Array array) => string.Concat("[", ", ".OnJoin(array.OfType<object>()), "]");

        internal static void InvokeVoid(object instance, string method)
        {
            instance.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, null);
        }

        internal static object Invoke(object instance, string method)
        {
            return instance.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, null);
        }

        internal static void InvokeVoid(object instance, string method, params object[] args)
        {
            instance.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, args);
        }

        internal static object Invoke(object instance, string method, params object[] args)
        {
            return instance.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, args);
        }

        internal static void InvokeStaticVoid(Type type, string method, params object[] args)
        {
            type.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
        }

        internal static object InvokeStatic(Type type, string method, params object[] args)
        {
            return type.GetMethod(method, BindingFlags.Static| BindingFlags.NonPublic).Invoke(null, args);
        }

        internal static object Get(object instance, string field)
        {
            return instance.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(instance);
        }

        internal static object GetStatic(Type type, string field)
        {
            return type.GetField(field, BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        }

        internal static void Set(object instance, string field, object value)
        {
            instance.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(instance, value);
        }

        internal static void Set(object instance, string field, object value, BindingFlags flags)
        {
            instance.GetType().GetField(field, flags).SetValue(instance, value);
        }

        internal static string GetFileName(string fileBody, string extension)
        {
            string name = fileBody + string.Format("-{0:yyyy-MM-dd_HH-mm-ss}." + extension, DateTime.Now);
            return Path.Combine(GetSavePath(), name);
        }

        internal static string GetSavePath()
        {
            string modDir = Settings.settings.reportDir?.Trim();

            if (!string.IsNullOrEmpty(modDir))
                try
                {
                    if (!Directory.Exists(modDir))
                        Directory.CreateDirectory(modDir);

                    return modDir;
                }
                catch (Exception)
                {
                    DebugPrint("Cannot create directory:", modDir);
                }

            return Settings.DefaultSavePath;
        }

        /// <summary>
        /// Creates a delegate for a non-public static void method in class 'type' that takes parameters of types P, Q, and R.
        /// </summary>
        public static Action<P, Q, R> CreateStaticAction<P, Q, R>(Type type, string methodName)
        {
            MethodInfo m = type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
            return (Action<P, Q, R>) Delegate.CreateDelegate(typeof(Action<P, Q, R>), m);
        }

        public static List<T> ToList<T>(this T[] array, int count)
        {
            List<T> ret = new List<T>(count + 5);

            for (int i = 0; i < count; i++)
                ret.Add(array[i]);

            return ret;
        }

        internal static PublishedFileId GetPackageId(string packageName)
        {
            ulong id;
            return ulong.TryParse(packageName, out id) ? new PublishedFileId(id) : PublishedFileId.invalid;
        }
    }

    internal static class Trace
    {
        static Dictionary<string, int> methods = new Dictionary<string, int>(16);
        static Dictionary<string, int> types = new Dictionary<string, int>(64);
        static List<string> seq = new List<string>(32);
        internal static long meshMicros, texBytes, texImage, texCreate, stringRead;

        static StreamWriter w;
        internal static void Start() => w = new StreamWriter(Util.GetFileName("trace", "txt"));
        internal static void Stop() { SaveAll(); w.Dispose(); }
        internal static void Newline() { w.WriteLine(); w.Flush(); }
        internal static void Flush() => w.Flush();

        internal static void Pr(params object[] args)
        {
            string s = " ".OnJoin(args);

            lock (seq)
            {
                w.WriteLine(s);
                w.Flush();
            }
        }

        internal static void Ind(int n, params object[] args)
        {
            string s = (new string(' ', n + n) + " ".OnJoin(args)).PadRight(96) + " (" + Profiling.Millis + ") (" + GC.CollectionCount(0) + ")";

            lock (seq)
            {
                w.WriteLine(s);
                w.Flush();
            }
        }

        internal static void Seq(params object[] args)
        {
            string s = (" ".OnJoin(args)).PadRight(96) + " (" + Profiling.Millis + ") (" + GC.CollectionCount(0) + ")";

            lock (seq)
            {
                w.WriteLine(s);
                w.Flush();
                seq.Add(s);
            }
        }

        internal static void Tra(string name)
        {
            int count;

            if (!methods.TryGetValue(name, out count))
                count = 0;

            methods[name] = count + 1;
        }

        internal static void Typ(Type type)
        {
            string name = type?.FullName ?? "null";
            int count;

            if (!types.TryGetValue(name, out count))
                count = 0;

            types[name] = count + 1;
        }

        static void SaveAll()
        {
            Newline();
            Pr("Methods:");
            foreach (var kvp in methods)
                Pr(kvp.Key.PadRight(32), kvp.Value);

            Newline();
            Pr("Types:");
            foreach (var kvp in types)
                Pr(kvp.Key.PadRight(48), kvp.Value);

            methods.Clear(); types.Clear();

            Newline();
            Pr("meshMicros", meshMicros);
            Pr("texBytes", texBytes);
            Pr("texImage", texImage);
            Pr("texCreate", texCreate);
            Pr("stringRead", stringRead);

            Newline();
            Pr("Seq:");
            foreach (var s in seq)
                Pr(s);

            seq.Clear();
        }
    }
}
