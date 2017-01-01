using ColossalFramework.Importers;
using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using ColossalFramework.Packaging;
using ColossalFramework;

namespace LoadingScreenMod
{
    internal sealed class AssetDeserializer
    {
        readonly Package package;
        readonly PackageReader reader;
        string checksum;
        bool isMain;
        int ind;

        public static object Instantiate(Package.Asset asset, bool isMain = true, int ind = 0)
        {
            using (Stream stream = Sharing.instance.GetStream(asset))
            using (PackageReader reader = Sharing.instance.GetReader(stream))
            {
                Trace.Ind(ind, "ASSET", ind == 0 ? Tester.instance.index + ":" : string.Empty, asset.fullName);
                return new AssetDeserializer(asset.package, reader, asset.checksum, isMain, ind).Deserialize();
            }
        }

        internal static object Instantiate(Package package, byte[] bytes, string checksum, bool isMain, int ind)
        {
            using (MemStream stream = new MemStream(bytes, 0))
            using (PackageReader reader = new MemReader(stream))
            {
                Trace.Ind(ind, "ASSET", ind == 0 ? Tester.instance.index + ":" : string.Empty, package.packageName);
                return new AssetDeserializer(package, reader, checksum, isMain, ind).Deserialize();
            }
        }

        internal AssetDeserializer(Package package, PackageReader reader, string checksum, bool isMain, int ind)
        {
            this.package = package;
            this.reader = reader;
            this.checksum = checksum;
            this.isMain = isMain;
            this.ind = ind;
        }

        internal object Deserialize()
        {
            ind++;
            Type type;

            if (!DeserializeHeader(out type))
                return null;

            if (type == typeof(GameObject))
                return DeserializeGameObject();
            if (type == typeof(Mesh))
                return DeserializeMesh();
            if (type == typeof(Material))
                return DeserializeMaterial();
            if (type == typeof(Texture2D) || type == typeof(Image))
                return DeserializeTexture();
            if (typeof(ScriptableObject).IsAssignableFrom(type))
                return DeserializeScriptableObject(type);

            return DeserializeObject(type);
        }

        object DeserializeSingleObject(Type type, Type expectedType)
        {
            Trace.customDeserialize -= Profiling.Micros;
            object obj = CustomDeserializer.CustomDeserialize(package, type, reader);
            Trace.customDeserialize += Profiling.Micros;

            if (obj != null)
                return obj;
            if (typeof(ScriptableObject).IsAssignableFrom(type))
                return Instantiate(FindAsset(reader.ReadString()), isMain, ind);
            if (typeof(GameObject).IsAssignableFrom(type))
                return Instantiate(FindAsset(reader.ReadString()), isMain, ind);

            if (package.version < 3u && expectedType != null && expectedType == typeof(Package.Asset))
                return reader.ReadUnityType(expectedType);

            return reader.ReadUnityType(type);
        }

        UnityEngine.Object DeserializeScriptableObject(Type type)
        {
            Trace.customDeserialize -= Profiling.Micros;
            object obj = CustomDeserializer.CustomDeserialize(package, type, reader);
            Trace.customDeserialize += Profiling.Micros;

            if (obj != null)
                return (UnityEngine.Object) obj;

            ScriptableObject so = ScriptableObject.CreateInstance(type);
            so.name = reader.ReadString();
            Trace.Ind(ind, "SO", so.name);
            DeserializeFields(so, type, false);
            return so;
        }

        void DeserializeFields(object obj, Type type, bool resolveMember)
        {
            int count = reader.ReadInt32();
            ind++;

            for (int i = 0; i < count; i++)
            {
                Type t;
                string name;

                if (DeserializeHeader(out t, out name))
                {
                    FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    if (field == null && resolveMember)
                        field = type.GetField(ResolveLegacyMember(t, type, name), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    Type expectedType = field?.FieldType;
                    object value;

                    if (t.IsArray)
                    {
                        int n = reader.ReadInt32();
                        Type elementType = t.GetElementType();

                        // Make the common case fast, avoid boxing.
                        if (elementType == typeof(float))
                        {
                            float[] array = new float[n]; value = array;

                            for (int j = 0; j < n; j++)
                                array[j] = reader.ReadSingle();
                        }
                        else if (elementType == typeof(Vector2))
                        {
                            Vector2[] array = new Vector2[n]; value = array;

                            for (int j = 0; j < n; j++)
                                array[j] = reader.ReadVector2();
                        }
                        else
                        {
                            Array array = Array.CreateInstance(elementType, n); value = array;

                            for (int j = 0; j < n; j++)
                                array.SetValue(DeserializeSingleObject(elementType, expectedType), j);
                        }
                    }
                    else
                    {
                        // Make the common case fast.
                        if (t == typeof(int))
                            value = reader.ReadInt32();
                        else if (t == typeof(bool))
                            value = reader.ReadBoolean();
                        else if (t == typeof(string))
                            value = reader.ReadString();
                        else if (t == typeof(float))
                            value = reader.ReadSingle();
                        else
                            value = DeserializeSingleObject(t, expectedType);
                    }

                    field?.SetValue(obj, value);
                }
            }

            ind--;
        }

        UnityEngine.Object DeserializeGameObject()
        {
            string name = reader.ReadString();
            GameObject go = new GameObject(name);
            go.tag = reader.ReadString();
            go.layer = reader.ReadInt32();
            go.SetActive(reader.ReadBoolean());
            int count = reader.ReadInt32();
            isMain = count > 3;
            Trace.Ind(ind, "GO", go.name + ", " + count);
            ind++;

            for (int i = 0; i < count; i++)
            {
                Type type;

                if (!DeserializeHeader(out type))
                    continue;

                if (type == typeof(Transform))
                    DeserializeTransform(go.transform);
                else if (type == typeof(MeshFilter))
                    DeserializeMeshFilter(go.AddComponent(type) as MeshFilter);
                else if (type == typeof(MeshRenderer))
                    DeserializeMeshRenderer(go.AddComponent(type) as MeshRenderer);
                else if (typeof(MonoBehaviour).IsAssignableFrom(type))
                    DeserializeMonoBehaviour((MonoBehaviour) go.AddComponent(type));
                else
                    throw new InvalidDataException("Unknown type to deserialize " + type.Name);
            }

            ind--;
            return go;
        }

        UnityEngine.Object DeserializeTexture()
        {
            string name = reader.ReadString();
            bool linear = reader.ReadBoolean();
            int count = reader.ReadInt32();
            Image image = new Image(reader.ReadBytes(count));
            Texture2D texture2D = image.CreateTexture(linear);
            texture2D.name = name;
            Trace.Ind(ind, "Texture", reader.BaseStream is MemStream ? "(pre)" : "(load)", checksum, name, texture2D.width, "x", texture2D.height);
            return texture2D;
        }

        MaterialData DeserializeMaterial()
        {
            string name = reader.ReadString();
            string shader = reader.ReadString();
            Material material = new Material(Shader.Find(shader));
            material.name = name;
            int count = reader.ReadInt32();
            int textureCount = 0;
            Trace.Ind(ind, "Material", reader.BaseStream is MemStream ? "(pre)" : "(load)", checksum, name);
            ind++;

            for (int i = 0; i < count; i++)
            {
                int kind = reader.ReadInt32();

                if (kind == 0)
                    material.SetColor(reader.ReadString(), reader.ReadColor());
                else if (kind == 1)
                    material.SetVector(reader.ReadString(), reader.ReadVector4());
                else if (kind == 2)
                    material.SetFloat(reader.ReadString(), reader.ReadSingle());
                else if (kind == 3)
                {
                    string propertyName = reader.ReadString();

                    if (!reader.ReadBoolean())
                    {
                        string checksum = reader.ReadString();
                        material.SetTexture(propertyName, Sharing.instance.GetTexture(checksum, package, isMain, ind));
                        textureCount++;
                    }
                    else
                        material.SetTexture(propertyName, null);
                }
            }

            ind--;
            return new MaterialData(material, textureCount); ;
        }

        void DeserializeTransform(Transform transform)
        {
            transform.localPosition = reader.ReadVector3();
            transform.localRotation = reader.ReadQuaternion();
            transform.localScale = reader.ReadVector3();
            Trace.Ind(ind, "Transform", transform.name);
        }

        void DeserializeMeshFilter(MeshFilter meshFilter)
        {
            string checksum = reader.ReadString();
            meshFilter.sharedMesh = Sharing.instance.GetMesh(checksum, package, isMain, ind);
        }

        void DeserializeMonoBehaviour(MonoBehaviour behaviour)
        {
            Trace.Ind(ind, behaviour.GetType().Name);
            DeserializeFields(behaviour, behaviour.GetType(), false);
        }

        object DeserializeObject(Type type)
        {
            Trace.Ind(ind, "object", type.Name);
            Trace.customDeserialize -= Profiling.Micros;
            object obj = CustomDeserializer.CustomDeserialize(package, type, reader);
            Trace.customDeserialize += Profiling.Micros;

            if (obj != null)
                return obj;

            obj = Activator.CreateInstance(type);
            reader.ReadString();
            DeserializeFields(obj, type, true);
            return obj;
        }

        void DeserializeMeshRenderer(MeshRenderer renderer)
        {
            int count = reader.ReadInt32();
            Material[] array = new Material[count];

            for (int i = 0; i < count; i++)
                array[i] = Sharing.instance.GetMaterial(reader.ReadString(), package, isMain, ind);

            renderer.sharedMaterials = array;
        }

        UnityEngine.Object DeserializeMesh()
        {
            Mesh mesh = new Mesh();
            mesh.name = reader.ReadString();
            mesh.vertices = reader.ReadVector3Array();
            mesh.colors = reader.ReadColorArray();
            mesh.uv = reader.ReadVector2Array();
            mesh.normals = reader.ReadVector3Array();
            mesh.tangents = reader.ReadVector4Array();
            mesh.boneWeights = reader.ReadBoneWeightsArray();
            mesh.bindposes = reader.ReadMatrix4x4Array();
            mesh.subMeshCount = reader.ReadInt32();

            for (int i = 0; i < mesh.subMeshCount; i++)
                mesh.SetTriangles(reader.ReadInt32Array(), i);

            Trace.Ind(ind, "Mesh", reader.BaseStream is MemStream ? "(pre)" : "(load)", checksum, mesh.name + ", " + mesh.vertexCount + ", " + mesh.triangles.Length);
            return mesh;
        }

        bool DeserializeHeader(out Type type)
        {
            type = null;

            if (reader.ReadBoolean())
                return false;

            string typeName = reader.ReadString();
            type = Type.GetType(typeName);

            if (type == null)
            {
                type = Type.GetType(ResolveLegacyType(typeName));

                if (type == null)
                {
                    if (HandleUnknownType(typeName) < 0)
                        throw new InvalidDataException("Unknown type to deserialize " + typeName);

                    return false;
                }
            }

            return true;
        }

        Package.Asset FindAsset(string checksum) => package.FindByChecksum(checksum);

        bool DeserializeHeader(out Type type, out string name)
        {
            type = null;
            name = null;

            if (reader.ReadBoolean())
                return false;

            string typeName = reader.ReadString();
            type = Type.GetType(typeName);
            name = reader.ReadString();

            if (type == null)
            {
                type = Type.GetType(ResolveLegacyType(typeName));

                if (type == null)
                {
                    if (HandleUnknownType(typeName) < 0)
                        throw new InvalidDataException("Unknown type to deserialize " + typeName);

                    return false;
                }
            }

            return true;
        }

        int HandleUnknownType(string type)
        {
            int num = PackageHelper.UnknownTypeHandler(type);
            CODebugBase<InternalLogChannel>.Warn(InternalLogChannel.Packer, string.Concat("Unexpected type '", type, "' detected. No resolver handled this type. Skipping ", num, " bytes."));

            if (num > 0)
            {
                reader.ReadBytes(num);
                return num;
            }
            return -1;
        }

        static string ResolveLegacyType(string type)
        {
            string text = PackageHelper.ResolveLegacyTypeHandler(type);
            CODebugBase<InternalLogChannel>.Warn(InternalLogChannel.Packer, string.Concat("Unkown type detected. Attempting to resolve from '", type, "' to '", text, "'"));
            return text;
        }

        static string ResolveLegacyMember(Type fieldType, Type classType, string member)
        {
            string text = PackageHelper.ResolveLegacyMemberHandler(classType, member);
            CODebugBase<InternalLogChannel>.Warn(InternalLogChannel.Packer, string.Concat("Unkown member detected of type ", fieldType.FullName, " in ", classType.FullName,
                ". Attempting to resolve from '", member, "' to '", text, "'"));
            return text;
        }
    }

    internal struct MaterialData
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
