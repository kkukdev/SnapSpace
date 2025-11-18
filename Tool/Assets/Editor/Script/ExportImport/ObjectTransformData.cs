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

            // 3. 검색 경로 목록에서 찾기
            foreach (var searchPath in _searchPaths)
            {
                if (string.IsNullOrEmpty(searchPath) || !Directory.Exists(searchPath))
                    continue;

                try
                {
                    foreach (var candidateFileName in candidateFileNames)
                    {
                        // 정확한 파일명으로 찾기
                        string exactPath = Path.Combine(searchPath, candidateFileName + ".obj");
                        if (File.Exists(exactPath))
                        {
                            return exactPath;
                        }

                        // 하위 디렉토리에서 찾기
                        var found = Directory.GetFiles(searchPath, candidateFileName + ".obj", SearchOption.AllDirectories);
                        if (found.Length > 0)
                        {
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
                                return file;
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // 검색 중 오류 발생 시 다음 경로로 계속
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

                try
                {
                    foreach (var candidateFileName in candidateFileNames)
                    {
                        var found = Directory.GetFiles(commonPath, candidateFileName + ".obj", SearchOption.AllDirectories);
                        if (found.Length > 0)
                        {
                            return found[0];
                        }
                    }
                }
                catch (Exception)
                {
                    // 검색 중 오류 발생 시 다음 경로로 계속
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
                catch (Exception)
                {
                    // 필드 저장 실패 시 건너뛰기
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
                    // AudioSource의 더 이상 지원하지 않는 속성은 건너뛰기
                    if (component is AudioSource)
                    {
                        string[] deprecatedProps = { "minVolume", "maxVolume", "rolloffFactor" };
                        if (deprecatedProps.Contains(prop.Name))
                            continue;
                    }
                    
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
                catch (Exception)
                {
                    // 속성 저장 실패 시 건너뛰기 (더 이상 지원하지 않는 속성 접근 시 예외 발생 가능)
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
            // 기본 무시 속성
            string[] ignored = { "enabled", "gameObject", "transform" };
            
            // AudioSource의 더 이상 지원하지 않는 속성들 (Unity 최신 버전에서 제거됨)
            string[] audioSourceDeprecated = { "minVolume", "maxVolume", "rolloffFactor" };
            
            return ignored.Contains(propName) || audioSourceDeprecated.Contains(propName);
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
            if (typeName != componentType && type.FullName != componentType)
            {
                return;
            }

            // properties 문자열을 파싱하여 키-값 쌍 복원
            if (string.IsNullOrEmpty(properties))
            {
                return;
            }
                
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
                catch (Exception)
                {
                    // 복원 실패 시 건너뛰기
                }
            }
        }

        /// <summary>
        /// 필드를 복원합니다.
        /// </summary>
        bool RestoreField(Component component, Type type, string fieldName, string jsonValue)
        {
            var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null || !IsSerializableField(field))
            {
                return false;
            }

            object value = DeserializeValue(jsonValue, field.FieldType);
            if (value != null)
            {
                field.SetValue(component, value);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 속성을 복원합니다.
        /// </summary>
        bool RestoreProperty(Component component, Type type, string propName, string jsonValue)
        {
            var prop = type.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null || !prop.CanWrite)
            {
                return false;
            }

            object value = DeserializeValue(jsonValue, prop.PropertyType);
            if (value != null)
            {
                prop.SetValue(component, value);
                return true;
            }
            return false;
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
        public string objFilePath; // 원본 OBJ 파일 경로 (선택적, 하위 호환성 유지)
        public string originalPath; // Original OBJ 파일 경로 (새로운 구조)
        public string retouchedPath; // Retouched OBJ 파일 경로 (새로운 구조)
        public bool isUsingRetouched; // 현재 Retouched 버전을 사용 중인지 여부
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
            objFilePath = objPath; // 하위 호환성 유지
            
            // 새로운 구조: Original/Retouched children 감지
            DetectOriginalRetouchedStructure(obj);
            
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
        
        /// <summary>
        /// GameObject에서 Original/Retouched 구조를 감지하고 경로를 저장합니다.
        /// </summary>
        void DetectOriginalRetouchedStructure(GameObject obj)
        {
            if (obj == null)
                return;
            
            // 새로운 구조: root GameObject의 children에서 "Original"과 "Retouched" 찾기
            Transform originalTransform = obj.transform.Find("Original");
            Transform retouchedTransform = obj.transform.Find("Retouched");
            
            bool hasNewStructure = (originalTransform != null || retouchedTransform != null);
            
            if (hasNewStructure)
            {
                // Original 경로 찾기
                if (originalTransform != null)
                {
                    string originalObjPath = ObjPathFinder.FindObjPath(originalTransform.gameObject);
                    if (!string.IsNullOrEmpty(originalObjPath) && File.Exists(originalObjPath))
                    {
                        originalPath = originalObjPath;
                    }
                }
                
                // Retouched 경로 찾기
                if (retouchedTransform != null)
                {
                    string retouchedObjPath = ObjPathFinder.FindObjPath(retouchedTransform.gameObject);
                    if (!string.IsNullOrEmpty(retouchedObjPath) && File.Exists(retouchedObjPath))
                    {
                        retouchedPath = retouchedObjPath;
                    }
                }
                
                // 현재 사용 중인 버전 확인
                if (retouchedTransform != null && retouchedTransform.gameObject.activeSelf)
                {
                    isUsingRetouched = true;
                }
                else if (originalTransform != null && originalTransform.gameObject.activeSelf)
                {
                    isUsingRetouched = false;
                }
                
                // objFilePath는 현재 활성화된 버전의 경로로 설정 (하위 호환성)
                if (isUsingRetouched && !string.IsNullOrEmpty(retouchedPath))
                {
                    objFilePath = retouchedPath;
                }
                else if (!string.IsNullOrEmpty(originalPath))
                {
                    objFilePath = originalPath;
                }
            }
            else
            {
                // 기존 구조: objPath를 originalPath로 설정
                if (!string.IsNullOrEmpty(objFilePath))
                {
                    originalPath = objFilePath;
                }
                isUsingRetouched = false;
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
                    if (!string.IsNullOrEmpty(compData.componentType))
                    {
                        components.Add(compData);
                    }
                }
                catch (Exception)
                {
                    // 컴포넌트 저장 실패 시 건너뛰기
                }
            }
        }

        void SaveChildren(GameObject obj)
        {
            foreach (Transform child in obj.transform)
            {
                // 메모 children(TextMesh)인 경우 Transform 정보를 저장하지 않고 컴포넌트만 저장
                bool isMemoChild = child.gameObject.TryGetComponent<TextMesh>(out _);
                
                if (isMemoChild)
                {
                    // 메모 children은 Transform 정보 없이 컴포넌트 정보만 저장
                    var childData = new ObjectTransformData(child.gameObject, null, false);
                    // Transform 정보를 0으로 설정 (사용하지 않음)
                    childData.positionX = 0;
                    childData.positionY = 0;
                    childData.positionZ = 0;
                    childData.rotationX = 0;
                    childData.rotationY = 0;
                    childData.rotationZ = 0;
                    childData.scaleX = 1;
                    childData.scaleY = 1;
                    childData.scaleZ = 1;
                    // children은 저장하지 않음 (메모는 자식이 없음)
                    childData.children.Clear();
                    children.Add(childData);
                }
                else
                {
                    // 메모가 아닌 children은 Transform 정보도 저장
                    var childData = new ObjectTransformData(child.gameObject, null, true);
                    children.Add(childData);
                }
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
        /// Transform, Components, Children을 한 곳에서 통합 처리합니다.
        /// </summary>
        public GameObject CreateGameObject()
        {
            GameObject obj = CreateBaseGameObject();
            if (obj == null)
            {
                Debug.LogError($"[CreateGameObject] CreateBaseGameObject 실패: {objectName}");
                return null;
            }

            // GameObject 기본 설정 (이름, Material 등)
            SetupGameObject(obj);
            
            // Transform 적용
            ApplyToGameObject(obj);
            
            // Components 복원
            RestoreAllComponents(obj);
            
            // Children 복원
            RestoreChildren(obj);
            
            RegisterUndo(obj);

            return obj;
        }

        /// <summary>
        /// 기본 GameObject를 생성합니다 (타입에 따라).
        /// </summary>
        GameObject CreateBaseGameObject()
        {
            // OBJ 파일 경로가 있으면 OBJ 파일로 처리 (objectType이 Empty여도)
            bool hasObjPath = !string.IsNullOrEmpty(objFilePath) || 
                              !string.IsNullOrEmpty(originalPath) || 
                              !string.IsNullOrEmpty(retouchedPath);
            
            if (hasObjPath)
            {
                return LoadObjFile();
            }
            
            GameObject result = objectType switch
            {
                ObjectType.Primitive => CreatePrimitiveObject(),
                ObjectType.ObjFile => LoadObjFile(),
                _ => new GameObject(objectName)
            };
            
            return result;
        }

        /// <summary>
        /// OBJ 파일에서 GameObject를 로드합니다.
        /// Original/Retouched 구조가 있으면 모두 로드하여 children으로 추가합니다.
        /// </summary>
        GameObject LoadObjFile()
        {
            // 새로운 구조: Original과 Retouched 경로가 모두 있는 경우
            bool hasNewStructure = !string.IsNullOrEmpty(originalPath) || !string.IsNullOrEmpty(retouchedPath);
            
            if (hasNewStructure)
            {
                return LoadObjFileWithBothVersions();
            }
            
            // 기존 구조: 단일 OBJ 파일 로드
            string objPath = string.IsNullOrEmpty(objFilePath) || !File.Exists(objFilePath)
                ? ObjPathFinder.FindObjPathForImport(objectName, objFilePath)
                : objFilePath;

            if (string.IsNullOrEmpty(objPath) || !File.Exists(objPath))
            {
                Debug.LogWarning($"[LoadObjFile] OBJ 파일을 찾을 수 없음: {objectName}, 경로: {objPath ?? "(null)"}");
                return new GameObject(objectName);
            }

            try
            {
                GameObject result = RuntimeObjLoader.LoadObj(objPath, preserveOriginalCoordinates: true);
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LoadObjFile] OBJ 파일 로드 실패: {objPath}, 오류: {ex.Message}");
                return new GameObject(objectName);
            }
        }
        
        /// <summary>
        /// Original과 Retouched 버전을 모두 로드하여 하나의 root GameObject에 children으로 추가합니다.
        /// ObjDropWatcherWindow.SpawnWithBothVersions와 동일한 로직을 사용합니다.
        /// </summary>
        GameObject LoadObjFileWithBothVersions()
        {
            try
            {
                // Original 파일이 없으면 오류
                string actualOriginalPath = !string.IsNullOrEmpty(originalPath) && File.Exists(originalPath)
                    ? originalPath
                    : (!string.IsNullOrEmpty(objFilePath) && File.Exists(objFilePath) ? objFilePath : null);
                
                if (string.IsNullOrEmpty(actualOriginalPath))
                {
                    // Original 경로를 찾지 못한 경우 시도
                    actualOriginalPath = ObjPathFinder.FindObjPathForImport(objectName, originalPath ?? objFilePath);
                }
                
                if (string.IsNullOrEmpty(actualOriginalPath) || !File.Exists(actualOriginalPath))
                {
                    // Original을 찾지 못하면 기존 방식으로 단일 파일 로드
                    Debug.LogWarning($"[LoadObjFileWithBothVersions] Original 파일을 찾지 못함, 단일 파일 로드로 전환: {objectName}");
                    return LoadObjFile();
                }
                
                // Root GameObject 생성
                string rootName = Path.GetFileNameWithoutExtension(actualOriginalPath);
                if (string.IsNullOrEmpty(rootName))
                    rootName = objectName;
                
                GameObject rootGo = new GameObject($"{rootName}_Root");
                #if UNITY_EDITOR
                Undo.RegisterCreatedObjectUndo(rootGo, "Load OBJ Root");
                #endif
                
                // Root를 Unity 원점에 배치
                rootGo.transform.position = Vector3.zero;
                rootGo.transform.rotation = Quaternion.identity;
                rootGo.transform.localScale = Vector3.one;
                
                // unitScale 가져오기
                float unitScale = MemoUtils.GetUnitScale();
                
                // Original OBJ 로드 및 설정
                GameObject originalGo = LoadSingleMeshFile(actualOriginalPath);
                if (originalGo == null)
                {
                    Debug.LogError($"[LoadObjFileWithBothVersions] Original OBJ 로드 실패: {actualOriginalPath}");
                    GameObject.DestroyImmediate(rootGo);
                    return null;
                }
                originalGo.name = "Original";
                originalGo.transform.SetParent(rootGo.transform, false);
                originalGo.transform.localPosition = Vector3.zero;
                originalGo.transform.localRotation = Quaternion.identity;
                
                // Original OBJ에 unitScale 적용 및 Y 오프셋 처리
                float minY = FindMinimumY(originalGo, unitScale);
                originalGo.transform.localScale = Vector3.one * unitScale;
                
                if (minY < 0f)
                {
                    float offsetY = -minY;
                    rootGo.transform.position = new Vector3(0f, offsetY, 0f);
                }
                
                // Original OBJ를 보이는 상태로 설정 (isUsingRetouched에 따라)
                originalGo.SetActive(!isUsingRetouched);
                
                // Retouched OBJ 로드 및 설정 (있는 경우)
                GameObject retouchedGo = null;
                string actualRetouchedPath = !string.IsNullOrEmpty(retouchedPath) && File.Exists(retouchedPath)
                    ? retouchedPath
                    : null;
                
                if (!string.IsNullOrEmpty(actualRetouchedPath))
                {
                    retouchedGo = LoadSingleMeshFile(actualRetouchedPath);
                    if (retouchedGo != null)
                    {
                        retouchedGo.name = "Retouched";
                        retouchedGo.transform.SetParent(rootGo.transform, false);
                        retouchedGo.transform.localPosition = Vector3.zero;
                        retouchedGo.transform.localRotation = Quaternion.identity;
                        retouchedGo.transform.localScale = Vector3.one * unitScale;
                        
                        // Retouched OBJ를 안보이는 상태로 설정 (isUsingRetouched에 따라)
                        retouchedGo.SetActive(isUsingRetouched);
                    }
                    else
                    {
                        Debug.LogWarning($"[LoadObjFileWithBothVersions] Retouched OBJ 로드 실패: {actualRetouchedPath}");
                    }
                }
                
                // 경로 정보 저장
                try
                {
                    ObjPathInfo.SetPath(rootGo, actualOriginalPath);
                    
                    // 모든 children에도 경로 저장
                    Transform[] allChildren = rootGo.GetComponentsInChildren<Transform>(true);
                    foreach (Transform child in allChildren)
                    {
                        if (child != null && child != rootGo.transform && child.gameObject != null)
                        {
                            ObjPathInfo.SetPath(child.gameObject, actualOriginalPath);
                        }
                    }
                    
                    #if UNITY_EDITOR
                    if ((rootGo.hideFlags & HideFlags.DontSaveInEditor) == 0)
                    {
                        EditorUtility.SetDirty(rootGo);
                    }
                    #endif
                }
                catch (System.Exception ex)
                {
                    // 경로 저장 실패 시 무시
                    Debug.LogWarning($"[LoadObjFileWithBothVersions] 경로 저장 실패: {ex.Message}");
                }
                return rootGo;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LoadObjFileWithBothVersions] 오류 발생: {ex.Message}\n{ex.StackTrace}");
                #if UNITY_EDITOR
                EditorUtility.DisplayDialog("로드 오류", $"파일 로드 중 오류가 발생했습니다:\n{ex.Message}", "OK");
                #endif
                return null;
            }
        }
        
        /// <summary>
        /// 단일 메시 파일을 로드합니다. (내부 헬퍼 메서드)
        /// </summary>
        GameObject LoadSingleMeshFile(string meshPath)
        {
            if (string.IsNullOrEmpty(meshPath) || !File.Exists(meshPath))
            {
                return null;
            }
            
            GameObject go = null;
            string extension = Path.GetExtension(meshPath).ToLowerInvariant();
            
            // 파일 확장자에 따라 적절한 로더 선택
            switch (extension)
            {
                case ".obj":
                    // OBJ 파일의 원본 좌표 시스템을 유지하기 위해 preserveOriginalCoordinates=true 사용
                    go = RuntimeObjLoader.LoadObj(meshPath, preserveOriginalCoordinates: true);
                    if (go != null)
                    {
                        #if UNITY_EDITOR
                        Undo.RegisterCreatedObjectUndo(go, "Load OBJ");
                        #endif
                    }
                    break;
                    
                case ".glb":
                case ".gltf":
                    // GLB/GLTF 파일은 Unity의 기본 임포트 기능 사용
                    go = LoadGlbOrGltf(meshPath);
                    if (go != null)
                    {
                        #if UNITY_EDITOR
                        Undo.RegisterCreatedObjectUndo(go, "Load GLB/GLTF");
                        #endif
                    }
                    break;
                    
                case ".fbx":
                    // FBX 파일은 Unity의 기본 임포트 기능 사용
                    go = LoadFbx(meshPath);
                    if (go != null)
                    {
                        #if UNITY_EDITOR
                        Undo.RegisterCreatedObjectUndo(go, "Load FBX");
                        #endif
                    }
                    break;
                    
                default:
                    #if UNITY_EDITOR
                    EditorUtility.DisplayDialog("지원하지 않는 형식", 
                        $"지원하지 않는 파일 형식입니다: {extension}\n\n지원 형식: .obj, .glb, .gltf, .fbx", "OK");
                    #endif
                    return null;
            }
            
            if (go == null)
            {
                #if UNITY_EDITOR
                EditorUtility.DisplayDialog("로드 실패", $"파일을 로드할 수 없습니다:\n{meshPath}", "OK");
                #endif
                return null;
            }
            
            return go;
        }
        
        /// <summary>
        /// GameObject와 그 모든 자식에서 메시 버텍스의 Y 좌표 최솟값을 찾습니다.
        /// </summary>
        float FindMinimumY(GameObject root, float scale)
        {
            if (root == null)
                return 0f;

            float minY = float.PositiveInfinity;
            
            // 모든 MeshFilter 컴포넌트 찾기 (자식 포함)
            MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
            
            Transform rootTransform = root.transform;
            
            foreach (MeshFilter mf in meshFilters)
            {
                if (mf == null || mf.sharedMesh == null)
                    continue;
                
                Mesh mesh = mf.sharedMesh;
                Vector3[] vertices = mesh.vertices;
                
                if (vertices == null || vertices.Length == 0)
                    continue;
                
                Transform meshTransform = mf.transform;
                
                // 메시가 속한 오브젝트가 루트인지 자식인지 확인
                bool isRootMesh = (meshTransform == rootTransform);
                
                foreach (Vector3 vertex in vertices)
                {
                    float vertexY;
                    
                    if (isRootMesh)
                    {
                        // 루트의 메시인 경우: 버텍스의 로컬 Y 좌표를 직접 사용
                        vertexY = vertex.y;
                    }
                    else
                    {
                        // 자식 오브젝트의 메시인 경우: 자식의 로컬 좌표를 루트의 로컬 좌표계로 변환
                        Vector3 vertexWorldPos = meshTransform.TransformPoint(vertex);
                        Vector3 vertexRootLocalPos = rootTransform.InverseTransformPoint(vertexWorldPos);
                        vertexY = vertexRootLocalPos.y;
                    }
                    
                    // 스케일 적용 후 Y 좌표 계산
                    float scaledY = vertexY * scale;
                    
                    // Y 좌표 최솟값 업데이트
                    if (scaledY < minY)
                        minY = scaledY;
                }
            }
            
            // 메시를 찾지 못한 경우 0 반환
            if (float.IsPositiveInfinity(minY))
                return 0f;
            
            return minY;
        }
        
        /// <summary>
        /// GLB/GLTF 파일을 로드합니다.
        /// Unity 에디터에서는 AssetDatabase를 사용하여 임포트합니다.
        /// </summary>
        GameObject LoadGlbOrGltf(string filePath)
        {
            try
            {
                // Unity 에디터에서만 작동
                #if UNITY_EDITOR
                // 파일을 Assets 폴더로 복사하여 임포트
                string fileName = Path.GetFileName(filePath);
                string tempAssetPath = $"Assets/Temp_{fileName}";
                
                // 파일 복사
                File.Copy(filePath, tempAssetPath, true);
                
                // AssetDatabase를 통해 임포트
                AssetDatabase.ImportAsset(tempAssetPath, ImportAssetOptions.ForceUpdate);
                
                // ModelImporter 설정 (필요시)
                ModelImporter importer = AssetImporter.GetAtPath(tempAssetPath) as ModelImporter;
                if (importer != null)
                {
                    // 스케일을 1로 설정 (나중에 unitScale 적용)
                    importer.globalScale = 1.0f;
                    importer.SaveAndReimport();
                }
                
                // 임포트된 게임오브젝트 로드
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(tempAssetPath);
                if (prefab != null)
                {
                    // 씬에 인스턴스 생성
                    GameObject instance = GameObject.Instantiate(prefab);
                    instance.name = Path.GetFileNameWithoutExtension(filePath);
                    
                    return instance;
                }
                else
                {
                    // 임시 파일 삭제
                    AssetDatabase.DeleteAsset(tempAssetPath);
                    return null;
                }
                #else
                return null;
                #endif
            }
            catch (Exception)
            {
                return null;
            }
        }
        
        /// <summary>
        /// FBX 파일을 로드합니다.
        /// Unity 에디터에서는 AssetDatabase를 사용하여 임포트합니다.
        /// </summary>
        GameObject LoadFbx(string filePath)
        {
            try
            {
                // Unity 에디터에서만 작동
                #if UNITY_EDITOR
                // 파일을 Assets 폴더로 복사하여 임포트
                string fileName = Path.GetFileName(filePath);
                string tempAssetPath = $"Assets/Temp_{fileName}";
                
                // 파일 복사
                File.Copy(filePath, tempAssetPath, true);
                
                // AssetDatabase를 통해 임포트
                AssetDatabase.ImportAsset(tempAssetPath, ImportAssetOptions.ForceUpdate);
                
                // ModelImporter 설정 (필요시)
                ModelImporter importer = AssetImporter.GetAtPath(tempAssetPath) as ModelImporter;
                if (importer != null)
                {
                    // 스케일을 1로 설정 (나중에 unitScale 적용)
                    importer.globalScale = 1.0f;
                    importer.SaveAndReimport();
                }
                
                // 임포트된 게임오브젝트 로드
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(tempAssetPath);
                if (prefab != null)
                {
                    // 씬에 인스턴스 생성
                    GameObject instance = GameObject.Instantiate(prefab);
                    instance.name = Path.GetFileNameWithoutExtension(filePath);
                    
                    return instance;
                }
                else
                {
                    // 임시 파일 삭제
                    AssetDatabase.DeleteAsset(tempAssetPath);
                    return null;
                }
                #else
                return null;
                #endif
            }
            catch (Exception)
            {
                return null;
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
            
            // Note: Transform 적용은 CreateGameObject()에서 직접 호출하므로
            // SetupGameObject()에서는 이름과 Material 설정만 수행
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
                    if (compType == null)
                    {
                        continue;
                    }

                    Component component = GetOrAddComponent(obj, compType);
                    if (component == null)
                    {
                        continue;
                    }
                    
                    compData.ApplyToComponent(component);
                }
                catch (Exception)
                {
                    // 컴포넌트 복원 실패 시 건너뛰기
                }
            }
        }

        /// <summary>
        /// 컴포넌트 타입을 찾습니다.
        /// </summary>
        static Type FindComponentType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return null;

            // 1. FullName 추출 (AssemblyQualifiedName에서)
            string fullName = typeName;
            if (typeName.Contains(","))
            {
                fullName = typeName.Split(',')[0].Trim();
            }

            // 2. Unity의 일반적인 컴포넌트 타입 직접 매핑
            if (fullName == "UnityEngine.MeshRenderer")
                return typeof(MeshRenderer);
            if (fullName == "UnityEngine.TextMesh")
                return typeof(TextMesh);
            if (fullName == "UnityEngine.MeshFilter")
                return typeof(MeshFilter);
            if (fullName == "UnityEngine.Renderer")
                return typeof(Renderer);

            // 3. FullName으로 직접 찾기
            Type type = Type.GetType(fullName);
            if (type != null) return type;

            // 4. AssemblyQualifiedName으로 직접 찾기
            type = Type.GetType(typeName);
            if (type != null) return type;

            // 5. 모든 어셈블리에서 찾기
            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                // FullName으로 찾기 시도
                type = assembly.GetType(fullName);
                if (type != null) return type;

                // AssemblyQualifiedName으로 찾기 시도 (단, 어셈블리 이름 부분만 매칭)
                if (typeName.Contains(","))
                {
                    string[] parts = typeName.Split(',');
                    if (parts.Length > 1)
                    {
                        // 어셈블리 이름 추출 (예: "UnityEngine.CoreModule")
                        string assemblyName = parts[1].Trim();
                        
                        // 어셈블리 이름이 일치하는 경우 FullName으로 찾기
                        if (assembly.FullName != null && assembly.FullName.StartsWith(assemblyName))
                        {
                            type = assembly.GetType(fullName);
                            if (type != null) return type;
                        }
                        
                        // AssemblyQualifiedName 전체로 찾기
                        type = assembly.GetType(typeName);
                        if (type != null) return type;
                    }
                }
            }

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
            {
                return;
            }

            foreach (var childData in children)
            {
                try
                {
                    // 메모 관련 children은 JSON에서 복원하지 않음 (memos.json에서 생성됨)
                    // 메모는 이름이 "Memo_" 또는 "AudioMemo_"로 시작하거나 TextMesh 컴포넌트를 가진 경우
                    bool isMemoChild = childData.objectName.StartsWith("Memo_", System.StringComparison.OrdinalIgnoreCase) ||
                                      childData.objectName.StartsWith("AudioMemo_", System.StringComparison.OrdinalIgnoreCase);
                    if (!isMemoChild && childData.components != null)
                    {
                        foreach (var compData in childData.components)
                        {
                            if (compData.componentType != null && 
                                (compData.componentType.Contains("TextMesh") || 
                                 compData.componentType.Contains("UnityEngine.TextMesh") ||
                                 compData.componentType.Contains("AudioMemoPlayer")))
                            {
                                isMemoChild = true;
                                break;
                            }
                        }
                    }
                    
                    if (isMemoChild)
                    {
                        continue;
                    }
                    
                    // 이미 같은 이름의 child가 존재하는지 확인
                    // LoadObjFileWithBothVersions()에서 이미 "Original", "Retouched" 등을 생성했을 수 있음
                    Transform existingChild = parent.transform.Find(childData.objectName);
                    if (existingChild != null)
                    {
                        // 이미 존재하는 경우 Transform만 적용
                        childData.ApplyToGameObject(existingChild.gameObject);
                        continue;
                    }

                    // 존재하지 않는 경우 새로 생성
                    GameObject child = childData.CreateGameObject();
                    if (child != null)
                    {
                        child.transform.SetParent(parent.transform, false);
                    }
                    else
                    {
                        Debug.LogError($"[RestoreChildrenToParent] Child 생성 실패: {childData.objectName}, parent: {parent.name}");
                    }
                }
                catch (Exception ex)
                {
                    // 자식 복원 실패 시 건너뛰기
                    Debug.LogError($"[RestoreChildrenToParent] Child 복원 중 오류: {childData.objectName}, parent: {parent.name}, 오류: {ex.Message}");
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
            // OBJ 파일 경로 업데이트 (필요한 경우만 - 루트 오브젝트나 OBJ 파일을 가진 오브젝트만)
            bool needsPathUpdate = !string.IsNullOrEmpty(objFilePath) || 
                                   !string.IsNullOrEmpty(originalPath) || 
                                   !string.IsNullOrEmpty(retouchedPath) ||
                                   objectType == ObjectType.ObjFile;
            
            if (needsPathUpdate && !UpdateObjFilePathIfNeeded())
            {
                // 경로를 찾지 못한 경우 null 반환 (ApplyCollection에서 처리)
                Debug.LogError($"[FindOrCreateGameObject] 경로 업데이트 실패: {objectName}");
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
                }
            }
            else
            {
                Debug.LogError($"[FindOrCreateGameObject] GameObject 생성 실패: {objectName}");
            }

            return newObj;
        }

        /// <summary>
        /// OBJ 파일 경로를 업데이트합니다 (필요한 경우).
        /// 경로를 찾지 못하면 사용자에게 경로를 지정하도록 요청합니다.
        /// </summary>
        bool UpdateObjFilePathIfNeeded()
        {
            // OBJ 파일 경로가 없으면 경로 업데이트 불필요
            bool hasObjPath = !string.IsNullOrEmpty(objFilePath) || 
                              !string.IsNullOrEmpty(originalPath) || 
                              !string.IsNullOrEmpty(retouchedPath);
            
            if (!hasObjPath && objectType != ObjectType.ObjFile)
            {
                return true; // OBJ 파일 경로가 없고 ObjFile 타입도 아니면 경로 업데이트 불필요
            }

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
                    return true;
                }
                else
                {
                    Debug.LogError($"[UpdateObjFilePathIfNeeded] 사용자가 지정한 경로가 유효하지 않음: {selectedPath ?? "(null)"}");
                    return false;
                }
            }
            #endif

            Debug.LogWarning($"[UpdateObjFilePathIfNeeded] 경로를 찾지 못함: {objectName}");
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

            GameObject primitive = null;
            
            // Unity 6에서는 CreatePrimitive가 직렬화 문제를 일으킬 수 있으므로
            // Unity 버전을 확인하여 처리 방식 결정
            bool useCreatePrimitive = true;
            
            #if UNITY_EDITOR
            // Unity 버전 파싱 (예: "6000.0.60f1" -> 6000)
            string unityVersion = Application.unityVersion;
            if (!string.IsNullOrEmpty(unityVersion))
            {
                string[] versionParts = unityVersion.Split('.');
                if (versionParts.Length > 0 && int.TryParse(versionParts[0], out int majorVersion))
                {
                    // Unity 6 이상에서는 CreatePrimitive가 직렬화 문제를 일으킬 수 있음
                    if (majorVersion >= 6000)
                    {
                        useCreatePrimitive = false;
                    }
                }
            }
            #endif

            if (useCreatePrimitive)
            {
                // Unity 5 이하에서는 CreatePrimitive 사용
                try
                {
                    primitive = GameObject.CreatePrimitive(primitiveTypeEnum.Value);
                    
                    // 즉시 HideFlags 설정 (Unity가 직렬화를 시도하기 전에)
                    #if UNITY_EDITOR
                    if (primitive != null)
                    {
                        primitive.hideFlags = HideFlags.None;
                    }
                    #endif
                    
                    // 이름 설정
                    if (primitive != null && !string.IsNullOrEmpty(objectName))
                    {
                        primitive.name = objectName;
                    }
                    
                    // 추가 HideFlags 설정 (Mesh, Material 등)
                    SetPrimitiveHideFlags(primitive);
                }
                catch (System.Exception ex)
                {
                    // CreatePrimitive 실패 시 수동 생성으로 전환
                    Debug.LogWarning($"[CreatePrimitiveObject] CreatePrimitive 실패: {primitiveType}, 오류: {ex.Message}");
                    useCreatePrimitive = false;
                }
            }
            
            // Unity 6이거나 CreatePrimitive 실패 시 수동 생성
            if (!useCreatePrimitive || primitive == null)
            {
                primitive = CreatePrimitiveMeshManually(primitiveTypeEnum.Value);
                if (primitive != null && !string.IsNullOrEmpty(objectName))
                {
                    primitive.name = objectName;
                }
            }
            
            return primitive;
        }
        
        /// <summary>
        /// Unity 6에서 CreatePrimitive가 직렬화 문제를 일으킬 때 사용하는 수동 primitive 생성 메서드
        /// </summary>
        GameObject CreatePrimitiveMeshManually(PrimitiveType primitiveType)
        {
            GameObject obj = new GameObject(objectName);
            #if UNITY_EDITOR
            obj.hideFlags = HideFlags.None;
            #endif
            
            MeshFilter meshFilter = obj.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = obj.AddComponent<MeshRenderer>();
            
            Mesh mesh = null;
            Collider collider = null;
            
            switch (primitiveType)
            {
                case PrimitiveType.Sphere:
                    mesh = CreateSphereMesh();
                    collider = obj.AddComponent<SphereCollider>();
                    break;
                case PrimitiveType.Cube:
                    mesh = CreateCubeMesh();
                    collider = obj.AddComponent<BoxCollider>();
                    break;
                case PrimitiveType.Cylinder:
                    mesh = CreateCylinderMesh();
                    var capsuleCollider = obj.AddComponent<CapsuleCollider>();
                    collider = capsuleCollider;
                    if (capsuleCollider != null)
                    {
                        // Cylinder는 CapsuleCollider를 사용하되 방향 조정
                        capsuleCollider.direction = 1; // Y축
                    }
                    break;
                case PrimitiveType.Capsule:
                    mesh = CreateCapsuleMesh();
                    collider = obj.AddComponent<CapsuleCollider>();
                    break;
                case PrimitiveType.Plane:
                    mesh = CreatePlaneMesh();
                    collider = obj.AddComponent<MeshCollider>();
                    break;
                default:
                    // 알 수 없는 타입은 빈 GameObject 반환
                    return obj;
            }
            
            if (mesh != null)
            {
                meshFilter.sharedMesh = mesh;
                #if UNITY_EDITOR
                mesh.hideFlags = HideFlags.None;
                #endif
            }
            
            // 기본 Material 할당
            Material material = new Material(Shader.Find("Standard"));
            meshRenderer.sharedMaterial = material;
            #if UNITY_EDITOR
            material.hideFlags = HideFlags.None;
            #endif
            
            return obj;
        }
        
        /// <summary>
        /// Sphere 메시를 수동으로 생성합니다.
        /// </summary>
        Mesh CreateSphereMesh()
        {
            // 간단한 구면 메시 생성 (세그먼트 수: 16)
            int segments = 16;
            int rings = 16;
            
            Mesh mesh = new Mesh();
            mesh.name = "Sphere";
            
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            List<Vector2> uvs = new List<Vector2>();
            
            // 정점 생성
            for (int ring = 0; ring <= rings; ring++)
            {
                float phi = Mathf.PI * ring / rings;
                float y = Mathf.Cos(phi);
                float radius = Mathf.Sin(phi);
                
                for (int seg = 0; seg <= segments; seg++)
                {
                    float theta = 2f * Mathf.PI * seg / segments;
                    float x = radius * Mathf.Cos(theta);
                    float z = radius * Mathf.Sin(theta);
                    
                    vertices.Add(new Vector3(x * 0.5f, y * 0.5f, z * 0.5f));
                    uvs.Add(new Vector2((float)seg / segments, (float)ring / rings));
                }
            }
            
            // 삼각형 생성
            for (int ring = 0; ring < rings; ring++)
            {
                for (int seg = 0; seg < segments; seg++)
                {
                    int current = ring * (segments + 1) + seg;
                    int next = current + segments + 1;
                    
                    triangles.Add(current);
                    triangles.Add(next);
                    triangles.Add(current + 1);
                    
                    triangles.Add(current + 1);
                    triangles.Add(next);
                    triangles.Add(next + 1);
                }
            }
            
            mesh.vertices = vertices.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            
            return mesh;
        }
        
        /// <summary>
        /// Cube 메시를 수동으로 생성합니다.
        /// </summary>
        Mesh CreateCubeMesh()
        {
            Mesh mesh = new Mesh();
            mesh.name = "Cube";
            
            // 8개 정점 (각 면마다 4개씩, 총 24개 정점 - 중복 허용)
            Vector3[] vertices = new Vector3[]
            {
                // 앞면
                new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f),
                new Vector3(0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f),
                // 뒷면
                new Vector3(0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f),
                // 위면
                new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f),
                new Vector3(0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f),
                // 아래면
                new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, -0.5f, 0.5f), new Vector3(-0.5f, -0.5f, 0.5f),
                // 오른쪽면
                new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, 0.5f),
                // 왼쪽면
                new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f, 0.5f),
                new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, -0.5f)
            };
            
            // UV 좌표
            Vector2[] uvs = new Vector2[]
            {
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1)
            };
            
            // 삼각형 인덱스 (각 면마다 2개 삼각형)
            int[] triangles = new int[]
            {
                0, 2, 1, 0, 3, 2,      // 앞면
                4, 6, 5, 4, 7, 6,      // 뒷면
                8, 10, 9, 8, 11, 10,   // 위면
                12, 14, 13, 12, 15, 14, // 아래면
                16, 18, 17, 16, 19, 18, // 오른쪽면
                20, 22, 21, 20, 23, 22  // 왼쪽면
            };
            
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            
            return mesh;
        }
        
        /// <summary>
        /// Cylinder 메시를 수동으로 생성합니다.
        /// </summary>
        Mesh CreateCylinderMesh()
        {
            int segments = 16;
            float height = 2f;
            float radius = 0.5f;
            
            Mesh mesh = new Mesh();
            mesh.name = "Cylinder";
            
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            List<Vector2> uvs = new List<Vector2>();
            
            // 상단 원
            vertices.Add(new Vector3(0, height / 2f, 0));
            uvs.Add(new Vector2(0.5f, 0.5f));
            for (int i = 0; i <= segments; i++)
            {
                float angle = 2f * Mathf.PI * i / segments;
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;
                vertices.Add(new Vector3(x, height / 2f, z));
                uvs.Add(new Vector2(0.5f + x / (2f * radius), 0.5f + z / (2f * radius)));
            }
            
            // 하단 원
            vertices.Add(new Vector3(0, -height / 2f, 0));
            uvs.Add(new Vector2(0.5f, 0.5f));
            int bottomCenterIndex = vertices.Count - 1;
            for (int i = 0; i <= segments; i++)
            {
                float angle = 2f * Mathf.PI * i / segments;
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;
                vertices.Add(new Vector3(x, -height / 2f, z));
                uvs.Add(new Vector2(0.5f + x / (2f * radius), 0.5f + z / (2f * radius)));
            }
            
            // 측면 정점
            for (int i = 0; i <= segments; i++)
            {
                float angle = 2f * Mathf.PI * i / segments;
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;
                vertices.Add(new Vector3(x, height / 2f, z));
                uvs.Add(new Vector2((float)i / segments, 1f));
                vertices.Add(new Vector3(x, -height / 2f, z));
                uvs.Add(new Vector2((float)i / segments, 0f));
            }
            
            // 상단 원 삼각형
            for (int i = 1; i <= segments; i++)
            {
                triangles.Add(0);
                triangles.Add(i + 1);
                triangles.Add(i);
            }
            
            // 하단 원 삼각형
            int bottomStart = segments + 2;
            for (int i = 1; i <= segments; i++)
            {
                triangles.Add(bottomCenterIndex);
                triangles.Add(bottomStart + i - 1);
                triangles.Add(bottomStart + i);
            }
            
            // 측면 삼각형
            int sideStart = bottomStart + segments + 1;
            for (int i = 0; i < segments; i++)
            {
                int baseIndex = sideStart + i * 2;
                triangles.Add(baseIndex);
                triangles.Add(baseIndex + 1);
                triangles.Add(baseIndex + 2);
                triangles.Add(baseIndex + 2);
                triangles.Add(baseIndex + 1);
                triangles.Add(baseIndex + 3);
            }
            
            mesh.vertices = vertices.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            
            return mesh;
        }
        
        /// <summary>
        /// Capsule 메시를 수동으로 생성합니다.
        /// </summary>
        Mesh CreateCapsuleMesh()
        {
            // Capsule은 상하 반구 + 중간 실린더로 구성
            int segments = 16;
            float height = 2f;
            float radius = 0.5f;
            
            Mesh mesh = new Mesh();
            mesh.name = "Capsule";
            
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            List<Vector2> uvs = new List<Vector2>();
            
            // 상단 반구
            for (int ring = 0; ring <= segments / 2; ring++)
            {
                float phi = Mathf.PI / 2f * ring / (segments / 2);
                float y = Mathf.Sin(phi) * radius + height / 2f;
                float ringRadius = Mathf.Cos(phi) * radius;
                
                for (int seg = 0; seg <= segments; seg++)
                {
                    float theta = 2f * Mathf.PI * seg / segments;
                    float x = Mathf.Cos(theta) * ringRadius;
                    float z = Mathf.Sin(theta) * ringRadius;
                    vertices.Add(new Vector3(x, y, z));
                    uvs.Add(new Vector2((float)seg / segments, (float)ring / (segments / 2)));
                }
            }
            
            // 하단 반구
            for (int ring = 0; ring <= segments / 2; ring++)
            {
                float phi = Mathf.PI / 2f * (1f + (float)ring / (segments / 2));
                float y = Mathf.Sin(phi) * radius - height / 2f;
                float ringRadius = Mathf.Cos(phi) * radius;
                
                for (int seg = 0; seg <= segments; seg++)
                {
                    float theta = 2f * Mathf.PI * seg / segments;
                    float x = Mathf.Cos(theta) * ringRadius;
                    float z = Mathf.Sin(theta) * ringRadius;
                    vertices.Add(new Vector3(x, y, z));
                    uvs.Add(new Vector2((float)seg / segments, 1f - (float)ring / (segments / 2)));
                }
            }
            
            // 삼각형 생성
            for (int ring = 0; ring < segments / 2; ring++)
            {
                for (int seg = 0; seg < segments; seg++)
                {
                    int current = ring * (segments + 1) + seg;
                    int next = current + segments + 1;
                    
                    triangles.Add(current);
                    triangles.Add(next);
                    triangles.Add(current + 1);
                    triangles.Add(current + 1);
                    triangles.Add(next);
                    triangles.Add(next + 1);
                }
            }
            
            // 하단 반구 삼각형
            int bottomStart = (segments / 2 + 1) * (segments + 1);
            for (int ring = 0; ring < segments / 2; ring++)
            {
                for (int seg = 0; seg < segments; seg++)
                {
                    int current = bottomStart + ring * (segments + 1) + seg;
                    int next = current + segments + 1;
                    
                    triangles.Add(current);
                    triangles.Add(current + 1);
                    triangles.Add(next);
                    triangles.Add(current + 1);
                    triangles.Add(next + 1);
                    triangles.Add(next);
                }
            }
            
            mesh.vertices = vertices.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            
            return mesh;
        }
        
        /// <summary>
        /// Plane 메시를 수동으로 생성합니다.
        /// </summary>
        Mesh CreatePlaneMesh()
        {
            Mesh mesh = new Mesh();
            mesh.name = "Plane";
            
            // Plane은 10x10 단위 크기
            float size = 10f;
            
            Vector3[] vertices = new Vector3[]
            {
                new Vector3(-size / 2f, 0, -size / 2f),
                new Vector3(size / 2f, 0, -size / 2f),
                new Vector3(size / 2f, 0, size / 2f),
                new Vector3(-size / 2f, 0, size / 2f)
            };
            
            Vector2[] uvs = new Vector2[]
            {
                new Vector2(0, 0),
                new Vector2(1, 0),
                new Vector2(1, 1),
                new Vector2(0, 1)
            };
            
            int[] triangles = new int[] { 0, 2, 1, 0, 3, 2 };
            
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            
            return mesh;
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
        /// Unity 6에서는 내장 리소스 접근이 실패할 수 있으므로 수동 생성 사용
        /// </summary>
        GameObject CreateQuadObject()
        {
            var quad = new GameObject(objectName);
            var meshFilter = quad.AddComponent<MeshFilter>();
            var meshRenderer = quad.AddComponent<MeshRenderer>();

            // Unity 6 (6000.0.0 이상)에서는 GetBuiltinResource가 실패할 수 있으므로
            // Unity 버전을 확인하여 처리 방식 결정
            bool useBuiltinResource = true;
            
            #if UNITY_EDITOR
            // Unity 버전 파싱 (예: "6000.0.60f1" -> 6000)
            string unityVersion = Application.unityVersion;
            if (!string.IsNullOrEmpty(unityVersion))
            {
                string[] versionParts = unityVersion.Split('.');
                if (versionParts.Length > 0 && int.TryParse(versionParts[0], out int majorVersion))
                {
                    // Unity 6 이상에서는 내장 리소스 접근이 불안정할 수 있음
                    if (majorVersion >= 6000)
                    {
                        useBuiltinResource = false;
                    }
                }
            }
            #endif

            Mesh quadMesh = null;
            
            if (useBuiltinResource)
            {
                // Unity 5 이하에서는 내장 리소스 사용 시도
                try
                {
                    quadMesh = Resources.GetBuiltinResource<Mesh>("Quad");
                }
                catch (System.Exception)
                {
                    // 내장 리소스 접근 실패 시 수동 생성으로 전환
                    useBuiltinResource = false;
                }
            }
            
            // 내장 리소스를 사용할 수 없는 경우 수동 생성
            if (!useBuiltinResource || quadMesh == null)
            {
                // Quad 메시를 수동 생성 (Unity 6 호환)
                var createdMesh = CreateQuadMesh();
                meshFilter.sharedMesh = createdMesh;
                #if UNITY_EDITOR
                createdMesh.hideFlags = HideFlags.None;
                #endif
            }
            else
            {
                meshFilter.sharedMesh = quadMesh;
                #if UNITY_EDITOR
                quadMesh.hideFlags = HideFlags.None;
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
            if (obj == null)
            {
                Debug.LogError($"[ApplyToGameObject] GameObject가 null: {objectName}");
                return;
            }
            
            // Transform 적용
            obj.transform.localPosition = GetPosition();
            obj.transform.localEulerAngles = GetRotation();
            obj.transform.localScale = GetScale();
            
            // Note: Children은 CreateGameObject()에서 이미 복원되므로 여기서는 복원하지 않음
            // ApplyToGameObject()는 주로 Import 시 Transform만 적용하는 용도로 사용됨
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
            string prefix = string.IsNullOrEmpty(logPrefix) ? "[Import]" : logPrefix;
            
            if (collection == null || collection.objects == null)
            {
                Debug.LogWarning($"{prefix} Collection이 null이거나 objects가 null입니다.");
                return new ImportResult { successCount = 0, failCount = 0 };
            }

            int successCount = 0;
            int failCount = 0;
            int skippedCount = 0;

            foreach (var data in collection.objects)
            {
                // OBJ 파일 경로가 있거나 ObjFile 타입인 경우 처리
                // (export 파일에서 objectType이 Empty(3)이지만 objFilePath가 있는 경우 처리)
                bool hasObjPath = !string.IsNullOrEmpty(data.objFilePath) || 
                                  !string.IsNullOrEmpty(data.originalPath) || 
                                  !string.IsNullOrEmpty(data.retouchedPath);
                
                if (data.objectType != ObjectType.ObjFile && !hasObjPath)
                {
                    // OBJ 파일 경로가 없고 ObjFile 타입도 아니면 건너뛰기
                    skippedCount++;
                    continue;
                }

                GameObject obj = data.FindOrCreateGameObject(createNew);
                
                if (obj != null)
                {
                    // CreateGameObject()에서 이미 Transform과 Children이 모두 적용되므로
                    // ApplyToGameObject()를 별도로 호출할 필요 없음
                    successCount++;
                }
                else
                {
                    Debug.LogError($"{prefix} [GameObject 생성 실패] {data.objectName}");
                    failCount++;
                }
            }

            Debug.Log($"{prefix} Import 완료: 성공 {successCount}개, 실패 {failCount}개, 건너뛰기 {skippedCount}개");
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
                originalPath = originalPath,
                retouchedPath = retouchedPath,
                isUsingRetouched = isUsingRetouched,
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
                originalPath = data.originalPath,
                retouchedPath = data.retouchedPath,
                isUsingRetouched = data.isUsingRetouched,
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
        public string objFilePath; // 하위 호환성 유지
        public string originalPath; // Original OBJ 파일 경로 (새로운 구조)
        public string retouchedPath; // Retouched OBJ 파일 경로 (새로운 구조)
        public bool isUsingRetouched; // 현재 Retouched 버전을 사용 중인지 여부
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

