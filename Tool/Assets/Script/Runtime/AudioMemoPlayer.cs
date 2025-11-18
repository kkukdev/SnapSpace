using System;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ObjDropWatcher.ExportImport
{
    /// <summary>
    /// 오디오 메모를 재생하는 컴포넌트
    /// Unity 에디터에서 오디오 파일을 로드하고 재생할 수 있습니다.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class AudioMemoPlayer : MonoBehaviour
    {
        [Header("오디오 설정")]
        [Tooltip("오디오 파일 경로 (절대 경로)")]
        public string audioFilePath;
        
        [Tooltip("오디오 제목")]
        public string audioTitle;
        
        [Header("재생 설정")]
        [Tooltip("자동 재생 여부")]
        public bool playOnAwake = false;
        
        [Tooltip("반복 재생 여부")]
        public bool loop = false;
        
        [Tooltip("볼륨 (0.0 ~ 1.0)")]
        [Range(0f, 1f)]
        public float volume = 1f;
        
        private AudioSource audioSource;
        private AudioClip audioClip;
        private bool isLoaded = false;
        
        /// <summary>
        /// 에디터 확장을 위한 AudioSource 접근자
        /// </summary>
        public AudioSource AudioSourceComponent => audioSource;
        
        void Awake()
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }
            
            audioSource.playOnAwake = playOnAwake;
            audioSource.loop = loop;
            audioSource.volume = volume;
        }
        
        void Start()
        {
            // 오디오 파일 로드
            if (!string.IsNullOrEmpty(audioFilePath))
            {
                LoadAudioFile(audioFilePath);
            }
        }
        
        /// <summary>
        /// 오디오 파일을 로드합니다.
        /// </summary>
        public void LoadAudioFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return;
            }
            
            // audioSource가 null이면 다시 가져오기 (에디터에서 Awake가 호출되지 않을 수 있음)
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
                if (audioSource == null)
                {
                    audioSource = gameObject.AddComponent<AudioSource>();
                }
                
                // AudioSource 설정
                audioSource.playOnAwake = playOnAwake;
                audioSource.loop = loop;
                audioSource.volume = volume;
            }
            
            audioFilePath = filePath;
            
            #if UNITY_EDITOR
            // Unity 에디터에서는 먼저 에셋으로 임포트 시도
            string assetPath = ImportAudioAsAsset(filePath);
            if (!string.IsNullOrEmpty(assetPath))
            {
                // 에셋으로 임포트 성공 시 에셋에서 로드
                AudioClip assetClip = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
                if (assetClip != null)
                {
                    audioClip = assetClip;
                    audioSource.clip = audioClip;
                    isLoaded = true;
                    return;
                }
            }
            
            // 에셋으로 임포트 실패 시 직접 로드
            StartCoroutine(LoadAudioClipCoroutine(filePath));
            #else
            // 런타임에서는 Resources나 StreamingAssets에서 로드
            #endif
        }
        
        #if UNITY_EDITOR
        /// <summary>
        /// 오디오 파일을 Unity 에셋으로 임포트합니다.
        /// </summary>
        private string ImportAudioAsAsset(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                    return null;
                
                // Assets/AudioMemos 폴더에 임포트
                string targetDir = "Assets/AudioMemos";
                if (!AssetDatabase.IsValidFolder(targetDir))
                {
                    AssetDatabase.CreateFolder("Assets", "AudioMemos");
                }
                
                // 파일명 가져오기
                string fileName = Path.GetFileName(filePath);
                string assetPath = Path.Combine(targetDir, fileName).Replace("\\", "/");
                
                // 이미 같은 이름의 에셋이 있는지 확인
                if (File.Exists(assetPath))
                {
                    // 파일이 변경되었는지 확인 (간단한 체크)
                    FileInfo sourceFile = new FileInfo(filePath);
                    FileInfo targetFile = new FileInfo(assetPath);
                    
                    // 파일 크기와 수정 시간 비교
                    if (sourceFile.Length == targetFile.Length && 
                        Math.Abs((sourceFile.LastWriteTime - targetFile.LastWriteTime).TotalSeconds) < 1)
                    {
                        // 동일한 파일이면 기존 에셋 사용
                        return assetPath;
                    }
                }
                
                // 파일 복사
                File.Copy(filePath, assetPath, true);
                
                // AssetDatabase 새로고침 (Refresh가 자동으로 임포트함)
                AssetDatabase.Refresh();
                
                // ImportAsset 호출 제거 - Refresh가 자동으로 임포트하므로 중복 호출은 Assertion 오류를 유발할 수 있음
                // AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.Default);
                
                return assetPath;
            }
            catch (Exception)
            {
                return null;
            }
        }
        #endif
        
        #if UNITY_EDITOR
        private System.Collections.IEnumerator LoadAudioClipCoroutine(string filePath)
        {
            // file:// URI로 변환
            string uri = ToFileUri(filePath);
            
            using (UnityEngine.Networking.UnityWebRequest www = UnityEngine.Networking.UnityWebRequestMultimedia.GetAudioClip(uri, GetAudioType(filePath)))
            {
                yield return www.SendWebRequest();
                
                if (www.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    audioClip = UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(www);
                    if (audioClip != null)
                    {
                        audioSource.clip = audioClip;
                        isLoaded = true;
                    }
                }
            }
        }
        
        private string ToFileUri(string path)
        {
            // \\server\share\path → file:////server/share/path
            if (path.StartsWith(@"\\")) { var s = path.Replace("\\", "/").TrimStart('/'); return "file:////" + s; }
            if (Path.IsPathRooted(path)) { var s = path.Replace("\\", "/"); return "file:///" + s; }
            var abs = Path.GetFullPath(path).Replace("\\", "/"); return "file:///" + abs;
        }
        
        private UnityEngine.AudioType GetAudioType(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            switch (ext)
            {
                case ".mp3":
                    return UnityEngine.AudioType.MPEG;
                case ".wav":
                    return UnityEngine.AudioType.WAV;
                case ".ogg":
                    return UnityEngine.AudioType.OGGVORBIS;
                case ".m4a":
                case ".aac":
                    return UnityEngine.AudioType.MPEG;
                default:
                    return UnityEngine.AudioType.UNKNOWN;
            }
        }
        #endif
        
        /// <summary>
        /// 오디오를 재생합니다.
        /// </summary>
        public void Play()
        {
            if (audioSource != null && audioSource.clip != null)
            {
                audioSource.Play();
            }
            else if (!isLoaded && !string.IsNullOrEmpty(audioFilePath))
            {
                // 아직 로드되지 않았으면 로드 후 재생
                LoadAudioFile(audioFilePath);
                // 재생은 로드 완료 후 수동으로 호출해야 함
            }
        }
        
        public void Stop()
        {
            if (audioSource != null) audioSource.Stop();
        }
        
        public void Pause()
        {
            if (audioSource != null) audioSource.Pause();
        }
        
        public void UnPause()
        {
            if (audioSource != null) audioSource.UnPause();
        }
        
        public bool IsPlaying()
        {
            return audioSource != null && audioSource.isPlaying;
        }
        
        void OnDestroy()
        {
            if (audioClip != null)
            {
#if UNITY_EDITOR
                DestroyImmediate(audioClip);
#else
                Destroy(audioClip);
#endif
            }
        }
    }
    
#if UNITY_EDITOR
    [CustomEditor(typeof(AudioMemoPlayer))]
    public class AudioMemoPlayerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            
            AudioMemoPlayer player = (AudioMemoPlayer)target;
            
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("오디오 재생 컨트롤", EditorStyles.boldLabel);
            
            EditorGUILayout.BeginHorizontal();
            
            GUI.enabled = player.AudioSourceComponent != null && player.AudioSourceComponent.clip != null;
            if (GUILayout.Button("▶ 재생", GUILayout.Height(30)))
            {
                player.Play();
            }
            
            GUI.enabled = player.IsPlaying();
            if (GUILayout.Button("■ 중지", GUILayout.Height(30)))
            {
                player.Stop();
            }
            
            GUI.enabled = player.IsPlaying();
            if (GUILayout.Button("⏸ 일시정지", GUILayout.Height(30)))
            {
                player.Pause();
            }
            
            GUI.enabled = player.AudioSourceComponent != null && !player.IsPlaying() && player.AudioSourceComponent.clip != null;
            if (GUILayout.Button("▶ 재개", GUILayout.Height(30)))
            {
                player.UnPause();
            }
            
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            
            if (!string.IsNullOrEmpty(player.audioFilePath))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("오디오 파일:", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(player.audioFilePath, EditorStyles.wordWrappedLabel);
                
                bool fileExists = File.Exists(player.audioFilePath);
                if (fileExists)
                    EditorGUILayout.HelpBox("✓ 오디오 파일이 존재합니다.", MessageType.Info);
                else
                    EditorGUILayout.HelpBox("✗ 오디오 파일을 찾을 수 없습니다.", MessageType.Warning);
            }
            
            if (player.AudioSourceComponent != null)
            {
                EditorGUILayout.Space();
                if (player.AudioSourceComponent.clip != null)
                {
                    EditorGUILayout.LabelField("로드된 오디오:", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField($"이름: {player.AudioSourceComponent.clip.name}");
                    EditorGUILayout.LabelField($"길이: {player.AudioSourceComponent.clip.length:F2}초");
                    EditorGUILayout.LabelField($"샘플레이트: {player.AudioSourceComponent.clip.frequency}Hz");
                    EditorGUILayout.LabelField($"채널: {player.AudioSourceComponent.clip.channels}");
                    
                    if (player.IsPlaying())
                    {
                        float currentTime = player.AudioSourceComponent.time;
                        float totalTime = player.AudioSourceComponent.clip.length;
                        EditorGUILayout.LabelField($"재생 시간: {currentTime:F2} / {totalTime:F2}초");
                        
                        float progress = totalTime > 0 ? currentTime / totalTime : 0f;
                        EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), progress, $"{progress * 100:F1}%");
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("오디오 클립이 로드되지 않았습니다.", MessageType.Warning);
                    
                    if (!string.IsNullOrEmpty(player.audioFilePath) && File.Exists(player.audioFilePath))
                    {
                        if (GUILayout.Button("오디오 파일 다시 로드", GUILayout.Height(25)))
                        {
                            player.LoadAudioFile(player.audioFilePath);
                        }
                    }
                }
            }
        }
    }
#endif
}

