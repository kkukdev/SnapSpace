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
        public MemoData[] memos;      // 메모 데이터
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
    class MemoData
    {
        public string type;
        public string anchor;
        public string content;
        public string source;
        public string file_path;
        public int file_size;
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
        // memos는 파일에서 직접 읽어오므로 API 응답에서는 제외
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
            string objPatterns = EditorGUILayout.TextField("Mesh File Patterns", cachedObjPatterns ?? "*.obj,*.glb,*.fbx");
            EditorGUILayout.HelpBox("메시 파일 검색 패턴을 쉼표로 구분하여 입력하세요.\n예: *.obj,*.glb,*.fbx", MessageType.Info);
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
            EditorGUILayout.LabelField("Mesh Files", EditorStyles.boldLabel);
        
        _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true), GUILayout.MinHeight(150));
        if (_items.Count == 0)
        {
            EditorGUILayout.HelpBox("메시 파일 목록이 비어있습니다.\nAPI 연동 후 그룹의 스캔 데이터를 조회할 수 있습니다.", MessageType.Info);
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
                            Spawn(it.originalPath, it.memos);
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
                            Spawn(it.retouchedPath, it.memos);
                        }
                        else
                        {
                            EditorUtility.DisplayDialog("파일 없음", $"파일을 찾을 수 없습니다:\n{it.retouchedPath}", "OK");
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                }
                
                // Memos 정보 표시
                if (it.memos != null && it.memos.Length > 0)
                {
                    int textMemosCount = it.memos.Count(m => m != null && m.type == "text");
                    if (textMemosCount > 0)
                    {
                        EditorGUILayout.HelpBox($"메모: {textMemosCount}개의 텍스트 메모가 있습니다. 메시를 Import하면 3D 공간에 표시됩니다.", MessageType.Info);
                    }
                }
                
                // 둘 다 없는 경우 (이론적으로는 발생하지 않아야 함)
                if (string.IsNullOrEmpty(it.originalPath) && string.IsNullOrEmpty(it.retouchedPath))
                {
                    EditorGUILayout.HelpBox("메시 파일 경로가 없습니다.", MessageType.Warning);
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
        
        // HTTP URL인 경우 Unity의 보안 설정 확인
        bool isHttp = apiUrl.StartsWith("http://", System.StringComparison.OrdinalIgnoreCase);
        if (isHttp)
        {
            // Unity 2021.2 이상에서는 HTTP 연결이 기본적으로 차단됨
            // 개발 환경에서는 HTTP를 허용하도록 설정 필요
            // 또는 CertificateHandler를 설정하여 HTTP 연결 허용 (보안 위험, 개발용)
        }
        
        // WatchConfig에서 엔드포인트 경로 가져오기
        string groupsEndpointPath = config.groupsEndpoint ?? "/api/v1/groups/";
        if (!groupsEndpointPath.StartsWith("/"))
            groupsEndpointPath = "/" + groupsEndpointPath;
        
        string groupsEndpoint = $"{apiUrl}{groupsEndpointPath}";
        
        _isLoadingGroups = true;
        _availableGroups.Clear();
        ScheduleRepaint();
        
        // UnityWebRequest를 사용하여 비동기 요청
        try
        {
            _pendingRequest = UnityWebRequest.Get(groupsEndpoint);
            _pendingRequest.SetRequestHeader("Content-Type", "application/json");
            
            // HTTP 연결 허용을 위한 CertificateHandler 설정 (개발 환경용)
            if (isHttp)
            {
                // HTTP 연결을 허용하기 위해 CertificateHandler 설정
                // 보안 위험이 있으므로 개발 환경에서만 사용
                _pendingRequest.certificateHandler = new BypassCertificateHandler();
            }
            
            _pendingRequest.SendWebRequest();
            
            // EditorApplication.update를 사용하여 요청 완료 대기
            EditorApplication.update += CheckPendingRequest;
        }
        catch (InvalidOperationException ex)
        {
            // Unity 2021.2 이상에서 HTTP 연결이 차단된 경우
            if (ex.Message.Contains("Insecure connection not allowed") || ex.Message.Contains("insecure"))
            {
                _isLoadingGroups = false;
                _pendingRequest?.Dispose();
                _pendingRequest = null;
                
                string errorMessage = $"HTTP 연결이 차단되었습니다.\n\n" +
                                    $"Unity Editor에서 HTTP 연결을 허용하려면:\n" +
                                    $"Edit > Project Settings > Player > Other Settings > " +
                                    $"Allow downloads over HTTP 옵션을 활성화하세요.\n\n" +
                                    $"또는 HTTPS를 사용하는 것이 권장됩니다.\n\n" +
                                    $"서버 URL: {apiUrl}";
                
                EditorUtility.DisplayDialog("HTTP 연결 차단", errorMessage, "OK");
                Debug.LogError($"[ObjDropWatcher] HTTP connection blocked: {ex.Message}");
            }
            else
            {
                _isLoadingGroups = false;
                _pendingRequest?.Dispose();
                _pendingRequest = null;
                
                EditorUtility.DisplayDialog("요청 오류", $"요청 전송 실패: {ex.Message}", "OK");
                Debug.LogError($"[ObjDropWatcher] Request failed: {ex.Message}");
            }
            ScheduleRepaint();
        }
        catch (Exception ex)
        {
            _isLoadingGroups = false;
            _pendingRequest?.Dispose();
            _pendingRequest = null;
            
            EditorUtility.DisplayDialog("요청 오류", $"예기치 않은 오류가 발생했습니다: {ex.Message}", "OK");
            Debug.LogError($"[ObjDropWatcher] Unexpected error: {ex.Message}\nStack trace: {ex.StackTrace}");
            ScheduleRepaint();
        }
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
        
        // HTTP URL인 경우 Unity의 보안 설정 확인
        bool isHttp = apiUrl.StartsWith("http://", System.StringComparison.OrdinalIgnoreCase);
        
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
        try
        {
            _pendingRequest = UnityWebRequest.Get(groupScansEndpoint);
            _pendingRequest.SetRequestHeader("Content-Type", "application/json");
            
            // HTTP 연결 허용을 위한 CertificateHandler 설정 (개발 환경용)
            if (isHttp)
            {
                // HTTP 연결을 허용하기 위해 CertificateHandler 설정
                // 보안 위험이 있으므로 개발 환경에서만 사용
                _pendingRequest.certificateHandler = new BypassCertificateHandler();
            }
            
            _pendingRequest.SendWebRequest();
            
            // EditorApplication.update를 사용하여 요청 완료 대기
            EditorApplication.update += CheckPendingScansRequest;
        }
        catch (InvalidOperationException ex)
        {
            // Unity 2021.2 이상에서 HTTP 연결이 차단된 경우
            if (ex.Message.Contains("Insecure connection not allowed") || ex.Message.Contains("insecure"))
            {
                _isLoadingScans = false;
                _pendingRequest?.Dispose();
                _pendingRequest = null;
                
                string errorMessage = $"HTTP 연결이 차단되었습니다.\n\n" +
                                    $"Unity Editor에서 HTTP 연결을 허용하려면:\n" +
                                    $"Edit > Project Settings > Player > Other Settings > " +
                                    $"Allow downloads over HTTP 옵션을 활성화하세요.\n\n" +
                                    $"또는 HTTPS를 사용하는 것이 권장됩니다.\n\n" +
                                    $"서버 URL: {apiUrl}";
                
                EditorUtility.DisplayDialog("HTTP 연결 차단", errorMessage, "OK");
                Debug.LogError($"[ObjDropWatcher] HTTP connection blocked: {ex.Message}");
            }
            else
            {
                _isLoadingScans = false;
                _pendingRequest?.Dispose();
                _pendingRequest = null;
                
                EditorUtility.DisplayDialog("요청 오류", $"요청 전송 실패: {ex.Message}", "OK");
                Debug.LogError($"[ObjDropWatcher] Request failed: {ex.Message}");
            }
            ScheduleRepaint();
        }
        catch (Exception ex)
        {
            _isLoadingScans = false;
            _pendingRequest?.Dispose();
            _pendingRequest = null;
            
            EditorUtility.DisplayDialog("요청 오류", $"예기치 않은 오류가 발생했습니다: {ex.Message}", "OK");
            Debug.LogError($"[ObjDropWatcher] Unexpected error: {ex.Message}\nStack trace: {ex.StackTrace}");
            ScheduleRepaint();
        }
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
                
                // 서버 응답이 없는 경우 (서버가 꺼져있거나 연결할 수 없는 경우)
                string errorMessage = _pendingRequest.error;
                string dialogTitle = "요청 실패";
                string dialogMessage = $"API 요청 실패: {errorMessage}";
                
                // ConnectionError인 경우 서버가 꺼져있을 가능성을 알림
                if (_pendingRequest.result == UnityWebRequest.Result.ConnectionError)
                {
                    dialogTitle = "서버 연결 실패";
                    string apiUrl = "";
                    try
                    {
                        if (config != null)
                        {
                            apiUrl = config.apiServerUrl ?? "";
                        }
                    }
                    catch (System.Exception)
                    {
                        // config 접근 실패 시 무시
                    }
                    
                    if (!string.IsNullOrEmpty(apiUrl))
                    {
                        dialogMessage = $"API 서버에 연결할 수 없습니다.\n\n" +
                                      $"서버 URL: {apiUrl}\n" +
                                      $"오류: {errorMessage}\n\n" +
                                      $"서버가 실행 중인지 확인하세요.";
                    }
                    else
                    {
                        dialogMessage = $"API 서버에 연결할 수 없습니다.\n\n" +
                                      $"오류: {errorMessage}\n\n" +
                                      $"서버가 실행 중인지 확인하세요.";
                    }
                }
                else if (_pendingRequest.result == UnityWebRequest.Result.ProtocolError)
                {
                    dialogTitle = "API 오류";
                    long responseCode = _pendingRequest.responseCode;
                    dialogMessage = $"API 서버에서 오류가 발생했습니다.\n\n" +
                                  $"HTTP 상태 코드: {responseCode}\n" +
                                  $"오류: {errorMessage}";
                }
                
                EditorUtility.DisplayDialog(dialogTitle, dialogMessage, "OK");
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
                    
                    // 기본 응답 구조 파싱 (memos는 파일에서 직접 읽어옴)
                    GroupScansResponse response = JsonUtility.FromJson<GroupScansResponse>(jsonResponse);
                    
                    if (response != null && response.success && response.data != null)
                    {
                        _items.Clear();
                        
                        foreach (var scan in response.data)
                        {
                            // original_file_path와 retouched_file_path는 파일 경로 또는 폴더 경로일 수 있음
                            // 파일 경로인 경우 그대로 사용, 폴더 경로인 경우 objPatterns 설정에 따라 메시 파일을 찾음
                            string originalPath = null;
                            string retouchedPath = null;
                            
                            // 원본 파일 경로 처리
                            if (!string.IsNullOrEmpty(scan.original_file_path))
                            {
                                // /project_root/ 부분을 실제 projectRoot 경로로 치환
                                string convertedPath = ReplaceProjectRootInPath(scan.original_file_path);
                                
                                // 파일인지 폴더인지 확인
                                if (File.Exists(convertedPath))
                                {
                                    // 파일 경로인 경우
                                    originalPath = convertedPath;
                                    Debug.Log($"[ObjDropWatcher] Found original file: {originalPath}");
                                }
                                else if (Directory.Exists(convertedPath))
                                {
                                    // 폴더 경로인 경우 (objPatterns 설정에 따라 파일 찾기)
                                    string[] meshFiles = FindMeshFiles(convertedPath);
                                    if (meshFiles.Length > 0)
                                    {
                                        // 첫 번째 파일 사용
                                        originalPath = meshFiles[0];
                                        Debug.Log($"[ObjDropWatcher] Found {meshFiles.Length} mesh file(s) in original folder: {convertedPath}");
                                    }
                                    else
                                    {
                                        Debug.LogWarning($"[ObjDropWatcher] No mesh files found in original folder: {convertedPath}");
                                    }
                                }
                                else
                                {
                                    Debug.LogWarning($"[ObjDropWatcher] Original path does not exist: {convertedPath}");
                                }
                            }
                            
                            // 리터치된 파일 경로 처리
                            if (!string.IsNullOrEmpty(scan.retouched_file_path))
                            {
                                // /project_root/ 부분을 실제 projectRoot 경로로 치환
                                string convertedPath = ReplaceProjectRootInPath(scan.retouched_file_path);
                                
                                // 파일인지 폴더인지 확인
                                if (File.Exists(convertedPath))
                                {
                                    // 파일 경로인 경우
                                    retouchedPath = convertedPath;
                                    Debug.Log($"[ObjDropWatcher] Found retouched file: {retouchedPath}");
                                }
                                else if (Directory.Exists(convertedPath))
                                {
                                    // 폴더 경로인 경우 (objPatterns 설정에 따라 파일 찾기)
                                    string[] meshFiles = FindMeshFiles(convertedPath);
                                    if (meshFiles.Length > 0)
                                    {
                                        // 첫 번째 파일 사용
                                        retouchedPath = meshFiles[0];
                                        Debug.Log($"[ObjDropWatcher] Found {meshFiles.Length} mesh file(s) in retouched folder: {convertedPath}");
                                    }
                                    else
                                    {
                                        Debug.LogWarning($"[ObjDropWatcher] No mesh files found in retouched folder: {convertedPath}");
                                    }
                                }
                                else
                                {
                                    Debug.LogWarning($"[ObjDropWatcher] Retouched path does not exist: {convertedPath}");
                                }
                            }
                            
                            // memos는 항상 original path의 파일에서 직접 읽어옴
                            MemoData[] memos = new MemoData[0];
                            
                            // original path의 디렉토리에서 memo.txt 파일 찾기
                            string memoFolderPath = null;
                            
                            // 1. originalPath가 있으면 그 디렉토리 사용
                            if (!string.IsNullOrEmpty(originalPath))
                            {
                                if (File.Exists(originalPath))
                                {
                                    memoFolderPath = Path.GetDirectoryName(originalPath);
                                }
                                else if (Directory.Exists(originalPath))
                                {
                                    memoFolderPath = originalPath;
                                }
                            }
                            
                            // 2. originalPath가 없으면 API 응답의 original_file_path에서 디렉토리 추출
                            if (string.IsNullOrEmpty(memoFolderPath) && !string.IsNullOrEmpty(scan.original_file_path))
                            {
                                string convertedPath = ReplaceProjectRootInPath(scan.original_file_path);
                                if (File.Exists(convertedPath))
                                {
                                    memoFolderPath = Path.GetDirectoryName(convertedPath);
                                }
                                else if (Directory.Exists(convertedPath))
                                {
                                    memoFolderPath = convertedPath;
                                }
                            }
                            
                            // memo.txt 파일 찾기 및 파싱
                            if (!string.IsNullOrEmpty(memoFolderPath) && Directory.Exists(memoFolderPath))
                            {
                                string memoFilePath = Path.Combine(memoFolderPath, "memo.txt");
                                if (File.Exists(memoFilePath))
                                {
                                    try
                                    {
                                        memos = ParseMemoFile(memoFilePath);
                                        if (memos != null && memos.Length > 0)
                                        {
                                            Debug.Log($"[ObjDropWatcher] Loaded {memos.Length} memo(s) from file: {memoFilePath}");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.LogWarning($"[ObjDropWatcher] Failed to parse memo file {memoFilePath}: {ex.Message}");
                                    }
                                }
                                else
                                {
                                    Debug.Log($"[ObjDropWatcher] memo.txt not found in folder: {memoFolderPath}");
                                }
                            }
                            else if (string.IsNullOrEmpty(scan.original_file_path))
                            {
                                Debug.Log($"[ObjDropWatcher] No original_file_path available, skipping memo.txt search");
                            }
                            
                            // original 또는 retouched 중 하나라도 있으면 Item 추가
                            // 또는 memos만 있어도 추가 (OBJ 파일이 없어도 메모는 표시 가능)
                            if (!string.IsNullOrEmpty(originalPath) || !string.IsNullOrEmpty(retouchedPath) || memos.Length > 0)
                            {
                                // 기본 경로 결정 (retouched가 있으면 retouched, 없으면 original)
                                string basePath = !string.IsNullOrEmpty(retouchedPath) ? retouchedPath : originalPath;
                                string folder = !string.IsNullOrEmpty(basePath) ? Path.GetDirectoryName(basePath) : "";
                                string label = $"Scan {scan.scan_id} (Status: {scan.status})";
                                
                                _items.Add(new Item 
                                { 
                                    folder = folder, 
                                    obj = basePath ?? "",  // 호환성을 위해 유지
                                    label = label,
                                    originalPath = originalPath ?? "",
                                    retouchedPath = retouchedPath ?? "",
                                    scanId = scan.scan_id,
                                    memos = memos
                                });
                                
                                if (memos.Length > 0)
                                {
                                    Debug.Log($"[ObjDropWatcher] Scan {scan.scan_id} has {memos.Length} memo(s)");
                                }
                            }
                        }
                        
                        int originalCount = _items.Count(it => !string.IsNullOrEmpty(it.originalPath));
                        int retouchedCount = _items.Count(it => !string.IsNullOrEmpty(it.retouchedPath));
                        int memosCount = _items.Sum(it => it.memos != null ? it.memos.Length : 0);
                        
                        Debug.Log($"[ObjDropWatcher] Successfully fetched {_items.Count} items from {response.data.Length} scans (Original: {originalCount}, Retouched: {retouchedCount}, Memos: {memosCount})");
                        
                        if (_items.Count == 0)
                        {
                            EditorUtility.DisplayDialog("스캔 조회 완료", 
                                $"조회된 스캔: {response.data.Length}개\n\nOBJ 파일이 없습니다.", "OK");
                        }
                        else
                        {
                            EditorUtility.DisplayDialog("스캔 조회 완료", 
                                $"조회된 스캔: {response.data.Length}개\n항목: {_items.Count}개\n\nOriginal 파일: {originalCount}개\nRetouched 파일: {retouchedCount}개\n메모: {memosCount}개", "OK");
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
    /// memo.txt 파일을 읽어서 파싱합니다.
    /// 파일 형식: [anchor]content 형태
    /// 예: [x:0.80,y:1.43,z:0.13]이용하
    /// </summary>
    MemoData[] ParseMemoFile(string filePath)
    {
        try
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return new MemoData[0];

            // 파일 읽기 (UTF-8 또는 CP949 인코딩 시도)
            string content = null;
            try
            {
                content = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
            }
            catch (Exception)
            {
                try
                {
                    content = File.ReadAllText(filePath, System.Text.Encoding.GetEncoding(949)); // CP949
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ObjDropWatcher] Failed to read memo file with UTF-8 and CP949: {ex.Message}");
                    return new MemoData[0];
                }
            }

            if (string.IsNullOrEmpty(content))
                return new MemoData[0];

            content = content.Trim();
            if (string.IsNullOrEmpty(content))
                return new MemoData[0];

            // 백엔드와 동일한 파싱 로직: 대괄호 패턴 찾기 [anchor]content
            List<MemoData> memos = new List<MemoData>();
            System.Text.RegularExpressions.Regex pattern = new System.Text.RegularExpressions.Regex(@"\[([^\]]+)\]");
            var matches = pattern.Matches(content);

            if (matches.Count == 0)
            {
                // 대괄호가 없으면 전체 내용을 빈 anchor로 저장
                if (!string.IsNullOrEmpty(content.Trim()))
                {
                    memos.Add(new MemoData
                    {
                        type = "text",
                        anchor = "",
                        content = content.Trim(),
                        source = Path.GetFileName(filePath),
                        file_path = filePath,
                        file_size = (int)new FileInfo(filePath).Length
                    });
                }
            }
            else
            {
                // 각 대괄호 구간 처리
                for (int i = 0; i < matches.Count; i++)
                {
                    var match = matches[i];
                    string anchor = match.Groups[1].Value.Trim();

                    // 현재 대괄호의 끝 위치
                    int startPos = match.Index + match.Length;

                    // 다음 대괄호의 시작 위치 (마지막이면 파일 끝)
                    int endPos = (i + 1 < matches.Count) 
                        ? matches[i + 1].Index 
                        : content.Length;

                    // value 추출 (대괄호 다음부터 다음 대괄호 전까지)
                    string value = content.Substring(startPos, endPos - startPos).Trim();

                    // 같은 anchor가 이미 있으면 기존 content 뒤에 추가
                    var existingMemo = memos.FirstOrDefault(m => m.anchor == anchor);
                    if (existingMemo != null)
                    {
                        existingMemo.content += "\n\n" + value;
                    }
                    else
                    {
                        memos.Add(new MemoData
                        {
                            type = "text",
                            anchor = anchor,
                            content = value,
                            source = Path.GetFileName(filePath),
                            file_path = filePath,
                            file_size = (int)new FileInfo(filePath).Length
                        });
                    }
                }
            }

            return memos.ToArray();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ObjDropWatcher] Failed to parse memo file {filePath}: {ex.Message}");
            return new MemoData[0];
        }
    }

    /// <summary>
    /// anchor 문자열을 Vector3로 파싱합니다.
    /// 예: "x:0.80,y:1.43,z:0.13" -> Vector3(0.80f, 1.43f, 0.13f)
    /// </summary>
    Vector3 ParseAnchor(string anchor)
    {
        if (string.IsNullOrEmpty(anchor))
            return Vector3.zero;

        Vector3 result = Vector3.zero;
        
        try
        {
            // "x:0.80,y:1.43,z:0.13" 형식 파싱
            string[] parts = anchor.Split(',');
            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                if (trimmed.StartsWith("x:", StringComparison.OrdinalIgnoreCase))
                {
                    string value = trimmed.Substring(2).Trim();
                    if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x))
                    {
                        result.x = x;
                    }
                }
                else if (trimmed.StartsWith("y:", StringComparison.OrdinalIgnoreCase))
                {
                    string value = trimmed.Substring(2).Trim();
                    if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y))
                    {
                        result.y = y;
                    }
                }
                else if (trimmed.StartsWith("z:", StringComparison.OrdinalIgnoreCase))
                {
                    string value = trimmed.Substring(2).Trim();
                    if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z))
                    {
                        result.z = z;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ObjDropWatcher] Failed to parse anchor '{anchor}': {ex.Message}");
        }
        
        return result;
    }

    /// <summary>
    /// objPatterns 설정에 따라 폴더에서 메시 파일을 찾습니다.
    /// 여러 패턴을 쉼표로 구분하여 지원합니다 (예: "*.obj,*.glb,*.fbx").
    /// </summary>
    string[] FindMeshFiles(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            return new string[0];
        
        // objPatterns 설정 가져오기
        string patternsStr = "*.obj"; // 기본값
        if (config != null)
        {
            try
            {
                patternsStr = config.objPatterns ?? "*.obj";
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[ObjDropWatcher] Failed to get objPatterns from config: {ex.Message}, using default: *.obj");
            }
        }
        
        // 쉼표로 구분된 패턴 파싱
        string[] patterns = patternsStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < patterns.Length; i++)
        {
            patterns[i] = patterns[i].Trim();
        }
        
        // 각 패턴으로 파일 검색
        List<string> foundFiles = new List<string>();
        foreach (string pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                continue;
            
            try
            {
                string[] files = Directory.GetFiles(folderPath, pattern, SearchOption.TopDirectoryOnly);
                foundFiles.AddRange(files);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ObjDropWatcher] Failed to search files with pattern '{pattern}' in '{folderPath}': {ex.Message}");
            }
        }
        
        // 중복 제거 및 정렬
        foundFiles = foundFiles.Distinct().OrderBy(f => f).ToList();
        
        return foundFiles.ToArray();
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

    void Spawn(string meshPath, MemoData[] memos = null)
    {
        try
        {
            if (string.IsNullOrEmpty(meshPath) || !File.Exists(meshPath))
            {
                EditorUtility.DisplayDialog("파일 없음", $"파일을 찾을 수 없습니다:\n{meshPath}", "OK");
                return;
            }
            
            GameObject go = null;
            string extension = Path.GetExtension(meshPath).ToLowerInvariant();
            
            // 파일 확장자에 따라 적절한 로더 선택
            switch (extension)
            {
                case ".obj":
                    // OBJ 파일의 원본 좌표 시스템을 유지하기 위해 preserveOriginalCoordinates=true 사용
                    // 이렇게 하면 RuntimeObjLoader가 메시를 이동시키지 않고 원본 좌표를 그대로 유지합니다.
                    // OBJ 파일에서 촬영 시작 지점(0,0,0)이 Unity의 원점과 일치합니다.
                    go = RuntimeObjLoader.LoadObj(meshPath, preserveOriginalCoordinates: true);
                    Undo.RegisterCreatedObjectUndo(go, "Spawn OBJ");
                    break;
                    
                case ".glb":
                case ".gltf":
                    // GLB/GLTF 파일은 Unity의 기본 임포트 기능 사용
                    go = LoadGlbOrGltf(meshPath);
                    if (go != null)
                    {
                        Undo.RegisterCreatedObjectUndo(go, "Spawn GLB/GLTF");
                    }
                    break;
                    
                case ".fbx":
                    // FBX 파일은 Unity의 기본 임포트 기능 사용
                    go = LoadFbx(meshPath);
                    if (go != null)
                    {
                        Undo.RegisterCreatedObjectUndo(go, "Spawn FBX");
                    }
                    break;
                    
                default:
                    EditorUtility.DisplayDialog("지원하지 않는 형식", 
                        $"지원하지 않는 파일 형식입니다: {extension}\n\n지원 형식: .obj, .glb, .gltf, .fbx", "OK");
                    return;
            }
            
            if (go == null)
            {
                EditorUtility.DisplayDialog("로드 실패", $"파일을 로드할 수 없습니다:\n{meshPath}", "OK");
                return;
            }
            
            Selection.activeObject = go;

            // 원본 좌표 시스템을 유지하므로, Unity 원점(0,0,0)에 배치
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;

            // 단위 보정: config의 unitScale 사용 (기본값 1000f = mm → m 변환)
            // 주의: 메시 파일의 좌표가 mm 단위라면, Unity의 m 단위로 변환하기 위해 스케일 적용
            // 예: 좌표 (1000mm, 2000mm, 3000mm) → Unity 스케일 1000 적용 → (1m, 2m, 3m)
            float unitScale = config != null ? config.unitScale : 1000f;
            go.transform.localScale = Vector3.one * unitScale;

            Debug.Log($"[Spawned {extension} with scale x{unitScale}, preserving original coordinates] {meshPath}");
            
            // memos가 있으면 GameObject의 자식으로 텍스트 표시
            if (memos != null && memos.Length > 0)
            {
                SpawnMemosAsChildren(go, memos, unitScale);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Spawn 실패: {meshPath}\n{ex}");
            EditorUtility.DisplayDialog("로드 오류", $"파일 로드 중 오류가 발생했습니다:\n{ex.Message}", "OK");
        }
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
                // 씬에 인스턴스 생성 (프리팹이 아닐 수도 있으므로 GameObject.Instantiate 사용)
                GameObject instance = GameObject.Instantiate(prefab);
                instance.name = Path.GetFileNameWithoutExtension(filePath);
                
                // 임시 파일 삭제는 사용자가 수동으로 할 수 있도록 주석 처리
                // AssetDatabase.DeleteAsset(tempAssetPath);
                
                return instance;
            }
            else
            {
                Debug.LogWarning($"[ObjDropWatcher] Failed to load GLB/GLTF as GameObject: {tempAssetPath}");
                // 임시 파일 삭제
                AssetDatabase.DeleteAsset(tempAssetPath);
                return null;
            }
            #else
            Debug.LogError("[ObjDropWatcher] GLB/GLTF loading is only supported in Unity Editor");
            return null;
            #endif
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ObjDropWatcher] Failed to load GLB/GLTF file {filePath}: {ex.Message}");
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
                // 씬에 인스턴스 생성 (프리팹이 아닐 수도 있으므로 GameObject.Instantiate 사용)
                GameObject instance = GameObject.Instantiate(prefab);
                instance.name = Path.GetFileNameWithoutExtension(filePath);
                
                // 임시 파일 삭제는 사용자가 수동으로 할 수 있도록 주석 처리
                // AssetDatabase.DeleteAsset(tempAssetPath);
                
                return instance;
            }
            else
            {
                Debug.LogWarning($"[ObjDropWatcher] Failed to load FBX as GameObject: {tempAssetPath}");
                // 임시 파일 삭제
                AssetDatabase.DeleteAsset(tempAssetPath);
                return null;
            }
            #else
            Debug.LogError("[ObjDropWatcher] FBX loading is only supported in Unity Editor");
            return null;
            #endif
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ObjDropWatcher] Failed to load FBX file {filePath}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// memos를 메시 GameObject의 자식으로 생성합니다.
    /// 메모의 좌표는 메시의 로컬 좌표계를 사용합니다.
    /// </summary>
    void SpawnMemosAsChildren(GameObject parentObj, MemoData[] memos, float unitScale)
    {
        if (memos == null || memos.Length == 0 || parentObj == null)
            return;

        int memoCount = 0;
        foreach (var memo in memos)
        {
            if (memo == null)
                continue;
            
            // type이 "text"인 경우만 표시
            if (memo.type != "text")
                continue;
            
            // anchor 좌표 파싱 (OBJ 파일의 원본 좌표, 예: mm 단위)
            Vector3 anchorPosition = ParseAnchor(memo.anchor);
            
            // RuntimeObjLoader는 OBJ 파일의 버텍스를 로드할 때 Z축을 뒤집습니다 (오른손→왼손 좌표계 변환)
            // 메모의 anchor 좌표도 같은 변환을 적용해야 OBJ 메시와 일치합니다.
            // OBJ 파일: (x, y, z) → RuntimeObjLoader: (x, y, -z)
            anchorPosition.z = -anchorPosition.z;
            
            // 메모는 메시 GameObject의 자식이므로 로컬 좌표계를 사용합니다.
            // 메시 GameObject에 unitScale (예: 1000)이 적용되어 있으므로,
            // 메모의 로컬 좌표는 파일의 원본 좌표(변환 후)를 그대로 사용하면 됩니다.
            // 메시 GameObject의 스케일이 자동으로 적용되어 올바른 위치에 표시됩니다.
            // 
            // 예시 (OBJ 파일의 경우):
            // - OBJ 파일 원본 좌표: (0.80mm, 1.43mm, 0.13mm)
            // - Z축 변환 후: (0.80mm, 1.43mm, -0.13mm)  <- RuntimeObjLoader와 동일한 변환
            // - 메모 로컬 좌표: (0.80, 1.43, -0.13)
            // - 메시 GameObject 스케일: (1000, 1000, 1000)
            // - 결과: 메모가 메시와 같은 위치에 표시됨 (메시의 스케일이 자동 적용)
            // 
            // 주의: unitScale을 곱하지 않음 (메모가 메시의 자식이므로 메시의 스케일을 상속받음)
            
            // 메시 GameObject의 자식으로 3D 텍스트 생성 (로컬 좌표 사용)
            Create3DTextAsChild(parentObj, memo.content, anchorPosition);
            memoCount++;
        }
        
        if (memoCount > 0)
        {
            Debug.Log($"[ObjDropWatcher] Spawned {memoCount} text memo(s) as children of mesh object");
        }
    }

    /// <summary>
    /// 메시 GameObject의 자식으로 3D 텍스트를 생성합니다.
    /// 위치는 부모 객체의 로컬 좌표계를 사용합니다.
    /// </summary>
    void Create3DTextAsChild(GameObject parentObj, string text, Vector3 localPosition)
    {
        try
        {
            // TextMesh를 사용하여 3D 텍스트 생성
            GameObject textObject = new GameObject($"Memo_{text.Substring(0, Math.Min(text.Length, 10))}");
            Undo.RegisterCreatedObjectUndo(textObject, "Create 3D Text Memo");
            
            // 부모 객체의 자식으로 설정
            textObject.transform.SetParent(parentObj.transform, false);
            
            // 로컬 위치 설정 (부모 객체의 로컬 좌표계)
            textObject.transform.localPosition = localPosition;
            textObject.transform.localRotation = Quaternion.identity;
            textObject.transform.localScale = Vector3.one; // 부모의 스케일을 상속받음
            
            // TextMesh 컴포넌트 추가
            TextMesh textMesh = textObject.AddComponent<TextMesh>();
            textMesh.text = text;
            textMesh.fontSize = 20;
            textMesh.characterSize = 0.1f; // 텍스트 크기 (부모 스케일과 함께 적용됨)
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.color = Color.yellow; // 노란색으로 표시
            
            Debug.Log($"[ObjDropWatcher] Created 3D text memo as child at local position {localPosition}: {text}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ObjDropWatcher] Failed to create 3D text memo as child: {ex.Message}");
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

/// <summary>
/// HTTP 연결을 허용하기 위한 CertificateHandler
/// Unity 2021.2 이상에서 HTTP 연결을 허용하기 위해 사용
/// 보안 위험이 있으므로 개발 환경에서만 사용해야 함
/// </summary>
public class BypassCertificateHandler : CertificateHandler
{
    protected override bool ValidateCertificate(byte[] certificateData)
    {
        // 개발 환경에서 모든 인증서를 허용
        // 프로덕션 환경에서는 사용하지 않아야 함
        return true;
    }
}
