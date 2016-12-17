using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using ColossalFramework.Packaging;
using ColossalFramework.PlatformServices;
using UnityEngine;

namespace LoadingScreenMod
{
    public sealed class Subscribe : MonoBehaviour
    {
        const string GAME_OBJECT = "LSM Subscriber";
        HashSet<ulong> packages;
        Stopwatch sw = new Stopwatch();

        public static void Begin(HashSet<ulong> packages)
        {
            Stop();

            if (packages.Count > 0)
            {
                new GameObject(GAME_OBJECT).AddComponent<Subscribe>().packages = packages;
                Util.DebugPrint(GAME_OBJECT, "GameObject created");
            }
        }

        public static void Stop()
        {
            GameObject go = GameObject.Find(GAME_OBJECT);

            if (!(go == null))
            {
                go.GetComponent<Subscribe>()?.StopAllCoroutines();
                UnityEngine.Object.Destroy(go);
                Util.DebugPrint(GAME_OBJECT, "GameObject destroyed");
            }
        }

        public void Start()
        {
            sw.Start();
            StartCoroutine(Subscriber());
            Util.DebugPrint(GAME_OBJECT, "started");
        }

        IEnumerator Subscriber()
        {
            // Let the game run
            yield return new WaitForSeconds(8f);

            foreach(ulong id64 in packages)
            {
                PublishedFileId id = new PublishedFileId(id64);

                if (HasPackage(id))
                    continue;

                try
                {
                    PlatformService.workshop.Subscribe(id);
                    Util.DebugPrint("Subscribing to", id, "at", sw.ElapsedMilliseconds);
                }
                catch (Exception e)
                {
                    Util.DebugPrint("Cannot subscribe to", id, "at", sw.ElapsedMilliseconds, ":", e.Message);
                    continue;
                }

                for (int i = 0; i < 20; i++)
                {
                    yield return new WaitForSeconds(0.5f);

                    if (HasPackage(id))
                        break;
                }
            }

            Util.DebugPrint(GAME_OBJECT, "finished at", sw.ElapsedMilliseconds);
        }

        bool HasPackage(PublishedFileId id)
        {
            Dictionary<PublishedFileId, HashSet<string>> packagesToPaths =
                (Dictionary<PublishedFileId, HashSet<string>>) Util.GetStatic(typeof(PackageManager), "m_PackagesSteamToPathsMap");

            return packagesToPaths.ContainsKey(id);
        }
    }
}
