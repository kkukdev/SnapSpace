using UnityEngine;

namespace ObjDropWatcher.ExportImport
{
    /// <summary>
    /// 메모 3D 디자인 설정을 관리하는 클래스
    /// 동그라미 마커 + 수직선 + 네모창 스타일
    /// </summary>
    [System.Serializable]
    public class MemoDesignConfig
    {
        [Header("마커 설정 (동그라미)")]
        [Tooltip("마커 반지름 (기본값: 0.05)")]
        public float markerRadius = 0.05f;
        
        [Tooltip("마커 색상 (기본값: 주황색)")]
        public Color markerColor = new Color32(255, 128, 0, 255); // 주황색
        
        [Header("선 설정 (수직선)")]
        [Tooltip("선 높이 (기본값: 0.5)")]
        public float lineHeight = 0.5f;
        
        [Tooltip("선 두께 (기본값: 0.01)")]
        public float lineWidth = 0.01f;
        
        [Tooltip("선 색상 (기본값: 주황색)")]
        public Color lineColor = new Color32(255, 128, 0, 255); // 주황색
        
        [Header("네모창 설정")]
        [Tooltip("네모창 너비 (기본값: 0.8)")]
        public float panelWidth = 0.8f;
        
        [Tooltip("네모창 높이 (기본값: 0.4)")]
        public float panelHeight = 0.4f;
        
        [Tooltip("네모창 배경 색상 (기본값: 밝은 초록색, 고가시성)")]
        public Color panelBackgroundColor = new Color32(128, 255, 128, 1); // 밝은 초록색, 불투명
        
        [Tooltip("네모창 테두리 색상 (기본값: 주황색)")]
        public Color panelBorderColor = new Color32(255, 153, 0, 255); // 주황색
        
        [Tooltip("네모창 테두리 두께 (기본값: 0.02)")]
        public float panelBorderWidth = 0.2f;
        
        [Tooltip("패널 내부 여백 (기본값: 0.1)")]
        public float panelPadding = 0.1f;
        
        [Header("텍스트 설정")]
        [Tooltip("텍스트 크기 (기본값: 21)")]
        public int fontSize = 21;
        
        [Tooltip("문자 크기 (기본값: 0.12)")]
        public float characterSize = 0.12f;
        
        [Tooltip("텍스트 색상 (기본값: 검은색, 고대비)")]
        public Color textColor = new Color32(26, 26, 26, 255); // 진한 검은색
        
        [Tooltip("텍스트 앵커 위치")]
        public TextAnchor anchor = TextAnchor.MiddleCenter;
        
        [Tooltip("텍스트 정렬 방식")]
        public TextAlignment alignment = TextAlignment.Center;
        
        [Header("고급 설정")]
        [Tooltip("게임 오브젝트 이름에 사용할 최대 텍스트 길이")]
        public int maxNameLength = 10;
        
        [Header("패널 높이 제어")]
        [Tooltip("패널을 특정 월드 Y 좌표에 고정할지 여부")]
        public bool lockPanelWorldY = false;
        
        [Tooltip("lockPanelWorldY가 true일 때 사용할 월드 Y 좌표")]
        public float fixedPanelWorldY = 5.0f;
        
        /// <summary>
        /// 기본 디자인 설정을 반환합니다. (주황색 마커 + 밝은 노란색 배경 + 검은색 텍스트)
        /// </summary>
        public static MemoDesignConfig Default => new MemoDesignConfig();
        
        /// <summary>
        /// 프리셋 디자인 설정들을 제공합니다.
        /// </summary>
        public static class Presets
        {
            /// <summary>
            /// 기본 주황색 디자인 (고대비, 가시성 우수)
            /// </summary>
            public static MemoDesignConfig DefaultOrange => Default;
            
            /// <summary>
            /// 빨간색 경고 스타일
            /// </summary>
            public static MemoDesignConfig RedWarning => new MemoDesignConfig
            {
                markerColor = new Color32(255, 0, 0, 255),
                lineColor = new Color32(255, 0, 0, 255),
                panelBackgroundColor = new Color32(102, 26, 26, 230),
                panelBorderColor = new Color32(255, 77, 77, 255),
                textColor = new Color32(255, 255, 255, 255)
            };
            
            /// <summary>
            /// 초록색 성공 스타일
            /// </summary>
            public static MemoDesignConfig GreenSuccess => new MemoDesignConfig
            {
                markerColor = new Color32(0, 255, 0, 255),
                lineColor = new Color32(0, 255, 0, 255),
                panelBackgroundColor = new Color32(26, 102, 26, 230),
                panelBorderColor = new Color32(77, 255, 77, 255),
                textColor = new Color32(255, 255, 255, 255)
            };
            
            /// <summary>
            /// 파란색 정보 스타일
            /// </summary>
            public static MemoDesignConfig BlueInfo => new MemoDesignConfig
            {
                markerColor = new Color32(0, 255, 255, 255),
                lineColor = new Color32(0, 255, 255, 255),
                panelBackgroundColor = new Color32(26, 51, 102, 230),
                panelBorderColor = new Color32(77, 153, 255, 255),
                textColor = new Color32(255, 255, 255, 255)
            };
            
            /// <summary>
            /// 노란색 강조 스타일
            /// </summary>
            public static MemoDesignConfig YellowHighlight => new MemoDesignConfig
            {
                markerColor = new Color32(255, 255, 0, 255),
                lineColor = new Color32(255, 255, 0, 255),
                panelBackgroundColor = new Color32(102, 77, 26, 230),
                panelBorderColor = new Color32(255, 204, 77, 255),
                textColor = new Color32(0, 0, 0, 255)
            };
        }
    }
}

