using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using ObjDropWatcher.ExportImport;

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
        public MemoUtils.MemoData[] memos;      // 메모 데이터
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

    // MemoData는 이제 MemoUtils.MemoData를 사용

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
        
        // DontSaveInEditor 플래그가 있는 config는 ObjectField에서 문제를 일으킬 수 있으므로
        // 안전하게 처리
        WatchConfig safeConfig = null;
        bool hasDontSaveFlag = false;
        
        if (config != null)
        {
            try
            {
                // Unity 객체가 파괴되었는지 확인
                if (!config.Equals(null))
                {
                    // DontSaveInEditor 플래그 확인
                    HideFlags flags = config.hideFlags;
                    hasDontSaveFlag = (flags & HideFlags.DontSaveInEditor) != 0;
                    
                    if (!hasDontSaveFlag)
                    {
                        // 플래그가 없으면 안전하게 사용 가능
                        safeConfig = config;
                    }
                }
            }
            catch (System.Exception)
            {
                // config 접근 실패 시 무시
                safeConfig = null;
            }
        }
        
        WatchConfig newConfig = null;
        bool configChanged = false;
        
        if (hasDontSaveFlag && config != null)
        {
            // DontSaveInEditor 플래그가 있으면 ObjectField 대신 텍스트로만 표시
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("WatchConfig", GUILayout.Width(EditorGUIUtility.labelWidth));
            EditorGUILayout.LabelField(config.name, EditorStyles.label);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox("현재 WatchConfig는 DontSaveInEditor 플래그가 설정되어 있어 ObjectField에서 표시할 수 없습니다.", MessageType.Info);
        }
        else
        {
            // 안전한 경우에만 ObjectField 사용
            try
            {
                EditorGUI.BeginChangeCheck();
                newConfig = (WatchConfig)EditorGUILayout.ObjectField("WatchConfig", safeConfig, typeof(WatchConfig), false);
                configChanged = EditorGUI.EndChangeCheck();
            }
            catch (System.Exception ex)
            {
                // ObjectField 사용 중 오류 발생 시 안전하게 처리
                EditorGUILayout.HelpBox($"WatchConfig 표시 중 오류가 발생했습니다: {ex.Message}", MessageType.Warning);
            }
        }
        
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
            catch (System.Exception)
            {
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
                // DontSaveInEditor 플래그가 있어도 값 읽기는 가능
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
                    catch (System.Exception)
                    {
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
                    catch (System.Exception)
                    {
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
                
                // Original과 Retouched 파일 정보 및 Import 버튼
                bool hasOriginal = !string.IsNullOrEmpty(it.originalPath) && File.Exists(it.originalPath);
                bool hasRetouched = !string.IsNullOrEmpty(it.retouchedPath) && File.Exists(it.retouchedPath);
                
                if (hasOriginal || hasRetouched)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    
                    // Original 파일 정보 표시
                    if (hasOriginal)
                    {
                        EditorGUILayout.LabelField("Original:", Path.GetFileName(it.originalPath), EditorStyles.miniLabel);
                    }
                    
                    // Retouched 파일 정보 표시
                    if (hasRetouched)
                    {
                        EditorGUILayout.LabelField("Retouched:", Path.GetFileName(it.retouchedPath), EditorStyles.miniLabel);
                    }
                    
                    // Import 버튼 (Original과 Retouched를 모두 로드)
                    if (GUILayout.Button("Import", GUILayout.Height(24)))
                    {
                        if (hasOriginal)
                        {
                            GameObject spawnedObj = SpawnWithBothVersions(it.originalPath, it.retouchedPath, it.memos);
                            // ObjectTransformManagerWindow에 경로 정보 전달
                            if (spawnedObj != null)
                            {
                                ObjectTransformManagerWindow.SetObjPaths(spawnedObj, it.originalPath, it.retouchedPath, it.originalPath);
                            }
                        }
                        else
                        {
                            EditorUtility.DisplayDialog("파일 없음", $"Original 파일을 찾을 수 없습니다:\n{it.originalPath}", "OK");
                        }
                    }
                    
                    EditorGUILayout.EndVertical();
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
            }
            else
            {
                _isLoadingGroups = false;
                _pendingRequest?.Dispose();
                _pendingRequest = null;
                
                EditorUtility.DisplayDialog("요청 오류", $"요청 전송 실패: {ex.Message}", "OK");
            }
            ScheduleRepaint();
        }
        catch (Exception ex)
        {
            _isLoadingGroups = false;
            _pendingRequest?.Dispose();
            _pendingRequest = null;
            
            EditorUtility.DisplayDialog("요청 오류", $"예기치 않은 오류가 발생했습니다: {ex.Message}", "OK");
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
            }
            else
            {
                _isLoadingScans = false;
                _pendingRequest?.Dispose();
                _pendingRequest = null;
                
                EditorUtility.DisplayDialog("요청 오류", $"요청 전송 실패: {ex.Message}", "OK");
            }
            ScheduleRepaint();
        }
        catch (Exception ex)
        {
            _isLoadingScans = false;
            _pendingRequest?.Dispose();
            _pendingRequest = null;
            
            EditorUtility.DisplayDialog("요청 오류", $"예기치 않은 오류가 발생했습니다: {ex.Message}", "OK");
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
                    
                    // Unity의 JsonUtility는 중첩된 구조를 파싱하기 어려울 수 있으므로
                    // 수동으로 파싱하거나 간단한 구조로 변환
                    GroupsResponse response = JsonUtility.FromJson<GroupsResponse>(jsonResponse);
                    
                    if (response != null && response.success && response.data != null && response.data.groups != null)
                    {
                        _availableGroups.Clear();
                        _availableGroups.AddRange(response.data.groups);
                    }
                    else
                    {
                        string errorMsg = response != null ? response.message : "Unknown error";
                        EditorUtility.DisplayDialog("API 오류", $"그룹 조회 실패: {errorMsg}", "OK");
                    }
                }
                catch (Exception ex)
                {
                    EditorUtility.DisplayDialog("파싱 오류", $"응답 파싱 실패: {ex.Message}\n\nUnity의 JsonUtility는 Dictionary 타입을 지원하지 않습니다.\nAPI 응답의 meta_data 필드가 문제일 수 있습니다.", "OK");
                }
            }
            else
            {
                
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
                                }
                                else if (Directory.Exists(convertedPath))
                                {
                                    // 폴더 경로인 경우 (objPatterns 설정에 따라 파일 찾기)
                                    string[] meshFiles = FindMeshFiles(convertedPath);
                                    if (meshFiles.Length > 0)
                                    {
                                        // 첫 번째 파일 사용
                                        originalPath = meshFiles[0];
                                    }
                                    else
                                    {
                                    }
                                }
                                else
                                {
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
                                }
                                else if (Directory.Exists(convertedPath))
                                {
                                    // 폴더 경로인 경우 (objPatterns 설정에 따라 파일 찾기)
                                    string[] meshFiles = FindMeshFiles(convertedPath);
                                    if (meshFiles.Length > 0)
                                    {
                                        // 첫 번째 파일 사용
                                        retouchedPath = meshFiles[0];
                                    }
                                    else
                                    {
                                    }
                                }
                                else
                                {
                                }
                            }
                            
                            // memos는 항상 original path의 파일에서 직접 읽어옴
                            MemoUtils.MemoData[] memos = new MemoUtils.MemoData[0];
                            
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
                                        memos = MemoUtils.ParseMemoFile(memoFilePath);
                                        if (memos != null && memos.Length > 0)
                                        {
                                        }
                                    }
                                    catch (Exception)
                                    {
                                    }
                                }
                                else
                                {
                                }
                            }
                            else if (string.IsNullOrEmpty(scan.original_file_path))
                            {
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
                                }
                            }
                        }
                        
                        int originalCount = _items.Count(it => !string.IsNullOrEmpty(it.originalPath));
                        int retouchedCount = _items.Count(it => !string.IsNullOrEmpty(it.retouchedPath));
                        int memosCount = _items.Sum(it => it.memos != null ? it.memos.Length : 0);
                        
                        
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
                        EditorUtility.DisplayDialog("API 오류", $"스캔 조회 실패: {errorMsg}", "OK");
                    }
                }
                catch (Exception ex)
                {
                    EditorUtility.DisplayDialog("파싱 오류", $"응답 파싱 실패: {ex.Message}", "OK");
                }
            }
            else
            {
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

    // ParseMemoFile과 ParseAnchor는 이제 MemoUtils를 사용

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
            catch (System.Exception)
            {
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
            catch (Exception)
            {
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
            catch (System.Exception)
            {
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

    /// <summary>
    /// 메시 파일을 로드합니다. (단일 파일 버전 - 호환성 유지)
    /// </summary>
    GameObject Spawn(string meshPath, MemoUtils.MemoData[] memos = null)
    {
        return SpawnWithBothVersions(meshPath, null, memos);
    }
    
    /// <summary>
    /// Original과 Retouched 버전을 모두 로드하여 하나의 root GameObject에 children으로 추가합니다.
    /// Original은 보이는 상태, Retouched는 안보이는 상태로 설정됩니다.
    /// </summary>
    GameObject SpawnWithBothVersions(string originalPath, string retouchedPath = null, MemoUtils.MemoData[] memos = null)
    {
        try
        {
            // Original 파일이 없으면 오류
            if (string.IsNullOrEmpty(originalPath) || !File.Exists(originalPath))
            {
                EditorUtility.DisplayDialog("파일 없음", $"Original 파일을 찾을 수 없습니다:\n{originalPath}", "OK");
                return null;
            }
            
            // Root GameObject 생성
            string rootName = Path.GetFileNameWithoutExtension(originalPath);
            GameObject rootGo = new GameObject($"{rootName}_Root");
            Undo.RegisterCreatedObjectUndo(rootGo, "Spawn OBJ Root");
            
            // Root를 Unity 원점에 배치
            rootGo.transform.position = Vector3.zero;
            rootGo.transform.rotation = Quaternion.identity;
            rootGo.transform.localScale = Vector3.one;
            
            float unitScale = config != null ? config.unitScale : 1000f;
            
            // Original OBJ 로드 및 설정
            GameObject originalGo = LoadMeshFile(originalPath);
            if (originalGo == null)
            {
                GameObject.DestroyImmediate(rootGo);
                return null;
            }
            
            originalGo.name = "Original";
            originalGo.transform.SetParent(rootGo.transform, false);
            originalGo.transform.localPosition = Vector3.zero;
            originalGo.transform.localRotation = Quaternion.identity;
            
            // Original OBJ에 unitScale 적용 및 Y 오프셋 처리
            float minY = FindMinimumY(originalGo, unitScale);
            originalGo.transform.localScale = Vector3.one * unitScale;
            
            if (minY < 0f)
            {
                float offsetY = -minY;
                rootGo.transform.position = new Vector3(0f, offsetY, 0f);
            }
            
            // Original OBJ를 보이는 상태로 설정
            originalGo.SetActive(true);
            
            // Retouched OBJ 로드 및 설정 (있는 경우)
            GameObject retouchedGo = null;
            if (!string.IsNullOrEmpty(retouchedPath) && File.Exists(retouchedPath))
            {
                retouchedGo = LoadMeshFile(retouchedPath);
                if (retouchedGo != null)
                {
                    retouchedGo.name = "Retouched";
                    retouchedGo.transform.SetParent(rootGo.transform, false);
                    retouchedGo.transform.localPosition = Vector3.zero;
                    retouchedGo.transform.localRotation = Quaternion.identity;
                    retouchedGo.transform.localScale = Vector3.one * unitScale;
                    
                    // Retouched OBJ를 안보이는 상태로 설정
                    retouchedGo.SetActive(false);
                }
            }
            
            // Memos를 root GameObject의 자식으로 추가
            // 메모의 anchor 좌표는 "스케일이 1일 때의 좌표" (unitScale = 1일 때의 Unity 월드 좌표계 기준)를 의미합니다.
            // unitScale이 변경되면 OBJ 메시의 스케일이 변경되므로, 메모도 그에 맞춰 조정되어야 합니다.
            // root의 localScale = 1이므로, 메모의 로컬 좌표는 anchor 좌표를 unitScale로 나눈 값입니다.
            if (memos != null && memos.Length > 0)
            {
                MemoUtils.SpawnMemosAsChildren(rootGo, memos, unitScale); // unitScale을 전달하여 스케일 보정
            }
            
            // 경로 정보 저장
            try
            {
                ObjPathInfo.SetPath(rootGo, originalPath);
                
                // 모든 children에도 경로 저장
                Transform[] allChildren = rootGo.GetComponentsInChildren<Transform>(true);
                foreach (Transform child in allChildren)
                {
                    if (child != null && child != rootGo.transform && child.gameObject != null)
                    {
                        ObjPathInfo.SetPath(child.gameObject, originalPath);
                    }
                }
                
                #if UNITY_EDITOR
                if ((rootGo.hideFlags & HideFlags.DontSaveInEditor) == 0)
                {
                    EditorUtility.SetDirty(rootGo);
                }
                #endif
            }
            catch (System.Exception)
            {
                // 경로 저장 실패 시 무시
            }
            
            Selection.activeObject = rootGo;
            return rootGo;
        }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog("로드 오류", $"파일 로드 중 오류가 발생했습니다:\n{ex.Message}", "OK");
            return null;
        }
    }
    
    /// <summary>
    /// 단일 메시 파일을 로드합니다. (내부 헬퍼 메서드)
    /// </summary>
    GameObject LoadMeshFile(string meshPath)
    {
        if (string.IsNullOrEmpty(meshPath) || !File.Exists(meshPath))
        {
            return null;
        }
        
        GameObject go = null;
        string extension = Path.GetExtension(meshPath).ToLowerInvariant();
        
        // 파일 확장자에 따라 적절한 로더 선택
        switch (extension)
        {
            case ".obj":
                // OBJ 파일의 원본 좌표 시스템을 유지하기 위해 preserveOriginalCoordinates=true 사용
                go = RuntimeObjLoader.LoadObj(meshPath, preserveOriginalCoordinates: true);
                if (go != null)
                {
                    Undo.RegisterCreatedObjectUndo(go, "Load OBJ");
                }
                break;
                
            case ".glb":
            case ".gltf":
                // GLB/GLTF 파일은 Unity의 기본 임포트 기능 사용
                go = LoadGlbOrGltf(meshPath);
                if (go != null)
                {
                    Undo.RegisterCreatedObjectUndo(go, "Load GLB/GLTF");
                }
                break;
                
            case ".fbx":
                // FBX 파일은 Unity의 기본 임포트 기능 사용
                go = LoadFbx(meshPath);
                if (go != null)
                {
                    Undo.RegisterCreatedObjectUndo(go, "Load FBX");
                }
                break;
                
            default:
                EditorUtility.DisplayDialog("지원하지 않는 형식", 
                    $"지원하지 않는 파일 형식입니다: {extension}\n\n지원 형식: .obj, .glb, .gltf, .fbx", "OK");
                return null;
        }
        
        if (go == null)
        {
            EditorUtility.DisplayDialog("로드 실패", $"파일을 로드할 수 없습니다:\n{meshPath}", "OK");
            return null;
        }
        
        return go;
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
                // 임시 파일 삭제
                AssetDatabase.DeleteAsset(tempAssetPath);
                return null;
            }
            #else
            return null;
            #endif
        }
        catch (Exception)
        {
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
                // 임시 파일 삭제
                AssetDatabase.DeleteAsset(tempAssetPath);
                return null;
            }
            #else
            return null;
            #endif
        }
        catch (Exception)
        {
            return null;
        }
    }

    // SpawnMemosAsChildren은 이제 MemoUtils.SpawnMemosAsChildren을 사용

    /// <summary>
    /// GameObject와 그 모든 자식에서 메시 버텍스의 Y 좌표 최솟값을 찾습니다.
    /// 루트의 로컬 좌표계 기준으로 계산하며, 스케일이 적용된 후의 값을 반환합니다.
    /// </summary>
    float FindMinimumY(GameObject root, float scale)
    {
        if (root == null)
            return 0f;

        float minY = float.PositiveInfinity;
        
        // 모든 MeshFilter 컴포넌트 찾기 (자식 포함)
        MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
        
        Transform rootTransform = root.transform;
        
        foreach (MeshFilter mf in meshFilters)
        {
            if (mf == null || mf.sharedMesh == null)
                continue;
            
            Mesh mesh = mf.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            
            if (vertices == null || vertices.Length == 0)
                continue;
            
            Transform meshTransform = mf.transform;
            
            // 메시가 속한 오브젝트가 루트인지 자식인지 확인
            bool isRootMesh = (meshTransform == rootTransform);
            
            foreach (Vector3 vertex in vertices)
            {
                float vertexY;
                
                if (isRootMesh)
                {
                    // 루트의 메시인 경우: 버텍스의 로컬 Y 좌표를 직접 사용
                    vertexY = vertex.y;
                }
                else
                {
                    // 자식 오브젝트의 메시인 경우: 자식의 로컬 좌표를 루트의 로컬 좌표계로 변환
                    // 자식의 월드 좌표로 변환 (스케일은 아직 적용 안 됨, 기본 1,1,1)
                    Vector3 vertexWorldPos = meshTransform.TransformPoint(vertex);
                    // 루트의 로컬 좌표계로 변환
                    Vector3 vertexRootLocalPos = rootTransform.InverseTransformPoint(vertexWorldPos);
                    vertexY = vertexRootLocalPos.y;
                }
                
                // 스케일 적용 후 Y 좌표 계산
                float scaledY = vertexY * scale;
                
                // Y 좌표 최솟값 업데이트
                if (scaledY < minY)
                    minY = scaledY;
            }
        }
        
        // 메시를 찾지 못한 경우 0 반환
        if (float.IsPositiveInfinity(minY))
            return 0f;
        
        return minY;
    }

    // Create3DTextAsChild는 이제 MemoUtils.Create3DTextAsChild를 사용


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
        catch (System.Exception)
        {
            // SetDirty 실패 시 무시 (메모리 오브젝트이거나 이미 삭제된 경우)
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
