using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using ObjDropWatcher.ExportImport;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class ObjDropWatcherWindow : EditorWindow, ISerializationCallbackReceiver
{
    [SerializeField] private WatchConfig config;
    [SerializeField] private string _configAssetPath; // DontSaveInEditor 플래그가 있을 때 사용
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
    class ApiMemoData
    {
        public string type;
        public string content;
        public string anchor;  // API 응답에서 anchor 문자열로 직접 제공됨 (예: "x:0.80,y:1.43,z:0.13")
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
        // memos는 API 응답에서 가져옴 (JSON 배열)
        // Unity JsonUtility는 List를 직접 지원하지 않으므로 별도로 파싱 필요
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

    private void OnEnable()
    {
        // 역직렬화 후 config 복원 (DontSaveInEditor 플래그가 있는 경우)
        // config가 null이고 asset path가 있으면 복원 시도
        if (config == null && !string.IsNullOrEmpty(_configAssetPath))
        {
            try
            {
                // 먼저 경로로 직접 로드 시도
                config = AssetDatabase.LoadAssetAtPath<WatchConfig>(_configAssetPath);
                
                // 경로로 찾을 수 없으면 GUID로 시도
                if (config == null)
                {
                    string guid = AssetDatabase.AssetPathToGUID(_configAssetPath);
                    if (!string.IsNullOrEmpty(guid))
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guid);
                        if (!string.IsNullOrEmpty(path))
                        {
                            config = AssetDatabase.LoadAssetAtPath<WatchConfig>(path);
                        }
                    }
                }
                
                // 복원 성공 시 asset path 업데이트 (경로가 변경되었을 수 있음)
                if (config != null)
                {
                    string actualPath = AssetDatabase.GetAssetPath(config);
                    if (!string.IsNullOrEmpty(actualPath))
                    {
                        _configAssetPath = actualPath;
                    }
                }
            }
            catch (System.Exception)
            {
                // 복원 실패 시 무시
            }
        }
        // config가 이미 있지만 asset path가 없으면 path 업데이트
        else if (config != null && string.IsNullOrEmpty(_configAssetPath))
        {
            try
            {
                if (AssetDatabase.Contains(config))
                {
                    string assetPath = AssetDatabase.GetAssetPath(config);
                    if (!string.IsNullOrEmpty(assetPath))
                    {
                        _configAssetPath = assetPath;
                    }
                }
            }
            catch (System.Exception)
            {
                // 경로 업데이트 실패 시 무시
            }
        }
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
        
        // config에 직접 접근하지 않고, 경로를 통해 안전하게 로드
        // 매 프레임 config에 접근하면 Unity가 직렬화를 시도하여 assertion 오류 발생
        WatchConfig safeConfig = null;
        bool isConfigSerializable = false;
        string configName = null;
        
        // config가 null이 아니면 직렬화 가능한지 확인 (하지만 config에 직접 접근하지 않음)
        if (!string.IsNullOrEmpty(_configAssetPath))
        {
            try
            {
                // 경로를 통해 안전하게 로드하여 체크
                WatchConfig tempConfig = AssetDatabase.LoadAssetAtPath<WatchConfig>(_configAssetPath);
                if (tempConfig != null)
                {
                    // 직렬화 가능한지 확인
                    isConfigSerializable = IsConfigSerializable(tempConfig);
                    
                    if (isConfigSerializable)
                    {
                        // 직렬화 가능하면 안전하게 사용 가능
                        safeConfig = tempConfig;
                        configName = tempConfig.name;
                    }
                    else
                    {
                        // 직렬화 불가능하면 이름만 가져오기
                        try
                        {
                            configName = tempConfig.name;
                        }
                        catch (System.Exception)
                        {
                            configName = System.IO.Path.GetFileNameWithoutExtension(_configAssetPath);
                        }
                    }
                }
            }
            catch (System.Exception)
            {
                // 로드 실패 시 경로에서 이름 추출
                configName = System.IO.Path.GetFileNameWithoutExtension(_configAssetPath);
            }
        }
        
        WatchConfig newConfig = null;
        bool configChanged = false;
        
        if (!isConfigSerializable && !string.IsNullOrEmpty(_configAssetPath))
        {
            // 직렬화 불가능한 플래그가 있으면 ObjectField 대신 텍스트로만 표시
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("WatchConfig", GUILayout.Width(EditorGUIUtility.labelWidth));
            EditorGUILayout.LabelField(configName ?? "(직렬화 불가능)", EditorStyles.label);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox("현재 WatchConfig는 직렬화 불가능한 HideFlags가 설정되어 있어 ObjectField에서 표시할 수 없습니다.", MessageType.Info);
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
                if (newConfig != null)
                {
                    // 새로운 config가 직렬화 가능한지 확인
                    // ObjectField에서 반환된 config도 HideFlags가 변경되었을 수 있음
                    if (IsConfigSerializable(newConfig) && AssetDatabase.Contains(newConfig))
                    {
                        config = newConfig;
                        // asset path도 업데이트
                        string assetPath = AssetDatabase.GetAssetPath(newConfig);
                        if (!string.IsNullOrEmpty(assetPath))
                        {
                            _configAssetPath = assetPath;
                        }
                    }
                    else
                    {
                        // 직렬화 불가능한 config는 경로만 저장
                        if (AssetDatabase.Contains(newConfig))
                        {
                            string assetPath = AssetDatabase.GetAssetPath(newConfig);
                            if (!string.IsNullOrEmpty(assetPath))
                            {
                                _configAssetPath = assetPath;
                            }
                        }
                        config = null;
                    }
                }
                else if (newConfig == null)
                {
                    config = null;
                    _configAssetPath = null;
                }
                // newConfig가 null이 아니지만 AssetDatabase에 없는 경우는 무시
            }
            catch (System.Exception)
            {
            }
        }

        // config에 직접 접근하지 않고, 경로를 통해 안전하게 로드
        // 매 프레임 config에 접근하면 Unity가 직렬화를 시도하여 assertion 오류 발생
        if (!string.IsNullOrEmpty(_configAssetPath))
        {
            // config 값들을 안전하게 캐시하여 반복 접근 방지
            string cachedApiUrl = null;
            int cachedScanDebounce = 0;
            string cachedObjPatterns = null;
            float cachedUnitScale = 0f;
            
            try
            {
                // 경로를 통해 안전하게 로드 (직접 참조하지 않음)
                WatchConfig tempConfig = AssetDatabase.LoadAssetAtPath<WatchConfig>(_configAssetPath);
                if (tempConfig != null)
                {
                    // 값 읽기 (읽기만 하면 일반적으로 문제 없음)
                    cachedApiUrl = tempConfig.apiServerUrl;
                    cachedScanDebounce = tempConfig.scanDebounceMs;
                    cachedObjPatterns = tempConfig.objPatterns;
                    cachedUnitScale = tempConfig.unitScale;
                }
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
                string configPath = _configAssetPath; // 클로저를 위해 복사
                EditorApplication.delayCall += () =>
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(configPath))
                        {
                            WatchConfig tempConfig = AssetDatabase.LoadAssetAtPath<WatchConfig>(configPath);
                            if (tempConfig != null)
                            {
                                tempConfig.apiServerUrl = apiUrlToSet;
                                MarkConfigDirty(tempConfig);
                                Repaint();
                            }
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
                string configPath = _configAssetPath; // 클로저를 위해 복사
                
                EditorApplication.delayCall += () =>
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(configPath))
                        {
                            WatchConfig tempConfig = AssetDatabase.LoadAssetAtPath<WatchConfig>(configPath);
                            if (tempConfig != null)
                            {
                                tempConfig.scanDebounceMs = scanDebounceToSet;
                                tempConfig.objPatterns = objPatternsToSet;
                                tempConfig.unitScale = unitScaleToSet;
                                MarkConfigDirty(tempConfig);
                                Repaint();
                            }
                        }
                    }
                    catch (System.Exception)
                    {
                    }
                };
            }
            
            EditorGUILayout.Space();
            DrawMemoPanelSettingsSection();
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
                            // Import 시에는 로컬 파일 시스템에서 memos.json을 직접 읽음
                            // API 응답의 memos는 목록 표시용일 뿐, 실제 import 시에는 파일에서 읽어야 함
                            MemoUtils.MemoData[] memosFromFile = MemoUtils.FindAndParseMemoFile(it.originalPath);
                            
                            // 그룹 이름과 스캔 ID를 전달하여 프리팹 저장
                            string groupName = GetSelectedGroupName();
                            GameObject spawnedObj = SpawnWithBothVersions(it.originalPath, it.retouchedPath, memosFromFile, groupName, it.scanId);
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
                
                // Memos 정보 표시 (API 응답에서 가져온 정보, 목록 표시용)
                if (it.memos != null && it.memos.Length > 0)
                {
                    int textMemosCount = it.memos.Count(m => m != null && m.type == "text");
                    if (textMemosCount > 0)
                    {
                        EditorGUILayout.HelpBox($"메모: {textMemosCount}개의 텍스트 메모가 있습니다. (API 응답)\nImport 시에는 로컬 파일 시스템의 memos.json을 읽어서 사용합니다.", MessageType.Info);
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
    
    void DrawMemoPanelSettingsSection()
    {
        MemoDesignConfig currentDesignConfig = MemoUtils.GetDesignConfig();
        if (currentDesignConfig == null)
        {
            EditorGUILayout.HelpBox("메모 디자인 설정을 불러올 수 없습니다.", MessageType.Warning);
            return;
        }
        
        EditorGUILayout.LabelField("Memo Panel", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        bool lockPanelWorldY = EditorGUILayout.Toggle("Lock Panel World Y", currentDesignConfig.lockPanelWorldY);
        EditorGUI.BeginDisabledGroup(!lockPanelWorldY);
        float fixedWorldY = EditorGUILayout.FloatField("Fixed Panel World Y", currentDesignConfig.fixedPanelWorldY);
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.HelpBox("Lock을 활성화하면 모든 메모 패널이 지정한 월드 Y 높이에 고정됩니다.", MessageType.Info);
        bool designChanged = EditorGUI.EndChangeCheck();
        
        if (designChanged)
        {
            MemoDesignConfig updatedConfig = CloneMemoDesignConfig(currentDesignConfig);
            if (updatedConfig != null)
            {
                updatedConfig.lockPanelWorldY = lockPanelWorldY;
                updatedConfig.fixedPanelWorldY = fixedWorldY;
                MemoUtils.SetDesignConfig(updatedConfig);
            }
        }
    }

    MemoDesignConfig CloneMemoDesignConfig(MemoDesignConfig source)
    {
        if (source == null)
            return null;
        
        return new MemoDesignConfig
        {
            markerRadius = source.markerRadius,
            markerColor = source.markerColor,
            lineHeight = source.lineHeight,
            lineWidth = source.lineWidth,
            lineColor = source.lineColor,
            panelWidth = source.panelWidth,
            panelHeight = source.panelHeight,
            panelBackgroundColor = source.panelBackgroundColor,
            panelBorderColor = source.panelBorderColor,
            panelBorderWidth = source.panelBorderWidth,
            panelPadding = source.panelPadding,
            fontSize = source.fontSize,
            characterSize = source.characterSize,
            textColor = source.textColor,
            anchor = source.anchor,
            alignment = source.alignment,
            maxNameLength = source.maxNameLength,
            lockPanelWorldY = source.lockPanelWorldY,
            fixedPanelWorldY = source.fixedPanelWorldY
        };
    }
    
    void RefreshGroupList()
    {
        // config에 직접 접근하지 않고, 경로를 통해 안전하게 로드
        if (string.IsNullOrEmpty(_configAssetPath))
        {
            ScheduleRepaint();
            return;
        }
        
        // config 접근을 안전하게 처리
        string apiUrl = null;
        string groupsEndpointPath = "/api/v1/groups/";
        try
        {
            WatchConfig tempConfig = AssetDatabase.LoadAssetAtPath<WatchConfig>(_configAssetPath);
            if (tempConfig != null)
            {
                apiUrl = tempConfig.apiServerUrl;
                groupsEndpointPath = tempConfig.groupsEndpoint ?? "/api/v1/groups/";
            }
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
        
        // WatchConfig에서 엔드포인트 경로 가져오기 (이미 위에서 로드됨)
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
        // config에 직접 접근하지 않고, 경로를 통해 안전하게 로드
        if (string.IsNullOrEmpty(_configAssetPath))
        {
            ScheduleRepaint();
            return;
        }
        
        // config 접근을 안전하게 처리
        string apiUrl = null;
        string groupScansEndpointPath = "/api/v1/groups/{group_id}/scans";
        try
        {
            WatchConfig tempConfig = AssetDatabase.LoadAssetAtPath<WatchConfig>(_configAssetPath);
            if (tempConfig != null)
            {
                apiUrl = tempConfig.apiServerUrl;
                groupScansEndpointPath = tempConfig.groupScansEndpoint ?? "/api/v1/groups/{group_id}/scans";
            }
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
        
        // WatchConfig에서 엔드포인트 경로 가져오기 (이미 위에서 로드됨)
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
                        if (!string.IsNullOrEmpty(_configAssetPath))
                        {
                            WatchConfig tempConfig = AssetDatabase.LoadAssetAtPath<WatchConfig>(_configAssetPath);
                            if (tempConfig != null)
                            {
                                apiUrl = tempConfig.apiServerUrl ?? "";
                            }
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
                    
                    // 기본 응답 구조 파싱 (memos는 API 응답에서 가져옴)
                    GroupScansResponse response = JsonUtility.FromJson<GroupScansResponse>(jsonResponse);
                    
                    // 전체 JSON을 Newtonsoft.Json으로 파싱하여 memos 추출
                    JObject jsonObj = null;
                    try
                    {
                        jsonObj = JObject.Parse(jsonResponse);
                    }
                    catch (Exception)
                    {
                        // JSON 파싱 실패 시 무시
                    }
                    
                    if (response != null && response.success && response.data != null)
                    {
                        _items.Clear();
                        
                        for (int i = 0; i < response.data.Length; i++)
                        {
                            var scan = response.data[i];
                            
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
                                }
                            }
                            
                            // memos는 API 응답에서 가져옴
                            MemoUtils.MemoData[] memos = new MemoUtils.MemoData[0];
                            
                            if (jsonObj != null)
                            {
                                try
                                {
                                    // data 배열에서 해당 scan의 memos 추출
                                    JArray dataArray = jsonObj["data"] as JArray;
                                    if (dataArray != null && i < dataArray.Count)
                                    {
                                        JToken scanToken = dataArray[i];
                                        JArray memosArray = scanToken["memos"] as JArray;
                                        
                                        if (memosArray != null && memosArray.Count > 0)
                                        {
                                            List<MemoUtils.MemoData> memoList = new List<MemoUtils.MemoData>();
                                            
                                            foreach (JToken memoToken in memosArray)
                                            {
                                                try
                                                {
                                                    ApiMemoData apiMemo = memoToken.ToObject<ApiMemoData>();
                                                    if (apiMemo != null)
                                                    {
                                                        // API 응답에서 anchor 문자열이 직접 제공됨
                                                        if (string.IsNullOrWhiteSpace(apiMemo.anchor))
                                                        {
                                                            continue;
                                                        }
                                                        
                                                        string anchor = apiMemo.anchor.Trim();
                                                        
                                                        // anchor 문자열 정규화 (소수점 정밀도 통일)
                                                        string normalizedAnchor = MemoUtils.NormalizeAnchor(anchor);
                                                        
                                                        string content = apiMemo.content ?? "";
                                                        
                                                        // source, file_path, file_size 정보 사용 (API 응답에서 제공되는 경우)
                                                        string source = !string.IsNullOrWhiteSpace(apiMemo.source) ? apiMemo.source : "API";
                                                        string file_path = apiMemo.file_path ?? "";
                                                        int file_size = apiMemo.file_size;
                                                        
                                                        memoList.Add(new MemoUtils.MemoData
                                                        {
                                                            type = apiMemo.type ?? "text",
                                                            anchor = normalizedAnchor,
                                                            content = content,
                                                            source = source,
                                                            file_path = file_path,
                                                            file_size = file_size
                                                        });
                                                    }
                                                }
                                                catch (Exception)
                                                {
                                                    // Memo 파싱 실패 시 무시
                                                }
                                            }
                                            
                                            memos = memoList.ToArray();
                                        }
                                    }
                                }
                                catch (Exception)
                                {
                                    // Memos 추출 실패 시 무시
                                }
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
        if (!string.IsNullOrEmpty(_configAssetPath))
        {
            try
            {
                WatchConfig tempConfig = AssetDatabase.LoadAssetAtPath<WatchConfig>(_configAssetPath);
                if (tempConfig != null)
                {
                    patternsStr = tempConfig.objPatterns ?? "*.obj";
                }
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
        if (!string.IsNullOrEmpty(_configAssetPath))
        {
            try
            {
                WatchConfig tempConfig = AssetDatabase.LoadAssetAtPath<WatchConfig>(_configAssetPath);
                if (tempConfig != null)
                {
                    string projectRoot = tempConfig.projectRoot;
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
    /// 폴더 이름에서 Unity 경로에 부적합한 문자를 제거합니다.
    /// </summary>
    string SanitizePathName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "Default";
        
        // Unity 경로에 부적합한 문자 제거
        char[] invalidChars = Path.GetInvalidFileNameChars();
        string sanitized = name;
        
        foreach (char c in invalidChars)
        {
            sanitized = sanitized.Replace(c, '_');
        }
        
        // 추가로 제거할 문자들 (Unity 특수 문자)
        sanitized = sanitized.Replace('/', '_').Replace('\\', '_').Replace(':', '_');
        sanitized = sanitized.Replace('*', '_').Replace('?', '_').Replace('"', '_');
        sanitized = sanitized.Replace('<', '_').Replace('>', '_').Replace('|', '_');
        
        // 연속된 언더스코어 제거
        while (sanitized.Contains("__"))
        {
            sanitized = sanitized.Replace("__", "_");
        }
        
        // 앞뒤 공백 및 언더스코어 제거
        sanitized = sanitized.Trim(' ', '_');
        
        // 빈 문자열이면 기본값 사용
        if (string.IsNullOrEmpty(sanitized))
            sanitized = "Default";
        
        return sanitized;
    }

    /// <summary>
    /// SnapSpace 폴더 안에 그룹 이름 폴더를 생성하거나 반환합니다.
    /// </summary>
    string GetOrCreateSnapSpaceFolder(string groupName)
    {
        if (string.IsNullOrEmpty(groupName))
            groupName = "Default";
        
        // 그룹 이름 정리
        string sanitizedGroupName = SanitizePathName(groupName);
        
        // SnapSpace 폴더 경로
        string snapSpacePath = "Assets/SnapSpace";
        string groupFolderPath = $"{snapSpacePath}/{sanitizedGroupName}";
        
        // SnapSpace 폴더가 없으면 생성
        if (!AssetDatabase.IsValidFolder(snapSpacePath))
        {
            string guid = AssetDatabase.CreateFolder("Assets", "SnapSpace");
            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogError($"SnapSpace 폴더 생성 실패");
                return null;
            }
            AssetDatabase.Refresh();
        }
        
        // 그룹 폴더가 없으면 생성
        if (!AssetDatabase.IsValidFolder(groupFolderPath))
        {
            string guid = AssetDatabase.CreateFolder(snapSpacePath, sanitizedGroupName);
            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogError($"그룹 폴더 생성 실패: {groupFolderPath}");
                return null;
            }
            AssetDatabase.Refresh();
        }
        
        return groupFolderPath;
    }

    /// <summary>
    /// 현재 선택된 그룹 이름을 가져옵니다.
    /// </summary>
    string GetSelectedGroupName()
    {
        if (!_selectedGroupId.HasValue || _availableGroups == null || _availableGroups.Count == 0)
            return null;
        
        var selectedGroup = _availableGroups.FirstOrDefault(g => g.group_id == _selectedGroupId.Value);
        return selectedGroup?.name;
    }

    /// <summary>
    /// OBJ 파일을 Assets/SnapSpace/그룹이름/model/으로 복사합니다.
    /// </summary>
    string CopyObjToAssets(string objPath, string groupName)
    {
        if (string.IsNullOrEmpty(objPath) || !File.Exists(objPath))
            return null;
        
        string groupFolderPath = GetOrCreateSnapSpaceFolder(groupName);
        if (string.IsNullOrEmpty(groupFolderPath))
            return null;
        
        // model 폴더 생성
        string modelFolderPath = $"{groupFolderPath}/model";
        if (!AssetDatabase.IsValidFolder(modelFolderPath))
        {
            string guid = AssetDatabase.CreateFolder(groupFolderPath, "model");
            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogError($"model 폴더 생성 실패: {modelFolderPath}");
                return null;
            }
            AssetDatabase.Refresh();
        }
        
        string fileName = Path.GetFileName(objPath);
        string targetAssetPath = $"{modelFolderPath}/{fileName}";
        
        // 이미 같은 파일이 있으면 재사용 (파일 내용 비교는 생략 - 단순히 경로만 확인)
        if (File.Exists(targetAssetPath))
        {
            // 기존 파일이 있으면 그대로 사용
            return targetAssetPath;
        }
        
        try
        {
            // 파일 복사
            File.Copy(objPath, targetAssetPath, true);
            
            // AssetDatabase 새로고침
            AssetDatabase.Refresh();
            
            // OBJ 파일 임포트 (Unity의 기본 임포트 설정 사용)
            AssetDatabase.ImportAsset(targetAssetPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();
            
            return targetAssetPath;
        }
        catch (Exception ex)
        {
            Debug.LogError($"OBJ 파일 복사 실패: {ex.Message}\n원본: {objPath}\n대상: {targetAssetPath}");
            return null;
        }
    }

    /// <summary>
    /// GameObject의 모든 Mesh를 Assets/SnapSpace/그룹이름/materials/에 저장합니다.
    /// 각 오브젝트가 고유한 Mesh를 사용하도록 OBJ 파일명을 파일명에 포함합니다.
    /// </summary>
    void SaveMeshesToAssets(GameObject go, string groupName, string objFileName)
    {
        if (go == null || string.IsNullOrEmpty(groupName) || string.IsNullOrEmpty(objFileName))
            return;
        
        #if UNITY_EDITOR
        try
        {
            // materials 폴더 경로 생성
            string groupFolderPath = GetOrCreateSnapSpaceFolder(groupName);
            if (string.IsNullOrEmpty(groupFolderPath))
                return;
            
            string materialsFolderPath = $"{groupFolderPath}/materials";
            
            // materials 폴더가 없으면 생성
            if (!AssetDatabase.IsValidFolder(materialsFolderPath))
            {
                string guid = AssetDatabase.CreateFolder(groupFolderPath, "materials");
                if (string.IsNullOrEmpty(guid))
                {
                    Debug.LogError($"materials 폴더 생성 실패: {materialsFolderPath}");
                    return;
                }
                AssetDatabase.Refresh();
            }
            
            // OBJ 파일명에서 확장자 제거하여 prefix로 사용
            string objPrefix = Path.GetFileNameWithoutExtension(objFileName);
            objPrefix = SanitizePathName(objPrefix);
            
            // 모든 MeshFilter를 찾아서 Mesh 저장
            MeshFilter[] filters = go.GetComponentsInChildren<MeshFilter>(true);
            
            foreach (MeshFilter filter in filters)
            {
                if (filter == null || filter.sharedMesh == null)
                    continue;
                
                Mesh mesh = filter.sharedMesh;
                
                // OBJ 파일명과 Mesh 이름을 조합하여 고유한 파일명 생성
                string meshFileName = $"{objPrefix}_{SanitizePathName(mesh.name)}.asset";
                string meshAssetPath = $"{materialsFolderPath}/{meshFileName}";
                
                // Mesh를 복사하여 Assets에 저장 (재사용하지 않고 항상 새로 생성)
                Mesh savedMesh = UnityEngine.Object.Instantiate(mesh);
                savedMesh.name = mesh.name; // 원본 이름 유지
                
                // Mesh를 Assets에 저장
                AssetDatabase.CreateAsset(savedMesh, meshAssetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                
                filter.sharedMesh = savedMesh;
                EditorUtility.SetDirty(filter);
            }
            
            AssetDatabase.Refresh();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Mesh 저장 실패: {ex.Message}\n{ex.StackTrace}");
        }
        #endif
    }

    /// <summary>
    /// GameObject의 모든 Material을 Assets/SnapSpace/그룹이름/materials/에 저장합니다.
    /// 각 오브젝트가 고유한 Material을 사용하도록 OBJ 파일명을 파일명에 포함합니다.
    /// 텍스처도 함께 Asset으로 저장합니다.
    /// </summary>
    void SaveMaterialsToAssets(GameObject go, string groupName, string objFileName)
    {
        if (go == null || string.IsNullOrEmpty(groupName) || string.IsNullOrEmpty(objFileName))
            return;
        
        #if UNITY_EDITOR
        try
        {
            // materials 폴더 경로 생성
            string groupFolderPath = GetOrCreateSnapSpaceFolder(groupName);
            if (string.IsNullOrEmpty(groupFolderPath))
                return;
            
            string materialsFolderPath = $"{groupFolderPath}/materials";
            
            // materials 폴더가 없으면 생성
            if (!AssetDatabase.IsValidFolder(materialsFolderPath))
            {
                string guid = AssetDatabase.CreateFolder(groupFolderPath, "materials");
                if (string.IsNullOrEmpty(guid))
                {
                    Debug.LogError($"materials 폴더 생성 실패: {materialsFolderPath}");
                    return;
                }
                AssetDatabase.Refresh();
            }
            
            // OBJ 파일명에서 확장자 제거하여 prefix로 사용
            string objPrefix = Path.GetFileNameWithoutExtension(objFileName);
            objPrefix = SanitizePathName(objPrefix);
            
            // 모든 MeshRenderer를 찾아서 Material 저장
            MeshRenderer[] renderers = go.GetComponentsInChildren<MeshRenderer>(true);
            
            foreach (MeshRenderer renderer in renderers)
            {
                if (renderer == null || renderer.sharedMaterials == null)
                    continue;
                
                Material[] materials = renderer.sharedMaterials;
                Material[] savedMaterials = new Material[materials.Length];
                
                for (int i = 0; i < materials.Length; i++)
                {
                    Material mat = materials[i];
                    if (mat == null)
                    {
                        savedMaterials[i] = null;
                        continue;
                    }
                    
                    // OBJ 파일명과 Material 이름을 조합하여 고유한 파일명 생성
                    string matFileName = $"{objPrefix}_{SanitizePathName(mat.name)}.mat";
                    string matAssetPath = $"{materialsFolderPath}/{matFileName}";
                    
                    // Material을 복사하여 Assets에 저장 (재사용하지 않고 항상 새로 생성)
                    Material savedMat = new Material(mat);
                    savedMat.name = mat.name; // 원본 이름 유지
                    
                    // Shader 설정 (먼저 shader를 설정해야 properties가 제대로 복사됨)
                    Shader shader = mat.shader;
                    if (shader != null)
                    {
                        savedMat.shader = shader;
                    }
                    
                    // Material의 모든 shader properties 복사 (색상, float, vector 등)
                    if (shader != null)
                    {
                        int propertyCount = shader.GetPropertyCount();
                        for (int j = 0; j < propertyCount; j++)
                        {
                            string propName = shader.GetPropertyName(j);
                            UnityEngine.Rendering.ShaderPropertyType propType = shader.GetPropertyType(j);
                            
                            try
                            {
                                switch (propType)
                                {
                                    case UnityEngine.Rendering.ShaderPropertyType.Color:
                                        if (mat.HasColor(propName))
                                            savedMat.SetColor(propName, mat.GetColor(propName));
                                        break;
                                    case UnityEngine.Rendering.ShaderPropertyType.Vector:
                                        if (mat.HasVector(propName))
                                            savedMat.SetVector(propName, mat.GetVector(propName));
                                        break;
                                    case UnityEngine.Rendering.ShaderPropertyType.Float:
                                    case UnityEngine.Rendering.ShaderPropertyType.Range:
                                        if (mat.HasFloat(propName))
                                            savedMat.SetFloat(propName, mat.GetFloat(propName));
                                        break;
                                    case UnityEngine.Rendering.ShaderPropertyType.Texture:
                                        if (mat.HasTexture(propName))
                                        {
                                            Texture tex = mat.GetTexture(propName);
                                            if (tex != null)
                                            {
                                                // 텍스처도 Asset인지 확인하고 필요시 저장
                                                Texture2D tex2d = tex as Texture2D;
                                                if (tex2d != null && !AssetDatabase.Contains(tex2d))
                                                {
                                                    // 텍스처 파일명도 OBJ 파일명을 포함하여 고유하게 생성
                                                    string texFileName = $"{objPrefix}_{SanitizePathName(tex2d.name)}.asset";
                                                    string texAssetPath = $"{materialsFolderPath}/{texFileName}";
                                                    
                                                    Texture2D savedTex = UnityEngine.Object.Instantiate(tex2d);
                                                    savedTex.name = tex2d.name;
                                                    AssetDatabase.CreateAsset(savedTex, texAssetPath);
                                                    AssetDatabase.SaveAssets();
                                                    AssetDatabase.Refresh();
                                                    savedMat.SetTexture(propName, savedTex);
                                                }
                                                else
                                                {
                                                    savedMat.SetTexture(propName, tex);
                                                }
                                            }
                                        }
                                        break;
                                }
                            }
                            catch (Exception)
                            {
                                // 특정 property 복사 실패는 무시
                            }
                        }
                    }
                    
                    // mainTexture 설정 (텍스처가 있으면 처리)
                    Texture2D mainTex = mat.mainTexture as Texture2D;
                    if (mainTex != null)
                    {
                        if (!AssetDatabase.Contains(mainTex))
                        {
                            // 텍스처 파일명도 OBJ 파일명을 포함하여 고유하게 생성
                            string texFileName = $"{objPrefix}_{SanitizePathName(mainTex.name)}.asset";
                            string texAssetPath = $"{materialsFolderPath}/{texFileName}";
                            
                            // Texture2D를 복사하여 Assets에 저장
                            Texture2D savedTex = UnityEngine.Object.Instantiate(mainTex);
                            savedTex.name = mainTex.name;
                            
                            // 텍스처를 Assets에 저장
                            AssetDatabase.CreateAsset(savedTex, texAssetPath);
                            AssetDatabase.SaveAssets();
                            AssetDatabase.Refresh();
                            
                            savedMat.mainTexture = savedTex;
                        }
                        else
                        {
                            // 이미 Asset인 텍스처는 그대로 사용
                            savedMat.mainTexture = mainTex;
                        }
                    }
                    
                    // Material의 기본 속성도 복사 (renderQueue, doubleSidedGI 등)
                    savedMat.renderQueue = mat.renderQueue;
                    savedMat.enableInstancing = mat.enableInstancing;
                    savedMat.doubleSidedGI = mat.doubleSidedGI;
                    
                    // Material을 Assets에 저장
                    AssetDatabase.CreateAsset(savedMat, matAssetPath);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                    
                    savedMaterials[i] = savedMat;
                }
                
                // 저장된 Material로 교체
                renderer.sharedMaterials = savedMaterials;
                EditorUtility.SetDirty(renderer);
            }
            
            AssetDatabase.Refresh();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Material 저장 실패: {ex.Message}\n{ex.StackTrace}");
        }
        #endif
    }

    /// <summary>
    /// GameObject를 프리팹으로 저장합니다 (중복 체크 포함).
    /// </summary>
    GameObject SaveAsPrefabAssetSafe(GameObject rootGo, string prefabPath, bool replaceExisting = false)
    {
        if (rootGo == null || string.IsNullOrEmpty(prefabPath))
            return null;
        
        // 디렉토리 존재 확인 및 생성
        string directory = Path.GetDirectoryName(prefabPath).Replace('\\', '/');
        if (directory.StartsWith("Assets/"))
        {
            directory = directory.Substring(7); // "Assets/" 제거
        }
        
        string[] folders = directory.Split('/');
        string currentPath = "Assets";
        
        foreach (string folder in folders)
        {
            if (string.IsNullOrEmpty(folder))
                continue;
            
            string nextPath = $"{currentPath}/{folder}";
            if (!AssetDatabase.IsValidFolder(nextPath))
            {
                string guid = AssetDatabase.CreateFolder(currentPath, folder);
                if (string.IsNullOrEmpty(guid))
                {
                    Debug.LogError($"폴더 생성 실패: {nextPath}");
                    return null;
                }
                AssetDatabase.Refresh();
            }
            currentPath = nextPath;
        }
        
        // 기존 프리팹 확인
        GameObject existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        
        if (existingPrefab != null && !replaceExisting)
        {
            // 기존 프리팹이 있으면 그대로 반환
            Debug.Log($"기존 프리팹 사용: {prefabPath}");
            return existingPrefab;
        }
        
        try
        {
            // 메모 children은 프리팹에 포함하지 않음 (런타임에 생성)
            GameObject prefabRoot = rootGo;
            
            // 메모 children 임시 비활성화 또는 제거 (프리팹 저장 전)
            List<GameObject> memoChildren = new List<GameObject>();
            foreach (Transform child in rootGo.transform)
            {
                if (child.gameObject.TryGetComponent<TextMesh>(out _) || 
                    child.gameObject.name.StartsWith("Memo_", StringComparison.OrdinalIgnoreCase) ||
                    child.gameObject.name.StartsWith("AudioMemo_", StringComparison.OrdinalIgnoreCase))
                {
                    memoChildren.Add(child.gameObject);
                }
            }
            
            // 메모 children 임시 비활성화 (프리팹 저장 시 포함되지 않도록)
            foreach (var memoChild in memoChildren)
            {
                memoChild.SetActive(false);
            }
            
            // AssetDatabase 완전 새로고침 및 저장 (Mesh/Material Asset 참조 확실히 하기 위해)
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            
            // 프리팹 저장
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(rootGo, prefabPath);
            
            // 메모 children 다시 활성화
            foreach (var memoChild in memoChildren)
            {
                memoChild.SetActive(true);
            }
            
            if (prefab != null)
            {
                #if UNITY_EDITOR
                AssetDatabase.SaveAssets();
                #endif
            }
            else
            {
                Debug.LogError($"프리팹 저장 실패: {prefabPath}");
            }
            
            return prefab;
        }
        catch (Exception ex)
        {
            Debug.LogError($"프리팹 저장 중 오류: {ex.Message}\n경로: {prefabPath}");
            return null;
        }
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
    /// 그룹 이름과 스캔 ID가 제공되면 프리팹으로 저장합니다.
    /// </summary>
    GameObject SpawnWithBothVersions(string originalPath, string retouchedPath = null, MemoUtils.MemoData[] memos = null, string groupName = null, int? scanId = null)
    {
        try
        {
            // Original 파일이 없으면 오류
            if (string.IsNullOrEmpty(originalPath) || !File.Exists(originalPath))
            {
                EditorUtility.DisplayDialog("파일 없음", $"Original 파일을 찾을 수 없습니다:\n{originalPath}", "OK");
                return null;
            }
            
            // 그룹 이름이 없으면 현재 선택된 그룹 사용
            if (string.IsNullOrEmpty(groupName))
            {
                groupName = GetSelectedGroupName();
            }
            
            // config에 직접 접근하지 않고, 경로를 통해 안전하게 로드
            float unitScale = 1000f; // 기본값
            if (!string.IsNullOrEmpty(_configAssetPath))
            {
                try
                {
                    WatchConfig tempConfig = AssetDatabase.LoadAssetAtPath<WatchConfig>(_configAssetPath);
                    if (tempConfig != null)
                    {
                        unitScale = tempConfig.unitScale;
                    }
                }
                catch (System.Exception)
                {
                    // config 접근 실패 시 기본값 사용
                }
            }
            
            string groupFolderPath = null;
            string prefabsFolderPath = null;
            string prefabPath = null;
            
            // 기존 프리팹이 있으면 즉시 사용
            if (!string.IsNullOrEmpty(groupName) && scanId.HasValue)
            {
                groupFolderPath = GetOrCreateSnapSpaceFolder(groupName);
                if (!string.IsNullOrEmpty(groupFolderPath))
                {
                    prefabsFolderPath = $"{groupFolderPath}/prefabs";
                    if (!AssetDatabase.IsValidFolder(prefabsFolderPath))
                    {
                        string guid = AssetDatabase.CreateFolder(groupFolderPath, "prefabs");
                        if (string.IsNullOrEmpty(guid))
                        {
                            Debug.LogError($"prefabs 폴더 생성 실패: {prefabsFolderPath}");
                        }
                        else
                        {
                            AssetDatabase.Refresh();
                        }
                    }
                    
                    prefabPath = $"{prefabsFolderPath}/{scanId.Value}_Root.prefab";
                    GameObject existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    if (existingPrefab != null)
                    {
                        GameObject prefabInstance = PrefabUtility.InstantiatePrefab(existingPrefab) as GameObject;
                        if (prefabInstance != null)
                        {
                            if (memos != null && memos.Length > 0)
                            {
                                MemoUtils.SpawnMemosAsChildren(prefabInstance, memos, unitScale);
                            }
                            
                            try
                            {
                                ObjPathInfo.SetPath(prefabInstance, originalPath);
                                Transform[] allInstanceChildren = prefabInstance.GetComponentsInChildren<Transform>(true);
                                foreach (Transform child in allInstanceChildren)
                                {
                                    if (child != null && child != prefabInstance.transform && child.gameObject != null)
                                    {
                                        ObjPathInfo.SetPath(child.gameObject, originalPath);
                                    }
                                }
                            }
                            catch (System.Exception)
                            {
                                // 경로 저장 실패 시 무시
                            }
                            
                            Selection.activeObject = prefabInstance;
                            return prefabInstance;
                        }
                    }
                }
            }
            
            // Root GameObject 생성
            string rootName = Path.GetFileNameWithoutExtension(originalPath);
            if (scanId.HasValue)
            {
                rootName = $"Scan_{scanId.Value}_{rootName}";
            }
            GameObject rootGo = new GameObject($"{rootName}_Root");
            Undo.RegisterCreatedObjectUndo(rootGo, "Spawn OBJ Root");
            
            // Root를 Unity 원점에 배치
            rootGo.transform.position = Vector3.zero;
            rootGo.transform.rotation = Quaternion.identity;
            rootGo.transform.localScale = Vector3.one;
            
            // Original OBJ 로드 및 설정 (그룹 이름 전달)
            GameObject originalGo = LoadMeshFile(originalPath, groupName);
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
            
            // Retouched OBJ 로드 및 설정 (있는 경우, 그룹 이름 전달)
            GameObject retouchedGo = null;
            if (!string.IsNullOrEmpty(retouchedPath) && File.Exists(retouchedPath))
            {
                retouchedGo = LoadMeshFile(retouchedPath, groupName);
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
            
            // 경로 정보 저장 (메모 생성 전에 root에 경로를 설정하여 MemoUtils가 참조할 수 있도록 함)
            try
            {
                ObjPathInfo.SetPath(rootGo, originalPath);
            }
            catch (System.Exception)
            {
                // 경로 설정 실패 시 무시 (이후 children 처리 시 다시 시도)
            }
            
            // Memos를 root GameObject의 자식으로 추가
            // 메모의 anchor 좌표는 "스케일이 1일 때의 좌표" (unitScale = 1일 때의 Unity 월드 좌표계 기준)를 의미합니다.
            // unitScale이 변경되면 OBJ 메시의 스케일이 변경되므로, 메모도 그에 맞춰 조정되어야 합니다.
            // root의 localScale = 1이므로, 메모의 로컬 좌표는 anchor 좌표를 unitScale로 나눈 값입니다.
            if (memos != null && memos.Length > 0)
            {
                MemoUtils.SpawnMemosAsChildren(rootGo, memos, unitScale); // unitScale을 전달하여 스케일 보정
            }
            
            // 경로 정보 저장 (children 포함)
            try
            {
                
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
                // SafeSetDirty를 사용하여 assertion 오류 방지
                SafeSetDirty(rootGo);
                #endif
            }
            catch (System.Exception)
            {
                // 경로 저장 실패 시 무시
            }
            
            // 프리팹 저장 (그룹 이름과 스캔 ID가 제공된 경우)
            if (!string.IsNullOrEmpty(groupName) && scanId.HasValue)
            {
                try
                {
                    if (string.IsNullOrEmpty(groupFolderPath))
                    {
                        groupFolderPath = GetOrCreateSnapSpaceFolder(groupName);
                    }
                    
                    if (string.IsNullOrEmpty(groupFolderPath))
                    {
                        throw new Exception("그룹 폴더를 생성할 수 없습니다.");
                    }
                    
                    if (string.IsNullOrEmpty(prefabsFolderPath))
                    {
                        prefabsFolderPath = $"{groupFolderPath}/prefabs";
                        if (!AssetDatabase.IsValidFolder(prefabsFolderPath))
                        {
                            string guid = AssetDatabase.CreateFolder(groupFolderPath, "prefabs");
                            if (string.IsNullOrEmpty(guid))
                            {
                                Debug.LogError($"prefabs 폴더 생성 실패: {prefabsFolderPath}");
                            }
                            else
                            {
                                AssetDatabase.Refresh();
                            }
                        }
                    }
                    
                    if (string.IsNullOrEmpty(prefabsFolderPath))
                    {
                        throw new Exception("prefabs 폴더를 생성할 수 없습니다.");
                    }
                    
                    if (string.IsNullOrEmpty(prefabPath))
                    {
                        prefabPath = $"{prefabsFolderPath}/{scanId.Value}_Root.prefab";
                    }
                    
                    GameObject prefab = SaveAsPrefabAssetSafe(rootGo, prefabPath, replaceExisting: true);
                    
                    if (prefab != null)
                    {
                        // 프리팹이 성공적으로 저장되었으면 프리팹에서 인스턴스 생성
                        GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                        
                        if (instance != null)
                        {
                            // 프리팹 인스턴스에 메모 다시 추가 (프리팹에는 포함되지 않았으므로)
                            if (memos != null && memos.Length > 0)
                            {
                                MemoUtils.SpawnMemosAsChildren(instance, memos, unitScale);
                            }
                            
                            // 원본 rootGo 삭제 (프리팹 인스턴스 사용)
                            GameObject.DestroyImmediate(rootGo);
                            rootGo = instance;
                            
                            // 프리팹 인스턴스에 경로 정보 저장
                            try
                            {
                                ObjPathInfo.SetPath(rootGo, originalPath);
                                Transform[] allInstanceChildren = rootGo.GetComponentsInChildren<Transform>(true);
                                foreach (Transform child in allInstanceChildren)
                                {
                                    if (child != null && child != rootGo.transform && child.gameObject != null)
                                    {
                                        ObjPathInfo.SetPath(child.gameObject, originalPath);
                                    }
                                }
                            }
                            catch (System.Exception)
                            {
                                // 경로 저장 실패 시 무시
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"프리팹 저장 중 오류 (씬 오브젝트 사용): {ex.Message}\n{ex.StackTrace}");
                    // 프리팹 저장 실패 시 기존 rootGo 사용
                }
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
    /// 그룹 이름이 제공되면 Assets/SnapSpace/그룹이름/으로 파일을 복사합니다.
    /// </summary>
    GameObject LoadMeshFile(string meshPath, string groupName = null)
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
                // OBJ 파일 복사 (그룹 이름이 있는 경우)
                string actualObjPath = meshPath;
                if (!string.IsNullOrEmpty(groupName))
                {
                    string copiedObjPath = CopyObjToAssets(meshPath, groupName);
                    if (!string.IsNullOrEmpty(copiedObjPath))
                    {
                        actualObjPath = copiedObjPath;
                    }
                }
                
                // OBJ 파일의 원본 좌표 시스템을 유지하기 위해 preserveOriginalCoordinates=true 사용
                // Assets로 복사된 경우에도 원본 경로를 RuntimeObjLoader에 전달 (메타데이터 유지)
                go = RuntimeObjLoader.LoadObj(meshPath, preserveOriginalCoordinates: true);
                if (go != null)
                {
                    Undo.RegisterCreatedObjectUndo(go, "Load OBJ");
                    
                    // 그룹 이름이 있으면 Mesh와 Material을 Assets에 저장
                    if (!string.IsNullOrEmpty(groupName))
                    {
                        string objFileName = Path.GetFileName(meshPath);
                        SaveMeshesToAssets(go, groupName, objFileName);
                        SaveMaterialsToAssets(go, groupName, objFileName);
                    }
                }
                break;
                
            case ".glb":
            case ".gltf":
                // GLB/GLTF 파일은 Unity의 기본 임포트 기능 사용 (그룹 이름 전달)
                go = LoadGlbOrGltf(meshPath, groupName);
                if (go != null)
                {
                    Undo.RegisterCreatedObjectUndo(go, "Load GLB/GLTF");
                }
                break;
                
            case ".fbx":
                // FBX 파일은 Unity의 기본 임포트 기능 사용 (그룹 이름 전달)
                go = LoadFbx(meshPath, groupName);
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
    /// 그룹 이름이 제공되면 Assets/SnapSpace/그룹이름/으로 복사합니다.
    /// </summary>
    GameObject LoadGlbOrGltf(string filePath, string groupName = null)
    {
        try
        {
            // Unity 에디터에서만 작동
            #if UNITY_EDITOR
            // 파일 존재 확인
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                Debug.LogError($"GLB 파일을 찾을 수 없습니다: {filePath}");
                EditorUtility.DisplayDialog("파일 없음", $"GLB 파일을 찾을 수 없습니다:\n{filePath}", "OK");
                return null;
            }
            
            // 파일을 Assets 폴더로 복사하여 임포트
            string fileName = Path.GetFileName(filePath);
            string tempAssetPath;
            
            if (!string.IsNullOrEmpty(groupName))
            {
                // SnapSpace/그룹이름/ 폴더 사용
                string groupFolderPath = GetOrCreateSnapSpaceFolder(groupName);
                if (string.IsNullOrEmpty(groupFolderPath))
                {
                    Debug.LogError($"그룹 폴더를 찾을 수 없습니다: {groupName}");
                    return null;
                }
                tempAssetPath = $"{groupFolderPath}/{fileName}";
            }
            else
            {
                // 기존 방식: Temp 폴더 사용
                tempAssetPath = $"Assets/Temp_{fileName}";
            }
            
            // 기존 파일이 있으면 삭제 (Temp 폴더인 경우만, SnapSpace는 재사용)
            bool shouldCopyFile = true;
            if (!string.IsNullOrEmpty(groupName) && File.Exists(tempAssetPath))
            {
                // SnapSpace 폴더인 경우 기존 파일 재사용 (복사하지 않음)
                shouldCopyFile = false;
            }
            else if (File.Exists(tempAssetPath))
            {
                // Temp 폴더인 경우 기존 파일 삭제
                AssetDatabase.DeleteAsset(tempAssetPath);
                AssetDatabase.Refresh();
            }
            
            // 파일 복사 (필요한 경우만)
            if (shouldCopyFile)
            {
                try
                {
                    File.Copy(filePath, tempAssetPath, true);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"GLB 파일 복사 실패: {ex.Message}");
                    EditorUtility.DisplayDialog("파일 복사 실패", $"GLB 파일을 Assets 폴더로 복사할 수 없습니다:\n{ex.Message}", "OK");
                    return null;
                }
            }
            
            // AssetDatabase 새로고침
            AssetDatabase.Refresh();
            
            // AssetDatabase를 통해 임포트
            AssetDatabase.ImportAsset(tempAssetPath, ImportAssetOptions.ForceUpdate);
            
            // 임포트 완료 대기 (비동기 임포트 완료를 위해)
            AssetDatabase.Refresh();
            
            // ModelImporter 설정 (필요시)
            ModelImporter importer = AssetImporter.GetAtPath(tempAssetPath) as ModelImporter;
            if (importer != null)
            {
                // 스케일을 1로 설정 (나중에 unitScale 적용)
                importer.globalScale = 1.0f;
                importer.SaveAndReimport();
                
                // 재임포트 완료 대기
                AssetDatabase.Refresh();
            }
            else
            {
                Debug.LogWarning($"GLB 파일의 ModelImporter를 찾을 수 없습니다: {tempAssetPath}");
            }
            
            // 임포트된 게임오브젝트 로드 (여러 번 시도)
            GameObject prefab = null;
            for (int i = 0; i < 5; i++)
            {
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(tempAssetPath);
                if (prefab != null)
                    break;
                
                // 잠시 대기 후 재시도
                System.Threading.Thread.Sleep(100);
                AssetDatabase.Refresh();
            }
            
            if (prefab != null)
            {
                // 씬에 인스턴스 생성 (프리팹이 아닐 수도 있으므로 GameObject.Instantiate 사용)
                GameObject instance = GameObject.Instantiate(prefab);
                instance.name = Path.GetFileNameWithoutExtension(filePath);
                
                Debug.Log($"GLB 파일 로드 성공: {filePath}");
                
                // Temp 폴더가 아닌 경우 (SnapSpace) 임시 파일 삭제하지 않음
                // Temp 폴더인 경우에도 삭제는 사용자가 수동으로 할 수 있도록 주석 처리
                // AssetDatabase.DeleteAsset(tempAssetPath);
                
                return instance;
            }
            else
            {
                Debug.LogError($"GLB 파일을 GameObject로 로드할 수 없습니다: {tempAssetPath}");
                EditorUtility.DisplayDialog("로드 실패", $"GLB 파일을 GameObject로 로드할 수 없습니다:\n{tempAssetPath}\n\n파일이 유효한 GLB/GLTF 형식인지 확인하세요.", "OK");
                
                // 임시 파일 삭제
                AssetDatabase.DeleteAsset(tempAssetPath);
                AssetDatabase.Refresh();
                return null;
            }
            #else
            return null;
            #endif
        }
        catch (Exception ex)
        {
            Debug.LogError($"GLB 파일 로드 중 오류 발생: {ex.Message}\n{ex.StackTrace}");
            EditorUtility.DisplayDialog("로드 오류", $"GLB 파일 로드 중 오류가 발생했습니다:\n{ex.Message}", "OK");
            return null;
        }
    }
    
    /// <summary>
    /// FBX 파일을 로드합니다.
    /// Unity 에디터에서는 AssetDatabase를 사용하여 임포트합니다.
    /// 그룹 이름이 제공되면 Assets/SnapSpace/그룹이름/으로 복사합니다.
    /// </summary>
    GameObject LoadFbx(string filePath, string groupName = null)
    {
        try
        {
            // Unity 에디터에서만 작동
            #if UNITY_EDITOR
            // 파일을 Assets 폴더로 복사하여 임포트
            string fileName = Path.GetFileName(filePath);
            string tempAssetPath;
            
            if (!string.IsNullOrEmpty(groupName))
            {
                // SnapSpace/그룹이름/ 폴더 사용
                string groupFolderPath = GetOrCreateSnapSpaceFolder(groupName);
                if (string.IsNullOrEmpty(groupFolderPath))
                {
                    Debug.LogError($"그룹 폴더를 찾을 수 없습니다: {groupName}");
                    return null;
                }
                tempAssetPath = $"{groupFolderPath}/{fileName}";
                
                // 기존 파일이 있으면 재사용 (복사하지 않음)
                if (File.Exists(tempAssetPath))
                {
                    // 파일 내용 비교는 생략하고 경로만 확인
                }
                else
                {
                    // 파일 복사
                    File.Copy(filePath, tempAssetPath, true);
                }
            }
            else
            {
                // 기존 방식: Temp 폴더 사용
                tempAssetPath = $"Assets/Temp_{fileName}";
                
                // 파일 복사
                File.Copy(filePath, tempAssetPath, true);
            }
            
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
                
                // Temp 폴더가 아닌 경우 (SnapSpace) 임시 파일 삭제하지 않음
                // Temp 폴더인 경우에도 삭제는 사용자가 수동으로 할 수 있도록 주석 처리
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


    void MarkConfigDirty(WatchConfig cfg = null)
    {
        // 파라미터가 없으면 기존 방식 (하위 호환성)
        WatchConfig targetConfig = cfg ?? config;
        
        if (targetConfig == null)
            return;

        // DontSaveInEditor 플래그가 설정되어 있으면 저장하지 않음
        try
        {
            if ((targetConfig.hideFlags & HideFlags.DontSaveInEditor) != 0)
                return;
        }
        catch (System.Exception)
        {
            return;
        }

        // AssetDatabase를 사용하여 더 안전하게 체크
        if (!AssetDatabase.Contains(targetConfig))
            return;

        // 객체가 파괴되었는지 체크 (Unity 특수 케이스)
        try
        {
            if (targetConfig.Equals(null))
                return;
        }
        catch (System.Exception)
        {
            return;
        }

        // SafeSetDirty를 사용하여 assertion 오류 방지
        SafeSetDirty(targetConfig);
    }
    
    /// <summary>
    /// 안전하게 SetDirty를 호출합니다. DontSaveInEditor 플래그가 있는 객체는 건너뜁니다.
    /// </summary>
    void SafeSetDirty(UnityEngine.Object obj)
    {
        if (obj == null) return;
        #if UNITY_EDITOR
        try
        {
            // DontSaveInEditor 플래그가 있으면 SetDirty 호출하지 않음 (assertion 오류 방지)
            if ((obj.hideFlags & HideFlags.DontSaveInEditor) == 0)
            {
                EditorUtility.SetDirty(obj);
            }
        }
        catch (System.ArgumentException)
        {
            // 잘못된 객체인 경우 무시 (예: DontSaveInEditor 객체)
        }
        catch (System.Exception)
        {
            // SetDirty 실패 시 무시 (메모리 오브젝트이거나 이미 삭제된 경우)
        }
        #endif
    }

    // 직렬화 가능한지 확인하는 헬퍼 메서드
    // Unity의 assertion 오류를 방지하기 위해 직렬화 불가능한 HideFlags를 체크
    private bool IsConfigSerializable(WatchConfig cfg)
    {
        if (cfg == null)
            return false;
        
        try
        {
            // Unity 객체가 파괴되었는지 확인
            if (cfg.Equals(null))
                return false;
            
            // AssetDatabase에 있는 실제 에셋인지 확인
            if (!AssetDatabase.Contains(cfg))
                return false;
            
            // 문제가 될 수 있는 HideFlags 체크
            // Unity의 assertion: (ptr->GetHideFlags() & m_RequiredHideFlags) == m_RequiredHideFlags
            // 이 assertion은 특정 HideFlags 조합에서 실패할 수 있음
            HideFlags flags = cfg.hideFlags;
            
            // 직렬화 시 문제를 일으킬 수 있는 HideFlags
            // DontSaveInEditor: 에디터에서 저장하지 않음 (가장 흔한 원인)
            // DontSave: 저장하지 않음
            // HideAndDontSave: 숨기고 저장하지 않음
            // 이 플래그들이 있으면 Unity가 직렬화 시 assertion 오류를 발생시킴
            HideFlags problematicFlags = HideFlags.DontSaveInEditor | 
                                         HideFlags.DontSave | 
                                         HideFlags.HideAndDontSave;
            
            if ((flags & problematicFlags) != 0)
            {
                return false;
            }
            
            // 일반적으로 None 또는 NotEditable만 허용
            // NotEditable은 일반적으로 문제가 되지 않음
            // 다른 플래그가 있으면 추가 검증 (안전을 위해)
            if (flags != HideFlags.None && flags != HideFlags.NotEditable)
            {
                // NotEditable만 있는 경우는 허용
                if ((flags & ~HideFlags.NotEditable) != HideFlags.None)
                {
                    // NotEditable 외의 다른 플래그가 있으면 직렬화 불가능
                    return false;
                }
            }
            
            return true;
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    // ISerializationCallbackReceiver 구현: 직렬화 전/후 config 유효성 검사
    public void OnBeforeSerialize()
    {
        // 직렬화 전에 config가 직렬화 가능한지 확인
        // 문제가 될 수 있는 HideFlags가 있는 경우 assertion 오류를 방지하기 위해 경로만 저장
        if (config != null)
        {
            try
            {
                // 직렬화 가능한지 확인
                if (!IsConfigSerializable(config))
                {
                    // 직렬화 불가능하면 경로만 저장하고 참조는 null로 설정
                    string assetPath = AssetDatabase.GetAssetPath(config);
                    if (!string.IsNullOrEmpty(assetPath))
                    {
                        _configAssetPath = assetPath;
                    }
                    config = null;
                    return;
                }
                
                // 직렬화 가능하면 경로도 저장하여 복원 시 사용
                string path = AssetDatabase.GetAssetPath(config);
                if (!string.IsNullOrEmpty(path))
                {
                    _configAssetPath = path;
                }
            }
            catch (System.Exception)
            {
                // 예외 발생 시 안전을 위해 config를 null로 설정
                // 이렇게 하면 직렬화 오류를 방지할 수 있음
                config = null;
                _configAssetPath = null;
            }
        }
        else
        {
            // config가 null이면 경로도 초기화
            if (string.IsNullOrEmpty(_configAssetPath))
            {
                _configAssetPath = null;
            }
        }
    }

    public void OnAfterDeserialize()
    {
        // 역직렬화 후 config 복원은 OnEnable()에서 수행
        // 여기서는 추가 검증만 수행
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
