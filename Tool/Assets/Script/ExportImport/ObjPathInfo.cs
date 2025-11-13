using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ObjDropWatcher.ExportImport
{
    /// <summary>
    /// GameObject에 원본 파일 경로를 저장하는 Component
    /// ObjDropWatcherWindow에서 스폰한 오브젝트의 경로를 추적하기 위해 사용됩니다.
    /// </summary>
    public class ObjPathInfo : MonoBehaviour
    {
        [SerializeField]
        private string _objFilePath = "";

        /// <summary>
        /// 원본 OBJ 파일 경로를 가져오거나 설정합니다.
        /// </summary>
        public string ObjFilePath
        {
            get { return _objFilePath; }
            set { _objFilePath = value; }
        }

        /// <summary>
        /// GameObject에서 ObjPathInfo Component를 가져오거나 추가합니다.
        /// </summary>
        public static ObjPathInfo GetOrAdd(GameObject obj)
        {
            if (obj == null)
            {
                Debug.LogError("[ObjPathInfo] GetOrAdd: GameObject가 null입니다.");
                return null;
            }
            
            try
            {
                Debug.Log($"[ObjPathInfo] GetOrAdd 시작: obj={obj?.name}, obj null: {obj == null}");
                
                var pathInfo = obj.GetComponent<ObjPathInfo>();
                Debug.Log($"[ObjPathInfo] GetComponent 결과: {pathInfo != null}");
                
                if (pathInfo == null)
                {
                    // GameObject가 파괴되었는지 확인
                    if (obj == null)
                    {
                        Debug.LogError("[ObjPathInfo] GetOrAdd: GameObject가 AddComponent 전에 파괴되었습니다.");
                        return null;
                    }
                    
                    Debug.Log($"[ObjPathInfo] AddComponent 호출 전: obj={obj.name}");
                    pathInfo = obj.AddComponent<ObjPathInfo>();
                    Debug.Log($"[ObjPathInfo] AddComponent 호출 후: pathInfo={pathInfo != null}");
                    
                    // AddComponent가 실패했는지 확인
                    if (pathInfo == null)
                    {
                        Debug.LogError($"[ObjPathInfo] GetOrAdd: AddComponent가 null을 반환했습니다. GameObject: {obj.name}");
                        return null;
                    }
                    
                    // hideFlags를 명시적으로 설정하여 저장 가능하도록 함
                    #if UNITY_EDITOR
                    try
                    {
                        if (pathInfo != null)
                        {
                            pathInfo.hideFlags = HideFlags.None;
                            Debug.Log($"[ObjPathInfo] hideFlags 설정 완료");
                        }
                    }
                    catch (System.Exception hideFlagsEx)
                    {
                        Debug.LogError($"[ObjPathInfo] hideFlags 설정 실패: {hideFlagsEx.Message}");
                    }
                    #endif
                    
                    // obj가 여전히 유효한지 확인
                    if (obj != null)
                    {
                        Debug.Log($"[ObjPathInfo] Component 추가됨: {obj.name}, InstanceID: {obj.GetInstanceID()}, pathInfo InstanceID: {pathInfo.GetInstanceID()}");
                    }
                    else
                    {
                        Debug.LogWarning("[ObjPathInfo] Component 추가 후 GameObject가 null이 되었습니다.");
                    }
                }
                else
                {
                    if (obj != null)
                    {
                        Debug.Log($"[ObjPathInfo] 기존 Component 사용: {obj.name}, InstanceID: {obj.GetInstanceID()}");
                    }
                }
                
                Debug.Log($"[ObjPathInfo] GetOrAdd 완료: pathInfo={pathInfo != null}");
                return pathInfo;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ObjPathInfo] GetOrAdd 실패: {ex.Message}\nStack trace: {ex.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// GameObject에서 경로를 가져옵니다.
        /// </summary>
        public static string GetPath(GameObject obj)
        {
            if (obj == null)
            {
                Debug.LogWarning("[ObjPathInfo] GetPath: GameObject가 null입니다.");
                return null;
            }
            
            var pathInfo = obj.GetComponent<ObjPathInfo>();
            if (pathInfo == null)
            {
                Debug.LogWarning($"[ObjPathInfo] GetPath: {obj.name}에 ObjPathInfo Component가 없습니다.");
                return null;
            }
            
            string path = pathInfo.ObjFilePath;
            Debug.Log($"[ObjPathInfo] GetPath: {obj.name} -> {path ?? "(null)"}");
            return path;
        }

        /// <summary>
        /// GameObject에 경로를 설정합니다.
        /// </summary>
        public static void SetPath(GameObject obj, string path)
        {
            if (obj == null)
            {
                Debug.LogError("[ObjPathInfo] SetPath: GameObject가 null입니다.");
                return;
            }
            
            try
            {
                string objName = obj != null ? obj.name : "(null)";
                Debug.Log($"[ObjPathInfo] SetPath 시작: GameObject={objName}, 경로={path}, obj null: {obj == null}");
                
                Debug.Log($"[ObjPathInfo] GetOrAdd 호출 전");
                var pathInfo = GetOrAdd(obj);
                Debug.Log($"[ObjPathInfo] GetOrAdd 호출 후 - pathInfo: {pathInfo != null}, obj: {obj != null}");
                
                if (pathInfo != null && obj != null)
                {
                    pathInfo.ObjFilePath = path;
                    #if UNITY_EDITOR
                    // 변경사항을 Unity에 알림 (DontSaveInEditor 플래그가 없을 때만)
                    try
                    {
                        // GameObject가 저장 가능한지 확인
                        if ((obj.hideFlags & HideFlags.DontSaveInEditor) == 0)
                        {
                            EditorUtility.SetDirty(obj);
                        }
                        
                        // Component가 저장 가능한지 확인
                        if ((pathInfo.hideFlags & HideFlags.DontSaveInEditor) == 0)
                        {
                            EditorUtility.SetDirty(pathInfo);
                        }
                    }
                    catch (System.Exception)
                    {
                        // Assertion 오류는 무시 (경로는 이미 저장됨)
                    }
                    #endif
                    Debug.Log($"[ObjPathInfo] SetPath 완료: {objName} -> {pathInfo.ObjFilePath}");
                }
                else
                {
                    Debug.LogError($"[ObjPathInfo] SetPath 실패: {objName}에 Component를 추가할 수 없습니다. (pathInfo: {pathInfo != null}, obj: {obj != null})");
                }
            }
            catch (System.Exception ex)
            {
                string objName = obj != null ? obj.name : "(null)";
                Debug.LogError($"[ObjPathInfo] SetPath 예외 발생: GameObject={objName}, 경로={path}\n오류: {ex.Message}\nStack trace: {ex.StackTrace}");
            }
        }
    }
}

