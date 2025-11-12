using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

public class ObjDropWatcherWindow : EditorWindow, ISerializationCallbackReceiver
{
    [SerializeField] private WatchConfig config;
    private Vector2 _scroll;
    private Vector2 _folderScroll;
    // TODO: API 연동 후 그룹의 스캔 데이터를 조회하여 _items를 채우는 기능 구현 필요
    private readonly List<Item> _items = new();
    private int? _selectedGroupId;
    private List<GroupData> _availableGroups = new();
    private bool _isLoadingGroups = false;
    private UnityWebRequest _pendingRequest = null;

    [Serializable] class Item { public string folder; public string obj; public string label; }
    
    [Serializable]
    class GroupData
    {
        public int group_id;
        public string name;
        // meta_data는 Dictionary이므로 JsonUtility로 파싱할 수 없음 (무시됨)
        public string created_at;
        public string updated_at;
    }
    
    [Serializable]
    class GroupsResponse
    {
        public string message;
        public bool success;
        public GroupsData data;
        public string timestamp;
    }
    
    [Serializable]
    class GroupsData
    {
        public GroupData[] groups;
        public int total;
        public int skip;
        public int limit;
    }


    [MenuItem("Tools/OBJ Drop Watcher")]
    public static void Open()
    {
        var w = GetWindow<ObjDropWatcherWindow>("OBJ Drop Watcher");
        w.minSize = new Vector2(520, 400);
        w.Show();
    }

    private void OnDisable()
    {
        // 진행 중인 요청 정리
        if (_pendingRequest != null)
        {
            EditorApplication.update -= CheckPendingRequest;
            _pendingRequest.Dispose();
            _pendingRequest = null;
        }
    }

    void OnGUI()
    {
        EditorGUILayout.Space();
        
        EditorGUI.BeginChangeCheck();
        var newConfig = (WatchConfig)EditorGUILayout.ObjectField("WatchConfig", config, typeof(WatchConfig), false);
        bool configChanged = EditorGUI.EndChangeCheck();
        
        if (configChanged)
        {
            // Config 변경은 즉시 적용 (GUI가 올바른 값을 표시하도록)
            // 단, 유효성 검사를 통해 안전하게 처리
            try
            {
                if (newConfig != null && AssetDatabase.Contains(newConfig))
                {
                    config = newConfig;
                }
                else if (newConfig == null)
                {
                    config = null;
                }
                // newConfig가 null이 아니지만 AssetDatabase에 없는 경우는 무시
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[ObjDropWatcher] Failed to set config: {ex.Message}");
            }
        }

        if (config != null)
        {
            // config 값들을 안전하게 캐시하여 반복 접근 방지
            string cachedApiUrl = null;
            bool cachedIncludeSubdirs = false;
            int cachedScanDebounce = 0;
            string cachedObjPatterns = null;
            float cachedUnitScale = 0f;
            
            try
            {
                cachedApiUrl = config.apiServerUrl;
                cachedIncludeSubdirs = config.includeSubdirectories;
                cachedScanDebounce = config.scanDebounceMs;
                cachedObjPatterns = config.objPatterns;
                cachedUnitScale = config.unitScale;
            }
            catch (System.Exception ex)
            {
                // config 접근 실패 시 경고하고 계속 진행
                EditorGUILayout.HelpBox($"Config 접근 오류: {ex.Message}", MessageType.Warning);
                return;
            }
            
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("API Server", EditorStyles.boldLabel);
            
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            string apiUrl = EditorGUILayout.TextField("Server URL", cachedApiUrl ?? "");
            bool apiUrlChanged = EditorGUI.EndChangeCheck();
            EditorGUILayout.EndHorizontal();
            
            if (apiUrlChanged && apiUrl != cachedApiUrl)
            {
                // GUI 이벤트 처리 중 직렬화를 피하기 위해 지연 실행
                string apiUrlToSet = apiUrl;
                EditorApplication.delayCall += () =>
                {
                    try
                    {
                        if (config != null)
                        {
                            config.apiServerUrl = apiUrlToSet;
                            MarkConfigDirty();
                            Repaint();
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"[ObjDropWatcher] Failed to update API URL: {ex.Message}");
                    }
                };
            }
            
            EditorGUILayout.HelpBox("API 서버 URL을 입력하세요.\n예: http://localhost:8000", MessageType.Info);
            
            // API URL 유효성 표시
            if (!string.IsNullOrWhiteSpace(cachedApiUrl))
            {
                bool isValidUrl = Uri.TryCreate(cachedApiUrl, UriKind.Absolute, out Uri result) && 
                                 (result.Scheme == Uri.UriSchemeHttp || result.Scheme == Uri.UriSchemeHttps);
                
                if (isValidUrl)
                {
                    EditorGUILayout.HelpBox($"✓ URL 유효: {cachedApiUrl}", MessageType.Info);
                    
                    // 그룹 선택
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("그룹 선택", EditorStyles.boldLabel);
                    
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("그룹 목록 새로고침", GUILayout.Height(22)))
                    {
                        if (!_isLoadingGroups)
                        {
                            EditorApplication.delayCall += RefreshGroupList;
                        }
                    }
                    
                    if (_isLoadingGroups)
                    {
                        EditorGUILayout.LabelField("로딩 중...", EditorStyles.miniLabel);
                    }
                    EditorGUILayout.EndHorizontal();
                    
                    if (_availableGroups.Count > 0)
                    {
                        EditorGUILayout.LabelField($"조회된 그룹: {_availableGroups.Count}개", EditorStyles.miniLabel);
                        
                        _folderScroll = EditorGUILayout.BeginScrollView(_folderScroll, GUILayout.Height(120));
                        foreach (var group in _availableGroups)
                        {
                            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                            
                            bool isSelected = _selectedGroupId == group.group_id;
                            var originalColor = GUI.backgroundColor;
                            if (isSelected)
                            {
                                GUI.backgroundColor = Color.green;
                            }
                            
                            int groupIdToSelect = group.group_id; // 클로저를 위한 로컬 변수
                            if (GUILayout.Button($"{group.name} (ID: {group.group_id})", GUILayout.Height(24)))
                            {
                                // GUI 이벤트 처리 중 직렬화를 피하기 위해 지연 실행
                                EditorApplication.delayCall += () =>
                                {
                                    _selectedGroupId = groupIdToSelect;
                                    Repaint();
                                };
                            }
                            
                            GUI.backgroundColor = originalColor;
                            
                            EditorGUILayout.EndHorizontal();
                        }
                        EditorGUILayout.EndScrollView();
                        
                        if (_selectedGroupId.HasValue)
                        {
                            var selectedGroup = _availableGroups.FirstOrDefault(g => g.group_id == _selectedGroupId.Value);
                            if (selectedGroup != null)
                            {
                                EditorGUILayout.HelpBox($"선택된 그룹: {selectedGroup.name} (ID: {selectedGroup.group_id})", MessageType.Info);
                            }
                        }
                        else
                        {
                            EditorGUILayout.HelpBox("감시할 그룹을 선택하세요.", MessageType.Info);
                        }
                    }
                    else if (!_isLoadingGroups)
                    {
                        EditorGUILayout.HelpBox("그룹이 없습니다.\n'그룹 목록 새로고침' 버튼을 눌러 그룹을 조회하세요.", MessageType.Warning);
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox($"✗ 잘못된 URL: {cachedApiUrl}\n올바른 HTTP/HTTPS URL을 입력하세요.", MessageType.Warning);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("API 서버 URL을 입력하세요.", MessageType.Info);
            }
            
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
            
            EditorGUI.BeginChangeCheck();
            bool includeSubdirs = EditorGUILayout.Toggle("Include Subdirectories", cachedIncludeSubdirs);
            int scanDebounce = EditorGUILayout.IntField("Scan Debounce (ms)", cachedScanDebounce);
            string objPatterns = EditorGUILayout.TextField("OBJ Patterns", cachedObjPatterns ?? "*.obj");
            float unitScale = EditorGUILayout.FloatField("Unit Scale", cachedUnitScale);
            bool settingsChanged = EditorGUI.EndChangeCheck();
            
            if (settingsChanged)
            {
                // GUI 이벤트 처리 중 직렬화를 피하기 위해 지연 실행
                bool includeSubdirsToSet = includeSubdirs;
                int scanDebounceToSet = scanDebounce;
                string objPatternsToSet = objPatterns;
                float unitScaleToSet = unitScale;
                
                EditorApplication.delayCall += () =>
                {
                    try
                    {
                        if (config != null)
                        {
                            config.includeSubdirectories = includeSubdirsToSet;
                            config.scanDebounceMs = scanDebounceToSet;
                            config.objPatterns = objPatternsToSet;
                            config.unitScale = unitScaleToSet;
                            MarkConfigDirty();
                            Repaint();
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"[ObjDropWatcher] Failed to update settings: {ex.Message}");
                    }
                };
            }
        }
        else
        {
            EditorGUILayout.HelpBox("WatchConfig를 먼저 선택하세요.\nCreate > Configs > WatchConfig로 생성할 수 있습니다.", MessageType.Info);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("OBJ Files", EditorStyles.boldLabel);
        
        _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true), GUILayout.MinHeight(150));
        if (_items.Count == 0)
        {
            EditorGUILayout.HelpBox("OBJ 파일 목록이 비어있습니다.\nAPI 연동 후 그룹의 스캔 데이터를 조회할 수 있습니다.", MessageType.Info);
        }
        else
        {
            foreach (var it in _items)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(it.label, EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Folder", it.folder);
                EditorGUILayout.LabelField("OBJ", it.obj);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Spawn in Scene", GUILayout.Height(22))) Spawn(it.obj);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }
        }
        EditorGUILayout.EndScrollView();
        
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("Transform Export/Import 기능은 별도 툴로 분리되었습니다.\nTools > Object Transform Exporter를 사용하세요.", MessageType.Info);
        if (GUILayout.Button("Open Transform Exporter", GUILayout.Height(24)))
        {
            ObjectTransformExporterWindow.Open();
        }
    }

    void RefreshGroupList()
    {
        if (config == null)
        {
            ScheduleRepaint();
            return;
        }
        
        // config 접근을 안전하게 처리
        string apiUrl = null;
        try
        {
            apiUrl = config.apiServerUrl;
        }
        catch (System.Exception)
        {
            // config가 유효하지 않은 경우 무시
            ScheduleRepaint();
            return;
        }
        
        if (string.IsNullOrWhiteSpace(apiUrl))
        {
            EditorUtility.DisplayDialog("API URL 없음", "API 서버 URL을 입력하세요.", "OK");
            ScheduleRepaint();
            return;
        }
        
        if (!Uri.TryCreate(apiUrl, UriKind.Absolute, out Uri result) || 
            (result.Scheme != Uri.UriSchemeHttp && result.Scheme != Uri.UriSchemeHttps))
        {
            EditorUtility.DisplayDialog("잘못된 URL", "올바른 HTTP/HTTPS URL을 입력하세요.", "OK");
            ScheduleRepaint();
            return;
        }
        
        // API URL 정규화 (끝에 슬래시 제거)
        apiUrl = apiUrl.TrimEnd('/');
        string groupsEndpoint = $"{apiUrl}/api/v1/groups/";
        
        _isLoadingGroups = true;
        _availableGroups.Clear();
        ScheduleRepaint();
        
        // UnityWebRequest를 사용하여 비동기 요청
        _pendingRequest = UnityWebRequest.Get(groupsEndpoint);
        _pendingRequest.SetRequestHeader("Content-Type", "application/json");
        _pendingRequest.SendWebRequest();
        
        // EditorApplication.update를 사용하여 요청 완료 대기
        EditorApplication.update += CheckPendingRequest;
    }
    
    void CheckPendingRequest()
    {
        if (_pendingRequest == null) return;
        
        if (_pendingRequest.isDone)
        {
            EditorApplication.update -= CheckPendingRequest;
            
            _isLoadingGroups = false;
            
            if (_pendingRequest.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    string jsonResponse = _pendingRequest.downloadHandler.text;
                    Debug.Log($"[ObjDropWatcher] API Response: {jsonResponse}");
                    
                    // Unity의 JsonUtility는 중첩된 구조를 파싱하기 어려울 수 있으므로
                    // 수동으로 파싱하거나 간단한 구조로 변환
                    GroupsResponse response = JsonUtility.FromJson<GroupsResponse>(jsonResponse);
                    
                    if (response != null && response.success && response.data != null && response.data.groups != null)
                    {
                        _availableGroups.Clear();
                        _availableGroups.AddRange(response.data.groups);
                        Debug.Log($"[ObjDropWatcher] Successfully fetched {_availableGroups.Count} groups from API");
                    }
                    else
                    {
                        string errorMsg = response != null ? response.message : "Unknown error";
                        Debug.LogWarning($"[ObjDropWatcher] API response indicates failure: {errorMsg}");
                        EditorUtility.DisplayDialog("API 오류", $"그룹 조회 실패: {errorMsg}", "OK");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ObjDropWatcher] Failed to parse API response: {ex.Message}\nStack trace: {ex.StackTrace}");
                    EditorUtility.DisplayDialog("파싱 오류", $"응답 파싱 실패: {ex.Message}\n\nUnity의 JsonUtility는 Dictionary 타입을 지원하지 않습니다.\nAPI 응답의 meta_data 필드가 문제일 수 있습니다.", "OK");
                }
            }
            else
            {
                Debug.LogError($"[ObjDropWatcher] API request failed: {_pendingRequest.error}");
                EditorUtility.DisplayDialog("요청 실패", $"API 요청 실패: {_pendingRequest.error}", "OK");
            }
            
            _pendingRequest.Dispose();
            _pendingRequest = null;
            ScheduleRepaint();
        }
    }
    
    void ScheduleRepaint()
    {
        // GUI 이벤트 처리 외부에서 리페인트를 안전하게 스케줄링
        EditorApplication.delayCall += Repaint;
    }

    void Spawn(string objPath)
    {
        try
        {
            var go = RuntimeObjLoader.LoadObj(objPath);
            Undo.RegisterCreatedObjectUndo(go, "Spawn OBJ");
            Selection.activeObject = go;

            // RuntimeObjLoader shifts the mesh so the lowest Y becomes 0, so spawn directly on ground.
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;

            // 단위 보정: config의 unitScale 사용 (기본값 1000f = mm → m 변환)
            float unitScale = config != null ? config.unitScale : 1000f;
            go.transform.localScale = Vector3.one * unitScale;

            Debug.Log($"[Spawned with scale x{unitScale}] {objPath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Spawn 실패: {objPath}\n{ex}");
        }
    }


    void MarkConfigDirty()
    {
        if (config == null)
            return;

        // DontSaveInEditor 플래그가 설정되어 있으면 저장하지 않음
        if ((config.hideFlags & HideFlags.DontSaveInEditor) != 0)
            return;

        // AssetDatabase를 사용하여 더 안전하게 체크
        if (!AssetDatabase.Contains(config))
            return;

        // 객체가 파괴되었는지 체크 (Unity 특수 케이스)
        if (config.Equals(null))
            return;

        try
        {
            // 객체가 실제로 Unity 에셋인지 확인
            string assetPath = AssetDatabase.GetAssetPath(config);
            if (string.IsNullOrEmpty(assetPath))
                return;

            EditorUtility.SetDirty(config);
        }
        catch (System.ArgumentException)
        {
            // 잘못된 객체인 경우 무시 (예: DontSaveInEditor 객체)
        }
        catch (System.Exception ex)
        {
            // SetDirty 실패 시 무시 (메모리 오브젝트이거나 이미 삭제된 경우)
            Debug.LogWarning($"[ObjDropWatcher] Failed to mark config dirty: {ex.Message}");
        }
    }

    // ISerializationCallbackReceiver 구현: 직렬화 전/후 config 유효성 검사
    public void OnBeforeSerialize()
    {
        // 직렬화 전에 config가 직렬화 가능한지 확인
        // DontSaveInEditor 플래그가 있는 경우 assertion 오류를 방지하기 위해 null로 설정
        if (config != null)
        {
            try
            {
                // Unity 객체가 파괴되었는지 확인
                if (config.Equals(null))
                {
                    config = null;
                    return;
                }
                
                // DontSaveInEditor 플래그 확인
                // 이 플래그가 있으면 Unity가 직렬화 시 assertion 오류를 발생시킴
                HideFlags flags = config.hideFlags;
                if ((flags & HideFlags.DontSaveInEditor) != 0)
                {
                    // DontSaveInEditor 플래그가 있으면 직렬화에서 제외
                    config = null;
                }
            }
            catch (System.Exception)
            {
                // 예외 발생 시 안전을 위해 config를 null로 설정
                // 이렇게 하면 직렬화 오류를 방지할 수 있음
                config = null;
            }
        }
    }

    public void OnAfterDeserialize()
    {
        // 역직렬화 후 추가 검증은 필요시 수행
        // 현재는 기본 동작에 의존
    }

}
