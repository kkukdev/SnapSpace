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
    private readonly List<Item> _items = new();
    private int? _selectedGroupId;
    private List<GroupData> _availableGroups = new();
    private bool _isLoadingGroups = false;
    private bool _isLoadingScans = false;
    private UnityWebRequest _pendingRequest = null;

    [Serializable] class Item 
    { 
        public string folder; 
        public string obj; 
        public string label;
        public string originalPath;  // 원본 파일 경로
        public string retouchedPath;  // 리터치된 파일 경로
        public int scanId;            // 스캔 ID
    }
    
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

    [Serializable]
    class ScanData
    {
        public int scan_id;
        public int group_id;
        public string status;
        public string original_file_path;
        public string retouched_file_path;
        public string created_at;
        public string updated_at;
    }

    [Serializable]
    class GroupScansResponse
    {
        public string message;
        public bool success;
        public ScanData[] data;  // 백엔드에서 data는 직접 배열로 반환됨
        public int total;
        public string timestamp;
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
            EditorApplication.update -= CheckPendingScansRequest;
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
            int cachedScanDebounce = 0;
            string cachedObjPatterns = null;
            float cachedUnitScale = 0f;
            
            try
            {
                cachedApiUrl = config.apiServerUrl;
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
                                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                                EditorGUILayout.HelpBox($"선택된 그룹: {selectedGroup.name} (ID: {selectedGroup.group_id})", MessageType.Info);
                                
                                EditorGUILayout.BeginHorizontal();
                                if (GUILayout.Button("스캔 데이터 조회", GUILayout.Height(24)))
                                {
                                    if (!_isLoadingScans)
                                    {
                                        EditorApplication.delayCall += () => RefreshGroupScans(_selectedGroupId.Value);
                                    }
                                }
                                
                                if (_isLoadingScans)
                                {
                                    EditorGUILayout.LabelField("로딩 중...", EditorStyles.miniLabel);
                                }
                                EditorGUILayout.EndHorizontal();
                                EditorGUILayout.EndVertical();
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
            int scanDebounce = EditorGUILayout.IntField("Scan Debounce (ms)", cachedScanDebounce);
            string objPatterns = EditorGUILayout.TextField("OBJ Patterns", cachedObjPatterns ?? "*.obj");
            float unitScale = EditorGUILayout.FloatField("Unit Scale", cachedUnitScale);
            bool settingsChanged = EditorGUI.EndChangeCheck();
            
            if (settingsChanged)
            {
                // GUI 이벤트 처리 중 직렬화를 피하기 위해 지연 실행
                int scanDebounceToSet = scanDebounce;
                string objPatternsToSet = objPatterns;
                float unitScaleToSet = unitScale;
                
                EditorApplication.delayCall += () =>
                {
                    try
                    {
                        if (config != null)
                        {
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
                
                // Original 파일 정보 및 버튼
                if (!string.IsNullOrEmpty(it.originalPath))
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Original:", Path.GetFileName(it.originalPath), GUILayout.Width(200));
                    if (GUILayout.Button("Import Original", GUILayout.Height(22), GUILayout.Width(120)))
                    {
                        if (File.Exists(it.originalPath))
                        {
                            Spawn(it.originalPath);
                        }
                        else
                        {
                            EditorUtility.DisplayDialog("파일 없음", $"파일을 찾을 수 없습니다:\n{it.originalPath}", "OK");
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                }
                
                // Retouched 파일 정보 및 버튼
                if (!string.IsNullOrEmpty(it.retouchedPath))
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Retouched:", Path.GetFileName(it.retouchedPath), GUILayout.Width(200));
                    if (GUILayout.Button("Import Retouched", GUILayout.Height(22), GUILayout.Width(120)))
                    {
                        if (File.Exists(it.retouchedPath))
                        {
                            Spawn(it.retouchedPath);
                        }
                        else
                        {
                            EditorUtility.DisplayDialog("파일 없음", $"파일을 찾을 수 없습니다:\n{it.retouchedPath}", "OK");
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                }
                
                // 둘 다 없는 경우 (이론적으로는 발생하지 않아야 함)
                if (string.IsNullOrEmpty(it.originalPath) && string.IsNullOrEmpty(it.retouchedPath))
                {
                    EditorGUILayout.HelpBox("OBJ 파일 경로가 없습니다.", MessageType.Warning);
                }
                
                EditorGUILayout.EndVertical();
            }
        }
        EditorGUILayout.EndScrollView();
        
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("Transform Export/Import 기능은 별도 툴로 분리되었습니다.\nTools > Object Transform Manager를 사용하세요.", MessageType.Info);
        if (GUILayout.Button("Open Transform Manager", GUILayout.Height(24)))
        {
            ObjectTransformManagerWindow.Open();
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
        
        // WatchConfig에서 엔드포인트 경로 가져오기
        string groupsEndpointPath = config.groupsEndpoint ?? "/api/v1/groups/";
        if (!groupsEndpointPath.StartsWith("/"))
            groupsEndpointPath = "/" + groupsEndpointPath;
        
        string groupsEndpoint = $"{apiUrl}{groupsEndpointPath}";
        
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
    
    void RefreshGroupScans(int groupId)
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
        
        // WatchConfig에서 엔드포인트 경로 가져오기
        string groupScansEndpointPath = config.groupScansEndpoint ?? "/api/v1/groups/{group_id}/scans";
        if (!groupScansEndpointPath.StartsWith("/"))
            groupScansEndpointPath = "/" + groupScansEndpointPath;
        
        // {group_id}를 실제 groupId로 치환
        string groupScansEndpoint = $"{apiUrl}{groupScansEndpointPath.Replace("{group_id}", groupId.ToString())}";
        
        _isLoadingScans = true;
        _items.Clear();
        ScheduleRepaint();
        
        // UnityWebRequest를 사용하여 비동기 요청
        _pendingRequest = UnityWebRequest.Get(groupScansEndpoint);
        _pendingRequest.SetRequestHeader("Content-Type", "application/json");
        _pendingRequest.SendWebRequest();
        
        // EditorApplication.update를 사용하여 요청 완료 대기
        EditorApplication.update += CheckPendingScansRequest;
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
    
    void CheckPendingScansRequest()
    {
        if (_pendingRequest == null) return;
        
        if (_pendingRequest.isDone)
        {
            EditorApplication.update -= CheckPendingScansRequest;
            
            _isLoadingScans = false;
            
            if (_pendingRequest.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    string jsonResponse = _pendingRequest.downloadHandler.text;
                    Debug.Log($"[ObjDropWatcher] Scans API Response: {jsonResponse}");
                    
                    GroupScansResponse response = JsonUtility.FromJson<GroupScansResponse>(jsonResponse);
                    
                    if (response != null && response.success && response.data != null)
                    {
                        _items.Clear();
                        
                        foreach (var scan in response.data)
                        {
                            // original_file_path와 retouched_file_path는 폴더 경로를 반환함
                            // 폴더 안에서 .obj 파일을 찾아야 함
                            string originalPath = null;
                            string retouchedPath = null;
                            
                            // 원본 폴더 경로 처리
                            if (!string.IsNullOrEmpty(scan.original_file_path))
                            {
                                // /project_root/ 부분을 실제 projectRoot 경로로 치환
                                string folderPath = ReplaceProjectRootInPath(scan.original_file_path);
                                
                                // 폴더가 존재하는지 확인
                                if (Directory.Exists(folderPath))
                                {
                                    // 폴더 안에서 .obj 파일 찾기
                                    string[] objFiles = Directory.GetFiles(folderPath, "*.obj", SearchOption.TopDirectoryOnly);
                                    if (objFiles.Length > 0)
                                    {
                                        // 첫 번째 .obj 파일 사용
                                        originalPath = objFiles[0];
                                        Debug.Log($"[ObjDropWatcher] Found {objFiles.Length} OBJ file(s) in original folder: {folderPath}");
                                    }
                                    else
                                    {
                                        Debug.LogWarning($"[ObjDropWatcher] No OBJ files found in original folder: {folderPath}");
                                    }
                                }
                                else
                                {
                                    Debug.LogWarning($"[ObjDropWatcher] Original folder does not exist: {folderPath}");
                                }
                            }
                            
                            // 리터치된 폴더 경로 처리
                            if (!string.IsNullOrEmpty(scan.retouched_file_path))
                            {
                                // /project_root/ 부분을 실제 projectRoot 경로로 치환
                                string folderPath = ReplaceProjectRootInPath(scan.retouched_file_path);
                                
                                // 폴더가 존재하는지 확인
                                if (Directory.Exists(folderPath))
                                {
                                    // 폴더 안에서 .obj 파일 찾기
                                    string[] objFiles = Directory.GetFiles(folderPath, "*.obj", SearchOption.TopDirectoryOnly);
                                    if (objFiles.Length > 0)
                                    {
                                        // 첫 번째 .obj 파일 사용
                                        retouchedPath = objFiles[0];
                                        Debug.Log($"[ObjDropWatcher] Found {objFiles.Length} OBJ file(s) in retouched folder: {folderPath}");
                                    }
                                    else
                                    {
                                        Debug.LogWarning($"[ObjDropWatcher] No OBJ files found in retouched folder: {folderPath}");
                                    }
                                }
                                else
                                {
                                    Debug.LogWarning($"[ObjDropWatcher] Retouched folder does not exist: {folderPath}");
                                }
                            }
                            
                            // original 또는 retouched 중 하나라도 있으면 Item 추가
                            if (!string.IsNullOrEmpty(originalPath) || !string.IsNullOrEmpty(retouchedPath))
                            {
                                // 기본 경로 결정 (retouched가 있으면 retouched, 없으면 original)
                                string basePath = !string.IsNullOrEmpty(retouchedPath) ? retouchedPath : originalPath;
                                string folder = Path.GetDirectoryName(basePath);
                                string label = $"Scan {scan.scan_id} (Status: {scan.status})";
                                
                                _items.Add(new Item 
                                { 
                                    folder = folder, 
                                    obj = basePath,  // 호환성을 위해 유지
                                    label = label,
                                    originalPath = originalPath,
                                    retouchedPath = retouchedPath,
                                    scanId = scan.scan_id
                                });
                            }
                        }
                        
                        int originalCount = _items.Count(it => !string.IsNullOrEmpty(it.originalPath));
                        int retouchedCount = _items.Count(it => !string.IsNullOrEmpty(it.retouchedPath));
                        
                        Debug.Log($"[ObjDropWatcher] Successfully fetched {_items.Count} items from {response.data.Length} scans (Original: {originalCount}, Retouched: {retouchedCount})");
                        
                        if (_items.Count == 0)
                        {
                            EditorUtility.DisplayDialog("스캔 조회 완료", 
                                $"조회된 스캔: {response.data.Length}개\n\nOBJ 파일이 없습니다.", "OK");
                        }
                        else
                        {
                            EditorUtility.DisplayDialog("스캔 조회 완료", 
                                $"조회된 스캔: {response.data.Length}개\n항목: {_items.Count}개\n\nOriginal 파일: {originalCount}개\nRetouched 파일: {retouchedCount}개", "OK");
                        }
                    }
                    else
                    {
                        string errorMsg = response != null ? response.message : "Unknown error";
                        Debug.LogWarning($"[ObjDropWatcher] API response indicates failure: {errorMsg}");
                        EditorUtility.DisplayDialog("API 오류", $"스캔 조회 실패: {errorMsg}", "OK");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ObjDropWatcher] Failed to parse scans API response: {ex.Message}\nStack trace: {ex.StackTrace}");
                    EditorUtility.DisplayDialog("파싱 오류", $"응답 파싱 실패: {ex.Message}", "OK");
                }
            }
            else
            {
                Debug.LogError($"[ObjDropWatcher] Scans API request failed: {_pendingRequest.error}");
                EditorUtility.DisplayDialog("요청 실패", $"스캔 조회 실패: {_pendingRequest.error}", "OK");
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

    /// <summary>
    /// 프로젝트 루트 경로를 가져옵니다.
    /// WatchConfig에 projectRoot가 설정되어 있으면 사용하고, 없으면 Unity 프로젝트 루트를 반환합니다.
    /// </summary>
    string GetProjectRoot()
    {
        if (config != null)
        {
            try
            {
                string projectRoot = config.projectRoot;
                if (!string.IsNullOrWhiteSpace(projectRoot))
                {
                    // 절대 경로로 변환
                    if (Path.IsPathRooted(projectRoot))
                    {
                        return projectRoot;
                    }
                    else
                    {
                        // 상대 경로인 경우 Application.dataPath 기준으로 변환
                        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", projectRoot));
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[ObjDropWatcher] Failed to get projectRoot from config: {ex.Message}");
            }
        }
        
        // 기본값: Unity 프로젝트 루트 (Assets의 상위 디렉토리)
        return Path.GetDirectoryName(Application.dataPath);
    }

    /// <summary>
    /// API 응답 경로에서 /project_root/ 부분을 실제 projectRoot 경로로 치환합니다.
    /// 예: "/project_root/storage/uploads/..." -> "C:\...\storage\uploads\..."
    /// </summary>
    string ReplaceProjectRootInPath(string apiPath)
    {
        if (string.IsNullOrEmpty(apiPath))
            return apiPath;

        string projectRootPath = GetProjectRoot();
        string remainingPath = null;

        // /project_root/ 또는 project_root/ 패턴 찾기
        string projectRootPlaceholder = "/project_root/";
        if (apiPath.StartsWith(projectRootPlaceholder, StringComparison.OrdinalIgnoreCase))
        {
            remainingPath = apiPath.Substring(projectRootPlaceholder.Length);
        }
        // project_root/로 시작하는 경우 (앞에 슬래시 없음)
        else if (apiPath.StartsWith("project_root/", StringComparison.OrdinalIgnoreCase))
        {
            remainingPath = apiPath.Substring("project_root/".Length);
        }
        // /project_root로 시작하는 경우 (뒤에 슬래시 없음)
        else if (apiPath.StartsWith("/project_root", StringComparison.OrdinalIgnoreCase) && 
                 apiPath.Length > "/project_root".Length)
        {
            remainingPath = apiPath.Substring("/project_root".Length).TrimStart('/');
        }

        if (remainingPath != null)
        {
            // 슬래시를 백슬래시로 변환 (Windows 경로 호환성)
            remainingPath = remainingPath.Replace('/', Path.DirectorySeparatorChar);
            // 경로 결합
            string fullPath = Path.Combine(projectRootPath, remainingPath);
            // 정규화 (.. 처리 등)
            return Path.GetFullPath(fullPath);
        }

        // 치환할 패턴이 없으면 원본 반환
        return apiPath;
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
