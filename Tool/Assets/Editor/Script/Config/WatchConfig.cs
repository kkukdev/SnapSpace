using UnityEngine;

[CreateAssetMenu(fileName = "WatchConfig", menuName = "Configs/WatchConfig")]
public class WatchConfig : ScriptableObject
{
    [Tooltip("감시할 루트 폴더 (UNC 가능: \\\\server\\share\\obj_drop)")]
    public string rootWatchDirectory;

    [Tooltip("하위 폴더까지 감시")]
    public bool includeSubdirectories = false;

    [Tooltip("복사/해제 지연 대비 (ms)")]
    public int scanDebounceMs = 800;

    [Tooltip("찾을 OBJ 패턴(쉼표 분리)")]
    public string objPatterns = "*.obj";

    [Tooltip("OBJ 단위 보정 배율 (예: mm→m이면 1000)")]
    public float unitScale = 1000f;
}
