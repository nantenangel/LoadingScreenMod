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
        static StreamWriter w;
        internal static void Start() => w = new StreamWriter(Util.GetFileName("trace", "txt"));
        internal static void Stop() => w.Dispose();
        internal static void Ind(int n, params object[] args) => w.WriteLine(new string(' ', n) + " ".OnJoin(args) + " (" + Profiling.Millis + ")");
        internal static void Newline() => w.WriteLine();
    }
}
