using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ColossalFramework.Packaging;
using UnityEngine;

namespace LoadingScreenMod
{
    internal sealed class Sharing : DetourUtility
    {
        internal static Sharing instance;

        // Delegates can be used to call non-public methods. Delegates have about the same performance as regular method calls.
        // Delegates are roughly 100 times faster than reflection in Unity 5.
        readonly Action<Package, GameObject, PackageReader> DeserializeComponent =
            Util.CreateStaticAction<Package, GameObject, PackageReader>(typeof(PackageDeserializer), "DeserializeComponent");

        int texhit, mathit, meshit, texload, matload, mesload, textureCount;
        Dictionary<string, Texture> textures = new Dictionary<string, Texture>();
        Dictionary<string, MaterialData> materials = new Dictionary<string, MaterialData>();
        Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
        bool shareTextures, shareMaterials, shareMeshes, isMain;
        static string indent = string.Empty;
        static StreamWriter w;

        internal Sharing()
        {
            instance = this;
            init(typeof(PackageDeserializer), "DeserializeGameObject");
            init(typeof(PackageDeserializer), "DeserializeMonoBehaviour");

            // Be quick, or the JIT Compiler will inline calls to this one. It is a small method, less than 32 IL bytes.
            init(typeof(PackageDeserializer), "DeserializeMeshFilter");
            init(typeof(PackageDeserializer), "DeserializeMaterial");
            init(typeof(PackageDeserializer), "DeserializeMeshRenderer");
            w = new StreamWriter(Util.GetFileName("Objects", "txt"));
        }

        internal override void Dispose()
        {
            Util.DebugPrint("Textures / Materials / Meshes loaded:", texload, "/", matload, "/", mesload, "referenced:", texhit, "/", mathit, "/", meshit);
            Revert();
            base.Dispose();
            textures.Clear(); materials.Clear(); meshes.Clear();
            instance = null; textures = null; materials = null; meshes = null;
            w.Dispose();
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

            string ind = Sharing.indent;
            Sharing.w.WriteLine(ind + "GO: " + gameObject.name + ", " + num + (Sharing.instance.isMain ? "\t\tMAIN" : String.Empty));
            Sharing.indent += "  ";

            for (int i = 0; i < num; i++)
                Sharing.instance.DeserializeComponent(package, gameObject, reader);

            Sharing.indent = ind;
            return gameObject;
        }

        internal static void DeserializeMonoBehaviour(Package package, MonoBehaviour behaviour, PackageReader reader)
        {
            int num = reader.ReadInt32();
            string ind = Sharing.indent;
            Sharing.w.WriteLine(ind + behaviour.GetType().Name + ": " + num);
            Sharing.indent += "  ";

            for (int i = 0; i < num; i++)
            {
                Type type;
                string name;

                if (DeserializeHeader(out type, out name, reader))
                {
                    FieldInfo field = behaviour.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    Type expectedType = (field != null) ? field.FieldType : null;

                    if (type.IsArray)
                    {
                        int num2 = reader.ReadInt32();
                        Array array = Array.CreateInstance(type.GetElementType(), num2);

                        for (int j = 0; j < num2; j++)
                            array.SetValue(Util.InvokeStatic(typeof(PackageDeserializer), "DeserializeSingleObject", package, type.GetElementType(), reader, expectedType), j);

                        if (field != null)
                            field.SetValue(behaviour, array);
                    }
                    else
                    {
                        object value = Util.InvokeStatic(typeof(PackageDeserializer), "DeserializeSingleObject", package, type, reader, expectedType);

                        if (field != null)
                            field.SetValue(behaviour, value);
                    }
                }
            }

            Sharing.indent = ind;
        }

        internal static UnityEngine.Object DeserializeMaterial(Package package, PackageReader reader)
        {
            string materialName = reader.ReadString();
            string shaderName = reader.ReadString();
            Material material = new Material(Shader.Find(shaderName));
            material.name = materialName;
            int numProperties = reader.ReadInt32();

            string ind = Sharing.indent;
            Sharing.w.WriteLine(ind + "Material: " + material.name + ", " + material.shader.name);
            Sharing.indent += "  ";
            bool share = Sharing.instance.shareTextures && Sharing.instance.isMain;

            for (int i = 0; i < numProperties; i++)
            {
                int kind = reader.ReadInt32();

                if (kind == 0)
                {
                    string s = reader.ReadString();
                    material.SetColor(s, reader.ReadColor());
                    Sharing.w.WriteLine(Sharing.indent + "Color " + s);
                }
                else if (kind == 1)
                {
                    string s = reader.ReadString();
                    material.SetVector(s, reader.ReadVector4());
                    Sharing.w.WriteLine(Sharing.indent + "Vector " + s);
                }
                else if (kind == 2)
                {
                    string s = reader.ReadString();
                    float f = reader.ReadSingle();
                    material.SetFloat(s, f);
                    Sharing.w.WriteLine(Sharing.indent + "Float " + s + ": " + f);
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

                        Sharing.w.WriteLine(Sharing.indent + texture.GetType().Name + " " + propertyName + ": " + texture.name + " " + texture.width + " x " + texture.height);
                        material.SetTexture(propertyName, texture);
                        Sharing.instance.textureCount++;
                    }
                    else
                    {
                        Sharing.w.WriteLine(Sharing.indent + "Texture " + propertyName + ": null");
                        material.SetTexture(propertyName, null);
                    }
                }
            }

            Sharing.indent = ind;
            return material;
        }

        internal static void DeserializeMeshRenderer(Package package, MeshRenderer renderer, PackageReader reader)
        {
            int count = reader.ReadInt32();
            Material[] materials = new Material[count];

            string ind = Sharing.indent;
            Sharing.w.WriteLine(ind + "Shared materials:");
            Sharing.indent += "  ";
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

            Sharing.w.WriteLine(ind + "Materials count was " + materials.Length);
            Sharing.indent = ind;
        }

        internal static void DeserializeMeshFilter(Package package, MeshFilter meshFilter, PackageReader reader)
        {
            bool share = Sharing.instance.shareMeshes;
            string checksum = reader.ReadString();
            Mesh mesh;

            string ind = Sharing.indent;
            Sharing.indent += "  ";

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

            Sharing.w.WriteLine(ind + "Mesh: " + mesh.name + ", " + mesh.vertexCount + ", " + mesh.subMeshCount);
            Sharing.indent = ind;
        }

        static bool DeserializeHeader(out Type type, out string name, PackageReader reader)
        {
            type = null;
            name = null;

            if (reader.ReadBoolean())
                return false;

            string text = reader.ReadString();
            type = Type.GetType(text);
            name = reader.ReadString();

            if (type == null)
            {
                type = Type.GetType((string) Util.InvokeStatic(typeof(PackageDeserializer), "ResolveLegacyType", text));

                if (type == null)
                {
                    if (HandleUnknownType(text, reader) < 0)
                        throw new InvalidDataException("Unknown type to deserialize " + text);

                    return false;
                }
            }

            return true;
        }

        static int HandleUnknownType(string type, PackageReader reader)
        {
            int num = (int) Util.InvokeStatic(typeof(PackageDeserializer), "HandleUnknownType", type);

            if (num > 0)
            {
                reader.ReadBytes(num);
                return num;
            }

            return -1;
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
