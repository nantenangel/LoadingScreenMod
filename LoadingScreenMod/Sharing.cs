using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using ColossalFramework.Packaging;
using UnityEngine;

namespace LoadingScreenMod
{
    internal sealed class Sharing : DetourUtility
    {
        internal static Sharing instance;

        const int cacheDepth = 3;
        const int dataHistory = (cacheDepth * 3 + 2) * 12;
        ConcurrentCounter loadAhead = new ConcurrentCounter(0, 0, cacheDepth), mtAhead = new ConcurrentCounter(0, 0, cacheDepth);
        static bool Supports(Package.AssetType type) => type <= Package.UnityTypeEnd && type >= Package.UnityTypeStart;

        // The assets to load, ordered for maximum performance.
        Package.Asset[] assetsQueue;
        object mutex = new object();

        // Asset checksum to asset data.
        Dictionary<string, object> data = new Dictionary<string, object>(dataHistory + 30);
        Dictionary<string, int> removed = new Dictionary<string, int>(512);
        int assetCount, totalBytes;

        // Meshes and textures from loadWorker to mtWorker.
        ConcurrentQueue<KeyValuePair<Package.Asset, byte[]>> mtQueue = new ConcurrentQueue<KeyValuePair<Package.Asset, byte[]>>(32);

        // These are local to loadWorker.
        Queue<string> dataQueue = new Queue<string>(dataHistory + 30);
        List<Package.Asset> loadList = new List<Package.Asset>(30);
        Dictionary<string, byte[]> loadMap = new Dictionary<string, byte[]>(30);

        internal void WaitForWorkers() => mtAhead.Decrement();

        void LoadPackage(Package package, int index)
        {
            loadList.Clear(); loadMap.Clear();

            lock (mutex)
            {
                foreach (Package.Asset asset in package)
                {
                    string name = asset.name;

                    if (name.EndsWith("_SteamPreview") || name.EndsWith("_Snapshot"))
                        continue;

                    if (Supports(asset.type) && !data.ContainsKey(asset.checksum))
                        loadList.Add(asset);
                }
            }

            loadList.Sort((a, b) => (int) (a.offset - b.offset));
            assetCount += loadList.Count;
            Trace.Seq("loads index ", index, package.packageName + "." + package.packageMainAsset);

            using (FileStream fs = File.OpenRead(package.packagePath))
                for (int i = 0; i < loadList.Count; i++)
                {
                    Package.Asset asset = loadList[i];
                    byte[] bytes = LoadAsset(fs, asset);
                    string checksum = asset.checksum;
                    loadMap[checksum] = bytes;
                    dataQueue.Enqueue(checksum);
                    totalBytes += bytes.Length;

                    if (asset.type == Package.AssetType.StaticMesh)
                        mtQueue.Enqueue(new KeyValuePair<Package.Asset, byte[]>(asset, bytes));
                }

            Trace.Seq("loaded index", index, package.packageName + "." + package.packageMainAsset);

            lock (mutex)
            {
                foreach (var kvp in loadMap)
                    if (!data.ContainsKey(kvp.Key))  // this check is necessary
                        data[kvp.Key] = kvp.Value;
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

        void LoadWorker()
        {
            Thread.CurrentThread.Name = "LoadWorker";
            Package.Asset[] q = assetsQueue;
            Package prevPackage = null;

            for (int index = 0; index < q.Length; index++)
            {
                Package p = q[index].package;

                if (!ReferenceEquals(p, prevPackage))
                    LoadPackage(p, index);

                mtQueue.Enqueue(default(KeyValuePair<Package.Asset, byte[]>)); // end-of-asset marker
                loadAhead.Increment();
                Trace.Seq("done with   ", index, p.packageName + "." + p.packageMainAsset, ":", data.Count, "assets");
                prevPackage = p;
                int count = 0;

                lock (mutex)
                {
                    int millis = Profiling.Millis;

                    while (data.Count > dataHistory)
                    {
                        string checksum = dataQueue.Dequeue();

                        if (data.Remove(checksum))
                        {
                            removed[checksum] = millis;
                            count++;
                        }
                    }
                }

                if (count > 0)
                    Trace.Seq("removed", count, ":", data.Count, "assets");
            }

            Trace.Seq("exits", data.Count, dataQueue.Count, assetCount, "/", q.Length, totalBytes, "/", assetCount);
            mtQueue.SetCompleted();
            dataQueue.Clear(); dataQueue = null;
        }

        void MTWorker()
        {
            Thread.CurrentThread.Name = "MTWorker A";
            int index = 0, count = 0;
            KeyValuePair<Package.Asset, byte[]> elem;

            while (mtQueue.Dequeue(out elem))
            {
                if (elem.Key == null)
                {
                    mtAhead.Increment();
                    loadAhead.Decrement();
                    Trace.Seq("done with", index++);
                }
                else if (elem.Key.type == Package.AssetType.StaticMesh)
                {
                    Trace.Seq("starts mesh   ", index, elem.Key.fullName);
                    DeserializeMeshObj(elem.Key, elem.Value);
                    count++;
                    Trace.Seq("completed mesh", index, elem.Key.fullName);
                }
            }

            Trace.Seq("exits", index, mtQueue.Count, count);
        }

        void DeserializeMeshObj(Package.Asset asset, byte[] bytes)
        {
            MeshObj mo;

            using (MemStream stream = new MemStream(bytes, 0))
            using (MemReader reader = new MemReader(stream))
            {
                if (DeserializeHeader(reader) != typeof(Mesh))
                {
                    Util.DebugPrint("Asset error:", asset.fullName, "should be Mesh");
                    return;
                }

                string name = reader.ReadString();
                Vector3[] vertices = reader.ReadVector3Array();
                Color[] colors = reader.ReadColorArray();
                Vector2[] uv = reader.ReadVector2Array();
                Vector3[] normals = reader.ReadVector3Array();
                Vector4[] tangents = reader.ReadVector4Array();
                BoneWeight[] boneWeights = reader.ReadBoneWeightsArray();
                Matrix4x4[] bindposes = reader.ReadMatrix4x4Array();
                int count = reader.ReadInt32();
                int[][] triangles = new int[count][];

                for (int i = 0; i < count; i++)
                    triangles[i] = reader.ReadInt32Array();

                mo = new MeshObj { name = name, vertices = vertices, colors = colors, uv = uv, normals = normals,
                                    tangents = tangents, boneWeights = boneWeights, bindposes = bindposes, triangles = triangles };
            }

            lock (mutex)
            {
                data[asset.checksum] = mo;
            }
        }

        static Type DeserializeHeader(MemReader reader)
        {
            if (reader.ReadBoolean())
                return null;

            return Type.GetType(reader.ReadString());
        }

        internal Stream GetStream(Package.Asset asset)
        {
            object obj;
            int count, removedMillis;

            lock (mutex)
            {
                data.TryGetValue(asset.checksum, out obj);
                removed.TryGetValue(asset.checksum, out removedMillis);
                count = data.Count;
            }

            byte[] bytes = obj as byte[];

            if (bytes != null)
                return new MemStream(bytes, 0);

            if (asset.type != UserAssetType.CustomAssetMetaData)
                Trace.Seq("MISS BYTES:", asset.fullName, asset.package.packagePath, " Assets", count, " Removed at", removedMillis);

            Trace.Pr("MISS BYTES:", asset.fullName, asset.package.packagePath, " Assets", count, " Removed at", removedMillis);
            return asset.GetStream();
        }

        internal Mesh GetMesh(string checksum, Package package, int ind)
        {
            object obj;
            int count, removedMillis;

            lock (mutex)
            {
                data.TryGetValue(checksum, out obj);
                removed.TryGetValue(checksum, out removedMillis);
                count = data.Count;
            }

            MeshObj mo = obj as MeshObj;

            if (mo != null)
            {
                Trace.meshMicros -= Profiling.Micros;
                Mesh mesh = new Mesh();
                mesh.name = mo.name;
                mesh.vertices = mo.vertices;
                mesh.colors = mo.colors;
                mesh.uv = mo.uv;
                mesh.normals = mo.normals;
                mesh.tangents = mo.tangents;
                mesh.boneWeights = mo.boneWeights;
                mesh.bindposes = mo.bindposes;

                for (int i = 0; i < mo.triangles.Length; i++)
                    mesh.SetTriangles(mo.triangles[i], i);

                Trace.meshMicros += Profiling.Micros;
                Trace.Ind(ind, "Mesh", mesh.name + ", " + mesh.vertexCount + ", " + mesh.triangles.Length);
                return mesh;
            }

            byte[] bytes = obj as byte[];

            if (bytes != null)
            {
                Trace.Pr("MISS MESH OBJ BUT GOT BYTES:  Assets", count, checksum);
                using (MemStream stream = new MemStream(bytes, 0))
                using (PackageReader reader = new MemReader(stream))
                {
                    Trace.Ind(ind, "ASSET");
                    return (Mesh) new AssetDeserializer(package, reader, ind).Deserialize();
                }
            }

            Package.Asset asset = package.FindByChecksum(checksum);
            Trace.Seq("MISS MESH OBJ AND BYTES:", asset.fullName, asset.package.packagePath, " Assets", count, " Removed at", removedMillis);
            Trace.Pr("MISS MESH OBJ AND BYTES:", asset.fullName, asset.package.packagePath, " Assets", count, " Removed at", removedMillis);
            return AssetDeserializer.Instantiate<Mesh>(asset, ind);
        }

        internal PackageReader GetReader(Stream stream)
        {
            MemStream ms = stream as MemStream;
            return ms != null ? new MemReader(ms) : new PackageReader(stream);
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

            assetsQueue = queue;
            new Thread(LoadWorker).Start();
            new Thread(MTWorker).Start();
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

    class MeshObj
    {
        internal string name;
        internal Vector3[] vertices;
        internal Color[] colors;
        internal Vector2[] uv;
        internal Vector3[] normals;
        internal Vector4[] tangents;
        internal BoneWeight[] boneWeights;
        internal Matrix4x4[] bindposes;
        internal int[][] triangles;
    }
}
