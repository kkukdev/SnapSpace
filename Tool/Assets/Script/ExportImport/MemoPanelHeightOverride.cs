using UnityEngine;

namespace ObjDropWatcher.ExportImport
{
    /// <summary>
    /// 하위에서 생성되는 메모 패널의 월드 Y 좌표를 오버라이드할 수 있는 컴포넌트
    /// </summary>
    [ExecuteAlways]
    public class MemoPanelHeightOverride : MonoBehaviour
    {
        [Tooltip("이 값이 true일 때만 메모 패널에 World Y 오버라이드를 적용합니다.")]
        public bool applyOverride = true;
        
        [Tooltip("메모 패널이 고정될 월드 Y 좌표")]
        public float targetWorldY = 2.0f;
    }
}

