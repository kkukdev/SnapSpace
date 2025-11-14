using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ObjDropWatcher.ExportImport;
using Newtonsoft.Json;

public class ObjectTransformManagerWindow : EditorWindow
{
    private enum ExportFormat { JSON, CSV, Binary }
    
    [Serializable]
    class ManagedObjItem
    {
        public GameObject gameObject;
        public string objPath;  // 현재 사용 중인 경로
        public string originalPath;  // 원본 OBJ 파일 경로
        public string retouchedPath;  // 리터치된 OBJ 파일 경로
        public bool isUsingRetouched;  // 현재 retouched 버전을 사용 중인지 여부
        public string objectName;
        public Vector3 position;
        public Vector3 rotation;
        public Vector3 scale;
        
        public ManagedObjItem(GameObject obj, string path)
        {
            gameObject = obj;
            objPath = path;
            originalPath = path;  // 기본적으로 originalPath로 설정
            retouchedPath = null;
            isUsingRetouched = false;
            if (obj != null)
            {
                objectName = obj.name;
                position = obj.transform.position;
                rotation = obj.transform.eulerAngles;
                scale = obj.transform.localScale;
            }
        }
        
        public ManagedObjItem(GameObject obj, string original, string retouched)
        {
            gameObject = obj;
            originalPath = original;
            retouchedPath = retouched;
            objPath = original;  // 기본적으로 originalPath 사용
            isUsingRetouched = false;
            if (obj != null)
            {
                objectName = obj.name;
                position = obj.transform.position;
                rotation = obj.transform.eulerAngles;
                scale = obj.transform.localScale;
            }
        }
        
        public void UpdateTransform()
        {
            if (gameObject != null)
            {
                objectName = gameObject.name;
                position = gameObject.transform.position;
                rotation = gameObject.transform.eulerAngles;
                scale = gameObject.transform.localScale;
            }
        }
        
        // 편의 메서드 (ObjectTransformData와 일관성 유지)
        public Vector3 GetPosition() => position;
        public Vector3 GetRotation() => rotation;
        public Vector3 GetScale() => scale;
        
        // 현재 사용 중인 경로 반환 (폴더 경로에서 .obj 파일 찾기)
        public string GetCurrentPath()
        {
            if (isUsingRetouched && !string.IsNullOrEmpty(retouchedPath))
            {
                // retouchedPath가 파일이면 그대로 사용, 폴더면 .obj 파일 찾기
                if (File.Exists(retouchedPath))
                    return retouchedPath;
                if (Directory.Exists(retouchedPath))
                {
                    string objFile = FindObjFileInFolder(retouchedPath);
                    if (!string.IsNullOrEmpty(objFile))
                        return objFile;
                }
            }
            
            if (!string.IsNullOrEmpty(originalPath))
            {
                // originalPath가 파일이면 그대로 사용, 폴더면 .obj 파일 찾기
                if (File.Exists(originalPath))
                    return originalPath;
                if (Directory.Exists(originalPath))
                {
                    string objFile = FindObjFileInFolder(originalPath);
                    if (!string.IsNullOrEmpty(objFile))
                        return objFile;
                }
            }
            
            return originalPath;
        }
        
        // retouched 버전이 사용 가능한지 확인
        public bool HasRetouchedVersion()
        {
            if (string.IsNullOrEmpty(retouchedPath))
                return false;
            
            // 파일이면 존재 여부 확인
            if (File.Exists(retouchedPath))
                return true;
            
            // 폴더면 .obj 파일이 있는지 확인
            if (Directory.Exists(retouchedPath))
            {
                string objFile = FindObjFileInFolder(retouchedPath);
                return !string.IsNullOrEmpty(objFile);
            }
            
            return false;
        }
        
        // 폴더 안의 .obj 파일 찾기 (헬퍼 메서드) - 외부에서도 사용 가능하도록 public static으로 변경
        public static string FindObjFileInFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                return null;
            
            try
            {
                string[] objFiles = Directory.GetFiles(folderPath, "*.obj", SearchOption.TopDirectoryOnly);
                if (objFiles.Length > 0)
                {
                    return objFiles[0]; // 첫 번째 .obj 파일 반환
                }
            }
            catch
            {
                // 파일 검색 실패 시 무시
            }
            
            return null;
        }
        
    }
    
    private List<ManagedObjItem> _managedObjects = new List<ManagedObjItem>();
    private Vector2 _scroll;
    private Vector2 _listScroll;
    private string _manualPath = "";
    private bool _autoDetectOnSceneChange = true;
    
    /// <summary>
    /// 경로가 유효한지 확인 (파일 또는 폴더 안에 .obj 파일이 있는지)
    /// </summary>
    static bool PathExists(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        
        // 파일이면 존재 여부 확인
        if (File.Exists(path))
            return true;
        
        // 폴더면 .obj 파일이 있는지 확인
        if (Directory.Exists(path))
        {
            string objFile = ManagedObjItem.FindObjFileInFolder(path);
            return !string.IsNullOrEmpty(objFile);
        }
        
        return false;
    }
    
    /// <summary>
    /// 기본 경로를 가져옵니다 (WatchConfig 또는 storage 경로)
    /// </summary>
    string GetDefaultPath()
    {
        // 1. WatchConfig에서 projectRoot 가져오기 시도
        try
        {
            var configs = Resources.FindObjectsOfTypeAll<WatchConfig>();
            if (configs != null && configs.Length > 0)
            {
                var config = configs[0];
                if (config != null && !string.IsNullOrWhiteSpace(config.projectRoot))
                {
                    string projectRoot = config.projectRoot;
                    // 절대 경로인지 확인
                    if (Path.IsPathRooted(projectRoot))
                    {
                        if (Directory.Exists(projectRoot))
                        {
                            // storage/uploads 경로 확인
                            string uploadsPath = Path.Combine(projectRoot, "storage", "uploads");
                            if (Directory.Exists(uploadsPath))
                            {
                                return uploadsPath;
                            }
                            return projectRoot;
                        }
                    }
                    else
                    {
                        // 상대 경로인 경우
                        string fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", projectRoot));
                        if (Directory.Exists(fullPath))
                        {
                            // storage/uploads 경로 확인
                            string uploadsPath = Path.Combine(fullPath, "storage", "uploads");
                            if (Directory.Exists(uploadsPath))
                            {
                                return uploadsPath;
                            }
                            return fullPath;
                        }
                    }
                }
            }
        }
        catch
        {
            // WatchConfig 접근 실패 시 무시
        }
        
        // 2. 기본 storage 경로 사용
        string[] commonPaths = {
            Path.Combine(Application.dataPath, "..", "storage", "uploads"),
            Path.Combine(Application.dataPath, "..", "storage", "outputs"),
            Path.Combine(Application.dataPath, "..", "storage")
        };
        
        foreach (var path in commonPaths)
        {
            string fullPath = Path.GetFullPath(path);
            if (Directory.Exists(fullPath))
            {
                return fullPath;
            }
        }
        
        // 3. 기본값: Application.dataPath
        return Application.dataPath;
    }

    [MenuItem("Tools/Object Transform Manager")]
    public static void Open()
    {
        var w = GetWindow<ObjectTransformManagerWindow>("Object Transform Manager");
        w.minSize = new Vector2(500, 600);
        w.Show();
    }

    void OnEnable()
    {
        // 씬 변경 감지
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
        
        // 기본 경로 설정 (처음 열 때만)
        if (string.IsNullOrEmpty(_manualPath))
        {
            _manualPath = GetDefaultPath();
        }
        
        // 기존 항목들 중 경로가 없는 항목에 대해 경로 찾기 시도
        EditorApplication.delayCall += () =>
        {
            TryFindMissingPaths();
            
            // 씬에서 자동으로 OBJ 감지
            if (_autoDetectOnSceneChange)
            {
                AutoDetectSceneObjects();
            }
        };
    }
    
    /// <summary>
    /// 경로가 없는 관리 항목들에 대해 경로를 찾아서 업데이트합니다.
    /// </summary>
    void TryFindMissingPaths()
    {
        if (_managedObjects == null || _managedObjects.Count == 0)
            return;
        
        SetupSearchPaths();
        int foundCount = 0;
        
        foreach (var item in _managedObjects)
        {
            // 경로가 없거나 파일이 존재하지 않는 경우에만 찾기
            if (string.IsNullOrEmpty(item.objPath) || !PathExists(item.objPath))
            {
                string foundPath = null;
                
                // GameObject가 있는 경우 GameObject에서 경로 찾기
                if (item.gameObject != null)
                {
                    foundPath = ObjPathFinder.FindObjPath(item.gameObject);
                }
                // GameObject가 없지만 이름이 있는 경우 이름으로 찾기
                else if (!string.IsNullOrEmpty(item.objectName))
                {
                    foundPath = ObjPathFinder.FindObjPathForImport(item.objectName, null);
                }
                
                if (!string.IsNullOrEmpty(foundPath) && PathExists(foundPath))
                {
                    item.objPath = GetFolderPath(foundPath);
                    foundCount++;
                }
            }
        }
        
        if (foundCount > 0)
        {
            Repaint();
        }
    }

    void OnDisable()
    {
        EditorApplication.hierarchyChanged -= OnHierarchyChanged;
    }

    void OnHierarchyChanged()
    {
        // Hierarchy에서 삭제된 GameObject를 관리 목록에서 제거
        EditorApplication.delayCall += () =>
        {
            if (_managedObjects != null && _managedObjects.Count > 0)
            {
                bool removed = false;
                for (int i = _managedObjects.Count - 1; i >= 0; i--)
                {
                    var item = _managedObjects[i];
                    // GameObject가 null이거나 삭제된 경우 (Unity의 오버로드된 == 연산자 사용)
                    if (item.gameObject == null)
                    {
                        _managedObjects.RemoveAt(i);
                        removed = true;
                    }
                }
                
                if (removed)
                {
                    Repaint();
                }
            }
        };
        
        if (_autoDetectOnSceneChange)
        {
            EditorApplication.delayCall += AutoDetectSceneObjects;
        }
    }

    void OnGUI()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("OBJ Manager", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("씬에 배치된 OBJ 오브젝트들을 관리하고 Transform 정보를 export/import합니다.", MessageType.Info);

        EditorGUILayout.Space();
        
        // 자동 감지 설정
        EditorGUILayout.BeginHorizontal();
        _autoDetectOnSceneChange = EditorGUILayout.Toggle("자동 감지", _autoDetectOnSceneChange);
        if (GUILayout.Button("수동 감지", GUILayout.Width(100)))
        {
            AutoDetectSceneObjects();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

        // OBJ 관리 리스트
        EditorGUILayout.LabelField($"관리 중인 OBJ: {_managedObjects.Count}개", EditorStyles.boldLabel);
        
        _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.Height(250));
        
        for (int i = _managedObjects.Count - 1; i >= 0; i--)
        {
            var item = _managedObjects[i];
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            
            // 오브젝트 정보
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            
            if (item.gameObject != null)
            {
                EditorGUILayout.LabelField($"오브젝트: {item.gameObject.name}", EditorStyles.boldLabel);
                
                // 현재 사용 중인 경로 표시
                string currentPath = item.GetCurrentPath();
                string pathLabel = item.isUsingRetouched ? "경로 (Retouched):" : "경로 (Original):";
                EditorGUILayout.LabelField($"{pathLabel} {currentPath ?? "(경로 없음)"}", EditorStyles.miniLabel);
                
                // Original/Retouched 경로 정보 표시
                EditorGUILayout.BeginHorizontal();
                if (!string.IsNullOrEmpty(item.originalPath))
                {
                    bool originalExists = PathExists(item.originalPath);
                    string originalDisplayName = Directory.Exists(item.originalPath) 
                        ? $"[폴더] {Path.GetFileName(item.originalPath)}" 
                        : Path.GetFileName(item.originalPath);
                    string originalStatus = originalExists ? "✓" : "✗";
                    EditorGUILayout.LabelField($"Original: {originalDisplayName} {originalStatus}", EditorStyles.miniLabel);
                }
                else
                {
                    EditorGUILayout.LabelField("Original: (없음)", EditorStyles.miniLabel);
                }
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.BeginHorizontal();
                if (!string.IsNullOrEmpty(item.retouchedPath))
                {
                    bool retouchedExists = PathExists(item.retouchedPath);
                    string retouchedDisplayName = Directory.Exists(item.retouchedPath) 
                        ? $"[폴더] {Path.GetFileName(item.retouchedPath)}" 
                        : Path.GetFileName(item.retouchedPath);
                    string retouchedStatus = retouchedExists ? "✓" : "✗";
                    EditorGUILayout.LabelField($"Retouched: {retouchedDisplayName} {retouchedStatus}", EditorStyles.miniLabel);
                }
                else
                {
                    // retouchedPath가 없으면 찾기 시도 버튼 표시
                    EditorGUILayout.LabelField("Retouched: (없음)", EditorStyles.miniLabel);
                    if (GUILayout.Button("찾기", GUILayout.Width(50), GUILayout.Height(18)))
                    {
                        // retouched 경로 찾기 시도
                        string retouchedPath = FindRetouchedPath(item.originalPath, item.objPath);
                        if (!string.IsNullOrEmpty(retouchedPath) && PathExists(retouchedPath))
                        {
                            item.retouchedPath = retouchedPath;
                            Repaint();
                        }
                        else
                        {
                            EditorUtility.DisplayDialog("찾을 수 없음", "Retouched 파일을 찾을 수 없습니다.", "OK");
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();
                
                var itemPos = item.GetPosition();
                EditorGUILayout.LabelField($"위치: ({itemPos.x:F2}, {itemPos.y:F2}, {itemPos.z:F2})", EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.LabelField($"오브젝트: {item.objectName} (씬에 없음)", EditorStyles.boldLabel);
                string currentPath = item.GetCurrentPath();
                EditorGUILayout.LabelField($"경로: {currentPath ?? "(경로 없음)"}", EditorStyles.miniLabel);
                EditorGUILayout.HelpBox("씬에서 오브젝트를 찾을 수 없습니다.", MessageType.Warning);
            }
            
            EditorGUILayout.EndVertical();
            
            // 버튼들
            EditorGUILayout.BeginVertical(GUILayout.Width(200));
            
            if (item.gameObject != null)
            {
                if (GUILayout.Button("선택", GUILayout.Height(22)))
                {
                    Selection.activeGameObject = item.gameObject;
                    EditorGUIUtility.PingObject(item.gameObject);
                }
                
                // Original/Retouched 토글 버튼
                bool hasRetouched = item.HasRetouchedVersion();
                bool hasOriginal = !string.IsNullOrEmpty(item.originalPath) && PathExists(item.originalPath);
                
                // 둘 다 있어야 토글 가능
                GUI.enabled = hasRetouched && hasOriginal;
                
                string toggleButtonText;
                if (item.isUsingRetouched)
                {
                    toggleButtonText = "Original로 전환";
                }
                else
                {
                    toggleButtonText = "Retouched로 전환";
                }
                
                if (GUILayout.Button(toggleButtonText, GUILayout.Height(22)))
                {
                    ToggleObjVersion(item);
                }
                GUI.enabled = true;  // 다시 활성화
                
                if (!hasRetouched && !hasOriginal)
                {
                    EditorGUILayout.LabelField("(경로 없음)", EditorStyles.miniLabel);
                }
                else if (!hasRetouched)
                {
                    EditorGUILayout.LabelField("(Retouched 없음)", EditorStyles.miniLabel);
                }
                else if (!hasOriginal)
                {
                    EditorGUILayout.LabelField("(Original 없음)", EditorStyles.miniLabel);
                }
                
                // 경로가 없으면 "경로 찾기", 있으면 "경로 수정"
                string pathButtonText = string.IsNullOrEmpty(item.GetCurrentPath()) || !File.Exists(item.GetCurrentPath())
                    ? "경로 찾기"
                    : "경로 수정";
                
                if (GUILayout.Button(pathButtonText, GUILayout.Height(22)))
                {
                    // 먼저 자동으로 경로 찾기 시도
                    SetupSearchPaths();
                    string foundPath = ObjPathFinder.FindObjPath(item.gameObject);
                    
                    // 경로를 찾았고 파일이 존재하면 사용
                    if (!string.IsNullOrEmpty(foundPath) && PathExists(foundPath))
                    {
                        string folderPath = GetFolderPath(foundPath);
                        // 기존 originalPath가 없으면 originalPath로 설정
                        if (string.IsNullOrEmpty(item.originalPath))
                        {
                            item.originalPath = folderPath;
                        }
                        item.objPath = folderPath;
                        item.UpdateTransform();
                        Repaint();
                    }
                    else
                    {
                        // 경로를 찾지 못한 경우 수동으로 선택
                        string defaultPath = GetDefaultPath();
                        string startPath = !string.IsNullOrEmpty(item.GetCurrentPath()) && PathExists(item.GetCurrentPath())
                            ? Path.GetDirectoryName(item.GetCurrentPath())
                            : defaultPath;
                        
                        string newPath = EditorUtility.OpenFilePanel("OBJ 파일 선택", startPath, "obj");
                        if (!string.IsNullOrEmpty(newPath) && PathExists(newPath))
                        {
                            string folderPath = GetFolderPath(newPath);
                            // 기존 originalPath가 없으면 originalPath로 설정
                            if (string.IsNullOrEmpty(item.originalPath))
                            {
                                item.originalPath = folderPath;
                            }
                            item.objPath = folderPath;
                            item.UpdateTransform();
                            Repaint();
                        }
                    }
                }
            }
            else
            {
                if (GUILayout.Button("경로로 로드", GUILayout.Height(22)))
                {
                    LoadObjFromPath(item.objPath, item);
                }
                
                // 경로가 없는 경우 경로 찾기 버튼 추가
                if (string.IsNullOrEmpty(item.objPath) || !PathExists(item.objPath))
                {
                    if (GUILayout.Button("경로 찾기", GUILayout.Height(22)))
                    {
                        SetupSearchPaths();
                        string foundPath = ObjPathFinder.FindObjPathForImport(item.objectName, null);
                        if (!string.IsNullOrEmpty(foundPath) && PathExists(foundPath))
                        {
                            item.objPath = GetFolderPath(foundPath);
                            Repaint();
                        }
                        else
                        {
                            // 경로를 찾지 못한 경우 수동으로 선택
                            string defaultPath = GetDefaultPath();
                            string newPath = EditorUtility.OpenFilePanel("OBJ 파일 선택", defaultPath, "obj");
                            if (!string.IsNullOrEmpty(newPath) && PathExists(newPath))
                            {
                                item.objPath = GetFolderPath(newPath);
                                Repaint();
                            }
                        }
                    }
                }
            }
            
            if (GUILayout.Button("삭제", GUILayout.Height(22)))
            {
                // GameObject가 있으면 Hierarchy에서도 삭제
                if (item.gameObject != null)
                {
                    Undo.DestroyObjectImmediate(item.gameObject);
                }
                
                // 관리 목록에서 제거
                _managedObjects.RemoveAt(i);
                Repaint();
            }
            
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }
        
        if (_managedObjects.Count == 0)
        {
            EditorGUILayout.HelpBox("관리 중인 OBJ가 없습니다.\n'수동 감지' 버튼을 눌러 씬의 OBJ를 감지하거나, 아래에서 수동으로 추가하세요.", MessageType.Info);
        }
        
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

        // 수동 추가
        EditorGUILayout.LabelField("수동 추가", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("OBJ 파일 경로:", GUILayout.Width(100));
        _manualPath = EditorGUILayout.TextField(_manualPath);
        if (GUILayout.Button("찾기", GUILayout.Width(60)))
        {
            // 기본 경로 가져오기
            string defaultPath = GetDefaultPath();
            string path = EditorUtility.OpenFilePanel("OBJ 파일 선택", defaultPath, "obj");
            if (!string.IsNullOrEmpty(path))
            {
                _manualPath = path;
            }
        }
        EditorGUILayout.EndHorizontal();
        
        if (GUILayout.Button("경로로 추가", GUILayout.Height(25)))
        {
            AddObjByPath(_manualPath);
        }
        
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

        // Export
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

        // Import
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

    void AutoDetectSceneObjects()
    {
        // SetupSearchPaths는 선택적으로만 호출 (경로를 찾을 때만 필요)
        // ObjDropWatcherWindow 없이도 동작하도록 독립적으로 만들기
        
        var allObjects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
        int addedCount = 0;
        int updatedCount = 0;
        
        foreach (var obj in allObjects)
        {
            // 루트 오브젝트만 처리
            if (obj.transform.parent != null)
                continue;
            
            // OBJ 파일인지 확인 (경로와 무관하게 판단)
            if (!IsObjFile(obj))
                continue;
            
            // 이미 관리 중인지 확인
            var existing = _managedObjects.FirstOrDefault(m => m.gameObject == obj);
            if (existing != null)
            {
                // 기존 항목 업데이트
                existing.UpdateTransform();
                
                // 경로가 없으면 찾기 시도 (선택적)
                if (string.IsNullOrEmpty(existing.objPath) || !File.Exists(existing.objPath))
                {
                    SetupSearchPaths(); // 경로를 찾을 때만 SetupSearchPaths 호출
                    string objPath = ObjPathFinder.FindObjPath(obj);
                    if (!string.IsNullOrEmpty(objPath) && PathExists(objPath))
                    {
                        string folderPath = GetFolderPath(objPath);
                        existing.objPath = folderPath;
                        // originalPath가 없으면 설정 (경로가 존재하는 경우에만)
                        if (string.IsNullOrEmpty(existing.originalPath))
                        {
                            existing.originalPath = folderPath;
                        }
                        
                        // 찾은 경로가 retouched인지 확인
                        string normalizedPath = Path.GetFullPath(objPath).Replace('\\', '/');
                        if (normalizedPath.Contains("/outputs/", StringComparison.OrdinalIgnoreCase) &&
                            !normalizedPath.Contains("/uploads/", StringComparison.OrdinalIgnoreCase))
                        {
                            existing.isUsingRetouched = true;
                            // retouchedPath가 없으면 현재 경로를 retouchedPath로 설정 (경로가 존재하는 경우에만)
                            if (string.IsNullOrEmpty(existing.retouchedPath))
                            {
                                existing.retouchedPath = folderPath;
                            }
                        }
                        else
                        {
                            existing.isUsingRetouched = false;
                            // originalPath가 없으면 현재 경로를 originalPath로 설정 (경로가 존재하는 경우에만)
                            if (string.IsNullOrEmpty(existing.originalPath))
                            {
                                existing.originalPath = folderPath;
                            }
                        }
                    }
                }
                
                // retouchedPath가 없으면 찾기 시도
                if (string.IsNullOrEmpty(existing.retouchedPath) || !PathExists(existing.retouchedPath))
                {
                    string retouchedPath = FindRetouchedPath(existing.originalPath, existing.objPath);
                    if (!string.IsNullOrEmpty(retouchedPath) && PathExists(retouchedPath))
                    {
                        existing.retouchedPath = retouchedPath;
                    }
                }
                
                // 현재 objPath가 retouched인지 다시 확인 (경로가 업데이트된 후)
                if (!string.IsNullOrEmpty(existing.objPath))
                {
                    string normalizedPath = Path.GetFullPath(existing.objPath).Replace('\\', '/');
                    if (normalizedPath.Contains("/outputs/", StringComparison.OrdinalIgnoreCase) &&
                        !normalizedPath.Contains("/uploads/", StringComparison.OrdinalIgnoreCase))
                    {
                        existing.isUsingRetouched = true;
                    }
                    else if (normalizedPath.Contains("/uploads/", StringComparison.OrdinalIgnoreCase))
                    {
                        existing.isUsingRetouched = false;
                    }
                }
                
                updatedCount++;
            }
            else
            {
                // 새 항목 추가
                // 경로는 나중에 찾거나 수정할 수 있으므로, 우선 null로 추가
                string objPath = null;
                
                // 경로를 찾을 수 있으면 찾기 시도 (선택적)
                try
                {
                    SetupSearchPaths();
                    objPath = ObjPathFinder.FindObjPath(obj);
                    if (string.IsNullOrEmpty(objPath) || !File.Exists(objPath))
                    {
                        objPath = null; // 경로를 찾지 못했어도 추가 (나중에 수정 가능)
                    }
                }
                catch
                {
                    // 경로 찾기 실패해도 계속 진행
                    objPath = null;
                }
                
                // objPath를 폴더 경로로 변환
                string folderPath = GetFolderPath(objPath);
                var newItem = new ManagedObjItem(obj, folderPath);
                
                // 찾은 경로가 retouched인지 확인 (파일명 기반 우선 확인)
                bool isRetouched = false;
                if (!string.IsNullOrEmpty(objPath))
                {
                    // 1. 파일명에 retouched suffix가 있으면 retouched로 간주
                    if (IsRetouchedFileName(objPath))
                    {
                        isRetouched = true;
                    }
                    // 2. 경로에 "outputs"가 포함되어 있고 "uploads"가 없으면 retouched로 간주
                    else
                    {
                        string normalizedPath = Path.GetFullPath(objPath).Replace('\\', '/');
                        if (normalizedPath.Contains("/outputs/", StringComparison.OrdinalIgnoreCase) &&
                            !normalizedPath.Contains("/uploads/", StringComparison.OrdinalIgnoreCase))
                        {
                            isRetouched = true;
                        }
                    }
                    
                    if (isRetouched)
                    {
                        newItem.isUsingRetouched = true;
                        // retouchedPath가 없으면 현재 경로를 retouchedPath로 설정
                        if (string.IsNullOrEmpty(newItem.retouchedPath))
                        {
                            newItem.retouchedPath = folderPath;
                        }
                        // originalPath 찾기 시도
                        if (string.IsNullOrEmpty(newItem.originalPath))
                        {
                            // retouched 경로에서 original 경로 추론
                            string originalPath = objPath.Replace("/outputs/final/", "/uploads/");
                            originalPath = originalPath.Replace("\\outputs\\final\\", "\\uploads\\");
                            // 파일명에서 retouched suffix 제거 시도
                            string fileName = Path.GetFileName(originalPath);
                            if (fileName.Contains("_cleaned_auto_flat"))
                            {
                                fileName = fileName.Replace("_cleaned_auto_flat", "");
                                originalPath = Path.Combine(Path.GetDirectoryName(originalPath), fileName);
                            }
                            else if (fileName.Contains("_cleaned_auto"))
                            {
                                fileName = fileName.Replace("_cleaned_auto", "");
                                originalPath = Path.Combine(Path.GetDirectoryName(originalPath), fileName);
                            }
                            else if (fileName.Contains("_cleaned"))
                            {
                                fileName = fileName.Replace("_cleaned", "");
                                originalPath = Path.Combine(Path.GetDirectoryName(originalPath), fileName);
                            }
                            
                            if (PathExists(originalPath))
                            {
                                newItem.originalPath = GetFolderPath(originalPath);
                            }
                        }
                    }
                    else
                    {
                        newItem.isUsingRetouched = false;
                        // originalPath가 없으면 현재 경로를 originalPath로 설정 (경로가 존재하는 경우에만)
                        if (string.IsNullOrEmpty(newItem.originalPath) && PathExists(objPath))
                        {
                            newItem.originalPath = folderPath;
                        }
                    }
                }
                
                // retouchedPath 찾기 시도
                if (string.IsNullOrEmpty(newItem.retouchedPath))
                {
                    string retouchedPath = FindRetouchedPath(newItem.originalPath, objPath);
                    if (!string.IsNullOrEmpty(retouchedPath) && PathExists(retouchedPath))
                    {
                        newItem.retouchedPath = retouchedPath;
                    }
                }
                
                _managedObjects.Add(newItem);
                addedCount++;
            }
        }
        
        if (addedCount > 0 || updatedCount > 0)
        {
            Repaint();
        }
    }

    void AddObjByPath(string objPath)
    {
        if (string.IsNullOrEmpty(objPath))
        {
            EditorUtility.DisplayDialog("경로 없음", "OBJ 파일 경로를 입력하거나 선택해주세요.", "OK");
            return;
        }
        
        // objPath가 폴더 경로일 수 있으므로 PathExists로 확인
        if (!PathExists(objPath))
        {
            EditorUtility.DisplayDialog("파일 없음", $"파일 또는 폴더를 찾을 수 없습니다:\n{objPath}", "OK");
            return;
        }
        
        // 이미 추가된 경로인지 확인 (폴더 경로로 비교)
        string folderPathToCheck = Directory.Exists(objPath) ? objPath : Path.GetDirectoryName(objPath);
        if (_managedObjects.Any(m => m.objPath == folderPathToCheck))
        {
            EditorUtility.DisplayDialog("이미 추가됨", "이 경로는 이미 관리 목록에 있습니다.", "OK");
            return;
        }
        
        // OBJ 파일 로드
        // objPath가 폴더 경로일 수 있으므로 파일 경로로 변환
        string actualObjPath = objPath;
        if (Directory.Exists(objPath))
        {
            string[] objFiles = Directory.GetFiles(objPath, "*.obj", SearchOption.TopDirectoryOnly);
            if (objFiles.Length > 0)
            {
                actualObjPath = objFiles[0];
            }
            else
            {
                EditorUtility.DisplayDialog("파일 없음", $"폴더에 OBJ 파일을 찾을 수 없습니다:\n{objPath}", "OK");
                return;
            }
        }
        
        try
        {
            // preserveOriginalCoordinates: true로 설정하여 원본 좌표 유지
            var go = RuntimeObjLoader.LoadObj(actualObjPath, preserveOriginalCoordinates: true);
            if (go != null)
            {
                // 경로 정보 저장 (루트와 children 모두) - 실제 파일 경로 사용
                ObjPathInfo.SetPath(go, actualObjPath);
                
                // 모든 children에도 경로 저장
                Transform[] allChildren = go.GetComponentsInChildren<Transform>(true);
                foreach (Transform child in allChildren)
                {
                    if (child != null && child != go.transform && child.gameObject != null)
                    {
                        ObjPathInfo.SetPath(child.gameObject, actualObjPath);
                    }
                }
                
                // MeshRenderer와 Material 확인 및 강제 설정
                // 루트와 children 모두 처리
                MeshRenderer[] allMeshRenderers = go.GetComponentsInChildren<MeshRenderer>(true);
                
                foreach (var meshRenderer in allMeshRenderers)
                {
                    if (meshRenderer == null) continue;
                    
                    // Material이 제대로 할당되었는지 확인
                    if (meshRenderer.sharedMaterials == null || meshRenderer.sharedMaterials.Length == 0)
                    {
                        // Material이 없으면 기본 Material 생성
                        var defaultMat = new Material(Shader.Find("Standard"));
                        defaultMat.color = Color.white;
                        meshRenderer.sharedMaterial = defaultMat;
                    }
                    else
                    {
                        // 기존 Material들의 renderQueue를 Geometry로 강제 설정
                        var materials = meshRenderer.sharedMaterials;
                        bool materialFixed = false;
                        
                        for (int i = 0; i < materials.Length; i++)
                        {
                            if (materials[i] != null)
                            {
                                if (materials[i].renderQueue >= (int)UnityEngine.Rendering.RenderQueue.Transparent)
                                {
                                    materials[i].renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
                                    materialFixed = true;
                                }
                                
                                if (materials[i].HasProperty("_Surface"))
                                {
                                    materials[i].SetFloat("_Surface", 0);
                                    materialFixed = true;
                                }
                                
                                if (materials[i].IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"))
                                {
                                    materials[i].DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                                    materialFixed = true;
                                }
                                
                                Color matColor = materials[i].color;
                                if (matColor.a < 0.999f)
                                {
                                    materials[i].color = new Color(matColor.r, matColor.g, matColor.b, 1f);
                                    materialFixed = true;
                                }
                                
                                #if UNITY_EDITOR
                                EditorUtility.SetDirty(materials[i]);
                                #endif
                            }
                        }
                        
                        if (materialFixed)
                        {
                            meshRenderer.sharedMaterials = materials;
                        }
                    }
                    
                    // MeshRenderer 활성화 확인
                    meshRenderer.enabled = true;
                    
                    #if UNITY_EDITOR
                    EditorUtility.SetDirty(meshRenderer);
                    #endif
                }
                
                // MeshFilter도 처리 (루트와 children 모두)
                MeshFilter[] allMeshFilters = go.GetComponentsInChildren<MeshFilter>(true);
                foreach (var meshFilter in allMeshFilters)
                {
                    if (meshFilter != null && meshFilter.sharedMesh != null)
                    {
                        #if UNITY_EDITOR
                        EditorUtility.SetDirty(meshFilter);
                        #endif
                    }
                }
                
                // GameObject 활성화 확인
                go.SetActive(true);
                
                // Transform 강제 동기화
                go.transform.hasChanged = true;
                
                #if UNITY_EDITOR
                // Unity 에디터에서 즉시 반영
                EditorUtility.SetDirty(go);
                
                // SceneView 강제 업데이트
                SceneView.RepaintAll();
                #endif
                
                // 메모를 children으로 생성 (OBJ 파일 경로에서 memo.txt 찾기)
                try
                {
                    var memos = MemoUtils.FindAndParseMemoFile(actualObjPath);
                    if (memos != null && memos.Length > 0)
                    {
                        float unitScale = MemoUtils.GetUnitScale();
                        MemoUtils.SpawnMemosAsChildren(go, memos, unitScale);
                    }
                }
                catch (Exception)
                {
                    // 메모 스폰 실패 시 무시
                }
                
                // 폴더 경로로 저장
                string folderPath = GetFolderPath(actualObjPath);
                _managedObjects.Add(new ManagedObjItem(go, folderPath));
                Undo.RegisterCreatedObjectUndo(go, "Load OBJ");
                Selection.activeGameObject = go;
                EditorGUIUtility.PingObject(go);
                _manualPath = ""; // 입력 필드 초기화
                Repaint();
            }
        }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog("로드 실패", $"OBJ 파일을 로드할 수 없습니다:\n{ex.Message}", "OK");
        }
    }

    void LoadObjFromPath(string objPath, ManagedObjItem item)
    {
        // objPath가 폴더 경로일 수 있으므로 파일 경로로 변환
        string actualObjPath = objPath;
        if (Directory.Exists(objPath))
        {
            string[] objFiles = Directory.GetFiles(objPath, "*.obj", SearchOption.TopDirectoryOnly);
            if (objFiles.Length > 0)
            {
                actualObjPath = objFiles[0];
            }
            else
            {
                EditorUtility.DisplayDialog("파일 없음", $"폴더에 OBJ 파일을 찾을 수 없습니다:\n{objPath}", "OK");
                return;
            }
        }
        
        if (string.IsNullOrEmpty(actualObjPath) || !File.Exists(actualObjPath))
        {
            EditorUtility.DisplayDialog("파일 없음", $"파일을 찾을 수 없습니다:\n{actualObjPath}", "OK");
            return;
        }
        
        try
        {
            // preserveOriginalCoordinates: true로 설정하여 원본 좌표 유지
            var go = RuntimeObjLoader.LoadObj(actualObjPath, preserveOriginalCoordinates: true);
            if (go != null)
            {
                // Transform 정보 복원
                go.transform.position = item.GetPosition();
                go.transform.eulerAngles = item.GetRotation();
                go.transform.localScale = item.GetScale();
                go.name = item.objectName;
                
                // 경로 정보 저장 (루트와 children 모두) - 실제 파일 경로 사용
                ObjPathInfo.SetPath(go, actualObjPath);
                
                // 모든 children에도 경로 저장
                Transform[] allChildren = go.GetComponentsInChildren<Transform>(true);
                foreach (Transform child in allChildren)
                {
                    if (child != null && child != go.transform && child.gameObject != null)
                    {
                        ObjPathInfo.SetPath(child.gameObject, actualObjPath);
                    }
                }
                
                // MeshRenderer와 Material 확인 및 강제 설정
                // 루트와 children 모두 처리
                MeshRenderer[] allMeshRenderers = go.GetComponentsInChildren<MeshRenderer>(true);
                
                foreach (var meshRenderer in allMeshRenderers)
                {
                    if (meshRenderer == null) continue;
                    
                    // Material이 제대로 할당되었는지 확인
                    if (meshRenderer.sharedMaterials == null || meshRenderer.sharedMaterials.Length == 0)
                    {
                        // Material이 없으면 기본 Material 생성
                        var defaultMat = new Material(Shader.Find("Standard"));
                        defaultMat.color = Color.white;
                        meshRenderer.sharedMaterial = defaultMat;
                    }
                    else
                    {
                        // 기존 Material들의 renderQueue를 Geometry로 강제 설정
                        var materials = meshRenderer.sharedMaterials;
                        bool materialFixed = false;
                        
                        for (int i = 0; i < materials.Length; i++)
                        {
                            if (materials[i] != null)
                            {
                                if (materials[i].renderQueue >= (int)UnityEngine.Rendering.RenderQueue.Transparent)
                                {
                                    materials[i].renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
                                    materialFixed = true;
                                }
                                
                                if (materials[i].HasProperty("_Surface"))
                                {
                                    materials[i].SetFloat("_Surface", 0);
                                    materialFixed = true;
                                }
                                
                                if (materials[i].IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"))
                                {
                                    materials[i].DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                                    materialFixed = true;
                                }
                                
                                Color matColor = materials[i].color;
                                if (matColor.a < 0.999f)
                                {
                                    materials[i].color = new Color(matColor.r, matColor.g, matColor.b, 1f);
                                    materialFixed = true;
                                }
                                
                                #if UNITY_EDITOR
                                EditorUtility.SetDirty(materials[i]);
                                #endif
                            }
                        }
                        
                        if (materialFixed)
                        {
                            meshRenderer.sharedMaterials = materials;
                        }
                    }
                    
                    // MeshRenderer 활성화 확인
                    meshRenderer.enabled = true;
                    
                    #if UNITY_EDITOR
                    EditorUtility.SetDirty(meshRenderer);
                    #endif
                }
                
                // MeshFilter도 처리 (루트와 children 모두)
                MeshFilter[] allMeshFilters = go.GetComponentsInChildren<MeshFilter>(true);
                foreach (var meshFilter in allMeshFilters)
                {
                    if (meshFilter != null && meshFilter.sharedMesh != null)
                    {
                        #if UNITY_EDITOR
                        EditorUtility.SetDirty(meshFilter);
                        #endif
                    }
                }
                
                // GameObject 활성화 확인
                go.SetActive(true);
                
                // Transform 강제 동기화
                go.transform.hasChanged = true;
                
                #if UNITY_EDITOR
                // Unity 에디터에서 즉시 반영
                EditorUtility.SetDirty(go);
                
                // SceneView 강제 업데이트
                SceneView.RepaintAll();
                #endif
                
                // 메모를 children으로 생성 (OBJ 파일 경로에서 memo.txt 찾기)
                try
                {
                    var memos = MemoUtils.FindAndParseMemoFile(actualObjPath);
                    if (memos != null && memos.Length > 0)
                    {
                        float unitScale = MemoUtils.GetUnitScale();
                        MemoUtils.SpawnMemosAsChildren(go, memos, unitScale);
                    }
                }
                catch (Exception)
                {
                    // 메모 스폰 실패 시 무시
                }
                
                item.gameObject = go;
                Undo.RegisterCreatedObjectUndo(go, "Load OBJ from Manager");
                Selection.activeGameObject = go;
                EditorGUIUtility.PingObject(go);
                Repaint();
            }
        }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog("로드 실패", $"OBJ 파일을 로드할 수 없습니다:\n{ex.Message}", "OK");
        }
    }

    /// <summary>
    /// GameObject에 originalPath와 retouchedPath 정보를 설정합니다.
    /// ObjDropWatcherWindow에서 호출하여 경로 정보를 전달합니다.
    /// 항목이 없으면 자동으로 추가합니다.
    /// </summary>
    /// <param name="obj">GameObject</param>
    /// <param name="originalPath">원본 OBJ 파일 경로</param>
    /// <param name="retouchedPath">리터치된 OBJ 파일 경로</param>
    /// <param name="currentLoadedPath">현재 로드된 OBJ 파일 경로 (retouched인지 original인지 판단용)</param>
    public static void SetObjPaths(GameObject obj, string originalPath, string retouchedPath = null, string currentLoadedPath = null)
    {
        if (obj == null)
            return;
        
        // DontSaveInEditor 플래그가 있는 객체는 직렬화에서 제외
        try
        {
            if ((obj.hideFlags & HideFlags.DontSaveInEditor) != 0)
            {
                // DontSaveInEditor 플래그가 있으면 Resources.FindObjectsOfTypeAll 사용 시 문제 발생 가능
                // EditorWindow를 직접 찾는 방식으로 변경
                var windows = UnityEngine.Resources.FindObjectsOfTypeAll<ObjectTransformManagerWindow>()
                    .Where(w => w != null && (w.hideFlags & HideFlags.DontSaveInEditor) == 0)
                    .ToList();
                
                foreach (var window in windows)
                {
                    if (window._managedObjects == null)
                        continue;
                    
                    ProcessObjPaths(window, obj, originalPath, retouchedPath, currentLoadedPath);
                    break;
                }
                return;
            }
        }
        catch
        {
            // 플래그 확인 실패 시 무시하고 계속 진행
        }
        
        // 모든 열려있는 ObjectTransformManagerWindow 인스턴스 찾기
        var allWindows = UnityEngine.Resources.FindObjectsOfTypeAll<ObjectTransformManagerWindow>();
        foreach (var window in allWindows)
        {
            if (window == null || window._managedObjects == null)
                continue;
            
            // DontSaveInEditor 플래그가 있는 윈도우는 건너뛰기
            try
            {
                if ((window.hideFlags & HideFlags.DontSaveInEditor) != 0)
                    continue;
            }
            catch
            {
                // 플래그 확인 실패 시 계속 진행
            }
            
            ProcessObjPaths(window, obj, originalPath, retouchedPath, currentLoadedPath);
            break;
        }
    }
    
    /// <summary>
    /// 파일 경로를 폴더 경로로 변환합니다 (파일이면 디렉토리 반환, 폴더면 그대로 반환)
    /// </summary>
    static string GetFolderPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;
        
        if (File.Exists(path))
        {
            return Path.GetDirectoryName(path);
        }
        else if (Directory.Exists(path))
        {
            return path;
        }
        
        // 존재하지 않으면 파일 경로로 가정하고 디렉토리 반환
        return Path.GetDirectoryName(path);
    }
    
    /// <summary>
    /// 파일명이나 경로가 retouched 버전인지 확인합니다 (_cleaned_auto_flat, _cleaned, _retouched 등의 suffix 확인)
    /// </summary>
    static bool IsRetouchedFileName(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        
        // 파일명 추출
        string fileName = Path.GetFileName(path);
        if (string.IsNullOrEmpty(fileName))
            fileName = path;
        
        // retouched를 나타내는 suffix 패턴들
        string[] retouchedSuffixes = { "_cleaned_auto_flat", "_cleaned_auto", "_cleaned", "_retouched", "_retouch" };
        
        foreach (var suffix in retouchedSuffixes)
        {
            if (fileName.Contains(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// 윈도우에 경로 정보를 처리하는 헬퍼 메서드
    /// </summary>
    static void ProcessObjPaths(ObjectTransformManagerWindow window, GameObject obj, string originalPath, string retouchedPath, string currentLoadedPath = null)
    {
        // 경로를 폴더 경로로 변환 (파일 경로면 디렉토리로 변환)
        string originalFolderPath = GetFolderPath(originalPath);
        string retouchedFolderPath = GetFolderPath(retouchedPath);
        string currentLoadedFolderPath = GetFolderPath(currentLoadedPath);
        
        // 현재 로드된 경로가 retouched인지 확인
        bool isCurrentlyRetouched = false;
        if (!string.IsNullOrEmpty(currentLoadedFolderPath))
        {
            string normalizedCurrentPath = Path.GetFullPath(currentLoadedFolderPath).Replace('\\', '/');
            
            // 0. 파일명에 retouched suffix가 있으면 retouched로 간주 (가장 우선)
            if (IsRetouchedFileName(currentLoadedPath))
            {
                isCurrentlyRetouched = true;
            }
            // 1. currentLoadedPath가 retouchedPath와 일치하면 retouched 버전
            else if (!string.IsNullOrEmpty(retouchedFolderPath))
            {
                string normalizedRetouchedPath = Path.GetFullPath(retouchedFolderPath).Replace('\\', '/');
                if (normalizedCurrentPath.Equals(normalizedRetouchedPath, StringComparison.OrdinalIgnoreCase))
                {
                    isCurrentlyRetouched = true;
                }
            }
            
            // 2. currentLoadedPath가 originalPath와 일치하면 original 버전
            if (!isCurrentlyRetouched && !string.IsNullOrEmpty(originalFolderPath))
            {
                string normalizedOriginalPath = Path.GetFullPath(originalFolderPath).Replace('\\', '/');
                if (normalizedCurrentPath.Equals(normalizedOriginalPath, StringComparison.OrdinalIgnoreCase))
                {
                    isCurrentlyRetouched = false;
                }
            }
            
            // 3. 경로에 "outputs"가 포함되어 있고 "uploads"가 없으면 retouched로 간주
            if (!isCurrentlyRetouched && 
                normalizedCurrentPath.Contains("/outputs/", StringComparison.OrdinalIgnoreCase) &&
                !normalizedCurrentPath.Contains("/uploads/", StringComparison.OrdinalIgnoreCase))
            {
                isCurrentlyRetouched = true;
            }
            
            // 4. 경로에 "uploads"가 포함되어 있으면 original로 간주
            if (normalizedCurrentPath.Contains("/uploads/", StringComparison.OrdinalIgnoreCase))
            {
                isCurrentlyRetouched = false;
            }
        }
        
        // originalPath와 retouchedPath 설정 시 파일명 기반으로도 확인
        // originalPath에 retouched suffix가 있으면 original이 아닌 retouched로 간주
        if (!string.IsNullOrEmpty(originalPath) && IsRetouchedFileName(originalPath))
        {
            // originalPath가 실제로는 retouched 파일이면, originalPath와 retouchedPath를 교체
            // 단, 경로가 실제로 존재하는지 확인
            if (PathExists(originalPath))
            {
                if (string.IsNullOrEmpty(retouchedPath))
                {
                    retouchedFolderPath = GetFolderPath(originalPath);
                    originalFolderPath = null; // originalPath를 찾아야 함
                }
                else if (PathExists(retouchedPath))
                {
                    // 둘 다 있으면 originalPath를 retouched로 간주하고, originalPath는 찾아야 함
                    retouchedFolderPath = GetFolderPath(originalPath);
                    originalFolderPath = GetFolderPath(retouchedPath);
                }
            }
        }
        
        // retouchedPath에 retouched suffix가 없으면 original일 수 있음
        if (!string.IsNullOrEmpty(retouchedPath) && !IsRetouchedFileName(retouchedPath))
        {
            // retouchedPath가 실제로는 original 파일이면 교체 (경로 존재 확인)
            if (PathExists(retouchedPath))
            {
                if (string.IsNullOrEmpty(originalPath))
                {
                    originalFolderPath = GetFolderPath(retouchedPath);
                    retouchedFolderPath = null;
                }
            }
        }
        
        // 경로 존재 여부 확인 후 null로 설정 (존재하지 않으면 설정하지 않음)
        if (!string.IsNullOrEmpty(originalFolderPath) && !PathExists(originalPath))
        {
            originalFolderPath = null;
        }
        if (!string.IsNullOrEmpty(retouchedFolderPath) && !string.IsNullOrEmpty(retouchedPath) && !PathExists(retouchedPath))
        {
            retouchedFolderPath = null;
        }
        if (!string.IsNullOrEmpty(currentLoadedFolderPath) && !string.IsNullOrEmpty(currentLoadedPath) && !PathExists(currentLoadedPath))
        {
            currentLoadedFolderPath = null;
        }
        
        // 해당 GameObject를 관리하는 항목 찾기
        var item = window._managedObjects.FirstOrDefault(m => m.gameObject == obj);
        if (item != null)
        {
            // 경로 정보 업데이트 (폴더 경로로 저장, 존재하는 경우에만)
            if (!string.IsNullOrEmpty(originalFolderPath) && PathExists(originalPath))
            {
                item.originalPath = originalFolderPath;
            }
            
            if (!string.IsNullOrEmpty(retouchedFolderPath) && !string.IsNullOrEmpty(retouchedPath) && PathExists(retouchedPath))
            {
                item.retouchedPath = retouchedFolderPath;
            }
            else
            {
                // retouchedPath가 없으면 자동으로 찾기 시도
                string foundRetouched = window.FindRetouchedPath(item.originalPath, item.objPath);
                if (!string.IsNullOrEmpty(foundRetouched) && PathExists(foundRetouched))
                {
                    // 찾은 경로도 폴더 경로로 변환
                    item.retouchedPath = GetFolderPath(foundRetouched);
                }
            }
            
            // 현재 로드된 경로 설정 및 isUsingRetouched 업데이트 (폴더 경로로 저장, 존재하는 경우에만)
            if (!string.IsNullOrEmpty(currentLoadedFolderPath) && !string.IsNullOrEmpty(currentLoadedPath) && PathExists(currentLoadedPath))
            {
                item.objPath = currentLoadedFolderPath;
                item.isUsingRetouched = isCurrentlyRetouched;
            }
            else if (string.IsNullOrEmpty(item.objPath))
            {
                // currentLoadedPath가 없으면 originalPath 사용 (존재하는 경우에만)
                if (!string.IsNullOrEmpty(originalFolderPath) && PathExists(originalPath))
                {
                    item.objPath = originalFolderPath;
                    item.isUsingRetouched = false;
                }
            }
            
            // Transform 정보 업데이트
            item.UpdateTransform();
            
            window.Repaint();
        }
        else
        {
            // 항목이 없으면 새로 추가 (폴더 경로로 저장, 존재하는 경우에만)
            string initialPath = null;
            if (!string.IsNullOrEmpty(currentLoadedFolderPath) && !string.IsNullOrEmpty(currentLoadedPath) && PathExists(currentLoadedPath))
            {
                initialPath = currentLoadedFolderPath;
            }
            else if (!string.IsNullOrEmpty(originalFolderPath) && PathExists(originalPath))
            {
                initialPath = originalFolderPath;
            }
            
            // originalPath와 retouchedPath는 존재하는 경우에만 설정
            string validOriginalPath = (!string.IsNullOrEmpty(originalFolderPath) && PathExists(originalPath)) ? originalFolderPath : null;
            string validRetouchedPath = (!string.IsNullOrEmpty(retouchedFolderPath) && !string.IsNullOrEmpty(retouchedPath) && PathExists(retouchedPath)) ? retouchedFolderPath : null;
            
            var newItem = new ManagedObjItem(obj, validOriginalPath, validRetouchedPath);
            if (!string.IsNullOrEmpty(initialPath))
            {
                newItem.objPath = initialPath;
            }
            
            // isUsingRetouched 설정 (currentLoadedPath 기반)
            if (!string.IsNullOrEmpty(currentLoadedFolderPath) && !string.IsNullOrEmpty(currentLoadedPath) && PathExists(currentLoadedPath))
            {
                newItem.isUsingRetouched = isCurrentlyRetouched;
            }
            else if (!string.IsNullOrEmpty(initialPath))
            {
                // currentLoadedPath가 없으면 경로로 판단
                string normalizedPath = Path.GetFullPath(initialPath).Replace('\\', '/');
                if (normalizedPath.Contains("/outputs/", StringComparison.OrdinalIgnoreCase) &&
                    !normalizedPath.Contains("/uploads/", StringComparison.OrdinalIgnoreCase))
                {
                    newItem.isUsingRetouched = true;
                }
                else
                {
                    newItem.isUsingRetouched = false;
                }
            }
            
            // retouchedPath가 없으면 자동으로 찾기 시도 (originalPath가 존재하는 경우에만)
            if (string.IsNullOrEmpty(newItem.retouchedPath) && !string.IsNullOrEmpty(validOriginalPath) && PathExists(originalPath))
            {
                string foundRetouched = window.FindRetouchedPath(validOriginalPath, null);
                if (!string.IsNullOrEmpty(foundRetouched) && PathExists(foundRetouched))
                {
                    // 찾은 경로도 폴더 경로로 변환
                    newItem.retouchedPath = GetFolderPath(foundRetouched);
                }
            }
            
            window._managedObjects.Add(newItem);
            window.Repaint();
        }
    }

    /// <summary>
    /// Original과 Retouched OBJ 버전을 토글합니다.
    /// 현재 GameObject를 삭제하고 새로운 OBJ 파일을 로드한 다음 Transform 정보를 복원합니다.
    /// </summary>
    void ToggleObjVersion(ManagedObjItem item)
    {
        if (item.gameObject == null)
        {
            EditorUtility.DisplayDialog("오류", "씬에 오브젝트가 없습니다.", "OK");
            return;
        }
        
        // 현재 GameObject 참조 저장 (먼저 저장)
        GameObject oldGameObject = item.gameObject;
        
        // 토글할 경로 결정 (폴더 경로에서 .obj 파일 찾기)
        string targetPath = null;
        bool newIsUsingRetouched = !item.isUsingRetouched;
        
        if (newIsUsingRetouched)
        {
            // Retouched로 전환
            if (string.IsNullOrEmpty(item.retouchedPath))
            {
                EditorUtility.DisplayDialog("파일 없음", "Retouched 경로가 설정되지 않았습니다.", "OK");
                return;
            }
            
            // retouchedPath가 파일이면 그대로 사용, 폴더면 .obj 파일 찾기
            if (File.Exists(item.retouchedPath))
            {
                targetPath = item.retouchedPath;
            }
            else if (Directory.Exists(item.retouchedPath))
            {
                string[] objFiles = Directory.GetFiles(item.retouchedPath, "*.obj", SearchOption.TopDirectoryOnly);
                if (objFiles.Length > 0)
                {
                    targetPath = objFiles[0]; // 첫 번째 .obj 파일 사용
                }
                else
                {
                    EditorUtility.DisplayDialog("파일 없음", $"Retouched 폴더에 OBJ 파일을 찾을 수 없습니다:\n{item.retouchedPath}", "OK");
                    return;
                }
            }
            else
            {
                EditorUtility.DisplayDialog("파일 없음", $"Retouched 경로를 찾을 수 없습니다:\n{item.retouchedPath}", "OK");
                return;
            }
        }
        else
        {
            // Original로 전환
            if (string.IsNullOrEmpty(item.originalPath))
            {
                EditorUtility.DisplayDialog("파일 없음", "Original 경로가 설정되지 않았습니다.", "OK");
                return;
            }
            
            // originalPath가 파일이면 그대로 사용, 폴더면 .obj 파일 찾기
            if (File.Exists(item.originalPath))
            {
                targetPath = item.originalPath;
            }
            else if (Directory.Exists(item.originalPath))
            {
                string[] objFiles = Directory.GetFiles(item.originalPath, "*.obj", SearchOption.TopDirectoryOnly);
                if (objFiles.Length > 0)
                {
                    targetPath = objFiles[0]; // 첫 번째 .obj 파일 사용
                }
                else
                {
                    EditorUtility.DisplayDialog("파일 없음", $"Original 폴더에 OBJ 파일을 찾을 수 없습니다:\n{item.originalPath}", "OK");
                    return;
                }
            }
            else
            {
                EditorUtility.DisplayDialog("파일 없음", $"Original 경로를 찾을 수 없습니다:\n{item.originalPath}", "OK");
                return;
            }
        }
        
        // 현재 Transform 정보 저장 (실제 GameObject에서 직접 가져오기)
        Vector3 savedPosition = oldGameObject.transform.position;
        Vector3 savedRotation = oldGameObject.transform.eulerAngles;
        Vector3 savedScale = oldGameObject.transform.localScale;
        string savedName = oldGameObject.name;
        
        // 현재 GameObject의 children 정보 저장 (메모 등)
        List<Transform> savedChildren = new List<Transform>();
        foreach (Transform child in oldGameObject.transform)
        {
            if (child != null)
            {
                savedChildren.Add(child);
            }
        }
        
        try
        {
            // 새로운 OBJ 파일 로드
            var newGo = RuntimeObjLoader.LoadObj(targetPath, preserveOriginalCoordinates: true);
            if (newGo == null)
            {
                EditorUtility.DisplayDialog("로드 실패", $"OBJ 파일을 로드할 수 없습니다:\n{targetPath}", "OK");
                return;
            }
            
            // Transform 정보 복원 (로드 후 즉시 적용)
            newGo.transform.position = savedPosition;
            newGo.transform.eulerAngles = savedRotation;
            newGo.transform.localScale = savedScale;
            newGo.name = savedName;
            
            // 기존 children 복원 (모든 children 복원, 메모 포함)
            foreach (var savedChild in savedChildren)
            {
                if (savedChild != null && savedChild.gameObject != null)
                {
                    GameObject childCopy = GameObject.Instantiate(savedChild.gameObject);
                    childCopy.transform.SetParent(newGo.transform, false);
                    childCopy.transform.localPosition = savedChild.localPosition;
                    childCopy.transform.localRotation = savedChild.localRotation;
                    childCopy.transform.localScale = savedChild.localScale;
                    
                    // (Clone) suffix 제거
                    if (childCopy.name.EndsWith("(Clone)"))
                    {
                        childCopy.name = childCopy.name.Substring(0, childCopy.name.Length - 7);
                    }
                }
            }
            
            // 경로 정보 저장 (루트와 children 모두)
            ObjPathInfo.SetPath(newGo, targetPath);
            
            // 모든 children에도 경로 저장
            Transform[] allChildren = newGo.GetComponentsInChildren<Transform>(true);
            foreach (Transform child in allChildren)
            {
                if (child != null && child != newGo.transform && child.gameObject != null)
                {
                    ObjPathInfo.SetPath(child.gameObject, targetPath);
                }
            }
            
            // MeshRenderer와 Material 확인 및 강제 설정
            MeshRenderer[] allMeshRenderers = newGo.GetComponentsInChildren<MeshRenderer>(true);
            
            foreach (var meshRenderer in allMeshRenderers)
            {
                if (meshRenderer == null) continue;
                
                // Material이 제대로 할당되었는지 확인
                if (meshRenderer.sharedMaterials == null || meshRenderer.sharedMaterials.Length == 0)
                {
                    // Material이 없으면 기본 Material 생성
                    var defaultMat = new Material(Shader.Find("Standard"));
                    defaultMat.color = Color.white;
                    meshRenderer.sharedMaterial = defaultMat;
                }
                else
                {
                    // 기존 Material들의 renderQueue를 Geometry로 강제 설정
                    var materials = meshRenderer.sharedMaterials;
                    bool materialFixed = false;
                    
                    for (int i = 0; i < materials.Length; i++)
                    {
                        if (materials[i] != null)
                        {
                            if (materials[i].renderQueue >= (int)UnityEngine.Rendering.RenderQueue.Transparent)
                            {
                                materials[i].renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
                                materialFixed = true;
                            }
                            
                            if (materials[i].HasProperty("_Surface"))
                            {
                                materials[i].SetFloat("_Surface", 0);
                                materialFixed = true;
                            }
                            
                            if (materials[i].IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"))
                            {
                                materials[i].DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                                materialFixed = true;
                            }
                            
                            Color matColor = materials[i].color;
                            if (matColor.a < 0.999f)
                            {
                                materials[i].color = new Color(matColor.r, matColor.g, matColor.b, 1f);
                                materialFixed = true;
                            }
                            
                            #if UNITY_EDITOR
                            EditorUtility.SetDirty(materials[i]);
                            #endif
                        }
                    }
                    
                    if (materialFixed)
                    {
                        meshRenderer.sharedMaterials = materials;
                    }
                }
                
                // MeshRenderer 활성화 확인
                meshRenderer.enabled = true;
                
                #if UNITY_EDITOR
                EditorUtility.SetDirty(meshRenderer);
                #endif
            }
            
            // MeshFilter도 처리 (루트와 children 모두)
            MeshFilter[] allMeshFilters = newGo.GetComponentsInChildren<MeshFilter>(true);
            foreach (var meshFilter in allMeshFilters)
            {
                if (meshFilter != null && meshFilter.sharedMesh != null)
                {
                    #if UNITY_EDITOR
                    EditorUtility.SetDirty(meshFilter);
                    #endif
                }
            }
            
            // GameObject 활성화 확인
            newGo.SetActive(true);
            
            // Transform 강제 동기화
            newGo.transform.hasChanged = true;
            
            #if UNITY_EDITOR
            // Unity 에디터에서 즉시 반영 (DontSaveInEditor 플래그 체크)
            try
            {
                if ((newGo.hideFlags & HideFlags.DontSaveInEditor) == 0)
                {
                    EditorUtility.SetDirty(newGo);
                }
            }
            catch
            {
                // SetDirty 실패 시 무시
            }
            
            // SceneView 강제 업데이트
            SceneView.RepaintAll();
            #endif
            
            // 기존 GameObject 삭제 (children 복원 후)
            if (oldGameObject != null)
            {
                Undo.DestroyObjectImmediate(oldGameObject);
            }
            
            // 메모를 children으로 생성 (OBJ 파일 경로에서 memo.txt 찾기)
            // memo.txt가 있으면 기존 메모를 제거하고 새로 생성 (memo.txt가 최신 정보)
            try
            {
                var memos = MemoUtils.FindAndParseMemoFile(targetPath);
                if (memos != null && memos.Length > 0)
                {
                    float unitScale = MemoUtils.GetUnitScale();
                    // 기존 메모 children 제거 (memo.txt가 있으면 memo.txt 우선)
                    List<GameObject> memoChildrenToRemove = new List<GameObject>();
                    foreach (Transform child in newGo.transform)
                    {
                        if (child != null && child.gameObject != null)
                        {
                            bool isMemo = child.gameObject.GetComponent<TextMesh>() != null ||
                                          child.gameObject.GetComponent<UnityEngine.TextMesh>() != null;
                            if (isMemo)
                            {
                                memoChildrenToRemove.Add(child.gameObject);
                            }
                        }
                    }
                    foreach (var memoChild in memoChildrenToRemove)
                    {
                        if (memoChild != null)
                        {
                            GameObject.DestroyImmediate(memoChild);
                        }
                    }
                    // 새로운 memo.txt에서 메모 생성
                    MemoUtils.SpawnMemosAsChildren(newGo, memos, unitScale);
                }
                // memo.txt가 없으면 기존 children에서 복원한 메모를 그대로 사용
            }
            catch (Exception)
            {
                // 메모 스폰 실패 시 무시 (기존 children에서 복원한 메모 사용)
            }
            
            // ManagedObjItem 업데이트
            item.gameObject = newGo;
            item.isUsingRetouched = newIsUsingRetouched;
            // targetPath는 파일 경로이므로 폴더 경로로 변환하여 저장
            item.objPath = GetFolderPath(targetPath);
            item.UpdateTransform();
            
            Undo.RegisterCreatedObjectUndo(newGo, $"Toggle OBJ to {(newIsUsingRetouched ? "Retouched" : "Original")}");
            Selection.activeGameObject = newGo;
            EditorGUIUtility.PingObject(newGo);
            Repaint();
        }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog("토글 실패", $"OBJ 버전 전환 중 오류가 발생했습니다:\n{ex.Message}", "OK");
        }
    }

    /// <summary>
    /// GameObject가 OBJ 파일인지 확인합니다.
    /// ObjDropWatcherWindow와 독립적으로 동작합니다.
    /// 루트 GameObject에 MeshFilter가 없어도 children에 MeshFilter가 있으면 OBJ 파일로 인식합니다.
    /// </summary>
    bool IsObjFile(GameObject obj)
    {
        if (obj == null) return false;

        // 1. 먼저 루트 GameObject 자체에 MeshFilter가 있는지 확인
        if (obj.TryGetComponent<MeshFilter>(out var meshFilter) && meshFilter.sharedMesh != null)
        {
            string meshName = meshFilter.sharedMesh.name;
            string objName = obj.name;

            // Unity 기본 메시인지 확인 (제외)
            string[] primitiveNames = { "Plane", "Cube", "Sphere", "Capsule", "Cylinder", "Quad" };
            if (!string.IsNullOrEmpty(meshName) && 
                primitiveNames.Any(name => string.Equals(meshName, name, StringComparison.OrdinalIgnoreCase)))
            {
                return false; // Unity primitive는 OBJ 파일이 아님
            }

            // MeshRenderer가 있어야 함 (렌더링 가능한 오브젝트)
            if (!obj.TryGetComponent<MeshRenderer>(out var meshRenderer))
                return false;

            // 메시 이름이나 오브젝트 이름이 .obj로 끝나는 경우
            if (!string.IsNullOrEmpty(meshName) && meshName.EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrEmpty(objName) && objName.EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
                return true;

            // MeshFilter가 있고 Unity primitive가 아닌 경우, OBJ 파일로 간주
            return true;
        }

        // 2. 루트에 MeshFilter가 없으면 children을 확인
        // RuntimeObjLoader가 children을 생성하는 경우를 처리
        if (obj.transform.childCount > 0)
        {
            // children 중 하나라도 MeshFilter가 있으면 OBJ 파일로 인식
            foreach (Transform child in obj.transform)
            {
                if (child != null && child.gameObject != null)
                {
                    if (child.gameObject.TryGetComponent<MeshFilter>(out var childMeshFilter) && 
                        childMeshFilter.sharedMesh != null)
                    {
                        string childMeshName = childMeshFilter.sharedMesh.name;
                        
                        // Unity 기본 메시인지 확인 (제외)
                        string[] primitiveNames = { "Plane", "Cube", "Sphere", "Capsule", "Cylinder", "Quad" };
                        if (!string.IsNullOrEmpty(childMeshName) && 
                            primitiveNames.Any(name => string.Equals(childMeshName, name, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue; // Unity primitive는 건너뛰기
                        }

                        // MeshRenderer가 있어야 함
                        if (child.gameObject.TryGetComponent<MeshRenderer>(out var childMeshRenderer))
                        {
                            // children에 MeshFilter와 MeshRenderer가 있으면 OBJ 파일로 인식
                            return true;
                        }
                    }
                }
            }
        }

        // 3. ObjPathInfo 컴포넌트가 있으면 OBJ 파일로 인식 (경로 정보가 저장된 경우)
        if (obj.TryGetComponent<ObjPathInfo>(out var pathInfo) && !string.IsNullOrEmpty(pathInfo.ObjFilePath))
        {
            return true;
        }

        // 4. children 중 하나라도 ObjPathInfo가 있으면 OBJ 파일로 인식
        if (obj.transform.childCount > 0)
        {
            foreach (Transform child in obj.transform)
            {
                if (child != null && child.gameObject != null)
                {
                    if (child.gameObject.TryGetComponent<ObjPathInfo>(out var childPathInfo) && 
                        !string.IsNullOrEmpty(childPathInfo.ObjFilePath))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Original 경로에서 Retouched 경로를 찾습니다.
    /// 패턴: project_root\storage\uploads\{group_name}\{scan_folder}\{original.obj} 
    ///    -> project_root\storage\outputs\final\{group_name}\{scan_folder}\{retouched.obj}
    /// 파일명이 다를 수 있으므로 같은 scan_folder에서 .obj 파일을 찾습니다.
    /// </summary>
    string FindRetouchedPath(string originalPath, string currentPath)
    {
        if (string.IsNullOrEmpty(originalPath) && string.IsNullOrEmpty(currentPath))
            return null;
        
        string basePath = !string.IsNullOrEmpty(originalPath) ? originalPath : currentPath;
        if (string.IsNullOrEmpty(basePath))
            return null;
        
        // basePath가 폴더인지 파일인지 확인
        string directory = null;
        string fileName = null;
        
        if (Directory.Exists(basePath))
        {
            // 폴더인 경우
            directory = basePath;
            // 폴더 안의 첫 번째 .obj 파일 찾기
            try
            {
                string[] objFiles = Directory.GetFiles(basePath, "*.obj", SearchOption.TopDirectoryOnly);
                if (objFiles.Length > 0)
                {
                    fileName = Path.GetFileName(objFiles[0]);
                }
            }
            catch
            {
                // 파일 검색 실패 시 무시
            }
        }
        else if (File.Exists(basePath))
        {
            // 파일인 경우
            directory = Path.GetDirectoryName(basePath);
            fileName = Path.GetFileName(basePath);
        }
        else
        {
            // 존재하지 않으면 파일 경로로 가정
            directory = Path.GetDirectoryName(basePath);
            fileName = Path.GetFileName(basePath);
        }
        
        if (string.IsNullOrEmpty(directory))
            return null;
        
        // 정규화된 경로 사용
        string normalizedDir = Path.GetFullPath(directory).Replace('\\', '/');
        string normalizedBase = Path.GetFullPath(basePath).Replace('\\', '/');
        
        // project_root 찾기
        string projectRoot = GetProjectRoot();
        if (string.IsNullOrEmpty(projectRoot))
            return null;
        
        // 1. storage/uploads/{group_name}/{scan_folder}/... -> storage/outputs/final/{group_name}/{scan_folder}/... 패턴
        if (normalizedDir.Contains("/storage/uploads/") || normalizedDir.Contains("\\storage\\uploads\\"))
        {
            // 경로에서 group_name과 scan_folder 추출
            // 예: .../storage/uploads/S102/20251113_104959_upload.scan/...
            string[] parts = normalizedDir.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            
            int uploadsIndex = -1;
            string groupName = null;
            string scanFolder = null;
            
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Equals("uploads", StringComparison.OrdinalIgnoreCase))
                {
                    uploadsIndex = i;
                    if (i + 1 < parts.Length)
                    {
                        groupName = parts[i + 1];
                    }
                    if (i + 2 < parts.Length)
                    {
                        scanFolder = parts[i + 2];
                    }
                    break;
                }
            }
            
            // group_name과 scan_folder가 있으면 정확한 경로로 변환
            if (!string.IsNullOrEmpty(groupName) && !string.IsNullOrEmpty(scanFolder) && !string.IsNullOrEmpty(projectRoot))
            {
                // project_root\storage\outputs\final\{group_name}\{scan_folder} 경로 생성 (폴더 경로 반환)
                string retouchedDir = Path.Combine(projectRoot, "storage", "outputs", "final", groupName, scanFolder);
                
                if (Directory.Exists(retouchedDir))
                {
                    // 폴더 경로를 반환 (폴더 안의 .obj 파일은 나중에 찾음)
                    return retouchedDir;
                }
            }
            
            // group_name만 있고 scan_folder가 없는 경우, group_name 하위에서 검색
            if (!string.IsNullOrEmpty(groupName) && string.IsNullOrEmpty(scanFolder) && !string.IsNullOrEmpty(projectRoot))
            {
                try
                {
                    string outputsFinalGroupPath = Path.Combine(projectRoot, "storage", "outputs", "final", groupName);
                    if (Directory.Exists(outputsFinalGroupPath))
                    {
                        // 원본 파일의 scan_folder 이름 추출 시도
                        // originalPath에서 scan_folder 이름 추출
                        string originalDir = Path.GetDirectoryName(basePath);
                        if (!string.IsNullOrEmpty(originalDir))
                        {
                            string[] originalParts = originalDir.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                            for (int i = 0; i < originalParts.Length; i++)
                            {
                                if (originalParts[i].Equals(groupName, StringComparison.OrdinalIgnoreCase) && i + 1 < originalParts.Length)
                                {
                                    string scanFolderName = originalParts[i + 1];
                                    string retouchedDir = Path.Combine(outputsFinalGroupPath, scanFolderName);
                                    if (Directory.Exists(retouchedDir))
                                    {
                                        return retouchedDir; // 폴더 경로 반환
                                    }
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // 검색 실패 시 무시
                }
            }
        }
        
        // 2. project_root\storage\outputs\final 하위에서 전체 검색 (fallback)
        if (!string.IsNullOrEmpty(projectRoot))
        {
            try
            {
                string outputsFinalPath = Path.Combine(projectRoot, "storage", "outputs", "final");
                if (Directory.Exists(outputsFinalPath))
                {
                    // 원본 파일명으로 검색 시도 (fileName은 이미 메서드 시작 부분에서 선언됨)
                    if (!string.IsNullOrEmpty(fileName))
                    {
                        string[] foundFiles = Directory.GetFiles(outputsFinalPath, fileName, SearchOption.AllDirectories);
                        if (foundFiles.Length > 0)
                        {
                            return foundFiles[0];
                        }
                    }
                    
                    // 파일명이 일치하지 않으면, 원본 파일명의 기본 이름으로 검색
                    // 예: "team table.obj" -> "team_table" 또는 "20251111_142203.obj" -> "20251111_142203"
                    string baseFileName = Path.GetFileNameWithoutExtension(fileName);
                    if (!string.IsNullOrEmpty(baseFileName))
                    {
                        // baseFileName이 포함된 .obj 파일 검색
                        string[] allObjFiles = Directory.GetFiles(outputsFinalPath, "*.obj", SearchOption.AllDirectories);
                        foreach (var objFile in allObjFiles)
                        {
                            string objFileName = Path.GetFileNameWithoutExtension(objFile);
                            if (objFileName.Contains(baseFileName) || baseFileName.Contains(objFileName))
                            {
                                return objFile;
                            }
                        }
                    }
                }
            }
            catch
            {
                // 검색 실패 시 무시
            }
        }
        
        // 3. storage/uploads -> storage/outputs/final 패턴 (group_name 없이)
        if (normalizedDir.Contains("uploads"))
        {
            string outputsPath = normalizedDir.Replace("uploads", "outputs");
            outputsPath = outputsPath.Replace("/", Path.DirectorySeparatorChar.ToString());
            outputsPath = outputsPath.Replace("\\", Path.DirectorySeparatorChar.ToString());
            
            // outputs/final 경로 시도 (폴더 경로 반환)
            string finalPath = Path.Combine(Path.GetDirectoryName(outputsPath), "final");
            if (Directory.Exists(finalPath))
            {
                // 폴더 안에 .obj 파일이 있는지 확인
                string[] objFiles = Directory.GetFiles(finalPath, "*.obj", SearchOption.TopDirectoryOnly);
                if (objFiles.Length > 0)
                {
                    return finalPath; // 폴더 경로 반환
                }
            }
            
            // outputs 직접 경로 시도 (폴더 경로 반환)
            if (Directory.Exists(outputsPath))
            {
                string[] objFiles = Directory.GetFiles(outputsPath, "*.obj", SearchOption.TopDirectoryOnly);
                if (objFiles.Length > 0)
                {
                    return outputsPath; // 폴더 경로 반환
                }
            }
        }
        
        // 4. 같은 디렉토리에서 찾기 (파일명이 다를 수 있음) - 폴더 경로 반환
        try
        {
            if (Directory.Exists(directory))
            {
                string[] files = Directory.GetFiles(directory, "*.obj", SearchOption.TopDirectoryOnly);
                if (files.Length > 0)
                {
                    // 같은 폴더에 .obj 파일이 있으면 폴더 경로 반환
                    return directory;
                }
            }
        }
        catch
        {
            // 디렉토리 접근 실패 시 무시
        }
        
        // 5. ObjPathFinder를 사용하여 검색 (폴더 경로 반환)
        try
        {
            SetupSearchPaths();
            // 파일명으로 검색
            string foundPath = ObjPathFinder.FindObjPathForImport(fileName, null);
            if (!string.IsNullOrEmpty(foundPath) && File.Exists(foundPath) && foundPath != basePath)
            {
                // outputs 디렉토리에 있으면 retouched로 간주
                if (foundPath.Contains("outputs"))
                {
                    // 파일 경로에서 폴더 경로 추출
                    string foundDir = Path.GetDirectoryName(foundPath);
                    if (Directory.Exists(foundDir))
                    {
                        return foundDir; // 폴더 경로 반환
                    }
                }
            }
        }
        catch
        {
            // 검색 실패 시 무시
        }
        
        return null;
    }
    
    /// <summary>
    /// 프로젝트 루트 경로를 가져옵니다.
    /// </summary>
    string GetProjectRoot()
    {
        try
        {
            var configs = Resources.FindObjectsOfTypeAll<WatchConfig>();
            if (configs != null && configs.Length > 0)
            {
                var config = configs[0];
                if (config != null && !string.IsNullOrWhiteSpace(config.projectRoot))
                {
                    string projectRoot = config.projectRoot;
                    if (Path.IsPathRooted(projectRoot))
                    {
                        if (Directory.Exists(projectRoot))
                        {
                            return projectRoot;
                        }
                    }
                    else
                    {
                        string fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", projectRoot));
                        if (Directory.Exists(fullPath))
                        {
                            return fullPath;
                        }
                    }
                }
            }
        }
        catch
        {
            // WatchConfig 접근 실패 시 무시
        }
        
        // 기본값: Unity 프로젝트 루트
        return Path.GetDirectoryName(Application.dataPath);
    }

    /// <summary>
    /// OBJ 파일 검색 경로를 설정합니다.
    /// ObjDropWatcherWindow와 독립적으로 동작합니다.
    /// </summary>
    void SetupSearchPaths()
    {
        var searchPaths = new List<string>();
        
        // 1. 관리 중인 OBJ들의 경로도 검색 경로에 추가 (우선순위 높음)
        foreach (var item in _managedObjects)
        {
            if (!string.IsNullOrEmpty(item.objPath) && PathExists(item.objPath))
            {
                // objPath가 폴더 경로면 그대로 사용, 파일 경로면 디렉토리 추출
                string folder = Directory.Exists(item.objPath) 
                    ? item.objPath 
                    : Path.GetDirectoryName(item.objPath);
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder) && !searchPaths.Contains(folder))
                {
                    searchPaths.Add(folder);
                }
            }
        }
        
        // 2. ObjDropWatcherWindow는 완전히 분리 - 더 이상 사용하지 않음
        // (이전에는 ObjDropWatcherWindow의 _items에서 경로를 가져왔지만, 이제는 독립적으로 동작)
        
        // 3. 일반적인 storage 경로 추가
        string[] commonPaths = {
            Path.Combine(Application.dataPath, "..", "storage", "uploads"),
            Path.Combine(Application.dataPath, "..", "storage", "outputs"),
            Path.Combine(Application.dataPath, "..", "storage", "outputs", "final"),
            Path.Combine(Application.dataPath, "..", "storage", "outputs", "optimized"),
            Path.Combine(Application.dataPath, "..", "storage", "outputs", "polygon"),
            Path.Combine(Application.dataPath, "..", "storage", "temp"),
            Path.Combine(Application.dataPath, "..", "storage")
        };
        
        foreach (var path in commonPaths)
        {
            string fullPath = Path.GetFullPath(path);
            if (Directory.Exists(fullPath) && !searchPaths.Contains(fullPath))
            {
                searchPaths.Add(fullPath);
            }
        }
        
        ObjPathFinder.SetSearchPaths(searchPaths);
    }

    void ExportObjects(ExportFormat format)
    {
        EditorApplication.delayCall += () =>
        {
            // 관리 중인 오브젝트들의 Transform 정보 업데이트
            foreach (var item in _managedObjects)
            {
                if (item.gameObject != null)
                {
                    item.UpdateTransform();
                }
            }
            
            // GameObject 리스트 생성 (null이 아닌 것만)
            var gameObjects = _managedObjects.Where(m => m.gameObject != null).Select(m => m.gameObject).ToList();
            
            if (gameObjects.Count == 0)
            {
                EditorUtility.DisplayDialog("Export 실패", 
                    "Export할 오브젝트가 없습니다.\n관리 목록에 OBJ 오브젝트를 추가해주세요.", "OK");
                return;
            }

            SetupSearchPaths();

            string extension = format switch
            {
                ExportFormat.JSON => "json",
                ExportFormat.CSV => "csv",
                ExportFormat.Binary => "bin",
                _ => "dat"
            };

            string defaultName = $"ObjectTransforms_{DateTime.Now:yyyyMMdd_HHmmss}.{extension}";
            
            string filePath = EditorUtility.SaveFilePanel("Export Object Transforms", 
                Application.dataPath, defaultName, extension);

            if (string.IsNullOrEmpty(filePath))
                return;

            try
            {
                // Export 시 경로 정보도 포함하도록 수정된 Exporter 사용
                switch (format)
                {
                    case ExportFormat.JSON:
                        ExportToJsonWithPaths(gameObjects, filePath);
                        break;
                    case ExportFormat.CSV:
                        CsvExporter.ExportToCsv(gameObjects, filePath);
                        break;
                    case ExportFormat.Binary:
                        BinaryExporter.ExportToBinary(gameObjects, filePath);
                        break;
                }
                
                EditorUtility.DisplayDialog("Export 완료", 
                    $"{gameObjects.Count}개의 오브젝트 정보를 export했습니다.\n\n파일: {filePath}", "OK");
                Repaint();
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Export 실패", $"Export 중 오류가 발생했습니다:\n{ex.Message}", "OK");
            }
        };
    }

    void ExportToJsonWithPaths(List<GameObject> objects, string filePath)
    {
        var collection = new ObjectTransformCollection();
        
        foreach (var obj in objects)
        {
            var item = _managedObjects.FirstOrDefault(m => m.gameObject == obj);
            // objPath가 폴더 경로일 수 있으므로 GetCurrentPath() 사용 (파일 경로 반환)
            string objPath = item != null ? item.GetCurrentPath() : ObjPathFinder.FindObjPath(obj);
            
            // children 포함하여 export (메모 등 자식 오브젝트도 포함)
            var data = new ObjectTransformData(obj, objPath, true);
            collection.objects.Add(data);
        }
        
        string json = JsonConvert.SerializeObject(collection, Formatting.Indented);
        File.WriteAllText(filePath, json);
    }

    void ImportObjects(ExportFormat format)
    {
        EditorApplication.delayCall += () =>
        {
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

                // Import 시 자동으로 OBJ 파일을 로드하고 배치
                int successCount = 0;
                int failCount = 0;
                
                foreach (var data in collection.objects)
                {
                    if (data.objectType != ObjectType.ObjFile)
                        continue;
                    
                    // 경로로 OBJ 파일 로드
                    string objPath = data.objFilePath;
                    if (string.IsNullOrEmpty(objPath) || !File.Exists(objPath))
                    {
                        // 경로를 찾지 못한 경우 시도
                        objPath = ObjPathFinder.FindObjPathForImport(data.objectName, data.objFilePath);
                    }
                    
                    if (!string.IsNullOrEmpty(objPath) && File.Exists(objPath))
                    {
                        try
                        {
                            // 1. 먼저 OBJ 파일 로드 (Transform은 나중에 적용)
                            var go = RuntimeObjLoader.LoadObj(objPath, preserveOriginalCoordinates: true);
                            if (go != null)
                            {
                                go.name = data.objectName;
                                
                                // 경로 정보 저장 (루트와 children 모두)
                                ObjPathInfo.SetPath(go, objPath);
                                
                                // 모든 children에도 경로 저장
                                Transform[] allChildren = go.GetComponentsInChildren<Transform>(true);
                                foreach (Transform child in allChildren)
                                {
                                    if (child != null && child != go.transform && child.gameObject != null)
                                    {
                                        ObjPathInfo.SetPath(child.gameObject, objPath);
                                    }
                                }
                                
                                // MeshRenderer와 Material 확인 및 강제 설정
                                // 루트와 children 모두 처리
                                MeshRenderer[] allMeshRenderers = go.GetComponentsInChildren<MeshRenderer>(true);
                                
                                foreach (var meshRenderer in allMeshRenderers)
                                {
                                    if (meshRenderer == null) continue;
                                    
                                    // Material이 제대로 할당되었는지 확인
                                    if (meshRenderer.sharedMaterials == null || meshRenderer.sharedMaterials.Length == 0)
                                    {
                                        // Material이 없으면 기본 Material 생성
                                        var defaultMat = new Material(Shader.Find("Standard"));
                                        defaultMat.color = Color.white;
                                        meshRenderer.sharedMaterial = defaultMat;
                                    }
                                    else
                                    {
                                        // 기존 Material들의 renderQueue를 Geometry로 강제 설정 (Transparent 문제 해결)
                                        var materials = meshRenderer.sharedMaterials;
                                        bool materialFixed = false;
                                        
                                        for (int i = 0; i < materials.Length; i++)
                                        {
                                            if (materials[i] != null)
                                            {
                                                // Transparent renderQueue를 Geometry로 변경
                                                if (materials[i].renderQueue >= (int)UnityEngine.Rendering.RenderQueue.Transparent)
                                                {
                                                    materials[i].renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
                                                    materialFixed = true;
                                                }
                                                
                                                // URP/Lit shader의 Surface Type을 Opaque로 강제 설정
                                                if (materials[i].HasProperty("_Surface"))
                                                {
                                                    materials[i].SetFloat("_Surface", 0); // 0=Opaque, 1=Transparent
                                                    materialFixed = true;
                                                }
                                                
                                                // 투명 관련 키워드 비활성화
                                                if (materials[i].IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"))
                                                {
                                                    materials[i].DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                                                    materialFixed = true;
                                                }
                                                
                                                // Alpha를 1.0으로 설정
                                                Color matColor = materials[i].color;
                                                if (matColor.a < 0.999f)
                                                {
                                                    materials[i].color = new Color(matColor.r, matColor.g, matColor.b, 1f);
                                                    materialFixed = true;
                                                }
                                                
                                                #if UNITY_EDITOR
                                                EditorUtility.SetDirty(materials[i]);
                                                #endif
                                            }
                                        }
                                        
                                        if (materialFixed)
                                        {
                                            // Material 배열을 다시 할당하여 변경사항 적용
                                            meshRenderer.sharedMaterials = materials;
                                        }
                                    }
                                    
                                    // MeshRenderer 활성화 확인
                                    meshRenderer.enabled = true;
                                    
                                    #if UNITY_EDITOR
                                    EditorUtility.SetDirty(meshRenderer);
                                    #endif
                                }
                                
                                // MeshFilter도 처리 (루트와 children 모두)
                                MeshFilter[] allMeshFilters = go.GetComponentsInChildren<MeshFilter>(true);
                                foreach (var meshFilter in allMeshFilters)
                                {
                                    if (meshFilter != null && meshFilter.sharedMesh != null)
                                    {
                                        #if UNITY_EDITOR
                                        EditorUtility.SetDirty(meshFilter);
                                        #endif
                                    }
                                }
                                
                                // GameObject 활성화 확인
                                go.SetActive(true);
                                
                                // Transform 강제 동기화
                                go.transform.hasChanged = true;
                                
                                #if UNITY_EDITOR
                                // Unity 에디터에서 즉시 반영
                                EditorUtility.SetDirty(go);
                                
                                // Transform 컴포넌트도 강제 업데이트
                                UnityEditorInternal.InternalEditorUtility.SetIsInspectorExpanded(go.transform, true);
                                
                                // SceneView 강제 업데이트
                                SceneView.RepaintAll();
                                
                                // Selection 업데이트하여 Transform Gizmo가 제대로 표시되도록
                                if (successCount == 0)
                                {
                                    Selection.activeGameObject = go;
                                    // Selection이 변경되었음을 Unity에 알림
                                    EditorGUIUtility.PingObject(go);
                                }
                                #endif
                                
                                // 2. JSON의 Transform 정보를 OBJ에 적용
                                go.transform.position = data.GetPosition();
                                go.transform.eulerAngles = data.GetRotation();
                                go.transform.localScale = data.GetScale();
                                
                                // 3. memo.txt에서 메모 읽기 및 스폰 (anchor 기반)
                                try
                                {
                                    var memos = MemoUtils.FindAndParseMemoFile(objPath);
                                    if (memos != null && memos.Length > 0)
                                    {
                                        float unitScale = MemoUtils.GetUnitScale();
                                        // memo.txt의 anchor와 텍스트만 사용하여 스폰
                                        MemoUtils.SpawnMemosAsChildren(go, memos, unitScale);
                                    }
                                }
                                catch (Exception)
                                {
                                    // 메모 스폰 실패 시 무시
                                }
                                
                                // 4. 메모가 아닌 children만 JSON에서 복원 (메모는 memo.txt 사용)
                                if (data.children != null && data.children.Count > 0)
                                {
                                    int nonMemoChildrenCount = 0;
                                    foreach (var childData in data.children)
                                    {
                                        // 메모 children인지 확인 (TextMesh 컴포넌트로 식별)
                                        bool isMemoChild = false;
                                        if (childData.components != null)
                                        {
                                            foreach (var compData in childData.components)
                                            {
                                                if (compData.componentType != null && 
                                                    (compData.componentType.Contains("TextMesh") || 
                                                     compData.componentType.Contains("UnityEngine.TextMesh")))
                                                {
                                                    isMemoChild = true;
                                                    break;
                                                }
                                            }
                                        }
                                        
                                        // 메모가 아닌 children만 복원
                                        if (!isMemoChild)
                                        {
                                            try
                                            {
                                                GameObject child = childData.CreateGameObject();
                                                if (child != null)
                                                {
                                                    childData.ApplyToGameObject(child);
                                                    child.transform.SetParent(go.transform, false);
                                                    nonMemoChildrenCount++;
                                                }
                                            }
                                            catch (Exception)
                                            {
                                                // 자식 복원 실패 시 무시
                                            }
                                        }
                                    }
                                    
                                    if (nonMemoChildrenCount > 0)
                                    {
                                        // 자식 복원 완료
                                    }
                                }
                                
                                // 관리 목록에 추가
                                _managedObjects.Add(new ManagedObjItem(go, objPath));
                                Undo.RegisterCreatedObjectUndo(go, "Import OBJ");
                                
                                successCount++;
                            }
                            else
                            {
                                failCount++;
                            }
                        }
                        catch (Exception)
                        {
                            failCount++;
                        }
                    }
                    else
                    {
                        failCount++;
                    }
                }
                
                EditorUtility.DisplayDialog("Import 완료", 
                    $"Import 완료:\n성공: {successCount}개\n실패: {failCount}개", "OK");
                Repaint();
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Import 실패", $"Import 중 오류가 발생했습니다:\n{ex.Message}", "OK");
            }
        };
    }
}


