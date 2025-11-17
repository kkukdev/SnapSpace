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
                return null;
            }
            
            try
            {
                var pathInfo = obj.GetComponent<ObjPathInfo>();
                
                if (pathInfo == null)
                {
                    // GameObject가 파괴되었는지 확인
                    if (obj == null)
                    {
                        return null;
                    }
                    
                    pathInfo = obj.AddComponent<ObjPathInfo>();
                    
                    // AddComponent가 실패했는지 확인
                    if (pathInfo == null)
                    {
                        return null;
                    }
                    
                    // hideFlags를 명시적으로 설정하여 저장 가능하도록 함
                    #if UNITY_EDITOR
                    try
                    {
                        if (pathInfo != null)
                        {
                            pathInfo.hideFlags = HideFlags.None;
                        }
                    }
                    catch (System.Exception)
                    {
                        // hideFlags 설정 실패 시 무시
                    }
                    #endif
                }
                
                return pathInfo;
            }
            catch (System.Exception)
            {
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
                return null;
            }
            
            var pathInfo = obj.GetComponent<ObjPathInfo>();
            if (pathInfo == null)
            {
                return null;
            }
            
            string path = pathInfo.ObjFilePath;
            return path;
        }

        /// <summary>
        /// GameObject에 경로를 설정합니다.
        /// </summary>
        public static void SetPath(GameObject obj, string path)
        {
            if (obj == null)
            {
                return;
            }
            
            try
            {
                var pathInfo = GetOrAdd(obj);
                
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
                }
            }
            catch (System.Exception)
            {
                // SetPath 실패 시 무시
            }
        }
    }
}

