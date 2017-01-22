using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using ColossalFramework.Importers;
using ColossalFramework.Packaging;
using UnityEngine;

namespace LoadingScreenModTest
{
    internal sealed class Sharing
    {
        internal static Sharing instance;

        const int cacheDepth = 3;
        const int dataHistory = cacheDepth * 44;
        ConcurrentCounter loadAhead = new ConcurrentCounter(0, 0, cacheDepth), mtAhead = new ConcurrentCounter(0, 0, cacheDepth);
        internal void WaitForWorkers() => mtAhead.Decrement();
        static bool Supports(Package.AssetType type) => type <= Package.UnityTypeEnd && type >= Package.UnityTypeStart;

        // The assets to load, ordered for maximum performance.
        volatile Package.Asset[] assetsQueue;
        object mutex = new object();

        // Asset checksum to asset data.
        LinkedHashMap<string, object> data = new LinkedHashMap<string, object>(dataHistory + 64);
        int maxCount;
        //Dictionary<string, int> removed = new Dictionary<string, int>(512);
        //int assetCount, totalBytes;

        // Meshes and textures from LoadWorker to MTWorker.
        ConcurrentQueue<KeyValuePair<Package.Asset, byte[]>> mtQueue = new ConcurrentQueue<KeyValuePair<Package.Asset, byte[]>>(48);

        // These are local to LoadWorker.
        List<Package.Asset> loadList = new List<Package.Asset>(32);
        Dictionary<string, byte[]> loadMap = new Dictionary<string, byte[]>(32);

        // Worker threads.
        internal Thread loadWorkerThread, mtWorkerThread;

        void LoadPackage(Package package, int index)
        {
            loadList.Clear(); loadMap.Clear();
            int re = 0;

            lock (mutex)
            {
                foreach (Package.Asset asset in package)
                {
                    string name = asset.name, checksum = asset.checksum;
                    Package.AssetType type = asset.type;

                    if (!Supports(type) || name.EndsWith("_SteamPreview") || name.EndsWith("_Snapshot"))
                        continue;

                    if (type == Package.AssetType.Texture && (texturesMain.ContainsKey(checksum) || texturesLod.ContainsKey(checksum)) ||
                        type == Package.AssetType.StaticMesh && meshes.ContainsKey(checksum) ||
                        type == Package.AssetType.Material && (materialsMain.ContainsKey(checksum) || materialsLod.ContainsKey(checksum)))
                    {
                        //Trace.loadHits++;
                        continue;
                    }

                    if (data.ContainsKey(checksum))
                    {
                        data.Reinsert(checksum);
                        re++;
                    }
                    else
                        loadList.Add(asset);
                }
            }

            loadList.Sort((a, b) => (int) (a.offset - b.offset));
            //assetCount += loadList.Count;
            //Trace.Seq("loads index ", index, package.packageName + "." + package.packageMainAsset);

            using (FileStream fs = File.OpenRead(package.packagePath))
                for (int i = 0; i < loadList.Count; i++)
                {
                    Package.Asset asset = loadList[i];
                    byte[] bytes = LoadAsset(fs, asset);
                    loadMap[asset.checksum] = bytes;
                    //totalBytes += bytes.Length;

                    if (asset.type == Package.AssetType.Texture || asset.type == Package.AssetType.StaticMesh)
                        mtQueue.Enqueue(new KeyValuePair<Package.Asset, byte[]>(asset, bytes));
                }

            //Trace.Seq("loaded index", index, package.packageName + "." + package.packageMainAsset, " Touched", loadList.Count + re);

            lock (mutex)
            {
                foreach (var kvp in loadMap)
                    if (!data.ContainsKey(kvp.Key)) // this check is necessary
                        data.Add(kvp.Key, kvp.Value);
            }
        }

        byte[] LoadAsset(FileStream fs, Package.Asset asset)
        {
            int remaining = asset.size;

            if (remaining > 222444000 || remaining < 0)
                throw new IOException("Asset " + asset.fullName + " size: " + remaining);

            fs.Position = asset.offset;
            byte[] bytes = new byte[remaining];
            int got = 0;

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
                    try
                    {
                        LoadPackage(p, index);
                    }
                    catch (Exception e)
                    {
                        Util.DebugPrint("LoadWorker:", e.Message);
                    }

                mtQueue.Enqueue(default(KeyValuePair<Package.Asset, byte[]>)); // end-of-asset marker
                loadAhead.Increment();
                //Trace.Seq("done with   ", index, p.packageName + "." + p.packageMainAsset, ":", data.Count, "assets");
                prevPackage = p;
                int count;

                lock (mutex)
                {
                    int millis = Profiling.Millis;

                    for (count = 0; count < 18 && data.Count > dataHistory; count++)
                    {
                        //string checksum = data.EldestKey;
                        data.RemoveEldest();
                        //removed[checksum] = millis;
                    }
                }

                //if (count > 0)
                //    Trace.Seq("removed", count, ":", data.Count, "assets");
            }

            //Trace.Seq("exits", data.Count, assetCount, "/", q.Length, totalBytes, "/", assetCount);
            mtQueue.SetCompleted();
            loadList.Clear(); loadList = null; loadMap.Clear(); loadMap = null; assetsQueue = null;
        }

        void MTWorker()
        {
            Thread.CurrentThread.Name = "MTWorker A";
            //int index = 0, countm = 0, countt = 0;
            KeyValuePair<Package.Asset, byte[]> elem;

            while (mtQueue.Dequeue(out elem))
            {
                try
                {
                    if (elem.Key == null)
                    {
                        mtAhead.Increment();
                        loadAhead.Decrement();
                        //Trace.Seq("done with", index++);
                    }
                    else if (elem.Key.type == Package.AssetType.Texture)
                    {
                        //Trace.Seq("starts text   ", index, elem.Key.fullName);
                        DeserializeTextObj(elem.Key, elem.Value);
                        //countt++;
                        //Trace.Seq("completed text", index, elem.Key.fullName);
                    }
                    else if (elem.Key.type == Package.AssetType.StaticMesh)
                    {
                        //Trace.Seq("starts mesh   ", index, elem.Key.fullName);
                        DeserializeMeshObj(elem.Key, elem.Value);
                        //countm++;
                        //Trace.Seq("completed mesh", index, elem.Key.fullName);
                    }
                }
                catch (Exception e)
                {
                    Util.DebugPrint("MTWorker:", e.Message);
                }
            }

            //Trace.Seq("exits", index, mtQueue.Count, countm, countt);
        }

        void DeserializeMeshObj(Package.Asset asset, byte[] bytes)
        {
            MeshObj mo;

            using (MemStream stream = new MemStream(bytes, 0))
            using (MemReader reader = new MemReader(stream))
            {
                //Trace.meshWorker -= Profiling.Micros;

                if (DeserializeHeader(reader) != typeof(Mesh))
                    throw new IOException("Asset " + asset.fullName + " should be Mesh");

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
                //Trace.meshWorker += Profiling.Micros;
            }

            lock (mutex)
            {
                data[asset.checksum] = mo;
            }
        }

        void DeserializeTextObj(Package.Asset asset, byte[] bytes)
        {
            TextObj to;

            using (MemStream stream = new MemStream(bytes, 0))
            using (MemReader reader = new MemReader(stream))
            {
                //Trace.texWorker -= Profiling.Micros;
                Type t = DeserializeHeader(reader);

                if (t != typeof(Texture2D) && t != typeof(Image))
                    throw new IOException("Asset " + asset.fullName + " should be Texture2D or Image");

                string name = reader.ReadString();
                bool linear = reader.ReadBoolean();
                int count = reader.ReadInt32();
                Image image = new Image(reader.ReadBytes(count));
                byte[] pix = image.GetAllPixels();
                //Trace.texBytes += bytes.Length;
                //Trace.texPixels += pix.Length;

                to = new TextObj { name = name, pixels = pix, width = image.width, height = image.height,
                                   format = image.format, mipmap = image.mipmapCount > 1, linear = linear };

                // image.Clear(); TODO test
                image = null;
                //Trace.texWorker += Profiling.Micros;
            }

            lock (mutex)
            {
                data[asset.checksum] = to;
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
            //int count, removedMillis;

            lock (mutex)
            {
                data.TryGetValue(asset.checksum, out obj);
                //removed.TryGetValue(asset.checksum, out removedMillis);
                //count = data.Count;
            }

            byte[] bytes = obj as byte[];

            if (bytes != null)
                return new MemStream(bytes, 0);

            //if (removedMillis > 0)
            //{
            //    Trace.Seq("MISS BYTES:", asset.fullName, asset.package.packagePath, " Assets", count, " Removed at", removedMillis);
            //    Trace.Pr("MISS BYTES:", asset.fullName, asset.package.packagePath, " Assets", count, " Removed at", removedMillis);
            //}

            return asset.GetStream();
        }

        internal Mesh GetMesh(string checksum, Package package, bool isMain, int ind)
        {
            Mesh mesh;
            object obj;
            //int count, removedMillis;

            lock (mutex)
            {
                if (meshes.TryGetValue(checksum, out mesh))
                {
                    //Trace.Ind(ind, "Mesh (hit)", checksum);
                    meshit++;
                    return mesh;
                }

                data.TryGetValue(checksum, out obj);
                //removed.TryGetValue(checksum, out removedMillis);
                //count = data.Count;
            }

            MeshObj mo = obj as MeshObj;
            byte[] bytes;

            if (mo != null)
            {
                // Trace.meshMain -= Profiling.Micros;
                mesh = new Mesh();
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

                // Trace.meshMain += Profiling.Micros;
                // Trace.Ind(ind, "Mesh (obj)", checksum, mesh.name + ", " + mesh.vertexCount + ", " + mesh.triangles.Length);
                mespre++;
            }
            else if ((bytes = obj as byte[]) != null)
            {
                //if (removedMillis > 0)
                //{
                //    Trace.Seq("MISS MESH OBJ BUT GOT BYTES:  Assets", count, checksum, " Removed at", removedMillis);
                //    Trace.Pr("MISS MESH OBJ BUT GOT BYTES:  Assets", count, checksum, " Removed at", removedMillis);
                //}

                mesh = AssetDeserializer.Instantiate(package, bytes, checksum, isMain, ind) as Mesh;
                mespre++;
            }
            else
            {
                Package.Asset asset = package.FindByChecksum(checksum);

                //if (removedMillis > 0)
                //{
                //    Trace.Seq("MISS MESH OBJ AND BYTES:", asset.fullName, asset.package.packagePath, " Assets", count, " Removed at", removedMillis);
                //    Trace.Pr("MISS MESH OBJ AND BYTES:", asset.fullName, asset.package.packagePath, " Assets", count, " Removed at", removedMillis);
                //}

                mesh = AssetDeserializer.Instantiate(asset, isMain, ind) as Mesh;
                mesload++;
            }

            if (shareMeshes)
                lock (mutex)
                {
                    meshes[checksum] = mesh;
                    data.Remove(checksum);
                }

            return mesh;
        }

        internal Texture2D GetTexture(string checksum, Package package, bool isMain, int ind)
        {
            Texture2D texture2D;
            object obj;
            // int count, removedMillis;

            lock (mutex)
            {
                if (isMain && texturesMain.TryGetValue(checksum, out texture2D))
                {
                    // Trace.Ind(ind, "Texture (hit)", checksum);
                    texhit++;
                    return texture2D;
                }
                else if (!isMain && texturesLod.TryGetValue(checksum, out texture2D))
                {
                    // Trace.Ind(ind, "Texture (copy)", checksum);
                    texpre++;
                    return UnityEngine.Object.Instantiate(texture2D);
                }

                data.TryGetValue(checksum, out obj);
                maxCount = Mathf.Max(data.Count, maxCount);
                //removed.TryGetValue(checksum, out removedMillis);
                //count = data.Count;
            }

            TextObj to = obj as TextObj;
            byte[] bytes;

            if (to != null)
            {
                //Trace.texMain -= Profiling.Micros;
                texture2D = new Texture2D(to.width, to.height, to.format, to.mipmap, to.linear);
                texture2D.LoadRawTextureData(to.pixels);
                texture2D.Apply();
                texture2D.name = to.name;
                //Trace.texMain += Profiling.Micros;
                //Trace.Ind(ind, "Texture (obj)", checksum, texture2D.name, texture2D.width, "x", texture2D.height);
                texpre++;
            }
            else if ((bytes = obj as byte[]) != null)
            {
                //if (removedMillis > 0)
                //{
                //    Trace.Seq("MISS TEXT OBJ BUT GOT BYTES:  Assets", count, checksum, " Removed at", removedMillis);
                //    Trace.Pr("MISS TEXT OBJ BUT GOT BYTES:  Assets", count, checksum, " Removed at", removedMillis);
                //}

                texture2D = AssetDeserializer.Instantiate(package, bytes, checksum, isMain, ind) as Texture2D;
                texpre++;
            }
            else
            {
                Package.Asset asset = package.FindByChecksum(checksum);

                //if (removedMillis > 0)
                //{
                //    Trace.Seq("MISS TEXT OBJ AND BYTES:", asset.fullName, asset.package.packagePath, " Assets", count, " Removed at", removedMillis);
                //    Trace.Pr("MISS TEXT OBJ AND BYTES:", asset.fullName, asset.package.packagePath, " Assets", count, " Removed at", removedMillis);
                //}

                texture2D = AssetDeserializer.Instantiate(asset, isMain, ind) as Texture2D;
                texload++;
            }

            if (shareTextures)
                lock (mutex)
                {
                    if (isMain)
                        texturesMain[checksum] = texture2D;
                    else
                        texturesLod[checksum] = texture2D;

                    data.Remove(checksum);
                }

            return texture2D;
        }

        internal Material GetMaterial(string checksum, Package package, bool isMain, int ind)
        {
            MaterialData mat;
            object obj;
            //int count, removedMillis;

            lock (mutex)
            {
                if (isMain && materialsMain.TryGetValue(checksum, out mat))
                {
                    //Trace.Ind(ind, "Material (hit)", checksum);
                    mathit++;
                    texhit += mat.textureCount;
                    return mat.material;
                }
                else if (!isMain && materialsLod.TryGetValue(checksum, out mat))
                {
                    //Trace.Ind(ind, "Material (copy)", checksum);
                    matpre++;
                    return new Material(mat.material);
                    // return mat.material; TODO test
                }

                data.TryGetValue(checksum, out obj);
                //removed.TryGetValue(checksum, out removedMillis);
                //count = data.Count;
            }

            byte[] bytes = obj as byte[];

            if (bytes != null)
            {
                mat = (MaterialData) AssetDeserializer.Instantiate(package, bytes, checksum, isMain, ind);
                matpre++;
            }
            else
            {
                Package.Asset asset = package.FindByChecksum(checksum);

                //if (removedMillis > 0)
                //{
                //    Trace.Seq("MISS MATERIAL BYTES:", asset.fullName, asset.package.packagePath, " Assets", count, " Removed at", removedMillis);
                //    Trace.Pr("MISS MATERIAL BYTES:", asset.fullName, asset.package.packagePath, " Assets", count, " Removed at", removedMillis);
                //}

                mat = (MaterialData) AssetDeserializer.Instantiate(asset, isMain, ind);
                matload++;
            }

            if (shareMaterials)
                lock (mutex)
                {
                    if (isMain)
                        materialsMain[checksum] = mat;
                    else
                        materialsLod[checksum] = mat;

                    data.Remove(checksum);
                }

            return mat.material;
        }

        internal PackageReader GetReader(Stream stream)
        {
            MemStream ms = stream as MemStream;
            return ms != null ? new MemReader(ms) : new PackageReader(stream);
        }

        int texhit, texpre, texload, mathit, matpre, matload, meshit, mespre, mesload;
        Dictionary<string, Texture2D> texturesMain = new Dictionary<string, Texture2D>(128);
        Dictionary<string, Texture2D> texturesLod = new Dictionary<string, Texture2D>(128);
        Dictionary<string, MaterialData> materialsMain = new Dictionary<string, MaterialData>(64);
        Dictionary<string, MaterialData> materialsLod = new Dictionary<string, MaterialData>(64);
        Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>(128);
        bool shareTextures, shareMaterials, shareMeshes;

        internal Sharing()
        {
            instance = this;
            //AssetDeserializer.INDEX = 0;
        }

        internal void Dispose()
        {
            Util.DebugPrint("Textures / Materials / Meshes shared:", texhit, "/", mathit, "/", meshit, "pre-loaded:", texpre, "/", matpre, "/", mespre,
                            "loaded:", texload, "/", matload, "/", mesload);

            Util.DebugPrint("Max cache", maxCount);

            lock (mutex)
            {
                data.Clear(); //removed.Clear();
                data = null; //removed = null;
                loadWorkerThread = null; mtWorkerThread = null;
                texturesMain.Clear(); texturesLod.Clear(); materialsMain.Clear(); materialsLod.Clear();  meshes.Clear();
                texturesMain = null; texturesLod = null; materialsMain = null; materialsLod = null; meshes = null; instance = null;
            }
        }

        internal void Start(Package.Asset[] queue)
        {
            assetsQueue = queue;
            shareTextures = Settings.settings.shareTextures;
            shareMaterials = Settings.settings.shareMaterials;
            shareMeshes = Settings.settings.shareMeshes;
            //AssetDeserializer.INDEX = 0;

            (loadWorkerThread = new Thread(LoadWorker)).Start();
            (mtWorkerThread = new Thread(MTWorker)).Start();
        }
    }

    sealed class MeshObj
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

    sealed class TextObj
    {
        internal string name;
        internal byte[] pixels;
        internal int width;
        internal int height;
        internal TextureFormat format;
        internal bool mipmap;
        internal bool linear;
    }

    // Critical fixes for loading performance.
    internal sealed class Fixes : DetourUtility
    {
        public static Fixes instance;

        // Delegates can be used to call non-public methods. Delegates have about the same performance as regular method calls.
        static readonly Action<Image> Dispoze;

        static Fixes()
        {
            Dispoze = Util.CreateAction<Image>("Dispose");
        }

        internal Fixes()
        {
            instance = this;
            init(typeof(Image), "Finalize", "Fnalize");
            init(typeof(BuildConfig), "ResolveCustomAssetName", typeof(CustomDeserializer), "ResolveCustomAssetName");
            init(typeof(PackageReader), "ReadByteArray", typeof(MemReader), "DreadByteArray");
        }

        internal override void Dispose()
        {
            Revert();
            base.Dispose();
            instance = null;
        }

        static void Fnalize(Image image)
        {
            Dispoze(image);
            //Trace.imgFinalizes++;
        }
    }
}
