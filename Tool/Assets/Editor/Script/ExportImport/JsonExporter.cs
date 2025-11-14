using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json;

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
                int skippedCount = 0;
                
                foreach (var obj in objects)
                {
                    if (obj == null) continue;
                    
                    string objPath = ObjectTransformData.GetObjPathForExport(obj);
                    
                    // OBJ 파일 경로가 없으면 건너뛰기
                    if (string.IsNullOrEmpty(objPath))
                    {
                        Debug.LogWarning($"[JSON Export] Skipping object '{obj.name}': OBJ file path not found");
                        skippedCount++;
                        continue;
                    }
                    
                    var data = new ObjectTransformData(obj, objPath, true);
                    
                    // OBJ 파일만 export
                    if (data.objectType == ObjectType.ObjFile)
                    {
                        collection.objects.Add(data);
                    }
                    else
                    {
                        Debug.LogWarning($"[JSON Export] Skipping non-OBJ object '{obj.name}': Type = {data.objectType}");
                        skippedCount++;
                    }
                }

                if (collection.objects.Count == 0)
                {
                    EditorUtility.DisplayDialog("Export 실패", 
                        "Export할 OBJ 오브젝트가 없습니다.\nOBJ 파일 경로를 찾을 수 있는 오브젝트만 export됩니다.", "OK");
                    return;
                }

                string json = JsonConvert.SerializeObject(collection, Formatting.Indented);
                File.WriteAllText(filePath, json);
                
                string message = $"JSON 형식으로 {collection.objects.Count}개의 OBJ 오브젝트를 export했습니다.";
                if (skippedCount > 0)
                {
                    message += $"\n({skippedCount}개의 오브젝트는 건너뛰었습니다)";
                }
                message += $"\n{filePath}";
                
                Debug.Log($"[JSON Export] Exported {collection.objects.Count} OBJ objects to: {filePath}");
                if (skippedCount > 0)
                {
                    Debug.Log($"[JSON Export] Skipped {skippedCount} objects");
                }
                
                EditorUtility.DisplayDialog("Export 완료", message, "OK");
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
                var collection = JsonConvert.DeserializeObject<ObjectTransformCollection>(json);
                
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

            var result = ObjectTransformData.ApplyCollection(collection, createNewObjects, "[JSON Import]");
            
            EditorUtility.DisplayDialog("Import 완료", 
                $"성공: {result.successCount}개\n실패: {result.failCount}개", "OK");
        }
    }
}

