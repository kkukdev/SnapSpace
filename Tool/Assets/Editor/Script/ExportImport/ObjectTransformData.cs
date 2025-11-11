using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ObjDropWatcher.ExportImport
{
    /// <summary>
    /// OBJ 파일 경로를 찾는 헬퍼 클래스
    /// </summary>
    public static class ObjPathFinder
    {
        private static List<string> _searchPaths = new List<string>();

        /// <summary>
        /// 검색 경로 목록을 설정합니다.
        /// </summary>
        public static void SetSearchPaths(List<string> paths)
        {
            _searchPaths = paths ?? new List<string>();
        }

        /// <summary>
        /// GameObject에서 실제 OBJ 파일 경로를 찾습니다.
        /// </summary>
        public static string FindObjPath(GameObject obj)
        {
            if (obj == null) return null;

            // 1. GameObject 이름에서 확장자 제거하여 파일명 추출
            string objName = obj.name;
            string fileName = objName;
            
            // 확장자가 있으면 제거
            if (fileName.EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
            {
                fileName = fileName.Substring(0, fileName.Length - 4);
            }

            // 2. MeshFilter의 메시 이름에서도 시도
            var meshFilter = obj.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                string meshName = meshFilter.sharedMesh.name;
                // 메시 이름이 파일명과 유사하면 사용
                if (!string.IsNullOrEmpty(meshName) && meshName.Contains(fileName))
                {
                    fileName = meshName;
                    if (fileName.EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
                    {
                        fileName = fileName.Substring(0, fileName.Length - 4);
                    }
                }
            }

            // 3. 검색 경로 목록에서 찾기
            foreach (var searchPath in _searchPaths)
            {
                if (string.IsNullOrEmpty(searchPath) || !Directory.Exists(searchPath))
                    continue;

                try
                {
                    // 정확한 파일명으로 찾기
                    string exactPath = Path.Combine(searchPath, fileName + ".obj");
                    if (File.Exists(exactPath))
                    {
                        return exactPath;
                    }

                    // 하위 디렉토리에서 찾기
                    var found = Directory.GetFiles(searchPath, fileName + ".obj", SearchOption.AllDirectories);
                    if (found.Length > 0)
                    {
                        return found[0];
                    }

                    // 대소문자 구분 없이 찾기
                    var allFiles = Directory.GetFiles(searchPath, "*.obj", SearchOption.AllDirectories);
                    foreach (var file in allFiles)
                    {
                        string fileBaseName = Path.GetFileNameWithoutExtension(file);
                        if (string.Equals(fileBaseName, fileName, StringComparison.OrdinalIgnoreCase))
                        {
                            return file;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ObjPathFinder] Error searching in {searchPath}: {ex.Message}");
                }
            }

            // 4. 일반적인 storage 경로에서 찾기
            string[] commonPaths = {
                Path.Combine(Application.dataPath, "..", "storage", "uploads"),
                Path.Combine(Application.dataPath, "..", "storage", "outputs"),
                Path.Combine(Application.dataPath, "..", "storage", "temp")
            };

            foreach (var commonPath in commonPaths)
            {
                if (!Directory.Exists(commonPath))
                    continue;

                try
                {
                    var found = Directory.GetFiles(commonPath, fileName + ".obj", SearchOption.AllDirectories);
                    if (found.Length > 0)
                    {
                        return found[0];
                    }
                }
                catch
                {
                    // 무시
                }
            }

            return null;
        }

        /// <summary>
        /// Import 시 OBJ 파일 경로를 찾습니다.
        /// </summary>
        public static string FindObjPathForImport(string objectName, string savedPath = null)
        {
            // 1. 저장된 경로가 있고 파일이 존재하면 사용
            if (!string.IsNullOrEmpty(savedPath) && File.Exists(savedPath))
            {
                return savedPath;
            }

            // 2. 저장된 경로의 디렉토리에서 찾기
            if (!string.IsNullOrEmpty(savedPath))
            {
                string dir = Path.GetDirectoryName(savedPath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    string fileName = Path.GetFileNameWithoutExtension(objectName);
                    if (fileName.EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
                    {
                        fileName = fileName.Substring(0, fileName.Length - 4);
                    }

                    try
                    {
                        var found = Directory.GetFiles(dir, fileName + ".obj", SearchOption.TopDirectoryOnly);
                        if (found.Length > 0)
                        {
                            return found[0];
                        }
                    }
                    catch
                    {
                        // 무시
                    }
                }
            }

            // 3. 객체 이름으로 검색
            string searchName = objectName;
            if (searchName.EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
            {
                searchName = searchName.Substring(0, searchName.Length - 4);
            }

            // 검색 경로 목록에서 찾기
            foreach (var searchPath in _searchPaths)
            {
                if (string.IsNullOrEmpty(searchPath) || !Directory.Exists(searchPath))
                    continue;

                try
                {
                    var found = Directory.GetFiles(searchPath, searchName + ".obj", SearchOption.AllDirectories);
                    if (found.Length > 0)
                    {
                        return found[0];
                    }
                }
                catch
                {
                    // 무시
                }
            }

            return null;
        }
    }
    /// <summary>
    /// Vector3를 직렬화 가능한 형태로 변환하는 헬퍼 구조체
    /// </summary>
    [Serializable]
    public struct SerializableVector3
    {
        public float x, y, z;

        public SerializableVector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static implicit operator Vector3(SerializableVector3 v)
        {
            return new Vector3(v.x, v.y, v.z);
        }

        public static implicit operator SerializableVector3(Vector3 v)
        {
            return new SerializableVector3(v.x, v.y, v.z);
        }
    }

    /// <summary>
    /// 오브젝트 타입을 나타내는 enum
    /// </summary>
    public enum ObjectType
    {
        Unknown,       // 알 수 없음
        ObjFile,       // OBJ 파일에서 로드된 오브젝트
        Primitive,     // Unity 기본 오브젝트 (Plane, Cube, Sphere 등)
        Empty          // 빈 GameObject
    }

    /// <summary>
    /// 오브젝트의 Transform 정보를 저장하는 데이터 클래스
    /// </summary>
    [Serializable]
    public class ObjectTransformData
    {
        public string objectName;
        public Vector3 position;
        public Vector3 rotation; // Euler angles
        public Vector3 scale;
        public string objFilePath; // 원본 OBJ 파일 경로 (선택적)
        public ObjectType objectType = ObjectType.Unknown; // 오브젝트 타입
        public string primitiveType; // Unity 기본 오브젝트 타입 (Plane, Cube, Sphere 등)

        public ObjectTransformData() { }

        public ObjectTransformData(GameObject obj, string objPath = null)
        {
            objectName = obj.name;
            position = obj.transform.position;
            rotation = obj.transform.eulerAngles;
            scale = obj.transform.localScale;
            objFilePath = objPath;
            
            // 오브젝트 타입 감지
            DetectObjectType(obj);
        }

        /// <summary>
        /// GameObject의 타입을 감지합니다.
        /// </summary>
        void DetectObjectType(GameObject obj)
        {
            if (obj == null)
            {
                objectType = ObjectType.Unknown;
                return;
            }

            // MeshFilter가 있는지 확인
            var meshFilter = obj.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                string meshName = meshFilter.sharedMesh.name;
                
                // Unity 기본 메시인지 확인
                if (IsUnityPrimitiveMesh(meshName))
                {
                    objectType = ObjectType.Primitive;
                    primitiveType = meshName;
                }
                else if (!string.IsNullOrEmpty(objFilePath))
                {
                    // OBJ 파일 경로가 있으면 OBJ 파일에서 로드된 것으로 간주
                    objectType = ObjectType.ObjFile;
                }
                else
                {
                    // 메시는 있지만 타입을 알 수 없음
                    objectType = ObjectType.Unknown;
                }
            }
            else
            {
                // MeshFilter가 없으면 빈 GameObject
                objectType = ObjectType.Empty;
            }
        }

        /// <summary>
        /// Unity 기본 메시인지 확인합니다.
        /// </summary>
        bool IsUnityPrimitiveMesh(string meshName)
        {
            if (string.IsNullOrEmpty(meshName))
                return false;

            // Unity 기본 메시 이름들
            string[] primitiveNames = {
                "Plane", "Cube", "Sphere", "Capsule", "Cylinder", "Quad"
            };

            foreach (var name in primitiveNames)
            {
                if (string.Equals(meshName, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// GameObject를 생성합니다.
        /// </summary>
        public GameObject CreateGameObject()
        {
            GameObject obj = null;

            switch (objectType)
            {
                case ObjectType.Primitive:
                    // Unity 기본 오브젝트 생성
                    obj = CreatePrimitiveObject();
                    break;

                case ObjectType.ObjFile:
                    // OBJ 파일에서 로드
                    if (!string.IsNullOrEmpty(objFilePath) && File.Exists(objFilePath))
                    {
                        try
                        {
                            obj = RuntimeObjLoader.LoadObj(objFilePath);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[ObjectTransformData] Failed to load OBJ: {objFilePath}\n{ex}");
                        }
                    }
                    break;

                case ObjectType.Empty:
                case ObjectType.Unknown:
                default:
                    // 빈 GameObject 생성
                    obj = new GameObject(objectName);
                    break;
            }

            if (obj != null)
            {
                obj.name = objectName;
                
                // Unity 에디터에서 DontSaveInEditor 플래그 제거
                #if UNITY_EDITOR
                obj.hideFlags = HideFlags.None;
                #endif
                
                ApplyToGameObject(obj);
                
                // Unity 에디터에서 Undo 시스템에 등록
                #if UNITY_EDITOR
                Undo.RegisterCreatedObjectUndo(obj, $"Import {objectName}");
                #endif
            }

            return obj;
        }

        /// <summary>
        /// Unity 기본 오브젝트를 생성합니다.
        /// </summary>
        GameObject CreatePrimitiveObject()
        {
            if (string.IsNullOrEmpty(primitiveType))
                return new GameObject(objectName);

            PrimitiveType primitiveTypeEnum;
            
            // 문자열을 PrimitiveType enum으로 변환
            switch (primitiveType.ToLower())
            {
                case "plane":
                    primitiveTypeEnum = PrimitiveType.Plane;
                    break;
                case "cube":
                    primitiveTypeEnum = PrimitiveType.Cube;
                    break;
                case "sphere":
                    primitiveTypeEnum = PrimitiveType.Sphere;
                    break;
                case "capsule":
                    primitiveTypeEnum = PrimitiveType.Capsule;
                    break;
                case "cylinder":
                    primitiveTypeEnum = PrimitiveType.Cylinder;
                    break;
                case "quad":
                    // Quad는 GameObject.CreatePrimitive로 생성할 수 없으므로 수동 생성
                    return CreateQuadObject();
                default:
                    return new GameObject(objectName);
            }

            var primitive = GameObject.CreatePrimitive(primitiveTypeEnum);
            
            // Unity 에디터에서 DontSaveInEditor 플래그 제거
            #if UNITY_EDITOR
            primitive.hideFlags = HideFlags.None;
            
            // MeshFilter와 MeshRenderer의 hideFlags도 설정
            var meshFilter = primitive.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                meshFilter.sharedMesh.hideFlags = HideFlags.None;
            }
            
            var meshRenderer = primitive.GetComponent<MeshRenderer>();
            if (meshRenderer != null && meshRenderer.sharedMaterial != null)
            {
                meshRenderer.sharedMaterial.hideFlags = HideFlags.None;
            }
            #endif
            
            return primitive;
        }

        /// <summary>
        /// Quad 오브젝트를 생성합니다. (Unity는 Quad를 CreatePrimitive로 생성할 수 없음)
        /// </summary>
        GameObject CreateQuadObject()
        {
            var quad = new GameObject(objectName);
            var meshFilter = quad.AddComponent<MeshFilter>();
            var meshRenderer = quad.AddComponent<MeshRenderer>();

            // Unity의 기본 Quad 메시 사용
            var quadMesh = Resources.GetBuiltinResource<Mesh>("Quad");
            if (quadMesh != null)
            {
                meshFilter.sharedMesh = quadMesh;
                #if UNITY_EDITOR
                quadMesh.hideFlags = HideFlags.None;
                #endif
            }
            else
            {
                // Quad 메시를 찾을 수 없으면 수동 생성
                var createdMesh = CreateQuadMesh();
                meshFilter.sharedMesh = createdMesh;
                #if UNITY_EDITOR
                createdMesh.hideFlags = HideFlags.None;
                #endif
            }

            // 기본 머티리얼 할당
            var material = new Material(Shader.Find("Standard"));
            meshRenderer.sharedMaterial = material;
            
            #if UNITY_EDITOR
            material.hideFlags = HideFlags.None;
            quad.hideFlags = HideFlags.None;
            #endif

            return quad;
        }

        /// <summary>
        /// Quad 메시를 수동으로 생성합니다.
        /// </summary>
        Mesh CreateQuadMesh()
        {
            var mesh = new Mesh();
            mesh.name = "Quad";

            // Quad의 정점들 (XY 평면)
            mesh.vertices = new Vector3[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f)
            };

            // UV 좌표
            mesh.uv = new Vector2[]
            {
                new Vector2(0, 0),
                new Vector2(1, 0),
                new Vector2(1, 1),
                new Vector2(0, 1)
            };

            // 삼각형 인덱스
            mesh.triangles = new int[]
            {
                0, 2, 1,
                0, 3, 2
            };

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        public void ApplyToGameObject(GameObject obj)
        {
            if (obj == null) return;
            obj.transform.position = position;
            obj.transform.eulerAngles = rotation;
            obj.transform.localScale = scale;
        }

        /// <summary>
        /// BinaryFormatter용 직렬화 가능한 데이터로 변환
        /// </summary>
        public SerializableObjectTransformData ToSerializable()
        {
            return new SerializableObjectTransformData
            {
                objectName = objectName,
                position = new SerializableVector3(position.x, position.y, position.z),
                rotation = new SerializableVector3(rotation.x, rotation.y, rotation.z),
                scale = new SerializableVector3(scale.x, scale.y, scale.z),
                objFilePath = objFilePath,
                objectType = (int)objectType,
                primitiveType = primitiveType ?? ""
            };
        }

        /// <summary>
        /// 직렬화 가능한 데이터에서 복원
        /// </summary>
        public static ObjectTransformData FromSerializable(SerializableObjectTransformData data)
        {
            return new ObjectTransformData
            {
                objectName = data.objectName,
                position = data.position,
                rotation = data.rotation,
                scale = data.scale,
                objFilePath = data.objFilePath,
                objectType = (ObjectType)data.objectType,
                primitiveType = data.primitiveType ?? ""
            };
        }
    }

    /// <summary>
    /// BinaryFormatter용 직렬화 가능한 Transform 데이터
    /// </summary>
    [Serializable]
    public class SerializableObjectTransformData
    {
        public string objectName;
        public SerializableVector3 position;
        public SerializableVector3 rotation;
        public SerializableVector3 scale;
        public string objFilePath;
        public int objectType; // ObjectType enum을 int로 저장
        public string primitiveType;
    }

    /// <summary>
    /// 여러 오브젝트의 Transform 정보를 저장하는 컨테이너
    /// </summary>
    [Serializable]
    public class ObjectTransformCollection
    {
        public string exportDate;
        public string unityVersion;
        public List<ObjectTransformData> objects = new List<ObjectTransformData>();

        public ObjectTransformCollection()
        {
            exportDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            unityVersion = Application.unityVersion;
        }
    }

    /// <summary>
    /// BinaryFormatter용 직렬화 가능한 컬렉션
    /// </summary>
    [Serializable]
    public class SerializableObjectTransformCollection
    {
        public string exportDate;
        public string unityVersion;
        public List<SerializableObjectTransformData> objects = new List<SerializableObjectTransformData>();

        public SerializableObjectTransformCollection()
        {
            exportDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            unityVersion = Application.unityVersion;
        }

        public ObjectTransformCollection ToObjectTransformCollection()
        {
            var collection = new ObjectTransformCollection
            {
                exportDate = exportDate,
                unityVersion = unityVersion
            };

            foreach (var data in objects)
            {
                collection.objects.Add(ObjectTransformData.FromSerializable(data));
            }

            return collection;
        }

        public static SerializableObjectTransformCollection FromObjectTransformCollection(ObjectTransformCollection collection)
        {
            var serializable = new SerializableObjectTransformCollection
            {
                exportDate = collection.exportDate,
                unityVersion = collection.unityVersion
            };

            foreach (var data in collection.objects)
            {
                serializable.objects.Add(data.ToSerializable());
            }

            return serializable;
        }
    }
}

