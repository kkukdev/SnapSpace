using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ObjDropWatcher.ExportImport
{
    /// <summary>
    /// JSON 형식으로 오브젝트 Transform 정보를 export/import
    /// </summary>
    public static class JsonExporter
    {
        public static void ExportToJson(IEnumerable<GameObject> objects, string filePath)
        {
            try
            {
                var collection = new ObjectTransformCollection();
                
                foreach (var obj in objects)
                {
                    if (obj == null) continue;
                    
                    // 실제 OBJ 파일 경로 찾기
                    string objPath = ObjPathFinder.FindObjPath(obj);
                    if (string.IsNullOrEmpty(objPath))
                    {
                        // 찾지 못한 경우 MeshFilter의 메시 이름 사용 (하위 호환성)
                        var meshFilter = obj.GetComponent<MeshFilter>();
                        if (meshFilter != null && meshFilter.sharedMesh != null)
                        {
                            objPath = meshFilter.sharedMesh.name;
                        }
                    }
                    
                    collection.objects.Add(new ObjectTransformData(obj, objPath));
                }

                string json = JsonUtility.ToJson(collection, true);
                File.WriteAllText(filePath, json);
                
                Debug.Log($"[JSON Export] Exported {collection.objects.Count} objects to: {filePath}");
                EditorUtility.DisplayDialog("Export 완료", 
                    $"JSON 형식으로 {collection.objects.Count}개의 오브젝트를 export했습니다.\n{filePath}", "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JSON Export] Failed: {ex}");
                EditorUtility.DisplayDialog("Export 실패", $"JSON export 실패:\n{ex.Message}", "OK");
            }
        }

        public static ObjectTransformCollection ImportFromJson(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    EditorUtility.DisplayDialog("Import 실패", $"파일을 찾을 수 없습니다:\n{filePath}", "OK");
                    return null;
                }

                string json = File.ReadAllText(filePath);
                var collection = JsonUtility.FromJson<ObjectTransformCollection>(json);
                
                Debug.Log($"[JSON Import] Imported {collection.objects.Count} objects from: {filePath}");
                return collection;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JSON Import] Failed: {ex}");
                EditorUtility.DisplayDialog("Import 실패", $"JSON import 실패:\n{ex.Message}", "OK");
                return null;
            }
        }

        public static void ApplyImportedData(ObjectTransformCollection collection, bool createNewObjects = false)
        {
            if (collection == null || collection.objects == null) return;

            int successCount = 0;
            int failCount = 0;

            foreach (var data in collection.objects)
            {
                GameObject obj = null;

                if (createNewObjects)
                {
                    // 먼저 씬에 같은 이름의 오브젝트가 있는지 확인
                    obj = GameObject.Find(data.objectName);
                    
                    if (obj != null)
                    {
                        // 기존 오브젝트가 있으면 Transform만 적용하고 새로 생성하지 않음
                        Debug.Log($"[JSON Import] Found existing object '{data.objectName}', applying transform only");
                    }
                    else
                    {
                        // 기존 오브젝트가 없으면 새로 생성
                        // OBJ 파일 경로가 없거나 찾을 수 없는 경우를 위해 FindObjPathForImport 사용
                        if (data.objectType == ObjectType.ObjFile)
                        {
                            string objPath = ObjPathFinder.FindObjPathForImport(data.objectName, data.objFilePath);
                            if (!string.IsNullOrEmpty(objPath) && File.Exists(objPath))
                            {
                                data.objFilePath = objPath; // 경로 업데이트
                            }
                        }
                        
                        obj = data.CreateGameObject();
                        if (obj != null)
                        {
                            Debug.Log($"[JSON Import] Created object '{data.objectName}' (Type: {data.objectType}, Primitive: {data.primitiveType})");
                        }
                    }
                }
                else
                {
                    // 기존 오브젝트 찾기
                    obj = GameObject.Find(data.objectName);
                }

                if (obj != null)
                {
                    data.ApplyToGameObject(obj);
                    successCount++;
                }
                else
                {
                    Debug.LogWarning($"[JSON Import] Object not found: {data.objectName}");
                    failCount++;
                }
            }

            EditorUtility.DisplayDialog("Import 완료", 
                $"성공: {successCount}개\n실패: {failCount}개", "OK");
        }
    }
}

