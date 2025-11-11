using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ObjDropWatcher.ExportImport
{
    /// <summary>
    /// CSV 형식으로 오브젝트 Transform 정보를 export/import
    /// </summary>
    public static class CsvExporter
    {
        public static void ExportToCsv(IEnumerable<GameObject> objects, string filePath)
        {
            try
            {
                var sb = new StringBuilder();
                int skippedCount = 0;
                int exportedCount = 0;
                
                // 헤더 (하위 호환성을 위해 기존 필드 유지, 새로운 필드 추가)
                sb.AppendLine("ObjectName,PositionX,PositionY,PositionZ,RotationX,RotationY,RotationZ,ScaleX,ScaleY,ScaleZ,ObjFilePath,ObjectType,PrimitiveType");
                
                foreach (var obj in objects)
                {
                    if (obj == null) continue;
                    
                    string objPath = ObjectTransformData.GetObjPathForExport(obj);
                    
                    // OBJ 파일 경로가 없으면 건너뛰기
                    if (string.IsNullOrEmpty(objPath))
                    {
                        Debug.LogWarning($"[CSV Export] Skipping object '{obj.name}': OBJ file path not found");
                        skippedCount++;
                        continue;
                    }
                    
                    var data = new ObjectTransformData(obj, objPath, true);
                    
                    // OBJ 파일만 export
                    if (data.objectType != ObjectType.ObjFile)
                    {
                        Debug.LogWarning($"[CSV Export] Skipping non-OBJ object '{obj.name}': Type = {data.objectType}");
                        skippedCount++;
                        continue;
                    }
                    
                    var pos = data.position;
                    var rot = data.rotation;
                    var scale = data.scale;
                    
                    // CSV 이스케이프 처리
                    string name = EscapeCsvField(data.objectName);
                    string path = EscapeCsvField(data.objFilePath ?? "");
                    string objType = ((int)data.objectType).ToString();
                    string primitive = EscapeCsvField(data.primitiveType ?? "");
                    
                    sb.AppendLine($"{name}," +
                        $"{pos.x.ToString(CultureInfo.InvariantCulture)}," +
                        $"{pos.y.ToString(CultureInfo.InvariantCulture)}," +
                        $"{pos.z.ToString(CultureInfo.InvariantCulture)}," +
                        $"{rot.x.ToString(CultureInfo.InvariantCulture)}," +
                        $"{rot.y.ToString(CultureInfo.InvariantCulture)}," +
                        $"{rot.z.ToString(CultureInfo.InvariantCulture)}," +
                        $"{scale.x.ToString(CultureInfo.InvariantCulture)}," +
                        $"{scale.y.ToString(CultureInfo.InvariantCulture)}," +
                        $"{scale.z.ToString(CultureInfo.InvariantCulture)}," +
                        $"{path}," +
                        $"{objType}," +
                        $"{primitive}");
                    
                    exportedCount++;
                }

                if (exportedCount == 0)
                {
                    EditorUtility.DisplayDialog("Export 실패", 
                        "Export할 OBJ 오브젝트가 없습니다.\nOBJ 파일 경로를 찾을 수 있는 오브젝트만 export됩니다.", "OK");
                    return;
                }

                File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
                
                string message = $"CSV 형식으로 {exportedCount}개의 OBJ 오브젝트를 export했습니다.";
                if (skippedCount > 0)
                {
                    message += $"\n({skippedCount}개의 오브젝트는 건너뛰었습니다)";
                }
                message += $"\n{filePath}";
                
                Debug.Log($"[CSV Export] Exported {exportedCount} OBJ objects to: {filePath}");
                if (skippedCount > 0)
                {
                    Debug.Log($"[CSV Export] Skipped {skippedCount} objects");
                }
                
                EditorUtility.DisplayDialog("Export 완료", message, "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CSV Export] Failed: {ex}");
                EditorUtility.DisplayDialog("Export 실패", $"CSV export 실패:\n{ex.Message}", "OK");
            }
        }

        public static ObjectTransformCollection ImportFromCsv(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    EditorUtility.DisplayDialog("Import 실패", $"파일을 찾을 수 없습니다:\n{filePath}", "OK");
                    return null;
                }

                var collection = new ObjectTransformCollection();
                var lines = File.ReadAllLines(filePath, Encoding.UTF8);
                
                if (lines.Length < 2)
                {
                    EditorUtility.DisplayDialog("Import 실패", "CSV 파일이 비어있거나 헤더만 있습니다.", "OK");
                    return null;
                }

                // 헤더 스킵하고 데이터 파싱
                for (int i = 1; i < lines.Length; i++)
                {
                    var line = lines[i].Trim();
                    if (string.IsNullOrEmpty(line)) continue;

                    var fields = ParseCsvLine(line);
                    if (fields.Length < 10) continue;

                    try
                    {
                        var data = new ObjectTransformData
                        {
                            objectName = UnescapeCsvField(fields[0]),
                            position = new Vector3(
                                float.Parse(fields[1], CultureInfo.InvariantCulture),
                                float.Parse(fields[2], CultureInfo.InvariantCulture),
                                float.Parse(fields[3], CultureInfo.InvariantCulture)
                            ),
                            rotation = new Vector3(
                                float.Parse(fields[4], CultureInfo.InvariantCulture),
                                float.Parse(fields[5], CultureInfo.InvariantCulture),
                                float.Parse(fields[6], CultureInfo.InvariantCulture)
                            ),
                            scale = new Vector3(
                                float.Parse(fields[7], CultureInfo.InvariantCulture),
                                float.Parse(fields[8], CultureInfo.InvariantCulture),
                                float.Parse(fields[9], CultureInfo.InvariantCulture)
                            ),
                            objFilePath = fields.Length > 10 ? UnescapeCsvField(fields[10]) : null,
                            // 새로운 필드 (하위 호환성을 위해 기본값 사용)
                            objectType = fields.Length > 11 && int.TryParse(fields[11], out int type) ? (ObjectType)type : ObjectType.Unknown,
                            primitiveType = fields.Length > 12 ? UnescapeCsvField(fields[12]) : null
                        };
                        collection.objects.Add(data);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[CSV Import] Failed to parse line {i + 1}: {ex.Message}");
                    }
                }

                Debug.Log($"[CSV Import] Imported {collection.objects.Count} objects from: {filePath}");
                return collection;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CSV Import] Failed: {ex}");
                EditorUtility.DisplayDialog("Import 실패", $"CSV import 실패:\n{ex.Message}", "OK");
                return null;
            }
        }

        private static string EscapeCsvField(string field)
        {
            if (string.IsNullOrEmpty(field)) return "";
            
            // 쉼표, 따옴표, 줄바꿈이 있으면 따옴표로 감싸고 따옴표는 두 개로
            if (field.Contains(",") || field.Contains("\"") || field.Contains("\n") || field.Contains("\r"))
            {
                return "\"" + field.Replace("\"", "\"\"") + "\"";
            }
            return field;
        }

        private static string UnescapeCsvField(string field)
        {
            if (string.IsNullOrEmpty(field)) return "";
            
            // 따옴표로 감싸져 있으면 제거하고 이스케이프된 따옴표 복원
            if (field.StartsWith("\"") && field.EndsWith("\""))
            {
                field = field.Substring(1, field.Length - 2);
                field = field.Replace("\"\"", "\"");
            }
            return field;
        }

        private static string[] ParseCsvLine(string line)
        {
            var fields = new List<string>();
            bool inQuotes = false;
            var currentField = new StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        // 이스케이프된 따옴표
                        currentField.Append('"');
                        i++; // 다음 따옴표 스킵
                    }
                    else
                    {
                        // 따옴표 시작/끝
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    // 필드 구분자
                    fields.Add(currentField.ToString());
                    currentField.Clear();
                }
                else
                {
                    currentField.Append(c);
                }
            }

            // 마지막 필드 추가
            fields.Add(currentField.ToString());

            return fields.ToArray();
        }

        public static void ApplyImportedData(ObjectTransformCollection collection, bool createNewObjects = false)
        {
            if (collection == null || collection.objects == null) return;

            var result = ObjectTransformData.ApplyCollection(collection, createNewObjects, "[CSV Import]");
            
            EditorUtility.DisplayDialog("Import 완료", 
                $"성공: {result.successCount}개\n실패: {result.failCount}개", "OK");
        }
    }
}

