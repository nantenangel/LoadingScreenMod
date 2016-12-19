using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using ColossalFramework.Packaging;
using UnityEngine;

namespace LoadingScreenMod
{
    internal sealed class Sharing : DetourUtility
    {
        internal static Sharing instance;

        int loadAhead;
        int cacheDepth = 4;
        readonly string SYNC = "SYNC";

        // The assets to load, ordered for maximum performance.
        Package.Asset[] queue;

        object sync = new object();

        // Asset checksum to bytes.
        Dictionary<string, byte[]> assets = new Dictionary<string, byte[]>(128);
        Queue<string> assetsQueue = new Queue<string>(128);
        internal static bool Supports(Package.AssetType type) => type <= Package.UnityTypeEnd && type >= Package.UnityTypeStart;

        // These are local to loadWorker.
        List<Package.Asset> loadList = new List<Package.Asset>(30);
        Dictionary<string, byte[]> loadMap = new Dictionary<string, byte[]>(30);
        int loadBytes, removeIndex;

        internal void WaitForLoad()
        {
            lock (sync)
            {
                while (loadAhead <= 0)
                    Monitor.Wait(sync);

                Interlocked.Decrement(ref loadAhead);
                Monitor.Pulse(sync);
            }
        }

        void LoadPackage(Package package)
        {
            loadList.Clear(); loadMap.Clear();

            foreach (Package.Asset asset in package)
            {
                string name = asset.name;

                if (name.EndsWith("_SteamPreview") || name.EndsWith("_Snapshot"))
                    continue;

                if (Supports(asset.type) && !assets.ContainsKey(asset.checksum)) // thread-safe for writer
                    loadList.Add(asset);
            }

            loadList.Sort((a, b) => (int) (a.offset - b.offset));

            using (FileStream fs = File.OpenRead(package.packagePath))
                for (int i = 0; i < loadList.Count; i++)
                    loadMap[loadList[i].checksum] = LoadAsset(fs, loadList[i]);

            lock(sync)
            {
                foreach (var kvp in loadMap)
                {
                    assets[kvp.Key] = kvp.Value;
                    assetsQueue.Enqueue(kvp.Key);
                    loadBytes += kvp.Value.Length;
                }
            }
        }

        byte[] LoadAsset(FileStream fs, Package.Asset asset)
        {
            fs.Position = asset.offset;
            int len = asset.size;
            byte[] bytes = new byte[len];
            int got = 0;
            int remaining = len;

            while (remaining > 0)
            {
                int n = fs.Read(bytes, got, remaining);

                if (n == 0)
                    throw new IOException("Unexpected end of file: " + asset.fullName);

                got += n; remaining -= n;
            }

            return bytes;
        }

        internal Stream GetStream(Package.Asset asset)
        {
            byte[] bytes = null;
            int count;

            lock(sync)
            {
                assets.TryGetValue(asset.checksum, out bytes);
                count = assets.Count;
            }

            if (bytes != null)
            {
                Trace.Seq("Got data at index", Tester.instance.index, "Assets:", count);
                return new MemStream(bytes, 0);
            }

            Trace.Pr("MISS:", asset.fullName, asset.package.packagePath, " Assets:", count);
            return asset.GetStream();
        }

        internal PackageReader GetReader(Stream stream)
        {
            MemStream ms = stream as MemStream;
            return ms != null ? new MemReader(ms) : new PackageReader(stream);
        }

        void LoadWorker()
        {
            Package.Asset[] q = queue;
            Package prevPackage = null;

            for (int index = 0; index < q.Length; index++)
            {
                Package p = q[index].package;

                if (!ReferenceEquals(p, prevPackage))
                    LoadPackage(p);

                assetsQueue.Enqueue(SYNC); // end-of-asset marker
                Trace.Seq("Loaded index", index, ":", assets.Count, "assets,", loadBytes, "bytes in memory");
                prevPackage = p;
                Interlocked.Increment(ref loadAhead);
                bool removed = false;

                lock (sync)
                {
                    Monitor.Pulse(sync);

                    while (loadAhead >= cacheDepth)
                        Monitor.Wait(sync);

                    while (index - removeIndex > 2 * cacheDepth)
                    {
                        removed = true;
                        string s = assetsQueue.Dequeue();

                        if (ReferenceEquals(s, SYNC))
                            removeIndex++;
                        else
                        {
                            loadBytes -= assets[s].Length;
                            assets.Remove(s);
                        }
                    }
                }

                if (removed)
                    Trace.Seq("Removed until", removeIndex, ":", assets.Count, "assets,", loadBytes, "bytes in memory");
            }

            Trace.Seq("LoadWorker exits", assets.Count, assetsQueue.Count);
            assetsQueue.Clear(); assetsQueue = null;
        }

        // Delegates can be used to call non-public methods. Delegates have about the same performance as regular method calls.
        // Delegates are roughly 100 times faster than reflection in Unity 5.
        readonly Action<Package, GameObject, PackageReader> DeserializeComponent =
            Util.CreateStaticAction<Package, GameObject, PackageReader>(typeof(PackageDeserializer), "DeserializeComponent");

        int texhit, mathit, meshit, texload, matload, mesload, textureCount;
        Dictionary<string, Texture> textures = new Dictionary<string, Texture>();
        Dictionary<string, MaterialData> materials = new Dictionary<string, MaterialData>();
        Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
        bool shareTextures, shareMaterials, shareMeshes, isMain;

        internal Sharing()
        {
            instance = this;

            // Be quick, or the JIT Compiler will inline calls to this one. It is a small method, less than 32 IL bytes.
            //init(typeof(PackageDeserializer), "DeserializeMeshFilter");
            //init(typeof(PackageDeserializer), "DeserializeMaterial");
            //init(typeof(PackageDeserializer), "DeserializeMeshRenderer");
            //init(typeof(PackageDeserializer), "DeserializeGameObject");
            init(typeof(PackageReader), "ReadByteArray", typeof(MemReader), "DreadByteArray");
        }

        internal override void Dispose()
        {
            Util.DebugPrint("Textures / Materials / Meshes loaded:", texload, "/", matload, "/", mesload, "referenced:", texhit, "/", mathit, "/", meshit);
            Revert();
            base.Dispose();
            textures.Clear(); materials.Clear(); meshes.Clear();
            textures = null; materials = null; meshes = null; instance = null;
        }

        internal void Start(Package.Asset[] queue)
        {
            shareTextures = Settings.settings.shareTextures;
            shareMaterials = Settings.settings.shareMaterials;
            shareMeshes = Settings.settings.shareMeshes;
            isMain = false;

            this.queue = queue;
            new Thread(LoadWorker).Start();
        }

        internal static UnityEngine.Object DeserializeGameObject(Package package, PackageReader reader)
        {
            string name = reader.ReadString();
            GameObject gameObject = new GameObject(name);
            gameObject.tag = reader.ReadString();
            gameObject.layer = reader.ReadInt32();
            gameObject.SetActive(reader.ReadBoolean());
            int num = reader.ReadInt32();
            Sharing.instance.isMain = num > 3;

            for (int i = 0; i < num; i++)
                Sharing.instance.DeserializeComponent(package, gameObject, reader);

            return gameObject;
        }

        internal static UnityEngine.Object DeserializeMaterial(Package package, PackageReader reader)
        {
            string materialName = reader.ReadString();
            string shaderName = reader.ReadString();
            Material material = new Material(Shader.Find(shaderName));
            material.name = materialName;
            int numProperties = reader.ReadInt32();
            bool share = Sharing.instance.shareTextures && Sharing.instance.isMain;

            for (int i = 0; i < numProperties; i++)
            {
                int kind = reader.ReadInt32();

                if (kind == 0)
                {
                    string s = reader.ReadString();
                    material.SetColor(s, reader.ReadColor());
                }
                else if (kind == 1)
                {
                    string s = reader.ReadString();
                    material.SetVector(s, reader.ReadVector4());
                }
                else if (kind == 2)
                {
                    string s = reader.ReadString();
                    material.SetFloat(s, reader.ReadSingle());
                }
                else if (kind == 3)
                {
                    string propertyName = reader.ReadString();

                    if (!reader.ReadBoolean())
                    {
                        string checksum = reader.ReadString();
                        Texture texture;

                        if (share && Sharing.instance.textures.TryGetValue(checksum, out texture))
                            Sharing.instance.texhit++;
                        else
                        {
                            texture = PackageManager.FindAssetByChecksum(checksum).Instantiate<Texture>();
                            Sharing.instance.texload++;

                            if (share)
                                Sharing.instance.textures[checksum] = texture;
                        }

                        material.SetTexture(propertyName, texture);
                        Sharing.instance.textureCount++;
                    }
                    else
                        material.SetTexture(propertyName, null);
                }
            }

            return material;
        }

        internal static void DeserializeMeshRenderer(Package package, MeshRenderer renderer, PackageReader reader)
        {
            int count = reader.ReadInt32();
            Material[] materials = new Material[count];
            bool share = Sharing.instance.shareMaterials && Sharing.instance.isMain;

            for (int i = 0; i < count; i++)
            {
                string checksum = reader.ReadString();
                Material material;
                MaterialData mat;

                if (share && Sharing.instance.materials.TryGetValue(checksum, out mat))
                {
                    material = mat.material;
                    Sharing.instance.mathit++;
                    Sharing.instance.texhit += mat.textureCount;
                }
                else
                {
                    Sharing.instance.textureCount = 0;
                    material = PackageManager.FindAssetByChecksum(checksum).Instantiate<Material>();
                    Sharing.instance.matload++;

                    if (share)
                        Sharing.instance.materials[checksum] = new MaterialData(material, Sharing.instance.textureCount);
                }

                materials[i] = material;
            }

            renderer.sharedMaterials = materials;
        }

        internal static void DeserializeMeshFilter(Package package, MeshFilter meshFilter, PackageReader reader)
        {
            bool share = Sharing.instance.shareMeshes;
            string checksum = reader.ReadString();
            Mesh mesh;

            if (share && Sharing.instance.meshes.TryGetValue(checksum, out mesh))
                Sharing.instance.meshit++;
            else
            {
                mesh = PackageManager.FindAssetByChecksum(checksum).Instantiate<Mesh>();
                Sharing.instance.mesload++;

                if (share)
                    Sharing.instance.meshes[checksum] = mesh;
            }

            meshFilter.sharedMesh = mesh;
        }
    }

    struct MaterialData
    {
        internal Material material;
        internal int textureCount;

        internal MaterialData(Material m, int count)
        {
            this.material = m;
            this.textureCount = count;
        }
    }
}
