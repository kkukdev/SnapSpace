using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ObjDropWatcher.ExportImport;

public class ObjectTransformExporterWindow : EditorWindow
{
    private enum ExportFormat { JSON, CSV, Binary }
    private List<GameObject> _collectedObjects = new List<GameObject>();
    private Vector2 _scroll;

    [MenuItem("Tools/Object Transform Exporter")]
    public static void Open()
    {
        var w = GetWindow<ObjectTransformExporterWindow>("Transform Exporter");
        w.minSize = new Vector2(400, 500);
        w.Show();
    }

    void OnGUI()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Object Transform Exporter/Importer", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("씬에 배치된 오브젝트들의 Transform 정보(position, rotation, scale)를 export/import합니다.", MessageType.Info);

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Collect Scene Objects", GUILayout.Height(30)))
        {
            CollectSceneObjects();
        }
        EditorGUILayout.EndHorizontal();

        if (_collectedObjects.Count > 0)
        {
            EditorGUILayout.HelpBox($"수집된 오브젝트: {_collectedObjects.Count}개", MessageType.Info);
            
            // 수집된 오브젝트 목록 표시
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Collected Objects:", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(150));
            foreach (var obj in _collectedObjects)
            {
                if (obj != null)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(obj.name);
                    if (GUILayout.Button("Select", GUILayout.Width(60)))
                    {
                        Selection.activeGameObject = obj;
                        EditorGUIUtility.PingObject(obj);
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.EndScrollView();
        }

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Export", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Export JSON", GUILayout.Height(25))) ExportObjects(ExportFormat.JSON);
        if (GUILayout.Button("Export CSV", GUILayout.Height(25))) ExportObjects(ExportFormat.CSV);
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Export Binary", GUILayout.Height(25))) ExportObjects(ExportFormat.Binary);
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Import", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Import JSON", GUILayout.Height(25))) ImportObjects(ExportFormat.JSON);
        if (GUILayout.Button("Import CSV", GUILayout.Height(25))) ImportObjects(ExportFormat.CSV);
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Import Binary", GUILayout.Height(25))) ImportObjects(ExportFormat.Binary);
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.EndVertical();
    }

    void CollectSceneObjects()
    {
        // OnGUI 중에 다이얼로그를 띄우면 레이아웃 상태가 꼬일 수 있으므로 delayCall 사용
        EditorApplication.delayCall += () =>
        {
            _collectedObjects.Clear();
            
            // OBJ 파일 검색 경로 설정
            SetupSearchPaths();
            
            // 씬의 모든 오브젝트 수집
            var allObjects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            
            foreach (var obj in allObjects)
            {
                // 루트 오브젝트만 수집 (자식은 제외)
                if (obj.transform.parent == null)
                {
                    // OBJ 파일인지 확인 (MeshFilter가 있고, OBJ 파일 경로를 찾을 수 있는 경우)
                    if (IsObjFile(obj))
                    {
                        _collectedObjects.Add(obj);
                    }
                }
            }

            EditorUtility.DisplayDialog("수집 완료", 
                $"{_collectedObjects.Count}개의 OBJ 오브젝트를 수집했습니다.\n이제 Export 버튼을 눌러 저장하세요.", "OK");
            Debug.Log($"[Object Collection] Collected {_collectedObjects.Count} OBJ objects from scene");
            Repaint();
        };
    }

    /// <summary>
    /// GameObject가 OBJ 파일인지 확인합니다.
    /// </summary>
    bool IsObjFile(GameObject obj)
    {
        if (obj == null) return false;

        // MeshFilter가 있어야 함
        if (!obj.TryGetComponent<MeshFilter>(out var meshFilter) || meshFilter.sharedMesh == null)
            return false;

        // OBJ 파일 경로를 찾을 수 있어야 함
        string objPath = ObjPathFinder.FindObjPath(obj);
        if (!string.IsNullOrEmpty(objPath) && File.Exists(objPath))
            return true;

        // 경로를 찾지 못했지만 메시 이름이 .obj로 끝나거나 OBJ 파일로 보이는 경우
        string meshName = meshFilter.sharedMesh.name;
        if (!string.IsNullOrEmpty(meshName) && 
            (meshName.EndsWith(".obj", StringComparison.OrdinalIgnoreCase) ||
             obj.name.EndsWith(".obj", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    void SetupSearchPaths()
    {
        var searchPaths = new List<string>();
        
        // 1. ObjDropWatcherWindow의 _items에서 경로 수집
        try
        {
            var watcherWindow = EditorWindow.GetWindow<ObjDropWatcherWindow>(false);
            if (watcherWindow != null)
            {
                // 리플렉션으로 _items 접근 시도 (private 필드)
                var itemsField = typeof(ObjDropWatcherWindow).GetField("_items", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (itemsField != null)
                {
                    var items = itemsField.GetValue(watcherWindow) as System.Collections.IList;
                    if (items != null)
                    {
                        foreach (var item in items)
                        {
                            var folderField = item.GetType().GetField("folder");
                            if (folderField != null)
                            {
                                string folder = folderField.GetValue(item) as string;
                                if (!string.IsNullOrEmpty(folder) && !searchPaths.Contains(folder))
                                {
                                    searchPaths.Add(folder);
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ObjectTransformExporter] Failed to get paths from ObjDropWatcherWindow: {ex.Message}");
        }
        
        // 2. 일반적인 storage 경로 추가
        string[] commonPaths = {
            Path.Combine(Application.dataPath, "..", "storage", "uploads"),
            Path.Combine(Application.dataPath, "..", "storage", "outputs", "final"),
            Path.Combine(Application.dataPath, "..", "storage", "outputs", "optimized"),
            Path.Combine(Application.dataPath, "..", "storage", "temp")
        };
        
        foreach (var path in commonPaths)
        {
            if (Directory.Exists(path) && !searchPaths.Contains(path))
            {
                searchPaths.Add(path);
            }
        }
        
        // 검색 경로 설정
        ObjPathFinder.SetSearchPaths(searchPaths);
        Debug.Log($"[ObjectTransformExporter] Set {searchPaths.Count} search paths for OBJ file lookup");
    }

    void ExportObjects(ExportFormat format)
    {
        // OnGUI 중에 다이얼로그를 띄우면 레이아웃 상태가 꼬일 수 있으므로 delayCall 사용
        EditorApplication.delayCall += () =>
        {
            if (_collectedObjects.Count == 0)
            {
                EditorUtility.DisplayDialog("Export 실패", 
                    "수집된 오브젝트가 없습니다.\n먼저 'Collect Scene Objects' 버튼을 눌러주세요.", "OK");
                return;
            }

            // OBJ 파일 검색 경로 설정
            SetupSearchPaths();

            string extension = format switch
            {
                ExportFormat.JSON => "json",
                ExportFormat.CSV => "csv",
                ExportFormat.Binary => "bin",
                _ => "dat"
            };

            string defaultName = $"ObjectTransforms_{DateTime.Now:yyyyMMdd_HHmmss}.{extension}";
            string defaultPath = Path.Combine(Application.dataPath, defaultName);
            
            string filePath = EditorUtility.SaveFilePanel("Export Object Transforms", 
                Application.dataPath, defaultName, extension);

            if (string.IsNullOrEmpty(filePath))
                return;

            try
            {
                switch (format)
                {
                    case ExportFormat.JSON:
                        JsonExporter.ExportToJson(_collectedObjects, filePath);
                        break;
                    case ExportFormat.CSV:
                        CsvExporter.ExportToCsv(_collectedObjects, filePath);
                        break;
                    case ExportFormat.Binary:
                        BinaryExporter.ExportToBinary(_collectedObjects, filePath);
                        break;
                }
                Repaint();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Export] Failed: {ex}");
                EditorUtility.DisplayDialog("Export 실패", $"Export 중 오류가 발생했습니다:\n{ex.Message}", "OK");
            }
        };
    }

    void ImportObjects(ExportFormat format)
    {
        // OnGUI 중에 다이얼로그를 띄우면 레이아웃 상태가 꼬일 수 있으므로 delayCall 사용
        EditorApplication.delayCall += () =>
        {
            // OBJ 파일 검색 경로 설정
            SetupSearchPaths();

            string extension = format switch
            {
                ExportFormat.JSON => "json",
                ExportFormat.CSV => "csv",
                ExportFormat.Binary => "bin",
                _ => "dat"
            };

            string filePath = EditorUtility.OpenFilePanel("Import Object Transforms", 
                Application.dataPath, extension);

            if (string.IsNullOrEmpty(filePath))
                return;

            try
            {
                ObjectTransformCollection collection = null;

                switch (format)
                {
                    case ExportFormat.JSON:
                        collection = JsonExporter.ImportFromJson(filePath);
                        break;
                    case ExportFormat.CSV:
                        collection = CsvExporter.ImportFromCsv(filePath);
                        break;
                    case ExportFormat.Binary:
                        collection = BinaryExporter.ImportFromBinary(filePath);
                        break;
                }

                if (collection == null)
                    return;

                // Import 옵션 선택
                bool createNew = EditorUtility.DisplayDialog("Import 옵션", 
                    "새 오브젝트를 생성하시겠습니까?\n\nYes: 새 오브젝트 생성\nNo: 기존 오브젝트에 Transform 적용", 
                    "새로 생성", "기존 오브젝트에 적용");

                switch (format)
                {
                    case ExportFormat.JSON:
                        JsonExporter.ApplyImportedData(collection, createNew);
                        break;
                    case ExportFormat.CSV:
                        CsvExporter.ApplyImportedData(collection, createNew);
                        break;
                    case ExportFormat.Binary:
                        BinaryExporter.ApplyImportedData(collection, createNew);
                        break;
                }
                Repaint();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Import] Failed: {ex}");
                EditorUtility.DisplayDialog("Import 실패", $"Import 중 오류가 발생했습니다:\n{ex.Message}", "OK");
            }
        };
    }
}

