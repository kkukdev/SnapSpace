using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEngine;

public class ObjDropWatcherWindow : EditorWindow
{
    [SerializeField] private WatchConfig config;
    private FileSystemWatcher _watcher;
    private bool _watching;
    private Vector2 _scroll;
    private Vector2 _folderScroll;
    private readonly List<Item> _items = new();
    private string _selectedSubFolder;
    private List<string> _availableFolders = new();

    [Serializable] class Item { public string folder; public string obj; public string label; }

    [MenuItem("Tools/OBJ Drop Watcher")]
    public static void Open()
    {
        var w = GetWindow<ObjDropWatcherWindow>("OBJ Drop Watcher");
        w.minSize = new Vector2(520, 340);
        w.Show();
    }

    private void OnDisable() => StopWatching();

    void OnGUI()
    {
        EditorGUILayout.Space();
        config = (WatchConfig)EditorGUILayout.ObjectField("WatchConfig", config, typeof(WatchConfig), false);

        if (config != null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Watch Directory", EditorStyles.boldLabel);
            
            EditorGUILayout.BeginHorizontal();
            config.rootWatchDirectory = EditorGUILayout.TextField("Path", config.rootWatchDirectory ?? "");
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                string defaultPath = string.IsNullOrEmpty(config.rootWatchDirectory) ? Application.dataPath : config.rootWatchDirectory;
                // UNC 경로인 경우 부모 디렉토리로 변경
                if (defaultPath.StartsWith(@"\\"))
                {
                    defaultPath = Application.dataPath;
                }
                
                string selectedPath = EditorUtility.OpenFolderPanel("Select Watch Directory", defaultPath, "");
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    config.rootWatchDirectory = selectedPath;
                    MarkConfigDirty();
                }
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.HelpBox("네트워크 경로(UNC)는 직접 입력하세요.\n예: \\\\server\\share\\folder", MessageType.Info);
            
            // 경로 유효성 표시
            if (!string.IsNullOrWhiteSpace(config.rootWatchDirectory))
            {
                bool exists = Directory.Exists(config.rootWatchDirectory);
                if (exists)
                {
                    EditorGUILayout.HelpBox($"✓ 경로 유효: {config.rootWatchDirectory}", MessageType.Info);
                    
                    // 하위 폴더 선택
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("분류 폴더 선택", EditorStyles.boldLabel);
                    
                    if (GUILayout.Button("폴더 목록 새로고침", GUILayout.Height(22)))
                    {
                        RefreshFolderList();
                    }
                    
                    if (_availableFolders.Count > 0)
                    {
                        EditorGUILayout.LabelField($"감지된 폴더: {_availableFolders.Count}개", EditorStyles.miniLabel);
                        
                        _folderScroll = EditorGUILayout.BeginScrollView(_folderScroll, GUILayout.Height(120));
                        foreach (var folder in _availableFolders)
                        {
                            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                            
                            bool isSelected = _selectedSubFolder == folder;
                            var originalColor = GUI.backgroundColor;
                            if (isSelected)
                            {
                                GUI.backgroundColor = Color.green;
                            }
                            
                            if (GUILayout.Button(Path.GetFileName(folder), GUILayout.Height(24)))
                            {
                                _selectedSubFolder = folder;
                            }
                            
                            GUI.backgroundColor = originalColor;
                            
                            if (GUILayout.Button("📁", GUILayout.Width(30), GUILayout.Height(24)))
                            {
                                Reveal(folder);
                            }
                            
                            EditorGUILayout.EndHorizontal();
                        }
                        EditorGUILayout.EndScrollView();
                        
                        if (!string.IsNullOrEmpty(_selectedSubFolder))
                        {
                            EditorGUILayout.HelpBox($"선택된 폴더: {_selectedSubFolder}", MessageType.Info);
                        }
                        else
                        {
                            EditorGUILayout.HelpBox("감시할 분류 폴더를 선택하세요.", MessageType.Info);
                        }
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("루트 경로 안에 하위 폴더가 없습니다.\n'폴더 목록 새로고침' 버튼을 눌러 폴더를 검색하세요.", MessageType.Warning);
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox($"✗ 경로 없음: {config.rootWatchDirectory}\n경로가 존재하지 않습니다.", MessageType.Warning);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("경로를 입력하거나 Browse 버튼으로 폴더를 선택하세요.", MessageType.Info);
            }
            
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
            config.includeSubdirectories = EditorGUILayout.Toggle("Include Subdirectories", config.includeSubdirectories);
            config.scanDebounceMs = EditorGUILayout.IntField("Scan Debounce (ms)", config.scanDebounceMs);
            config.objPatterns = EditorGUILayout.TextField("OBJ Patterns", config.objPatterns ?? "*.obj");
            config.unitScale = EditorGUILayout.FloatField("Unit Scale", config.unitScale);
            
            if (GUI.changed)
            {
                MarkConfigDirty();
            }
        }
        else
        {
            EditorGUILayout.HelpBox("WatchConfig를 먼저 선택하세요.\nCreate > Configs > WatchConfig로 생성할 수 있습니다.", MessageType.Info);
        }

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(config == null || string.IsNullOrWhiteSpace(_selectedSubFolder) || !Directory.Exists(_selectedSubFolder)))
        {
            EditorGUILayout.BeginHorizontal();
            if (!_watching)
            {
                if (GUILayout.Button("Start Watching", GUILayout.Height(24))) StartWatching();
            }
            else
            {
                if (GUILayout.Button("Stop", GUILayout.Height(24))) StopWatching();
            }
            if (GUILayout.Button("Initial Scan", GUILayout.Height(24))) InitialScan();
            if (GUILayout.Button("Open Folder", GUILayout.Height(24))) Reveal(GetWatchDirectory());
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Detected OBJ files", EditorStyles.boldLabel);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        if (_items.Count == 0)
        {
            EditorGUILayout.HelpBox("감지된 항목이 없습니다.\n- Start Watching 후 새 폴더를 만들고 OBJ를 넣어보세요.\n- 또는 Initial Scan으로 기존 폴더를 스캔하세요.", MessageType.Info);
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
                if (GUILayout.Button("Ping Folder", GUILayout.Height(22))) Reveal(it.folder);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }
        }
        EditorGUILayout.EndScrollView();
    }

    void RefreshFolderList()
    {
        _availableFolders.Clear();
        
        if (config == null || string.IsNullOrWhiteSpace(config.rootWatchDirectory))
            return;
        
        if (!Directory.Exists(config.rootWatchDirectory))
            return;
        
        try
        {
            var dirs = Directory.GetDirectories(config.rootWatchDirectory, "*", SearchOption.TopDirectoryOnly);
            _availableFolders.AddRange(dirs);
            Debug.Log($"[ObjDropWatcher] Found {_availableFolders.Count} folders in {config.rootWatchDirectory}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ObjDropWatcher] Failed to refresh folder list: {ex.Message}");
        }
    }
    
    string GetWatchDirectory()
    {
        if (!string.IsNullOrEmpty(_selectedSubFolder))
            return _selectedSubFolder;
        if (config != null)
            return config.rootWatchDirectory;
        return "";
    }

    void StartWatching()
    {
        if (config == null)
        {
            EditorUtility.DisplayDialog("Config 없음", "WatchConfig를 먼저 선택하세요.", "OK");
            return;
        }
        
        if (string.IsNullOrWhiteSpace(_selectedSubFolder))
        {
            EditorUtility.DisplayDialog("분류 폴더 미선택", "감시할 분류 폴더를 선택하세요.", "OK");
            return;
        }
        
        var watchDir = GetWatchDirectory();
        if (string.IsNullOrWhiteSpace(watchDir))
        {
            EditorUtility.DisplayDialog("경로 없음", "Watch Directory 경로를 입력하세요.", "OK");
            return;
        }
        
        if (!Directory.Exists(watchDir))
        {
            EditorUtility.DisplayDialog("경로 없음", $"경로가 존재하지 않습니다:\n{watchDir}\n\n경로를 확인하고 다시 시도하세요.", "OK");
            return;
        }

        _watcher = new FileSystemWatcher(watchDir)
        {
            IncludeSubdirectories = config.includeSubdirectories,
            NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite
        };
        _watcher.Created += OnFsEvent;
        _watcher.Renamed += OnFsEvent;
        _watcher.EnableRaisingEvents = true;
        _watching = true;
        ShowNotification(new GUIContent($"Watching: {watchDir}"));
    }

    void StopWatching()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnFsEvent;
            _watcher.Renamed -= OnFsEvent;
            _watcher.Dispose();
            _watcher = null;
        }
        _watching = false;
        RemoveNotification();
    }

    void OnFsEvent(object s, FileSystemEventArgs e)
    {
        int delay = Mathf.Max(0, config ? config.scanDebounceMs : 800);
        new Thread(() =>
        {
            // 1️⃣ 기본 지연 (복사 완료 대기)
            Thread.Sleep(delay);

            string dir = null;
            if (Directory.Exists(e.FullPath))
                dir = e.FullPath;
            else if (File.Exists(e.FullPath))
                dir = Path.GetDirectoryName(e.FullPath);

            if (string.IsNullOrEmpty(dir)) return;

            // 2️⃣ 추가 감시 루프: obj+mtl 모두 있는지 확인
            bool ready = false;
            for (int i = 0; i < 10; i++) // 최대 10회, 약 10초
            {
                var objExists = Directory.GetFiles(dir, "*.obj").Length > 0;
                var mtlExists = Directory.GetFiles(dir, "*.mtl").Length > 0;
                if (objExists && mtlExists)
                {
                    ready = true;
                    break;
                }
                Thread.Sleep(1000); // 1초 대기 후 재확인
            }

            if (!ready) return;

            // 3️⃣ 에디터 메인 스레드에서 실행
            EditorApplication.delayCall += () =>
            {
                try
                {
                    ScanFolder(dir);   // 목록 추가
                    AutoSpawnIfReady(dir); // 자동 스폰
                }
                catch (Exception ex) { Debug.LogException(ex); }
                Repaint();
            };
        }).Start();
    }

    void InitialScan()
    {
        if (config == null)
        {
            EditorUtility.DisplayDialog("Config 없음", "WatchConfig를 먼저 선택하세요.", "OK");
            return;
        }
        
        if (string.IsNullOrWhiteSpace(_selectedSubFolder))
        {
            EditorUtility.DisplayDialog("분류 폴더 미선택", "감시할 분류 폴더를 선택하세요.", "OK");
            return;
        }
        
        _items.Clear();
        var watchDir = GetWatchDirectory();
        if (string.IsNullOrWhiteSpace(watchDir))
        {
            EditorUtility.DisplayDialog("경로 없음", "Watch Directory 경로를 입력하세요.", "OK");
            return;
        }
        
        if (!Directory.Exists(watchDir))
        {
            EditorUtility.DisplayDialog("경로 없음", $"경로가 존재하지 않습니다:\n{watchDir}\n\n경로를 확인하고 다시 시도하세요.", "OK");
            return;
        }
        foreach (var d in Directory.GetDirectories(watchDir, "*", SearchOption.TopDirectoryOnly))
            ScanFolder(d);
        Repaint();
    }

    void ScanFolder(string dir)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
        var patterns = (config.objPatterns ?? "*.obj").Split(',');
        foreach (var p in patterns)
        {
            var files = Directory.GetFiles(dir, p.Trim(), SearchOption.TopDirectoryOnly);
            foreach (var f in files)
            {
                if (_items.Exists(x => x.obj == f)) continue;
                _items.Add(new Item { folder = dir, obj = f, label = $"{Path.GetFileName(dir)} / {Path.GetFileName(f)}" });
            }
        }
    }

    void Spawn(string objPath)
    {
        try
        {
            var go = RuntimeObjLoader.LoadObj(objPath);
            Undo.RegisterCreatedObjectUndo(go, "Spawn OBJ");
            Selection.activeObject = go;

            go.transform.position = Vector3.up * 0.5f;
            go.transform.rotation = Quaternion.identity;

            // ----------------------------------------------------
            // 👇 단위 보정: mm → m 변환 (1000배 확대)
            // ----------------------------------------------------
            float unitScale = 1000f; // CAD나 스캐너 OBJ는 mm단위 → 1m 단위로 변환
            go.transform.localScale = Vector3.one * unitScale;

            Debug.Log($"[Spawned with scale x{unitScale}] {objPath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Spawn 실패: {objPath}\n{ex}");
        }
    }

    void AutoSpawnIfReady(string dir)
    {
        var objs = Directory.GetFiles(dir, "*.obj", SearchOption.TopDirectoryOnly);
        foreach (var objPath in objs)
        {
            // 이미 목록에 있으면 스킵
            if (_items.Exists(x => x.obj == objPath)) continue;

            _items.Add(new Item
            {
                folder = dir,
                obj = objPath,
                label = $"{Path.GetFileName(dir)} / {Path.GetFileName(objPath)}"
            });

            Debug.Log($"[Auto Detected] {objPath}");

            // 바로 씬에 스폰
            try
            {
                var go = RuntimeObjLoader.LoadObj(objPath);
                go.transform.position = Vector3.up * 0.5f;
                go.transform.rotation = Quaternion.identity;

                float unitScale = config ? config.unitScale : 1000f; // mm→m 기본 1000배
                go.transform.localScale = Vector3.one * unitScale;

                Undo.RegisterCreatedObjectUndo(go, "Auto Spawn OBJ");
                Selection.activeObject = go;

                Debug.Log($"[Spawned Automatically with MTL] {objPath}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AutoSpawn Error] {objPath}\n{ex}");
            }
        }
    }


    static void Reveal(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        EditorUtility.RevealInFinder(path);
    }

    void MarkConfigDirty()
    {
        if (config == null)
            return;

        if ((config.hideFlags & HideFlags.DontSaveInEditor) != 0)
            return;

        if (EditorUtility.IsPersistent(config))
        {
            EditorUtility.SetDirty(config);
        }
    }
}
