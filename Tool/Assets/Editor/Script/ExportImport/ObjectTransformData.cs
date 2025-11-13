using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
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
        /// 루트 GameObject에 경로가 없으면 children에서도 찾습니다.
        /// </summary>
        public static string FindObjPath(GameObject obj)
        {
            if (obj == null) return null;

            // 1. ObjPathInfo Component에서 경로 가져오기 (가장 우선순위가 높음)
            // 먼저 루트 GameObject에서 찾기
            string pathFromComponent = ObjPathInfo.GetPath(obj);
            if (!string.IsNullOrEmpty(pathFromComponent) && System.IO.File.Exists(pathFromComponent))
            {
                Debug.Log($"[ObjPathFinder] Found path from ObjPathInfo component (root): {pathFromComponent}");
                return pathFromComponent;
            }

            // 루트에 경로가 없으면 children에서 찾기
            if (obj.transform.childCount > 0)
            {
                foreach (Transform child in obj.transform)
                {
                    if (child != null && child.gameObject != null)
                    {
                        string childPath = ObjPathInfo.GetPath(child.gameObject);
                        if (!string.IsNullOrEmpty(childPath) && System.IO.File.Exists(childPath))
                        {
                            Debug.Log($"[ObjPathFinder] Found path from ObjPathInfo component (child '{child.name}'): {childPath}");
                            return childPath;
                        }
                    }
                }
            }

            string objName = obj.name;
            List<string> candidateFileNames = new List<string>();

            // 1. GameObject 이름에서 파일명 추출
            string fileName = objName;
            if (fileName.EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
            {
                fileName = fileName.Substring(0, fileName.Length - 4);
            }
            candidateFileNames.Add(fileName);

            // 2. MeshFilter의 메시 이름에서도 시도 (루트에서)
            if (obj.TryGetComponent<MeshFilter>(out var meshFilter) && meshFilter.sharedMesh != null)
            {
                string meshName = meshFilter.sharedMesh.name;
                if (!string.IsNullOrEmpty(meshName))
                {
                    // 메시 이름에서 .obj 확장자 제거
                    string meshFileName = meshName;
                    if (meshFileName.EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
                    {
                        meshFileName = meshFileName.Substring(0, meshFileName.Length - 4);
                    }
                    
                    // 메시 이름이 오브젝트 이름과 다르면 후보에 추가
                    if (!string.Equals(meshFileName, fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        candidateFileNames.Add(meshFileName);
                    }
                    
                    // 메시 이름 전체도 후보에 추가 (파일명이 메시 이름과 정확히 일치하는 경우)
                    candidateFileNames.Add(meshName.EndsWith(".obj", StringComparison.OrdinalIgnoreCase) 
                        ? meshName.Substring(0, meshName.Length - 4) 
                        : meshName);
                }
            }
            
            // 3. children의 MeshFilter에서도 메시 이름 추출
            if (obj.transform.childCount > 0)
            {
                foreach (Transform child in obj.transform)
                {
                    if (child != null && child.gameObject != null)
                    {
                        if (child.gameObject.TryGetComponent<MeshFilter>(out var childMeshFilter) && 
                            childMeshFilter.sharedMesh != null)
                        {
                            string childMeshName = childMeshFilter.sharedMesh.name;
                            if (!string.IsNullOrEmpty(childMeshName))
                            {
                                string childMeshFileName = childMeshName;
                                if (childMeshFileName.EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
                                {
                                    childMeshFileName = childMeshFileName.Substring(0, childMeshFileName.Length - 4);
                                }
                                
                                if (!candidateFileNames.Contains(childMeshFileName, StringComparer.OrdinalIgnoreCase))
                                {
                                    candidateFileNames.Add(childMeshFileName);
                                }
                            }
                        }
                    }
                }
            }

            Debug.Log($"[ObjPathFinder] Searching for OBJ file for '{objName}' with candidates: {string.Join(", ", candidateFileNames)}");

            // 3. 검색 경로 목록에서 찾기
            foreach (var searchPath in _searchPaths)
            {
                if (string.IsNullOrEmpty(searchPath) || !Directory.Exists(searchPath))
                    continue;

                Debug.Log($"[ObjPathFinder] Searching in path: {searchPath}");

                try
                {
                    foreach (var candidateFileName in candidateFileNames)
                    {
                        // 정확한 파일명으로 찾기
                        string exactPath = Path.Combine(searchPath, candidateFileName + ".obj");
                        if (File.Exists(exactPath))
                        {
                            Debug.Log($"[ObjPathFinder] Found OBJ file: {exactPath}");
                            return exactPath;
                        }

                        // 하위 디렉토리에서 찾기
                        var found = Directory.GetFiles(searchPath, candidateFileName + ".obj", SearchOption.AllDirectories);
                        if (found.Length > 0)
                        {
                            Debug.Log($"[ObjPathFinder] Found OBJ file in subdirectory: {found[0]}");
                            return found[0];
                        }
                    }

                    // 대소문자 구분 없이 찾기
                    var allFiles = Directory.GetFiles(searchPath, "*.obj", SearchOption.AllDirectories);
                    foreach (var file in allFiles)
                    {
                        string fileBaseName = Path.GetFileNameWithoutExtension(file);
                        foreach (var candidateFileName in candidateFileNames)
                        {
                            if (string.Equals(fileBaseName, candidateFileName, StringComparison.OrdinalIgnoreCase))
                            {
                                Debug.Log($"[ObjPathFinder] Found OBJ file (case-insensitive): {file}");
                                return file;
                            }
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
                Path.Combine(Application.dataPath, "..", "storage", "outputs", "final"),
                Path.Combine(Application.dataPath, "..", "storage", "outputs", "optimized"),
                Path.Combine(Application.dataPath, "..", "storage", "temp")
            };

            foreach (var commonPath in commonPaths)
            {
                if (!Directory.Exists(commonPath))
                    continue;

                Debug.Log($"[ObjPathFinder] Searching in common path: {commonPath}");

                try
                {
                    foreach (var candidateFileName in candidateFileNames)
                    {
                        var found = Directory.GetFiles(commonPath, candidateFileName + ".obj", SearchOption.AllDirectories);
                        if (found.Length > 0)
                        {
                            Debug.Log($"[ObjPathFinder] Found OBJ file in common path: {found[0]}");
                            return found[0];
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ObjPathFinder] Error searching in common path {commonPath}: {ex.Message}");
                }
            }

            Debug.LogWarning($"[ObjPathFinder] Could not find OBJ file for '{objName}'");
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
    /// 컴포넌트 속성의 키-값 쌍
    /// </summary>
    [Serializable]
    public class PropertyPair
    {
        public string key;
        public string value;

        public PropertyPair() { }
        public PropertyPair(string k, string v) { key = k; value = v; }
    }

    /// <summary>
    /// 컴포넌트의 속성값을 저장하는 데이터 클래스
    /// </summary>
    [Serializable]
    public class ComponentData
    {
        public string componentType; // 컴포넌트 타입 이름
        // 깊이 제한을 피하기 위해 리스트 대신 단일 문자열로 저장 (형식: "key1:value1|key2:value2|...")
        public string properties = ""; // 속성명:값 쌍들을 파이프(|)로 구분, 각 쌍은 콜론(:)으로 구분

        public ComponentData() 
        {
            properties = "";
        }

        public ComponentData(Component component) : this()
        {
            if (component == null) return;

            // Unity 타입은 FullName 대신 AssemblyQualifiedName 사용 (더 정확함)
            Type type = component.GetType();
            componentType = type.AssemblyQualifiedName ?? type.FullName;
            SaveComponentProperties(component);
        }

        void SaveComponentProperties(Component component)
        {
            Type type = component.GetType();
            
            SaveFields(component, type);
            SaveProperties(component, type);
        }

        /// <summary>
        /// 필드들을 저장합니다.
        /// </summary>
        void SaveFields(Component component, Type type)
        {
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
                .Where(IsSerializableField)
                .Where(f => !typeof(UnityEngine.Object).IsAssignableFrom(f.FieldType)); // Unity Object 타입 제외

            foreach (var field in fields)
            {
                try
                {
                    object value = field.GetValue(component);
                    if (value != null)
                    {
                        // Unity Object 타입은 건너뛰기
                        if (typeof(UnityEngine.Object).IsAssignableFrom(value.GetType()))
                            continue;
                            
                        string jsonValue = SerializeValue(value);
                        if (jsonValue != null)
                        {
                            // 깊이 제한을 피하기 위해 단일 문자열에 추가 (형식: "key:value")
                            if (!string.IsNullOrEmpty(properties))
                                properties += "|";
                            // 값에 특수문자가 포함될 수 있으므로 Base64 인코딩 또는 이스케이프 처리
                            string escapedValue = jsonValue.Replace(":", "::").Replace("|", "||");
                            properties += $"field_{field.Name}:{escapedValue}";
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ComponentData] Failed to save field {field.Name}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 속성들을 저장합니다.
        /// </summary>
        void SaveProperties(Component component, Type type)
        {
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
                .Where(p => !IsIgnoredProperty(p.Name))
                .Where(p => IsSerializablePropertyType(p.PropertyType)); // 직렬화 가능한 타입만

            foreach (var prop in props)
            {
                try
                {
                    object value = prop.GetValue(component);
                    if (value != null)
                    {
                        // Unity Object 타입은 건너뛰기
                        if (typeof(UnityEngine.Object).IsAssignableFrom(value.GetType()))
                            continue;
                            
                        string jsonValue = SerializeValue(value);
                        if (jsonValue != null)
                        {
                            // 깊이 제한을 피하기 위해 단일 문자열에 추가 (형식: "key:value")
                            if (!string.IsNullOrEmpty(properties))
                                properties += "|";
                            // 값에 특수문자가 포함될 수 있으므로 이스케이프 처리
                            string escapedValue = jsonValue.Replace(":", "::").Replace("|", "||");
                            properties += $"property_{prop.Name}:{escapedValue}";
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ComponentData] Failed to save property {prop.Name}: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// 속성 타입이 직렬화 가능한지 확인합니다.
        /// </summary>
        bool IsSerializablePropertyType(Type propertyType)
        {
            // Unity Object 타입은 제외
            if (typeof(UnityEngine.Object).IsAssignableFrom(propertyType))
            {
                return false;
            }
            
            // 기본 타입만 허용
            if (propertyType.IsPrimitive || propertyType == typeof(string) || propertyType == typeof(decimal))
            {
                return true;
            }
            
            // Unity 기본 타입만 허용
            if (propertyType == typeof(Vector2) || propertyType == typeof(Vector3) || propertyType == typeof(Vector4) ||
                propertyType == typeof(Quaternion) || propertyType == typeof(Color) || propertyType == typeof(Rect) ||
                propertyType == typeof(Bounds) || propertyType.IsEnum)
            {
                return true;
            }
            
            // 배열이나 리스트는 단순 타입만 허용
            if (propertyType.IsArray)
            {
                Type elementType = propertyType.GetElementType();
                if (elementType != null && 
                    (elementType.IsPrimitive || elementType == typeof(string) ||
                     elementType == typeof(Vector2) || elementType == typeof(Vector3) ||
                     elementType == typeof(Vector4) || elementType == typeof(Quaternion) ||
                     elementType == typeof(Color)))
                {
                    return true;
                }
            }
            
            // 그 외 복잡한 타입은 저장하지 않음
            return false;
        }

        /// <summary>
        /// 무시할 속성인지 확인합니다.
        /// </summary>
        static bool IsIgnoredProperty(string propName)
        {
            string[] ignored = { "enabled", "gameObject", "transform" };
            return ignored.Contains(propName);
        }

        bool IsSerializableField(FieldInfo field)
        {
            // Unity 직렬화 가능한 타입인지 확인
            Type fieldType = field.FieldType;
            
            // UnityEngine.Object 타입은 참조이므로 저장하지 않음
            if (typeof(UnityEngine.Object).IsAssignableFrom(fieldType))
            {
                return false;
            }
            
            // 기본 타입만 허용 (직렬화 깊이 제한 방지)
            if (fieldType.IsPrimitive || fieldType == typeof(string) || fieldType == typeof(decimal))
            {
                return field.IsPublic || field.GetCustomAttributes(typeof(SerializeField), true).Length > 0;
            }
            
            // Unity 기본 타입만 허용
            if (fieldType == typeof(Vector2) || fieldType == typeof(Vector3) || fieldType == typeof(Vector4) ||
                fieldType == typeof(Quaternion) || fieldType == typeof(Color) || fieldType == typeof(Rect) ||
                fieldType == typeof(Bounds) || fieldType.IsEnum)
            {
                return field.IsPublic || field.GetCustomAttributes(typeof(SerializeField), true).Length > 0;
            }
            
            // 배열이나 리스트는 단순 타입만 허용
            if (fieldType.IsArray)
            {
                Type elementType = fieldType.GetElementType();
                if (elementType != null && 
                    (elementType.IsPrimitive || elementType == typeof(string) ||
                     elementType == typeof(Vector2) || elementType == typeof(Vector3) ||
                     elementType == typeof(Vector4) || elementType == typeof(Quaternion) ||
                     elementType == typeof(Color)))
                {
                    return field.IsPublic || field.GetCustomAttributes(typeof(SerializeField), true).Length > 0;
                }
            }
            
            // 그 외 복잡한 타입은 저장하지 않음 (직렬화 깊이 제한 방지)
            return false;
        }

        string SerializeValue(object value)
        {
            if (value == null) return null;

            try
            {
                Type valueType = value.GetType();
                
                // Unity Object 타입은 직렬화하지 않음 (참조이므로)
                if (typeof(UnityEngine.Object).IsAssignableFrom(valueType))
                {
                    return null;
                }
                
                // 기본 타입들은 직접 문자열로 변환
                if (value is int || value is float || value is double || value is bool || value is string || value is Enum)
                    return value.ToString();

                // Unity 기본 타입들은 JSON으로 직렬화
                if (value is Vector2 || value is Vector3 || value is Vector4 || 
                    value is Quaternion || value is Color || value is Rect || value is Bounds)
                {
                    try
                    {
                        return JsonUtility.ToJson(value);
                    }
                    catch
                    {
                        return null;
                    }
                }

                // 배열이나 리스트 처리 (단순 타입만)
                if (value is System.Collections.IEnumerable enumerable && !(value is string))
                {
                    // 배열/리스트의 요소 타입 확인
                    Type elementType = null;
                    if (valueType.IsArray)
                    {
                        elementType = valueType.GetElementType();
                    }
                    else if (valueType.IsGenericType)
                    {
                        elementType = valueType.GetGenericArguments()[0];
                    }
                    
                    // Unity Object나 복잡한 타입이 포함된 배열은 건너뛰기
                    if (elementType != null && typeof(UnityEngine.Object).IsAssignableFrom(elementType))
                    {
                        return null;
                    }
                    
                    // 단순 타입 배열만 직렬화 (깊이 제한 방지를 위해 직접 문자열로 변환)
                    if (elementType != null && 
                        (elementType.IsPrimitive || elementType == typeof(string) || 
                         elementType == typeof(Vector2) || elementType == typeof(Vector3) || 
                         elementType == typeof(Vector4) || elementType == typeof(Quaternion) || 
                         elementType == typeof(Color)))
                    {
                        try
                        {
                            // 깊이 제한을 피하기 위해 직접 문자열 배열로 변환
                            var list = new List<string>();
                            foreach (var item in enumerable)
                            {
                                if (item == null) continue;
                                
                                // 기본 타입은 직접 문자열로 변환
                                if (item is int || item is float || item is double || item is bool || item is string || item is Enum)
                                {
                                    list.Add(item.ToString());
                                }
                                // Unity 기본 타입은 JSON으로 직렬화 (단일 레벨만)
                                else if (item is Vector2 || item is Vector3 || item is Vector4 || 
                                         item is Quaternion || item is Color)
                                {
                                    try
                                    {
                                        list.Add(JsonUtility.ToJson(item));
                                    }
                                    catch
                                    {
                                        // 직렬화 실패 시 건너뛰기
                                    }
                                }
                            }
                            
                            // 배열을 JSON 배열 형태의 문자열로 변환
                            return "[" + string.Join(",", list) + "]";
                        }
                        catch
                        {
                            return null;
                        }
                    }
                    
                    // 복잡한 타입 배열은 건너뛰기
                    return null;
                }

                // 복잡한 객체는 직렬화하지 않음 (깊이 제한 방지)
                // Unity의 JsonUtility는 깊은 중첩 구조나 순환 참조를 처리하지 못함
                return null;
            }
            catch
            {
                return null;
            }
        }

        public void ApplyToComponent(Component component)
        {
            if (component == null) return;

            Type type = component.GetType();
            string typeName = type.AssemblyQualifiedName ?? type.FullName;
            if (typeName != componentType && type.FullName != componentType) return;

            // properties 문자열을 파싱하여 키-값 쌍 복원
            if (string.IsNullOrEmpty(properties))
                return;
                
            string[] pairs = properties.Split('|');
            foreach (string pair in pairs)
            {
                if (string.IsNullOrEmpty(pair))
                    continue;
                    
                int colonIndex = pair.IndexOf(':');
                if (colonIndex < 0)
                    continue;
                    
                string key = pair.Substring(0, colonIndex);
                string value = pair.Substring(colonIndex + 1).Replace("||", "|").Replace("::", ":");
                
                try
                {
                    if (key.StartsWith("field_"))
                    {
                        RestoreField(component, type, key.Substring(6), value);
                    }
                    else if (key.StartsWith("property_"))
                    {
                        RestoreProperty(component, type, key.Substring(9), value);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ComponentData] Failed to restore {key}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 필드를 복원합니다.
        /// </summary>
        void RestoreField(Component component, Type type, string fieldName, string jsonValue)
        {
            var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null || !IsSerializableField(field)) return;

            object value = DeserializeValue(jsonValue, field.FieldType);
            if (value != null)
                field.SetValue(component, value);
        }

        /// <summary>
        /// 속성을 복원합니다.
        /// </summary>
        void RestoreProperty(Component component, Type type, string propName, string jsonValue)
        {
            var prop = type.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null || !prop.CanWrite) return;

            object value = DeserializeValue(jsonValue, prop.PropertyType);
            if (value != null)
                prop.SetValue(component, value);
        }

        object DeserializeValue(string jsonValue, Type targetType)
        {
            if (string.IsNullOrEmpty(jsonValue)) return null;

            try
            {
                // 기본 타입 파싱
                if (targetType == typeof(int)) return int.Parse(jsonValue);
                if (targetType == typeof(float)) return float.Parse(jsonValue);
                if (targetType == typeof(bool)) return bool.Parse(jsonValue);
                if (targetType == typeof(string)) return jsonValue;
                if (targetType.IsEnum) return Enum.Parse(targetType, jsonValue);

                // Unity 타입 JSON 역직렬화
                return targetType.Name switch
                {
                    nameof(Vector2) => JsonUtility.FromJson<Vector2>(jsonValue),
                    nameof(Vector3) => JsonUtility.FromJson<Vector3>(jsonValue),
                    nameof(Vector4) => JsonUtility.FromJson<Vector4>(jsonValue),
                    nameof(Quaternion) => JsonUtility.FromJson<Quaternion>(jsonValue),
                    nameof(Color) => JsonUtility.FromJson<Color>(jsonValue),
                    _ => JsonUtility.FromJson(jsonValue, targetType)
                };
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// 오브젝트의 Transform 정보를 저장하는 데이터 클래스
    /// </summary>
    [Serializable]
    public class ObjectTransformData
    {
        public string objectName;
        // 깊이 제한을 피하기 위해 구조체 대신 단순 float 필드로 분리
        public float positionX, positionY, positionZ;
        public float rotationX, rotationY, rotationZ; // Euler angles
        public float scaleX, scaleY, scaleZ;
        public string objFilePath; // 원본 OBJ 파일 경로 (선택적)
        public ObjectType objectType = ObjectType.Unknown; // 오브젝트 타입
        public string primitiveType; // Unity 기본 오브젝트 타입 (Plane, Cube, Sphere 등)
        public List<ComponentData> components = new List<ComponentData>(); // 모든 컴포넌트 정보
        public List<ObjectTransformData> children = new List<ObjectTransformData>(); // 자식 오브젝트들

        // 편의 메서드 (직렬화되지 않음)
        public Vector3 GetPosition() => new Vector3(positionX, positionY, positionZ);
        public void SetPosition(Vector3 pos) { positionX = pos.x; positionY = pos.y; positionZ = pos.z; }
        
        public Vector3 GetRotation() => new Vector3(rotationX, rotationY, rotationZ);
        public void SetRotation(Vector3 rot) { rotationX = rot.x; rotationY = rot.y; rotationZ = rot.z; }
        
        public Vector3 GetScale() => new Vector3(scaleX, scaleY, scaleZ);
        public void SetScale(Vector3 scl) { scaleX = scl.x; scaleY = scl.y; scaleZ = scl.z; }

        public ObjectTransformData() { }

        public ObjectTransformData(GameObject obj, string objPath = null, bool includeChildren = true)
        {
            objectName = obj.name;
            positionX = obj.transform.localPosition.x;
            positionY = obj.transform.localPosition.y;
            positionZ = obj.transform.localPosition.z;
            rotationX = obj.transform.localEulerAngles.x;
            rotationY = obj.transform.localEulerAngles.y;
            rotationZ = obj.transform.localEulerAngles.z;
            scaleX = obj.transform.localScale.x;
            scaleY = obj.transform.localScale.y;
            scaleZ = obj.transform.localScale.z;
            objFilePath = objPath;
            
            // 오브젝트 타입 감지
            DetectObjectType(obj);
            
            // 모든 컴포넌트 저장 (Transform 제외)
            SaveAllComponents(obj);
            
            // 자식 오브젝트들 저장
            if (includeChildren)
            {
                SaveChildren(obj);
            }
        }

        void SaveAllComponents(GameObject obj)
        {
            var allComponents = obj.GetComponents<Component>()
                .Where(c => !(c is Transform)); // Transform 제외

            foreach (var component in allComponents)
            {
                try
                {
                    var compData = new ComponentData(component);
                    if (!string.IsNullOrEmpty(compData.properties) || !string.IsNullOrEmpty(compData.componentType))
                    {
                        components.Add(compData);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ObjectTransformData] Failed to save component {component.GetType().Name}: {ex.Message}");
                }
            }
        }

        void SaveChildren(GameObject obj)
        {
            foreach (Transform child in obj.transform)
            {
                var childData = new ObjectTransformData(child.gameObject, null, true);
                children.Add(childData);
            }
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

            if (!obj.TryGetComponent<MeshFilter>(out var meshFilter) || meshFilter.sharedMesh == null)
            {
                objectType = ObjectType.Empty;
                return;
            }

            string meshName = meshFilter.sharedMesh.name;
            
            // Unity 기본 메시인지 확인
            if (IsUnityPrimitiveMesh(meshName))
            {
                objectType = ObjectType.Primitive;
                primitiveType = meshName;
            }
            else if (!string.IsNullOrEmpty(objFilePath))
            {
                objectType = ObjectType.ObjFile;
            }
            else
            {
                objectType = ObjectType.Unknown;
            }
        }

        /// <summary>
        /// Unity 기본 메시인지 확인합니다.
        /// </summary>
        static bool IsUnityPrimitiveMesh(string meshName)
        {
            if (string.IsNullOrEmpty(meshName))
                return false;

            string[] primitiveNames = { "Plane", "Cube", "Sphere", "Capsule", "Cylinder", "Quad" };
            return primitiveNames.Any(name => string.Equals(meshName, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// GameObject를 생성하고 모든 정보를 복원합니다.
        /// </summary>
        public GameObject CreateGameObject()
        {
            GameObject obj = CreateBaseGameObject();
            if (obj == null) return null;

            SetupGameObject(obj);
            RestoreAllComponents(obj);
            RestoreChildren(obj);
            RegisterUndo(obj);

            return obj;
        }

        /// <summary>
        /// 기본 GameObject를 생성합니다 (타입에 따라).
        /// </summary>
        GameObject CreateBaseGameObject()
        {
            return objectType switch
            {
                ObjectType.Primitive => CreatePrimitiveObject(),
                ObjectType.ObjFile => LoadObjFile(),
                _ => new GameObject(objectName)
            };
        }

        /// <summary>
        /// OBJ 파일에서 GameObject를 로드합니다.
        /// </summary>
        GameObject LoadObjFile()
        {
            // OBJ 파일 경로 찾기
            string objPath = string.IsNullOrEmpty(objFilePath) || !File.Exists(objFilePath)
                ? ObjPathFinder.FindObjPathForImport(objectName, objFilePath)
                : objFilePath;

            if (string.IsNullOrEmpty(objPath) || !File.Exists(objPath))
            {
                Debug.LogWarning($"[ObjectTransformData] OBJ file not found for '{objectName}', creating empty GameObject");
                return new GameObject(objectName);
            }

            try
            {
                return RuntimeObjLoader.LoadObj(objPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ObjectTransformData] Failed to load OBJ: {objPath}\n{ex}");
                return new GameObject(objectName);
            }
        }

        /// <summary>
        /// GameObject의 기본 설정을 적용합니다.
        /// </summary>
        void SetupGameObject(GameObject obj)
        {
            obj.name = objectName;
            
            #if UNITY_EDITOR
            obj.hideFlags = HideFlags.None;
            
            // MeshRenderer의 material이 제대로 보이도록 설정
            if (obj.TryGetComponent<MeshRenderer>(out var meshRenderer))
            {
                // Material의 renderQueue와 투명도 설정 확인
                if (meshRenderer.sharedMaterials != null)
                {
                    bool materialFixed = false;
                    foreach (var mat in meshRenderer.sharedMaterials)
                    {
                        if (mat != null)
                        {
                            Color originalColor = mat.color;
                            bool needsFix = false;
                            
                            // 1. renderQueue가 Transparent 범위에 있으면 무조건 Geometry로 변경
                            if (mat.renderQueue >= (int)UnityEngine.Rendering.RenderQueue.Transparent)
                            {
                                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
                                needsFix = true;
                            }
                            
                            // 2. _Surface 속성이 Transparent(1)이면 Opaque(0)로 변경
                            if (mat.HasProperty("_Surface"))
                            {
                                float surface = mat.GetFloat("_Surface");
                                if (surface >= 0.5f) // 1이면 Transparent
                                {
                                    mat.SetFloat("_Surface", 0); // Opaque
                                    needsFix = true;
                                }
                            }
                            
                            // 3. 투명 관련 키워드 비활성화
                            if (mat.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"))
                            {
                                mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                                needsFix = true;
                            }
                            if (mat.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON"))
                            {
                                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                                needsFix = true;
                            }
                            
                            // 4. Color alpha가 1.0이 아니면 1.0으로 변경
                            if (originalColor.a < 0.999f)
                            {
                                mat.color = new Color(originalColor.r, originalColor.g, originalColor.b, 1f);
                                needsFix = true;
                            }
                            
                            // 5. Blend 모드가 Transparent용이면 Opaque용으로 변경
                            if (mat.HasProperty("_SrcBlend") && mat.HasProperty("_DstBlend"))
                            {
                                int srcBlend = mat.GetInt("_SrcBlend");
                                int dstBlend = mat.GetInt("_DstBlend");
                                // Transparent blend 모드인지 확인
                                if (srcBlend == (int)UnityEngine.Rendering.BlendMode.SrcAlpha && 
                                    dstBlend == (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha)
                                {
                                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                                    needsFix = true;
                                }
                            }
                            
                            // 6. ZWrite가 0이면 1로 변경 (Opaque는 ZWrite 필요)
                            if (mat.HasProperty("_ZWrite"))
                            {
                                int zWrite = mat.GetInt("_ZWrite");
                                if (zWrite == 0)
                                {
                                    mat.SetInt("_ZWrite", 1);
                                    needsFix = true;
                                }
                            }
                            
                            if (needsFix)
                            {
                                materialFixed = true;
                                Debug.Log($"[ObjectTransformData] Fixed material '{mat.name}' to be opaque (renderQueue: {mat.renderQueue}, color alpha: {mat.color.a})");
                            }
                            
                            EditorUtility.SetDirty(mat);
                        }
                    }
                    
                    if (materialFixed)
                    {
                        // Material 배열을 다시 할당하여 변경사항 적용
                        var materials = meshRenderer.sharedMaterials;
                        meshRenderer.sharedMaterials = materials;
                    }
                }
                
                EditorUtility.SetDirty(meshRenderer);
            }
            #endif
            
            ApplyToGameObject(obj);
        }

        /// <summary>
        /// Undo 시스템에 등록합니다.
        /// </summary>
        void RegisterUndo(GameObject obj)
        {
            #if UNITY_EDITOR
            Undo.RegisterCreatedObjectUndo(obj, $"Import {objectName}");
            #endif
        }

        /// <summary>
        /// 모든 컴포넌트를 복원합니다.
        /// </summary>
        void RestoreAllComponents(GameObject obj)
        {
            foreach (var compData in components)
            {
                try
                {
                    Type compType = FindComponentType(compData.componentType);
                    if (compType == null) continue;

                    Component component = GetOrAddComponent(obj, compType);
                    compData.ApplyToComponent(component);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ObjectTransformData] Failed to restore component {compData.componentType}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 컴포넌트 타입을 찾습니다.
        /// </summary>
        static Type FindComponentType(string typeName)
        {
            // 1. AssemblyQualifiedName으로 직접 찾기
            Type type = Type.GetType(typeName);
            if (type != null) return type;

            // 2. FullName으로 찾기
            if (typeName.Contains(","))
            {
                string fullName = typeName.Split(',')[0];
                type = Type.GetType(fullName);
                if (type != null) return type;
            }

            // 3. 모든 어셈블리에서 찾기
            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(typeName);
                if (type != null) return type;

                if (typeName.Contains(","))
                {
                    string fullName = typeName.Split(',')[0];
                    type = assembly.GetType(fullName);
                    if (type != null) return type;
                }
            }

            Debug.LogWarning($"[ObjectTransformData] Component type not found: {typeName}");
            return null;
        }

        /// <summary>
        /// 컴포넌트를 가져오거나 추가합니다.
        /// </summary>
        static Component GetOrAddComponent(GameObject obj, Type componentType)
        {
            Component existing = obj.GetComponent(componentType);
            return existing ?? obj.AddComponent(componentType);
        }

        /// <summary>
        /// 자식 오브젝트들을 복원합니다.
        /// </summary>
        void RestoreChildren(GameObject parent)
        {
            RestoreChildrenToParent(parent);
        }

        /// <summary>
        /// 자식 오브젝트들을 부모 GameObject에 복원합니다. (public 메서드)
        /// </summary>
        public void RestoreChildrenToParent(GameObject parent)
        {
            if (parent == null || children == null || children.Count == 0)
                return;

            foreach (var childData in children)
            {
                try
                {
                    GameObject child = childData.CreateGameObject();
                    if (child != null)
                    {
                        child.transform.SetParent(parent.transform, false);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ObjectTransformData] Failed to restore child {childData.objectName}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Import 시 GameObject를 찾거나 생성합니다.
        /// </summary>
        public GameObject FindOrCreateGameObject(bool createNew)
        {
            if (!createNew)
            {
                return GameObject.Find(objectName);
            }

            // createNew가 true일 때는 항상 새로 생성 (같은 모델을 여러 번 import 가능하도록)
            // OBJ 파일 경로 업데이트 (필요한 경우)
            if (!UpdateObjFilePathIfNeeded())
            {
                // 경로를 찾지 못한 경우 null 반환 (ApplyCollection에서 처리)
                return null;
            }

            // 새 오브젝트 생성 (고유한 이름으로)
            GameObject newObj = CreateGameObject();
            if (newObj != null)
            {
                // 같은 이름이 이미 존재하면 고유한 이름 생성
                if (GameObject.Find(newObj.name) != null && GameObject.Find(newObj.name) != newObj)
                {
                    int counter = 1;
                    string baseName = newObj.name;
                    string uniqueName;
                    do
                    {
                        uniqueName = $"{baseName} ({counter})";
                        counter++;
                    } while (GameObject.Find(uniqueName) != null);
                    
                    newObj.name = uniqueName;
                    Debug.Log($"[ObjectTransformData] Renamed object to '{uniqueName}' to avoid duplicate");
                }
            }

            return newObj;
        }

        /// <summary>
        /// OBJ 파일 경로를 업데이트합니다 (필요한 경우).
        /// 경로를 찾지 못하면 사용자에게 경로를 지정하도록 요청합니다.
        /// </summary>
        bool UpdateObjFilePathIfNeeded()
        {
            if (objectType != ObjectType.ObjFile)
                return true; // OBJ 파일이 아니면 경로 업데이트 불필요

            // 이미 경로가 있고 파일이 존재하면 OK
            if (!string.IsNullOrEmpty(objFilePath) && File.Exists(objFilePath))
            {
                // 절대 경로로 변환
                if (!Path.IsPathRooted(objFilePath))
                {
                    objFilePath = Path.GetFullPath(objFilePath);
                }
                return true;
            }

            // 저장된 경로가 있지만 파일이 없는 경우
            if (!string.IsNullOrEmpty(objFilePath) && !File.Exists(objFilePath))
            {
                // 절대 경로로 변환 후 다시 확인
                string absolutePath = Path.IsPathRooted(objFilePath) ? objFilePath : Path.GetFullPath(objFilePath);
                if (File.Exists(absolutePath))
                {
                    objFilePath = absolutePath;
                    return true;
                }
            }

            // 검색 경로에서 찾기 시도
            string foundPath = ObjPathFinder.FindObjPathForImport(objectName, objFilePath);
            if (!string.IsNullOrEmpty(foundPath) && File.Exists(foundPath))
            {
                if (!Path.IsPathRooted(foundPath))
                    foundPath = Path.GetFullPath(foundPath);
                objFilePath = foundPath;
                return true;
            }

            // 경로를 찾지 못한 경우 - 사용자에게 경로 지정 요청
            #if UNITY_EDITOR
            string message = $"OBJ 파일을 찾을 수 없습니다.\n\n오브젝트: {objectName}\n저장된 경로: {objFilePath ?? "(없음)"}\n\n파일 경로를 지정하시겠습니까?";
            bool selectPath = EditorUtility.DisplayDialog("OBJ 파일 경로 찾기", message, "경로 지정", "건너뛰기");
            
            if (selectPath)
            {
                string selectedPath = EditorUtility.OpenFilePanel("OBJ 파일 선택", Application.dataPath, "obj");
                if (!string.IsNullOrEmpty(selectedPath) && File.Exists(selectedPath))
                {
                    objFilePath = selectedPath;
                    Debug.Log($"[ObjectTransformData] User selected OBJ path for '{objectName}': {objFilePath}");
                    return true;
                }
                else
                {
                    Debug.LogWarning($"[ObjectTransformData] User cancelled or invalid path for '{objectName}'");
                    return false;
                }
            }
            #endif

            return false; // 경로를 찾지 못함
        }

        /// <summary>
        /// Unity 기본 오브젝트를 생성합니다.
        /// </summary>
        GameObject CreatePrimitiveObject()
        {
            if (string.IsNullOrEmpty(primitiveType))
                return new GameObject(objectName);

            // Quad는 특별 처리
            if (string.Equals(primitiveType, "Quad", StringComparison.OrdinalIgnoreCase))
                return CreateQuadObject();

            // PrimitiveType enum으로 변환
            PrimitiveType? primitiveTypeEnum = primitiveType.ToLower() switch
            {
                "plane" => PrimitiveType.Plane,
                "cube" => PrimitiveType.Cube,
                "sphere" => PrimitiveType.Sphere,
                "capsule" => PrimitiveType.Capsule,
                "cylinder" => PrimitiveType.Cylinder,
                _ => null
            };

            if (!primitiveTypeEnum.HasValue)
                return new GameObject(objectName);

            var primitive = GameObject.CreatePrimitive(primitiveTypeEnum.Value);
            SetPrimitiveHideFlags(primitive);
            
            return primitive;
        }

        /// <summary>
        /// Primitive 오브젝트의 hideFlags를 설정합니다.
        /// </summary>
        void SetPrimitiveHideFlags(GameObject primitive)
        {
            #if UNITY_EDITOR
            primitive.hideFlags = HideFlags.None;
            
            if (primitive.TryGetComponent<MeshFilter>(out var meshFilter) && meshFilter.sharedMesh != null)
                meshFilter.sharedMesh.hideFlags = HideFlags.None;
            
            if (primitive.TryGetComponent<MeshRenderer>(out var meshRenderer) && meshRenderer.sharedMaterial != null)
                meshRenderer.sharedMaterial.hideFlags = HideFlags.None;
            #endif
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
            obj.transform.localPosition = position;
            obj.transform.localEulerAngles = rotation;
            obj.transform.localScale = scale;
        }

        /// <summary>
        /// Import 결과를 나타내는 구조체
        /// </summary>
        public struct ImportResult
        {
            public int successCount;
            public int failCount;
        }

        /// <summary>
        /// Export 시 OBJ 파일 경로를 가져옵니다. (공통 로직)
        /// 절대 경로를 반환하며, 파일이 존재하는 경우에만 반환합니다.
        /// </summary>
        public static string GetObjPathForExport(GameObject obj)
        {
            string objPath = ObjPathFinder.FindObjPath(obj);
            
            // 절대 경로로 변환하고 파일 존재 여부 확인
            if (!string.IsNullOrEmpty(objPath))
            {
                // 상대 경로인 경우 절대 경로로 변환
                if (!Path.IsPathRooted(objPath))
                {
                    objPath = Path.GetFullPath(objPath);
                }
                
                // 파일이 존재하는지 확인
                if (File.Exists(objPath))
                {
                    return objPath;
                }
            }
            
            // 찾지 못한 경우 MeshFilter의 메시 이름으로 시도
            if (obj.TryGetComponent<MeshFilter>(out var meshFilter) && meshFilter.sharedMesh != null)
            {
                string meshName = meshFilter.sharedMesh.name;
                if (!string.IsNullOrEmpty(meshName))
                {
                    // 메시 이름이 .obj로 끝나는 경우
                    if (meshName.EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
                    {
                        // 검색 경로에서 찾기
                        string foundPath = ObjPathFinder.FindObjPath(obj);
                        if (!string.IsNullOrEmpty(foundPath) && File.Exists(foundPath))
                        {
                            if (!Path.IsPathRooted(foundPath))
                                foundPath = Path.GetFullPath(foundPath);
                            return foundPath;
                        }
                    }
                }
            }
            
            return null; // 경로를 찾지 못한 경우
        }

        /// <summary>
        /// 컬렉션의 모든 오브젝트를 Import합니다. (공통 로직)
        /// </summary>
        public static ImportResult ApplyCollection(ObjectTransformCollection collection, bool createNew, string logPrefix)
        {
            if (collection == null || collection.objects == null)
                return new ImportResult { successCount = 0, failCount = 0 };

            int successCount = 0;
            int failCount = 0;
            int skippedCount = 0;

            foreach (var data in collection.objects)
            {
                // OBJ 파일만 처리 (다른 타입은 건너뛰기)
                if (data.objectType != ObjectType.ObjFile)
                {
                    Debug.Log($"{logPrefix} Skipping non-OBJ object: {data.objectName} (Type: {data.objectType})");
                    skippedCount++;
                    continue;
                }

                GameObject obj = data.FindOrCreateGameObject(createNew);
                
                if (obj != null)
                {
                    data.ApplyToGameObject(obj);
                    successCount++;
                }
                else
                {
                    Debug.LogWarning($"{logPrefix} Failed to create/find object: {data.objectName} (OBJ file path not found or invalid)");
                    failCount++;
                }
            }

            if (skippedCount > 0)
            {
                Debug.Log($"{logPrefix} Skipped {skippedCount} non-OBJ objects");
            }

            return new ImportResult { successCount = successCount, failCount = failCount };
        }

        /// <summary>
        /// BinaryFormatter용 직렬화 가능한 데이터로 변환
        /// </summary>
        public SerializableObjectTransformData ToSerializable()
        {
            var serializable = new SerializableObjectTransformData
            {
                objectName = objectName,
                positionX = positionX,
                positionY = positionY,
                positionZ = positionZ,
                rotationX = rotationX,
                rotationY = rotationY,
                rotationZ = rotationZ,
                scaleX = scaleX,
                scaleY = scaleY,
                scaleZ = scaleZ,
                objFilePath = objFilePath,
                objectType = (int)objectType,
                primitiveType = primitiveType ?? ""
            };

            // 컴포넌트 변환
            foreach (var comp in components)
            {
                serializable.components.Add(new SerializableComponentData
                {
                    componentType = comp.componentType,
                    properties = comp.properties
                });
            }

            // 자식 오브젝트 변환
            foreach (var child in children)
            {
                serializable.children.Add(child.ToSerializable());
            }

            return serializable;
        }

        /// <summary>
        /// 직렬화 가능한 데이터에서 복원
        /// </summary>
        public static ObjectTransformData FromSerializable(SerializableObjectTransformData data)
        {
            var objData = new ObjectTransformData
            {
                objectName = data.objectName,
                positionX = data.positionX,
                positionY = data.positionY,
                positionZ = data.positionZ,
                rotationX = data.rotationX,
                rotationY = data.rotationY,
                rotationZ = data.rotationZ,
                scaleX = data.scaleX,
                scaleY = data.scaleY,
                scaleZ = data.scaleZ,
                objFilePath = data.objFilePath,
                objectType = (ObjectType)data.objectType,
                primitiveType = data.primitiveType ?? ""
            };

            // 컴포넌트 복원
            foreach (var comp in data.components)
            {
                objData.components.Add(new ComponentData
                {
                    componentType = comp.componentType,
                    properties = comp.properties
                });
            }

            // 자식 오브젝트 복원
            foreach (var child in data.children)
            {
                objData.children.Add(FromSerializable(child));
            }

            return objData;
        }
    }

    /// <summary>
    /// BinaryFormatter용 직렬화 가능한 Component 데이터
    /// </summary>
    [Serializable]
    public class SerializableComponentData
    {
        public string componentType;
        // 깊이 제한을 피하기 위해 리스트 대신 단일 문자열로 저장
        public string properties = "";
    }

    /// <summary>
    /// BinaryFormatter용 직렬화 가능한 Transform 데이터
    /// </summary>
    [Serializable]
    public class SerializableObjectTransformData
    {
        public string objectName;
        // 깊이 제한을 피하기 위해 구조체 대신 단순 float 필드로 분리
        public float positionX, positionY, positionZ;
        public float rotationX, rotationY, rotationZ;
        public float scaleX, scaleY, scaleZ;
        public string objFilePath;
        public int objectType; // ObjectType enum을 int로 저장
        public string primitiveType;
        public List<SerializableComponentData> components = new List<SerializableComponentData>();
        public List<SerializableObjectTransformData> children = new List<SerializableObjectTransformData>();
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

