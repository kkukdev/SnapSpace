using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEditor;
using UnityEngine;

namespace ObjDropWatcher.ExportImport
{
    /// <summary>
    /// 바이너리 형식(BinaryFormatter)으로 오브젝트 Transform 정보를 export/import
    /// </summary>
    public static class BinaryExporter
    {
        public static void ExportToBinary(IEnumerable<GameObject> objects, string filePath)
        {
            // GameObject를 ObjectTransformData로 변환하여 collection 생성
            var collection = new ObjectTransformCollection();
            foreach (var obj in objects)
            {
                if (obj == null) continue;
                
                string objPath = ObjectTransformData.GetObjPathForExport(obj);
                if (string.IsNullOrEmpty(objPath)) continue;
                
                var data = new ObjectTransformData(obj, objPath, true);
                if (data.objectType == ObjectType.ObjFile)
                {
                    collection.objects.Add(data);
                }
            }
            
            ExportToBinary(collection, filePath);
        }
        
        public static void ExportToBinary(ObjectTransformCollection collection, string filePath)
        {
            if (collection == null || collection.objects == null)
            {
                EditorUtility.DisplayDialog("Export 실패", "Export할 데이터가 없습니다.", "OK");
                return;
            }
            
            ExportToBinary(collection.objects, filePath);
        }
        
        public static void ExportToBinary(IEnumerable<ObjectTransformData> dataList, string filePath)
        {
            try
            {
                var collection = new ObjectTransformCollection();
                int skippedCount = 0;
                
                foreach (var data in dataList)
                {
                    if (data == null) continue;
                    
                    // OBJ 파일만 export
                    if (data.objectType == ObjectType.ObjFile)
                    {
                        collection.objects.Add(data);
                    }
                    else
                    {
                        skippedCount++;
                    }
                }

                if (collection.objects.Count == 0)
                {
                    EditorUtility.DisplayDialog("Export 실패", 
                        "Export할 OBJ 오브젝트가 없습니다.\nOBJ 파일 경로를 찾을 수 있는 오브젝트만 export됩니다.", "OK");
                    return;
                }

                // BinaryFormatter용 직렬화 가능한 형태로 변환
                var serializableCollection = SerializableObjectTransformCollection.FromObjectTransformCollection(collection);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    var formatter = new BinaryFormatter();
                    formatter.Serialize(stream, serializableCollection);
                }

                int count = collection.objects.Count;
                string message = $"바이너리 형식으로 {count}개의 OBJ 오브젝트를 export했습니다.";
                if (skippedCount > 0)
                {
                    message += $"\n({skippedCount}개의 오브젝트는 건너뛰었습니다)";
                }
                message += $"\n{filePath}";
                
                EditorUtility.DisplayDialog("Export 완료", message, "OK");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Export 실패", $"바이너리 export 실패:\n{ex.Message}", "OK");
            }
        }

        public static ObjectTransformCollection ImportFromBinary(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    EditorUtility.DisplayDialog("Import 실패", $"파일을 찾을 수 없습니다:\n{filePath}", "OK");
                    return null;
                }

                SerializableObjectTransformCollection serializableCollection;
                using (var stream = new FileStream(filePath, FileMode.Open))
                {
                    var formatter = new BinaryFormatter();
                    serializableCollection = (SerializableObjectTransformCollection)formatter.Deserialize(stream);
                }

                // 직렬화 가능한 형태에서 일반 컬렉션으로 변환
                var collection = serializableCollection.ToObjectTransformCollection();

                return collection;
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Import 실패", $"바이너리 import 실패:\n{ex.Message}", "OK");
                return null;
            }
        }

        public static void ApplyImportedData(ObjectTransformCollection collection, bool createNewObjects = false)
        {
            if (collection == null || collection.objects == null) return;

            var result = ObjectTransformData.ApplyCollection(collection, createNewObjects, "[Binary Import]");
            
            EditorUtility.DisplayDialog("Import 완료", 
                $"성공: {result.successCount}개\n실패: {result.failCount}개", "OK");
        }
    }
}

