using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ObjDropWatcher.ExportImport
{
    /// <summary>
    /// 메모의 마커와 패널 사이를 연결하는 동적 선을 관리하는 컴포넌트
    /// </summary>
    public class MemoLineConnector : MonoBehaviour
    {
        private GameObject markerObj;
        private GameObject panelObj;
        private MemoDesignConfig config;
        private Renderer lineRenderer;
        
        public void Initialize(GameObject marker, GameObject panel, MemoDesignConfig designConfig)
        {
            markerObj = marker;
            panelObj = panel;
            config = designConfig;
            
            // 선 색상 설정
            lineRenderer = GetComponent<Renderer>();
            if (lineRenderer != null)
            {
                // Unlit shader 사용 (더 안정적)
                Shader shader = Shader.Find("Unlit/Color");
                if (shader == null)
                {
                    // Unlit/Color가 없으면 Standard 사용
                    shader = Shader.Find("Standard");
                }
                
                if (shader != null)
                {
                    Material mat = new Material(shader);
                    mat.color = config.lineColor;
                    if (shader.name == "Standard")
                    {
                        mat.SetFloat("_Metallic", 0f);
                        mat.SetFloat("_Glossiness", 0.3f);
                    }
                    lineRenderer.material = mat;
                }
            }
            
            // 초기 업데이트
            UpdateLine();
        }
        
        private void Update()
        {
            if (markerObj != null && panelObj != null)
            {
                UpdateLine();
            }
        }
        
        private void UpdateLine()
        {
            if (markerObj == null || panelObj == null)
                return;
            
            // 마커와 패널의 월드 위치 가져오기
            Vector3 markerWorldPos = markerObj.transform.position;
            Vector3 panelWorldPos = panelObj.transform.position;
            
            // 선의 방향과 길이 계산
            Vector3 direction = panelWorldPos - markerWorldPos;
            float distance = direction.magnitude;
            
            if (distance < 0.001f)
                return;
            
            // 선의 중점 계산
            Vector3 lineCenter = (markerWorldPos + panelWorldPos) / 2f;
            
            // 선의 방향 (Y축 up)
            Vector3 upDirection = direction.normalized;
            
            // 현재 오브젝트의 부모를 기준으로 로컬 위치 계산
            Transform parent = transform.parent;
            if (parent != null)
            {
                lineCenter = parent.InverseTransformPoint(lineCenter);
            }
            
            // 선의 위치 설정
            transform.localPosition = lineCenter;
            
            // 선의 회전 설정 (Y축이 upDirection을 향하도록)
            if (upDirection != Vector3.zero)
            {
                Quaternion rotation = Quaternion.FromToRotation(Vector3.up, upDirection);
                transform.localRotation = rotation;
            }
            
            // 선의 크기 설정 (Cylinder 기본 높이 2, 반지름 0.5)
            // 높이: distance, 반지름: lineWidth / 2
            float lineWidth = config.lineWidth;
            transform.localScale = new Vector3(lineWidth, distance / 2f, lineWidth);
        }
    }
}



