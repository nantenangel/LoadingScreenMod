using System;
using System.Collections.Generic;
using System.IO;
using ColossalFramework.Packaging;
using UnityEngine;

namespace LoadingScreenMod
{
    internal sealed class Sharing : DetourUtility
    {
        internal static Sharing instance;

        // Asset checksum to bytes.
        Dictionary<string, byte[]> assets = new Dictionary<string, byte[]>();
        internal static bool Supports(Package.AssetType type) => type <= Package.UnityTypeEnd && type >= Package.UnityTypeStart;

        internal void LoadPackage(Package package)
        {
            List<Package.Asset> list = new List<Package.Asset>(32);
            int i = 0;

            foreach (Package.Asset asset in package)
            {
                string name = asset.name;
                i++;

                if (i == 1 && name.EndsWith("_SteamPreview") || i == 2 && name.EndsWith("_Snapshot"))
                    continue;

                if (Supports(asset.type) && !assets.ContainsKey(asset.checksum))
                    list.Add(asset);
            }

            list.Sort((a, b) => (int) (a.offset - b.offset));

            using (FileStream fs = File.OpenRead(package.packagePath))
                for (i = 0; i < list.Count; i++)
                    assets[list[i].checksum] = LoadAsset(fs, list[i]);
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
            byte[] bytes;

            if (assets.TryGetValue(asset.checksum, out bytes))
                return new MemStream(bytes, 0);

            Trace.Pr("NOT IN MEMORY:", asset.fullName, asset.package.packagePath);
            return asset.GetStream();
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
            assets.Clear(); textures.Clear(); materials.Clear(); meshes.Clear();
            assets = null; textures = null; materials = null; meshes = null; instance = null;
        }

        internal void Start()
        {
            shareTextures = Settings.settings.shareTextures;
            shareMaterials = Settings.settings.shareMaterials;
            shareMeshes = Settings.settings.shareMeshes;
            isMain = false;
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
