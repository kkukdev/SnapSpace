using UnityEngine;

[CreateAssetMenu(fileName = "WatchConfig", menuName = "Configs/WatchConfig")]
public class WatchConfig : ScriptableObject
{
    [Tooltip("API 서버 URL (예: http://localhost:8000)")]
    public string apiServerUrl;

    [Tooltip("그룹 목록 조회 API 엔드포인트 (예: /api/v1/groups/)")]
    public string groupsEndpoint = "/api/v1/groups/";

    [Tooltip("그룹의 스캔 목록 조회 API 엔드포인트 (예: /api/v1/groups/{group_id}/scans)")]
    public string groupScansEndpoint = "/api/v1/groups/{group_id}/scans";

    [Tooltip("프로젝트 루트 경로 (비어있으면 Unity 프로젝트 루트 자동 사용)")]
    public string projectRoot = "";

    [Tooltip("하위 폴더까지 감시")]
    public bool includeSubdirectories = false;

    [Tooltip("복사/해제 지연 대비 (ms)")]
    public int scanDebounceMs = 800;

    [Tooltip("찾을 OBJ 패턴(쉼표 분리)")]
    public string objPatterns = "*.obj";

    [Tooltip("OBJ 단위 보정 배율 (예: mm→m이면 1000)")]
    public float unitScale = 1000f;
}
