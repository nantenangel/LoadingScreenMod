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
        readonly Package.Asset asset;
        readonly PackageReader reader;

        public static T Instantiate<T>(Package.Asset asset) where T : class
        {
            return Instantiate(asset) as T;
        }

        public static object Instantiate(Package.Asset asset)
        {
            if ((asset.type >= Package.UnityTypeStart && asset.type <= Package.UnityTypeEnd) || asset.type >= Package.AssetType.User)
            {
                using (Stream stream = asset.GetStream())
                using (PackageReader reader = new PackageReader(stream))
                {
                    AssetDeserializer d = new AssetDeserializer(asset, reader);
                    return d.Deserialize();
                }
            }
            else
                return asset.Instantiate();
        }

        AssetDeserializer(Package.Asset asset, PackageReader reader)
        {
            this.asset = asset;
            this.package = asset.package;
            this.reader = reader;
        }

        object Deserialize()
        {
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
            object obj = PackageHelper.CustomDeserialize(package, type, reader);

            if (obj != null)
                return obj;
            if (typeof(ScriptableObject).IsAssignableFrom(type))
                return Instantiate(reader.ReadAsset());
            if (typeof(GameObject).IsAssignableFrom(type))
                return Instantiate(reader.ReadAsset());
            if (package.version < 3u && expectedType != null && expectedType == typeof(Package.Asset))
                return reader.ReadUnityType(expectedType);

            return reader.ReadUnityType(type);
        }

        UnityEngine.Object DeserializeScriptableObject(Type type)
        {
            object obj = PackageHelper.CustomDeserialize(package, type, reader);

            if (obj != null)
                return (UnityEngine.Object) obj;

            ScriptableObject scriptableObject = ScriptableObject.CreateInstance(type);
            scriptableObject.name = reader.ReadString();
            int num = reader.ReadInt32();

            for (int i = 0; i < num; i++)
            {
                Type type2;
                string name;

                if (DeserializeHeader(out type2, out name))
                {
                    FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    Type expectedType = field?.FieldType;

                    if (type2.IsArray)
                    {
                        int num2 = reader.ReadInt32();
                        Array array = Array.CreateInstance(type2.GetElementType(), num2);

                        for (int j = 0; j < num2; j++)
                            array.SetValue(DeserializeSingleObject(type2.GetElementType(), expectedType), j);

                        field?.SetValue(scriptableObject, array);
                    }
                    else
                    {
                        object value = DeserializeSingleObject(type2, expectedType);
                        field?.SetValue(scriptableObject, value);
                    }
                }
            }

            return scriptableObject;
        }

        UnityEngine.Object DeserializeGameObject()
        {
            string name = reader.ReadString();
            GameObject gameObject = new GameObject(name);
            gameObject.tag = reader.ReadString();
            gameObject.layer = reader.ReadInt32();
            gameObject.SetActive(reader.ReadBoolean());
            int num = reader.ReadInt32();

            for (int i = 0; i < num; i++)
                DeserializeComponent(gameObject);

            return gameObject;
        }

        void DeserializeComponent(GameObject go)
        {
            Type type;

            if (!DeserializeHeader(out type))
                return;
            if (type == typeof(Transform))
            {
                DeserializeTransform(go.transform);
                return;
            }
            if (type == typeof(MeshFilter))
            {
                DeserializeMeshFilter(go.AddComponent(type) as MeshFilter);
                return;
            }
            if (type == typeof(MeshRenderer))
            {
                DeserializeMeshRenderer(go.AddComponent(type) as MeshRenderer);
                return;
            }
            if (typeof(MonoBehaviour).IsAssignableFrom(type))
            {
                DeserializeMonoBehaviour((MonoBehaviour) go.AddComponent(type));
                return;
            }
            throw new InvalidDataException("Unknown type to deserialize " + type.Name);
        }

        UnityEngine.Object DeserializeTexture()
        {
            string name = reader.ReadString();
            bool linear = reader.ReadBoolean();
            int count = reader.ReadInt32();
            byte[] fileByte = reader.ReadBytes(count);
            Image image = new Image(fileByte);
            Texture2D texture2D = image.CreateTexture(linear);
            texture2D.name = name;
            return texture2D;
        }

        UnityEngine.Object DeserializeMaterial()
        {
            string name = reader.ReadString();
            string name2 = reader.ReadString();
            Material material = new Material(Shader.Find(name2));
            material.name = name;
            int num = reader.ReadInt32();

            for (int i = 0; i < num; i++)
            {
                int num2 = reader.ReadInt32();
                if (num2 == 0)
                {
                    material.SetColor(reader.ReadString(), reader.ReadColor());
                }
                else if (num2 == 1)
                {
                    material.SetVector(reader.ReadString(), reader.ReadVector4());
                }
                else if (num2 == 2)
                {
                    material.SetFloat(reader.ReadString(), reader.ReadSingle());
                }
                else if (num2 == 3)
                {
                    string propertyName = reader.ReadString();
                    if (!reader.ReadBoolean())
                    {
                        material.SetTexture(propertyName, Instantiate<Texture>(reader.ReadAsset()));
                    }
                    else
                    {
                        material.SetTexture(propertyName, null);
                    }
                }
            }
            return material;
        }

        void DeserializeTransform(Transform transform)
        {
            transform.localPosition = reader.ReadVector3();
            transform.localRotation = reader.ReadQuaternion();
            transform.localScale = reader.ReadVector3();
        }

        void DeserializeMeshFilter(MeshFilter meshFilter)
        {
            meshFilter.sharedMesh = Instantiate<Mesh>(reader.ReadAsset());
        }

        void DeserializeMonoBehaviour(MonoBehaviour behaviour)
        {
            int num = reader.ReadInt32();

            for (int i = 0; i < num; i++)
            {
                Type type;
                string name;

                if (DeserializeHeader(out type, out name))
                {
                    FieldInfo field = behaviour.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    Type expectedType = field?.FieldType;

                    if (type.IsArray)
                    {
                        int num2 = reader.ReadInt32();
                        Array array = Array.CreateInstance(type.GetElementType(), num2);

                        for (int j = 0; j < num2; j++)
                            array.SetValue(DeserializeSingleObject(type.GetElementType(), expectedType), j);

                        field?.SetValue(behaviour, array);
                    }
                    else
                    {
                        object value = DeserializeSingleObject(type, expectedType);
                        field?.SetValue(behaviour, value);
                    }
                }
            }
        }

        object DeserializeObject(Type type)
        {
            object obj = PackageHelper.CustomDeserialize(package, type, reader);

            if (obj != null)
                return obj;

            object obj2 = Activator.CreateInstance(type);
            reader.ReadString();
            int num = reader.ReadInt32();

            for (int i = 0; i < num; i++)
            {
                Type type2;
                string text;

                if (DeserializeHeader(out type2, out text))
                {
                    FieldInfo field = type.GetField(text, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    if (field == null)
                    {
                        text = ResolveLegacyMember(type2, type, text);
                        field = type.GetField(text, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    }

                    Type expectedType = field?.FieldType;

                    if (type2.IsArray)
                    {
                        int num2 = reader.ReadInt32();
                        Array array = Array.CreateInstance(type2.GetElementType(), num2);

                        for (int j = 0; j < num2; j++)
                            array.SetValue(DeserializeSingleObject(type2.GetElementType(), expectedType), j);

                        field?.SetValue(obj2, array);
                    }
                    else
                    {
                        object value = DeserializeSingleObject(type2, expectedType);
                        field?.SetValue(obj2, value);
                    }
                }
            }
            return obj2;
        }

        void DeserializeMeshRenderer(MeshRenderer renderer)
        {
            int num = reader.ReadInt32();
            Material[] array = new Material[num];

            for (int i = 0; i < num; i++)
                array[i] = Instantiate<Material>(reader.ReadAsset());

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

            return mesh;
        }

        bool DeserializeHeader(out Type type)
        {
            type = null;
            if (reader.ReadBoolean())
            {
                return false;
            }
            string text = reader.ReadString();
            type = Type.GetType(text);
            if (type == null)
            {
                type = Type.GetType(ResolveLegacyType(text));
                if (type == null)
                {
                    if (HandleUnknownType(text) < 0)
                    {
                        throw new InvalidDataException("Unknown type to deserialize " + text);
                    }
                    return false;
                }
            }
            return true;
        }

        bool DeserializeHeader(out Type type, out string name)
        {
            type = null;
            name = null;
            if (reader.ReadBoolean())
            {
                return false;
            }
            string text = reader.ReadString();
            type = Type.GetType(text);
            name = reader.ReadString();
            if (type == null)
            {
                type = Type.GetType(ResolveLegacyType(text));
                if (type == null)
                {
                    if (HandleUnknownType(text) < 0)
                    {
                        throw new InvalidDataException("Unknown type to deserialize " + text);
                    }
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
}
