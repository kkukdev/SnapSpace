using UnityEngine;

namespace ObjDropWatcher.ExportImport
{
    /// <summary>
    /// 메모 패널을 월드 Y 평면에 고정하고, 마커의 월드 X/Z 좌표 위에 유지시킵니다.
    /// parent의 회전과 상관없이 항상 XZ 평면에서 정면으로 보이도록 회전도 보정합니다.
    /// </summary>
    [ExecuteAlways]
    public class MemoPanelHeightLock : MonoBehaviour
    {
        [SerializeField]
        private Transform markerTransform;
        
        [SerializeField]
        private float markerYOffset = 0.0f;
        
        [SerializeField]
        private bool useAbsoluteWorldY = false;
        
        [SerializeField]
        private float absoluteWorldY = 2.0f;
        
        // 패널이 항상 유지해야 할 월드 rotation (XZ 평면에서 정면으로 보이도록)
        private static readonly Quaternion DesiredWorldRotation = Quaternion.Euler(90f, 0f, 0f);
        
        private void OnEnable()
        {
            ApplyPosition();
        }
        
        private void Update()
        {
            ApplyPosition();
        }
        
        private void OnValidate()
        {
            ApplyPosition();
        }
        
        /// <summary>
        /// 외부에서 초기화를 호출하여 잠금 설정을 구성합니다.
        /// </summary>
        public void Initialize(Transform marker, float defaultOffset, float? overrideWorldY)
        {
            markerTransform = marker;
            markerYOffset = defaultOffset;
            useAbsoluteWorldY = overrideWorldY.HasValue;
            if (overrideWorldY.HasValue)
            {
                absoluteWorldY = overrideWorldY.Value;
            }
            ApplyPosition();
        }
        
        private void ApplyPosition()
        {
            if (markerTransform == null)
                return;
            
            Vector3 markerWorldPos = markerTransform.position;
            Vector3 targetWorldPos = markerWorldPos;
            targetWorldPos.y = useAbsoluteWorldY ? absoluteWorldY : markerWorldPos.y + markerYOffset;
            
            transform.position = targetWorldPos;
            
            // 회전 보정: parent의 회전을 역으로 적용하여 항상 같은 방향 유지
            ApplyRotation();
        }
        
        /// <summary>
        /// 패널이 parent의 회전과 상관없이 항상 XZ 평면에서 정면으로 보이도록 회전을 보정합니다.
        /// </summary>
        private void ApplyRotation()
        {
            Transform parent = transform.parent;
            if (parent == null)
                return;
            
            // parent의 현재 월드 rotation을 가져옴
            Quaternion parentWorldRotation = parent.rotation;
            
            // 원하는 월드 rotation을 localRotation으로 변환
            // localRotation = Inverse(parent rotation) * desired world rotation
            transform.localRotation = Quaternion.Inverse(parentWorldRotation) * DesiredWorldRotation;
        }
    }
}

