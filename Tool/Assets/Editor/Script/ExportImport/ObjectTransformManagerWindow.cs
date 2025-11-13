using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ObjDropWatcher.ExportImport;

public class ObjectTransformManagerWindow : EditorWindow
{
    private enum ExportFormat { JSON, CSV, Binary }
    
    [Serializable]
    class ManagedObjItem
    {
        public GameObject gameObject;
        public string objPath;
        public string objectName;
        public Vector3 position;
        public Vector3 rotation;
        public Vector3 scale;
        
        public ManagedObjItem(GameObject obj, string path)
        {
            gameObject = obj;
            objPath = path;
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
    }
    
    private List<ManagedObjItem> _managedObjects = new List<ManagedObjItem>();
    private Vector2 _scroll;
    private Vector2 _listScroll;
    private string _manualPath = "";
    private bool _autoDetectOnSceneChange = true;
    
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
            if (string.IsNullOrEmpty(item.objPath) || !File.Exists(item.objPath))
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
                
                if (!string.IsNullOrEmpty(foundPath) && File.Exists(foundPath))
                {
                    item.objPath = foundPath;
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
                EditorGUILayout.LabelField($"경로: {item.objPath ?? "(경로 없음)"}", EditorStyles.miniLabel);
                var itemPos = item.GetPosition();
                EditorGUILayout.LabelField($"위치: ({itemPos.x:F2}, {itemPos.y:F2}, {itemPos.z:F2})", EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.LabelField($"오브젝트: {item.objectName} (씬에 없음)", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"경로: {item.objPath ?? "(경로 없음)"}", EditorStyles.miniLabel);
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
                
                // 경로가 없으면 "경로 찾기", 있으면 "경로 수정"
                string pathButtonText = string.IsNullOrEmpty(item.objPath) || !File.Exists(item.objPath)
                    ? "경로 찾기"
                    : "경로 수정";
                
                if (GUILayout.Button(pathButtonText, GUILayout.Height(22)))
                {
                    // 먼저 자동으로 경로 찾기 시도
                    SetupSearchPaths();
                    string foundPath = ObjPathFinder.FindObjPath(item.gameObject);
                    
                    // 경로를 찾았고 파일이 존재하면 사용
                    if (!string.IsNullOrEmpty(foundPath) && File.Exists(foundPath))
                    {
                        item.objPath = foundPath;
                        item.UpdateTransform();
                        Repaint();
                    }
                    else
                    {
                        // 경로를 찾지 못한 경우 수동으로 선택
                        string defaultPath = GetDefaultPath();
                        string startPath = !string.IsNullOrEmpty(item.objPath) && File.Exists(item.objPath)
                            ? Path.GetDirectoryName(item.objPath)
                            : defaultPath;
                        
                        string newPath = EditorUtility.OpenFilePanel("OBJ 파일 선택", startPath, "obj");
                        if (!string.IsNullOrEmpty(newPath) && File.Exists(newPath))
                        {
                            item.objPath = newPath;
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
                if (string.IsNullOrEmpty(item.objPath) || !File.Exists(item.objPath))
                {
                    if (GUILayout.Button("경로 찾기", GUILayout.Height(22)))
                    {
                        SetupSearchPaths();
                        string foundPath = ObjPathFinder.FindObjPathForImport(item.objectName, null);
                        if (!string.IsNullOrEmpty(foundPath) && File.Exists(foundPath))
                        {
                            item.objPath = foundPath;
                            Repaint();
                        }
                        else
                        {
                            // 경로를 찾지 못한 경우 수동으로 선택
                            string defaultPath = GetDefaultPath();
                            string newPath = EditorUtility.OpenFilePanel("OBJ 파일 선택", defaultPath, "obj");
                            if (!string.IsNullOrEmpty(newPath) && File.Exists(newPath))
                            {
                                item.objPath = newPath;
                                Repaint();
                            }
                        }
                    }
                }
            }
            
            if (GUILayout.Button("삭제", GUILayout.Height(22)))
            {
                _managedObjects.RemoveAt(i);
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
                    if (!string.IsNullOrEmpty(objPath) && File.Exists(objPath))
                    {
                        existing.objPath = objPath;
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
                
                _managedObjects.Add(new ManagedObjItem(obj, objPath));
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
        
        if (!File.Exists(objPath))
        {
            EditorUtility.DisplayDialog("파일 없음", $"파일을 찾을 수 없습니다:\n{objPath}", "OK");
            return;
        }
        
        // 이미 추가된 경로인지 확인
        if (_managedObjects.Any(m => m.objPath == objPath))
        {
            EditorUtility.DisplayDialog("이미 추가됨", "이 경로는 이미 관리 목록에 있습니다.", "OK");
            return;
        }
        
        // OBJ 파일 로드
        try
        {
            // preserveOriginalCoordinates: true로 설정하여 원본 좌표 유지
            var go = RuntimeObjLoader.LoadObj(objPath, preserveOriginalCoordinates: true);
            if (go != null)
            {
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
                        Debug.Log($"[OBJ Manager] Created default material for '{meshRenderer.gameObject.name}'");
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
                            Debug.Log($"[OBJ Manager] Fixed materials for '{meshRenderer.gameObject.name}'");
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
                    var memos = MemoUtils.FindAndParseMemoFile(objPath);
                    if (memos != null && memos.Length > 0)
                    {
                        float unitScale = MemoUtils.GetUnitScale();
                        MemoUtils.SpawnMemosAsChildren(go, memos, unitScale);
                    }
                }
                catch (Exception memoEx)
                {
                    Debug.LogWarning($"[OBJ Manager] Failed to spawn memos for OBJ '{go.name}': {memoEx.Message}");
                }
                
                _managedObjects.Add(new ManagedObjItem(go, objPath));
                Undo.RegisterCreatedObjectUndo(go, "Load OBJ");
                Selection.activeGameObject = go;
                EditorGUIUtility.PingObject(go);
                _manualPath = ""; // 입력 필드 초기화
                Repaint();
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[OBJ Manager] Failed to load OBJ: {objPath}\n{ex}");
            EditorUtility.DisplayDialog("로드 실패", $"OBJ 파일을 로드할 수 없습니다:\n{ex.Message}", "OK");
        }
    }

    void LoadObjFromPath(string objPath, ManagedObjItem item)
    {
        if (string.IsNullOrEmpty(objPath) || !File.Exists(objPath))
        {
            EditorUtility.DisplayDialog("파일 없음", $"파일을 찾을 수 없습니다:\n{objPath}", "OK");
            return;
        }
        
        try
        {
            // preserveOriginalCoordinates: true로 설정하여 원본 좌표 유지
            var go = RuntimeObjLoader.LoadObj(objPath, preserveOriginalCoordinates: true);
            if (go != null)
            {
                // Transform 정보 복원
                go.transform.position = item.GetPosition();
                go.transform.eulerAngles = item.GetRotation();
                go.transform.localScale = item.GetScale();
                go.name = item.objectName;
                
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
                        Debug.Log($"[OBJ Manager] Created default material for '{meshRenderer.gameObject.name}'");
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
                    var memos = MemoUtils.FindAndParseMemoFile(objPath);
                    if (memos != null && memos.Length > 0)
                    {
                        float unitScale = MemoUtils.GetUnitScale();
                        MemoUtils.SpawnMemosAsChildren(go, memos, unitScale);
                    }
                }
                catch (Exception memoEx)
                {
                    Debug.LogWarning($"[OBJ Manager] Failed to spawn memos for loaded OBJ '{go.name}': {memoEx.Message}");
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
            Debug.LogError($"[OBJ Manager] Failed to load OBJ: {objPath}\n{ex}");
            EditorUtility.DisplayDialog("로드 실패", $"OBJ 파일을 로드할 수 없습니다:\n{ex.Message}", "OK");
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
    /// OBJ 파일 검색 경로를 설정합니다.
    /// ObjDropWatcherWindow와 독립적으로 동작합니다.
    /// </summary>
    void SetupSearchPaths()
    {
        var searchPaths = new List<string>();
        
        // 1. 관리 중인 OBJ들의 경로도 검색 경로에 추가 (우선순위 높음)
        foreach (var item in _managedObjects)
        {
            if (!string.IsNullOrEmpty(item.objPath) && File.Exists(item.objPath))
            {
                string folder = Path.GetDirectoryName(item.objPath);
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
                Debug.LogError($"[Export] Failed: {ex}");
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
            string objPath = item != null ? item.objPath : ObjPathFinder.FindObjPath(obj);
            
            // children 포함하여 export (메모 등 자식 오브젝트도 포함)
            var data = new ObjectTransformData(obj, objPath, true);
            collection.objects.Add(data);
        }
        
        string json = JsonUtility.ToJson(collection, true);
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
                                
                                // 2. memo.txt에서 메모 읽기 및 스폰 (기본 anchor 위치)
                                MemoUtils.MemoData[] memos = null;
                                try
                                {
                                    memos = MemoUtils.FindAndParseMemoFile(objPath);
                                }
                                catch (Exception memoEx)
                                {
                                    Debug.LogWarning($"[OBJ Manager] Failed to parse memo file for '{go.name}': {memoEx.Message}");
                                }
                                
                                // memo.txt의 메모를 먼저 스폰
                                int spawnedMemoCount = 0;
                                if (memos != null && memos.Length > 0)
                                {
                                    float unitScale = MemoUtils.GetUnitScale();
                                    MemoUtils.SpawnMemosAsChildren(go, memos, null, unitScale);
                                    spawnedMemoCount = memos.Length;
                                    Debug.Log($"[OBJ Manager] [DEBUG] Spawned {spawnedMemoCount} memo(s) from memo.txt for '{go.name}'");
                                }
                                else
                                {
                                    Debug.Log($"[OBJ Manager] [DEBUG] No memos found in memo.txt for '{go.name}'");
                                }
                                
                                // 3. JSON의 Transform 정보를 OBJ와 메모에 적용
                                // OBJ의 Transform 적용
                                go.transform.position = data.GetPosition();
                                go.transform.eulerAngles = data.GetRotation();
                                go.transform.localScale = data.GetScale();
                                
                                // JSON의 children에서 메모 Transform 정보 추출
                                Dictionary<string, (Vector3 position, Quaternion rotation, Vector3 scale)> memoTransforms = new Dictionary<string, (Vector3, Quaternion, Vector3)>();
                                
                                if (data.children != null && data.children.Count > 0)
                                {
                                    Debug.Log($"[OBJ Manager] [DEBUG] Processing {data.children.Count} children from JSON");
                                    
                                    foreach (var childData in data.children)
                                    {
                                        // 메모 children인지 확인 (TextMesh 컴포넌트로 식별)
                                        bool isMemoChild = false;
                                        try
                                        {
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
                                        }
                                        catch
                                        {
                                            // 무시
                                        }
                                        
                                        if (isMemoChild)
                                        {
                                            // localPosition을 anchor로 역변환
                                            Vector3 localPos = childData.position;
                                            Debug.Log($"[OBJ Manager] [DEBUG] Memo child found: '{childData.objectName}', original position={localPos}");
                                            
                                            // Z축 변환 고려
                                            localPos.z = -localPos.z;
                                            string anchor = MemoUtils.Vector3ToAnchor(localPos);
                                            
                                            // Transform 정보 저장
                                            memoTransforms[anchor] = (childData.position, Quaternion.Euler(childData.rotation), childData.scale);
                                            Debug.Log($"[OBJ Manager] [DEBUG] Extracted memo Transform - anchor: '{anchor}', position: {childData.position}, rotation: {childData.rotation}, scale: {childData.scale}");
                                        }
                                    }
                                    
                                    Debug.Log($"[OBJ Manager] [DEBUG] Extracted {memoTransforms.Count} memo Transform(s) from JSON");
                                }
                                else
                                {
                                    Debug.Log($"[OBJ Manager] [DEBUG] No children in JSON data");
                                }
                                
                                // 4. 스폰된 메모들에 JSON의 Transform 정보 적용
                                int appliedTransformCount = 0;
                                
                                if (memos != null && memos.Length > 0 && memoTransforms.Count > 0)
                                {
                                    Debug.Log($"[OBJ Manager] [DEBUG] Starting Transform application - spawned memos: {spawnedMemoCount}, JSON transforms: {memoTransforms.Count}");
                                    
                                    // 스폰된 메모 children 찾기
                                    List<Transform> spawnedMemoChildren = new List<Transform>();
                                    foreach (Transform child in go.transform)
                                    {
                                        if (child == null || child.gameObject == null)
                                            continue;
                                        
                                        if (child.gameObject.TryGetComponent<TextMesh>(out var textMesh))
                                        {
                                            spawnedMemoChildren.Add(child);
                                        }
                                    }
                                    
                                    Debug.Log($"[OBJ Manager] [DEBUG] Found {spawnedMemoChildren.Count} TextMesh children in scene");
                                    
                                    foreach (Transform child in spawnedMemoChildren)
                                    {
                                        // 현재 메모의 localPosition을 anchor로 변환
                                        Vector3 localPos = child.transform.localPosition;
                                        Debug.Log($"[OBJ Manager] [DEBUG] Processing memo '{child.name}' - current localPosition: {localPos}");
                                        
                                        Vector3 posBeforeZFlip = localPos;
                                        localPos.z = -localPos.z;
                                        string anchor = MemoUtils.Vector3ToAnchor(localPos);
                                        
                                        Debug.Log($"[OBJ Manager] [DEBUG] Memo '{child.name}' - anchor after Z-flip: '{anchor}' (before Z-flip: {MemoUtils.Vector3ToAnchor(posBeforeZFlip)})");
                                        
                                        // JSON에서 해당 anchor의 Transform 정보 찾기
                                        if (memoTransforms.TryGetValue(anchor, out var transformInfo))
                                        {
                                            // Transform 정보 적용
                                            child.transform.localPosition = transformInfo.position;
                                            child.transform.localEulerAngles = transformInfo.rotation.eulerAngles;
                                            child.transform.localScale = transformInfo.scale;
                                            
                                            appliedTransformCount++;
                                            Debug.Log($"[OBJ Manager] [DEBUG] ✓ Applied JSON Transform to memo '{child.name}' - anchor: '{anchor}', position: {transformInfo.position}, rotation: {transformInfo.rotation.eulerAngles}, scale: {transformInfo.scale}");
                                        }
                                        else
                                        {
                                            Debug.Log($"[OBJ Manager] [DEBUG] ✗ No exact match for anchor '{anchor}' in JSON transforms");
                                            
                                            // 정확한 매칭 실패 시 거리 기반으로 가장 가까운 Transform 찾기
                                            float minDistance = float.MaxValue;
                                            string closestAnchor = null;
                                            
                                            foreach (var kvp in memoTransforms)
                                            {
                                                Vector3 exportAnchorPos = MemoUtils.ParseAnchor(kvp.Key);
                                                exportAnchorPos.z = -exportAnchorPos.z;
                                                
                                                float distance = Vector3.Distance(localPos, exportAnchorPos);
                                                if (distance < minDistance)
                                                {
                                                    minDistance = distance;
                                                    closestAnchor = kvp.Key;
                                                }
                                            }
                                            
                                            Debug.Log($"[OBJ Manager] [DEBUG] Closest anchor: '{closestAnchor}', distance: {minDistance:F6}");
                                            
                                            // 거리가 매우 가까우면 (0.01 이하) 매칭
                                            if (closestAnchor != null && minDistance < 0.01f)
                                            {
                                                var transformInfo2 = memoTransforms[closestAnchor];
                                                child.transform.localPosition = transformInfo2.position;
                                                child.transform.localEulerAngles = transformInfo2.rotation.eulerAngles;
                                                child.transform.localScale = transformInfo2.scale;
                                                
                                                appliedTransformCount++;
                                                Debug.Log($"[OBJ Manager] [DEBUG] ✓ Applied JSON Transform by distance - anchor: '{anchor}' matched with '{closestAnchor}', distance: {minDistance:F6}");
                                            }
                                            else
                                            {
                                                Debug.LogWarning($"[OBJ Manager] [DEBUG] ✗ Failed to match memo '{child.name}' - closest distance: {minDistance:F6} (threshold: 0.01)");
                                            }
                                        }
                                    }
                                    
                                    Debug.Log($"[OBJ Manager] [DEBUG] Applied Transform to {appliedTransformCount} out of {spawnedMemoChildren.Count} memo(s)");
                                }
                                else
                                {
                                    if (memos == null || memos.Length == 0)
                                    {
                                        Debug.Log($"[OBJ Manager] [DEBUG] No memos from memo.txt - skipping Transform application");
                                    }
                                    if (memoTransforms.Count == 0)
                                    {
                                        Debug.Log($"[OBJ Manager] [DEBUG] No memo transforms in JSON - skipping Transform application");
                                    }
                                }
                                
                                // 5. 메모가 아닌 다른 children 복원 (JSON에서)
                                if (data.children != null && data.children.Count > 0)
                                {
                                    int nonMemoChildrenCount = 0;
                                    foreach (var childData in data.children)
                                    {
                                        // 메모 children인지 확인
                                        bool isMemoChild = false;
                                        try
                                        {
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
                                        }
                                        catch
                                        {
                                            // 무시
                                        }
                                        
                                        if (!isMemoChild)
                                        {
                                            // 메모가 아닌 children은 복원
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
                                            catch (Exception ex)
                                            {
                                                Debug.LogWarning($"[OBJ Manager] Failed to restore non-memo child {childData.objectName}: {ex.Message}");
                                            }
                                        }
                                    }
                                    
                                }
                                
                                // 관리 목록에 추가
                                _managedObjects.Add(new ManagedObjItem(go, objPath));
                                Undo.RegisterCreatedObjectUndo(go, "Import OBJ");
                                
                                successCount++;
                            }
                            else
                            {
                                Debug.LogError($"[OBJ Manager] LoadObj returned null for: {objPath}");
                                failCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[OBJ Manager] Failed to load OBJ: {objPath}\n{ex}");
                            failCount++;
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[OBJ Manager] OBJ file not found: {data.objFilePath ?? data.objectName}");
                        failCount++;
                    }
                }
                
                EditorUtility.DisplayDialog("Import 완료", 
                    $"Import 완료:\n성공: {successCount}개\n실패: {failCount}개", "OK");
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


