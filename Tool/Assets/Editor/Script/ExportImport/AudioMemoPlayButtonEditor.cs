using System;
using UnityEngine;
using UnityEditor;
using ObjDropWatcher.ExportImport;

namespace ObjDropWatcher.ExportImport
{
    /// <summary>
    /// Scene View에서 오디오 메모의 플레이 버튼을 클릭하여 오디오를 재생하는 Editor 스크립트
    /// </summary>
    [InitializeOnLoad]
    public static class AudioMemoPlayButtonEditor
    {
        private const string PLAY_BUTTON_PANEL_NAME = "PlayButtonPanel";
        
        static AudioMemoPlayButtonEditor()
        {
            // Scene View 이벤트 구독
            SceneView.duringSceneGui += OnSceneGUI;
        }
        
        /// <summary>
        /// Scene View에서 GUI 이벤트를 처리합니다.
        /// </summary>
        private static void OnSceneGUI(SceneView sceneView)
        {
            // 마우스 클릭 이벤트만 처리 (성능 최적화)
            Event currentEvent = Event.current;
            
            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0) // 왼쪽 마우스 버튼
            {
                HandlePlayButtonClick(currentEvent);
            }
        }
        
        /// <summary>
        /// 플레이 버튼 클릭을 처리합니다.
        /// </summary>
        private static void HandlePlayButtonClick(Event currentEvent)
        {
            // 마우스 위치에서 Ray 생성
            Ray ray = HandleUtility.GUIPointToWorldRay(currentEvent.mousePosition);
            
            // Raycast로 클릭한 오브젝트 감지
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit))
            {
                GameObject clickedObject = hit.collider.gameObject;
                
                // 클릭한 오브젝트가 플레이 버튼 패널인지 확인
                if (clickedObject.name == PLAY_BUTTON_PANEL_NAME)
                {
                    // 부모 오브젝트에서 AudioMemoPlayer 찾기
                    AudioMemoPlayer player = FindAudioMemoPlayerInParent(clickedObject.transform);
                    
                    if (player != null)
                    {
                        // 오디오 재생
                        player.Play();
                        
                        // 이벤트 소비 (다른 오브젝트 선택 방지)
                        currentEvent.Use();
                    }
                }
            }
        }
        
        /// <summary>
        /// 부모 오브젝트 계층에서 AudioMemoPlayer 컴포넌트를 찾습니다.
        /// </summary>
        private static AudioMemoPlayer FindAudioMemoPlayerInParent(Transform startTransform)
        {
            Transform current = startTransform;
            
            // 최대 10단계까지 부모를 탐색 (무한 루프 방지)
            int maxDepth = 10;
            int depth = 0;
            
            while (current != null && depth < maxDepth)
            {
                AudioMemoPlayer player = current.GetComponent<AudioMemoPlayer>();
                if (player != null)
                {
                    return player;
                }
                
                current = current.parent;
                depth++;
            }
            
            return null;
        }
    }
}

