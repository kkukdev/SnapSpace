using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ObjDropWatcher.ExportImport
{
    /// <summary>
    /// 메모 관련 유틸리티 클래스
    /// </summary>
    public static class MemoUtils
    {
        [Serializable]
        public class MemoData
        {
            public string type;
            public string anchor;
            public string content;
            public string source;
            public string file_path;
            public int file_size;
        }

        /// <summary>
        /// memo.txt 파일을 읽어서 파싱합니다.
        /// 파일 형식: [anchor]content 형태
        /// 예: [x:0.80,y:1.43,z:0.13]이용하
        /// </summary>
        public static MemoData[] ParseMemoFile(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                    return new MemoData[0];

                // 파일 읽기 (UTF-8 또는 CP949 인코딩 시도)
                string content = null;
                try
                {
                    content = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
                }
                catch (Exception)
                {
                    try
                    {
                        content = File.ReadAllText(filePath, System.Text.Encoding.GetEncoding(949)); // CP949
                    }
                    catch (Exception)
                    {
                        return new MemoData[0];
                    }
                }

                if (string.IsNullOrEmpty(content))
                    return new MemoData[0];

                content = content.Trim();
                if (string.IsNullOrEmpty(content))
                    return new MemoData[0];

                // 백엔드와 동일한 파싱 로직: 대괄호 패턴 찾기 [anchor]content
                List<MemoData> memos = new List<MemoData>();
                System.Text.RegularExpressions.Regex pattern = new System.Text.RegularExpressions.Regex(@"\[([^\]]+)\]");
                var matches = pattern.Matches(content);

                if (matches.Count == 0)
                {
                    // 대괄호가 없으면 전체 내용을 빈 anchor로 저장
                    if (!string.IsNullOrEmpty(content.Trim()))
                    {
                        memos.Add(new MemoData
                        {
                            type = "text",
                            anchor = "",
                            content = content.Trim(),
                            source = Path.GetFileName(filePath),
                            file_path = filePath,
                            file_size = (int)new FileInfo(filePath).Length
                        });
                    }
                }
                else
                {
                    // 각 대괄호 구간 처리
                    for (int i = 0; i < matches.Count; i++)
                    {
                        var match = matches[i];
                        string anchor = match.Groups[1].Value.Trim();

                        // 현재 대괄호의 끝 위치
                        int startPos = match.Index + match.Length;

                        // 다음 대괄호의 시작 위치 (마지막이면 파일 끝)
                        int endPos = (i + 1 < matches.Count) 
                            ? matches[i + 1].Index 
                            : content.Length;

                        // value 추출 (대괄호 다음부터 다음 대괄호 전까지)
                        string value = content.Substring(startPos, endPos - startPos).Trim();

                        // 같은 anchor가 이미 있으면 기존 content 뒤에 추가
                        var existingMemo = memos.FirstOrDefault(m => m.anchor == anchor);
                        if (existingMemo != null)
                        {
                            existingMemo.content += "\n\n" + value;
                        }
                        else
                        {
                            memos.Add(new MemoData
                            {
                                type = "text",
                                anchor = anchor,
                                content = value,
                                source = Path.GetFileName(filePath),
                                file_path = filePath,
                                file_size = (int)new FileInfo(filePath).Length
                            });
                        }
                    }
                }

                return memos.ToArray();
            }
            catch (Exception)
            {
                return new MemoData[0];
            }
        }

        /// <summary>
        /// OBJ 파일 경로에서 memo.txt 파일을 찾아서 파싱합니다.
        /// </summary>
        public static MemoData[] FindAndParseMemoFile(string objFilePath)
        {
            if (string.IsNullOrEmpty(objFilePath) || !File.Exists(objFilePath))
                return new MemoData[0];

            // OBJ 파일의 디렉토리에서 memo.txt 찾기
            string objDir = Path.GetDirectoryName(objFilePath);
            if (string.IsNullOrEmpty(objDir) || !Directory.Exists(objDir))
                return new MemoData[0];

            string memoFilePath = Path.Combine(objDir, "memo.txt");
            if (!File.Exists(memoFilePath))
                return new MemoData[0];

            return ParseMemoFile(memoFilePath);
        }

        /// <summary>
        /// Vector3를 anchor 문자열로 변환합니다.
        /// 예: Vector3(0.80f, 1.43f, 0.13f) -> "x:0.80,y:1.43,z:0.13"
        /// 정밀도를 높이기 위해 소수점 4자리까지 저장
        /// </summary>
        public static string Vector3ToAnchor(Vector3 position)
        {
            return $"x:{position.x:F4},y:{position.y:F4},z:{position.z:F4}";
        }
        
        /// <summary>
        /// anchor 문자열을 정규화합니다 (소수점 정밀도 통일).
        /// </summary>
        public static string NormalizeAnchor(string anchor)
        {
            if (string.IsNullOrEmpty(anchor))
                return anchor;
            
            try
            {
                Vector3 pos = ParseAnchor(anchor);
                return Vector3ToAnchor(pos);
            }
            catch
            {
                return anchor;
            }
        }

        /// <summary>
        /// anchor 문자열을 Vector3로 파싱합니다.
        /// 예: "x:0.80,y:1.43,z:0.13" -> Vector3(0.80f, 1.43f, 0.13f)
        /// </summary>
        public static Vector3 ParseAnchor(string anchor)
        {
            if (string.IsNullOrEmpty(anchor))
                return Vector3.zero;

            Vector3 result = Vector3.zero;
            
            try
            {
                // "x:0.80,y:1.43,z:0.13" 형식 파싱
                string[] parts = anchor.Split(',');
                foreach (string part in parts)
                {
                    string trimmed = part.Trim();
                    if (trimmed.StartsWith("x:", StringComparison.OrdinalIgnoreCase))
                    {
                        string value = trimmed.Substring(2).Trim();
                        if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x))
                        {
                            result.x = x;
                        }
                    }
                    else if (trimmed.StartsWith("y:", StringComparison.OrdinalIgnoreCase))
                    {
                        string value = trimmed.Substring(2).Trim();
                        if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y))
                        {
                            result.y = y;
                        }
                    }
                    else if (trimmed.StartsWith("z:", StringComparison.OrdinalIgnoreCase))
                    {
                        string value = trimmed.Substring(2).Trim();
                        if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z))
                        {
                            result.z = z;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // anchor 파싱 실패 시 zero 반환
            }
            
            return result;
        }

        /// <summary>
        /// memos를 메시 GameObject의 자식으로 생성합니다.
        /// 메모의 좌표는 메시의 로컬 좌표계를 사용합니다.
        /// </summary>
        public static void SpawnMemosAsChildren(GameObject parentObj, MemoData[] memos, float unitScale = 1000f)
        {
            SpawnMemosAsChildren(parentObj, memos, null, unitScale);
        }

        /// <summary>
        /// memos를 메시 GameObject의 자식으로 생성합니다. (Export된 Transform 정보 사용 가능)
        /// exportTransforms가 제공되면 anchor를 기준으로 매칭하여 Transform 정보를 적용합니다.
        /// </summary>
        public static void SpawnMemosAsChildren(GameObject parentObj, MemoData[] memos, Dictionary<string, (Vector3 position, Quaternion rotation, Vector3 scale)> exportTransforms = null, float unitScale = 1000f)
        {
            if (memos == null || memos.Length == 0 || parentObj == null)
                return;

            int memoCount = 0;
            foreach (var memo in memos)
            {
                if (memo == null)
                    continue;
                
                // type이 "text"인 경우만 표시
                if (memo.type != "text")
                    continue;
                
                // anchor 좌표 파싱 (스케일이 1일 때의 좌표, 즉 Unity 월드 좌표계 기준)
                Vector3 anchorPosition = ParseAnchor(memo.anchor);
                
                // RuntimeObjLoader는 OBJ 파일의 버텍스를 로드할 때 Z축을 뒤집습니다 (오른손→왼손 좌표계 변환)
                // 메모의 anchor 좌표도 같은 변환을 적용해야 OBJ 메시와 일치합니다.
                // OBJ 파일: (x, y, z) -> RuntimeObjLoader: (x, y, -z)
                anchorPosition.z = -anchorPosition.z;
                
                // Export된 Transform 정보가 있으면 사용, 없으면 기본값 사용
                Vector3 localPosition = anchorPosition;
                Quaternion localRotation = Quaternion.identity;
                Vector3 localScale = Vector3.one;
                
                if (exportTransforms != null && !string.IsNullOrEmpty(memo.anchor))
                {
                    // anchor를 키로 사용하여 Export된 Transform 정보 찾기
                    if (exportTransforms.TryGetValue(memo.anchor, out var transformInfo))
                    {
                        localPosition = transformInfo.position;
                        localRotation = transformInfo.rotation;
                        localScale = transformInfo.scale;
                    }
                }
                
                // 메모는 parent GameObject의 자식이므로 로컬 좌표계를 사용합니다.
                // 메모의 anchor 좌표와 스케일에 unitScale을 곱하여 적용합니다.
                // 예: anchor 좌표 (1m, 2m, 3m), unitScale = 1000 -> Unity 로컬 좌표 (1000m, 2000m, 3000m) = (1, 2, 3) * 1000
                if (exportTransforms == null || !exportTransforms.ContainsKey(memo.anchor))
                {
                    // Export된 Transform 정보가 없으면
                    // 메모의 anchor 좌표에 unitScale을 곱하여 적용
                    localPosition = anchorPosition * unitScale;
                }
                
                // 메모의 스케일에도 unitScale을 적용
                localScale = localScale * unitScale;
                
                // 메시 GameObject의 자식으로 3D 텍스트 생성 (로컬 좌표 사용)
                Create3DTextAsChild(parentObj, memo.content, localPosition, localRotation, localScale);
                memoCount++;
            }
        }

        /// <summary>
        /// 현재 사용 중인 메모 디자인 설정 (기본값: DefaultOrange)
        /// </summary>
        private static MemoDesignConfig _currentDesignConfig = MemoDesignConfig.Presets.DefaultOrange;
        
        /// <summary>
        /// 메모 디자인 설정을 변경합니다.
        /// </summary>
        public static void SetDesignConfig(MemoDesignConfig config)
        {
            if (config != null)
                _currentDesignConfig = config;
        }
        
        /// <summary>
        /// 현재 메모 디자인 설정을 가져옵니다.
        /// </summary>
        public static MemoDesignConfig GetDesignConfig()
        {
            return _currentDesignConfig;
        }

        /// <summary>
        /// 메시 GameObject의 자식으로 3D 텍스트를 생성합니다.
        /// 위치는 부모 객체의 로컬 좌표계를 사용합니다.
        /// </summary>
        public static void Create3DTextAsChild(GameObject parentObj, string text, Vector3 localPosition)
        {
            Create3DTextAsChild(parentObj, text, localPosition, Quaternion.identity, Vector3.one);
        }

        /// <summary>
        /// 메시 GameObject의 자식으로 3D 텍스트를 생성합니다. (Transform 정보 포함)
        /// 위치는 부모 객체의 로컬 좌표계를 사용합니다.
        /// </summary>
        public static void Create3DTextAsChild(GameObject parentObj, string text, Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
        {
            Create3DTextAsChild(parentObj, text, localPosition, localRotation, localScale, _currentDesignConfig);
        }
        
        /// <summary>
        /// 메시 GameObject의 자식으로 3D 메모를 생성합니다. (커스텀 디자인 설정 사용)
        /// 동그라미 마커 + 수직선 + 네모창 스타일
        /// 위치는 부모 객체의 로컬 좌표계를 사용합니다.
        /// </summary>
        public static void Create3DTextAsChild(GameObject parentObj, string text, Vector3 localPosition, Quaternion localRotation, Vector3 localScale, MemoDesignConfig designConfig)
        {
            if (designConfig == null)
                designConfig = MemoDesignConfig.Presets.DefaultOrange;
                
            try
            {
                // 메모 루트 오브젝트 생성 (마커와 패널의 부모)
                string objectName = $"Memo_{text.Substring(0, Math.Min(text.Length, designConfig.maxNameLength))}";
                GameObject memoRoot = new GameObject(objectName);
                #if UNITY_EDITOR
                Undo.RegisterCreatedObjectUndo(memoRoot, "Create 3D Memo");
                #endif
                
                // 부모 객체의 자식으로 설정
                memoRoot.transform.SetParent(parentObj.transform, false);
                
                // 로컬 Transform 설정 (부모 객체의 로컬 좌표계)
                memoRoot.transform.localPosition = localPosition;
                memoRoot.transform.localRotation = localRotation;
                memoRoot.transform.localScale = localScale;
                
                // 1. 동그라미 마커 생성 (좌표 위치)
                CreateMarker(memoRoot, designConfig);
                
                // 2. 수직선 생성 (마커에서 위로)
                CreateVerticalLine(memoRoot, designConfig);
                
                // 3. 네모창 생성 (선 끝에, 텍스트 크기에 맞게 동적 조정)
                GameObject panelObj = CreatePanel(memoRoot, designConfig, text);
                
                // 4. 텍스트 생성 (네모창 안에)
                CreateTextInPanel(panelObj, text, designConfig);
            }
            catch (Exception)
            {
                // 3D 메모 생성 실패 시 무시
            }
        }
        
        /// <summary>
        /// 동그라미 마커를 생성합니다.
        /// </summary>
        private static void CreateMarker(GameObject parent, MemoDesignConfig config)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Marker";
            marker.transform.SetParent(parent.transform, false);
            marker.transform.localPosition = Vector3.zero;
            marker.transform.localScale = Vector3.one * (config.markerRadius * 2f);
            
            // 마커 색상 설정
            Renderer renderer = marker.GetComponent<Renderer>();
            if (renderer != null)
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
                    mat.color = config.markerColor;
                    if (shader.name == "Standard")
                    {
                        mat.SetFloat("_Metallic", 0f);
                        mat.SetFloat("_Glossiness", 0.5f);
                    }
                    renderer.material = mat;
                }
            }
            
            #if UNITY_EDITOR
            Undo.RegisterCreatedObjectUndo(marker, "Create Marker");
            #endif
        }
        
        /// <summary>
        /// 수직선을 생성합니다.
        /// </summary>
        private static void CreateVerticalLine(GameObject parent, MemoDesignConfig config)
        {
            GameObject line = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            line.name = "Line";
            line.transform.SetParent(parent.transform, false);
            
            // Cylinder는 기본적으로 Y축이 up이므로 회전 불필요
            // 선의 중심을 마커 위로 이동 (Cylinder 기본 높이는 2이므로)
            float lineCenterY = config.lineHeight / 2f;
            line.transform.localPosition = new Vector3(0, lineCenterY, 0);
            
            // 회전 없음 (Cylinder는 이미 Y축이 up)
            line.transform.localRotation = Quaternion.identity;
            
            // 선의 크기 설정 (Cylinder 기본 높이 2, 반지름 0.5)
            // 높이: lineHeight, 반지름: lineWidth / 2
            line.transform.localScale = new Vector3(config.lineWidth, config.lineHeight / 2f, config.lineWidth);
            
            // 선 색상 설정
            Renderer renderer = line.GetComponent<Renderer>();
            if (renderer != null)
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
                    renderer.material = mat;
                }
            }
            
            #if UNITY_EDITOR
            Undo.RegisterCreatedObjectUndo(line, "Create Line");
            #endif
        }
        
        /// <summary>
        /// 네모창을 생성합니다. (텍스트 크기에 맞게 동적 조정)
        /// </summary>
        private static GameObject CreatePanel(GameObject parent, MemoDesignConfig config, string text)
        {
            // 텍스트 크기를 먼저 계산하여 패널 크기 결정
            Vector2 textSize = CalculateTextSize(text, config);
            
            // 패널 크기 = 텍스트 크기 + 여백 (양쪽)
            float panelWidth = textSize.x + config.panelPadding * 2f;
            float panelHeight = textSize.y + config.panelPadding * 2f;
            
            // 최소 크기 보장
            panelWidth = Mathf.Max(panelWidth, config.panelWidth);
            panelHeight = Mathf.Max(panelHeight, config.panelHeight);
            
            // 네모창 메인 (배경)
            GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
            panel.name = "Panel";
            panel.transform.SetParent(parent.transform, false);
            
            // 네모창 위치 (선 끝 위)
            float panelY = config.lineHeight + panelHeight / 2f;
            panel.transform.localPosition = new Vector3(0, panelY, 0);
            
            // 네모창이 카메라를 향하도록 회전 (Billboard 효과)
            panel.transform.localRotation = Quaternion.identity;
            
            // 네모창 크기 (동적으로 계산된 크기 사용)
            panel.transform.localScale = new Vector3(panelWidth, panelHeight, 1f);
            
            // 패널 크기를 config에 임시 저장 (테두리 생성 시 사용)
            // config는 참조 타입이므로 직접 수정 가능하지만, 안전하게 별도 변수 사용
            MemoDesignConfig tempConfig = new MemoDesignConfig
            {
                markerRadius = config.markerRadius,
                markerColor = config.markerColor,
                lineHeight = config.lineHeight,
                lineWidth = config.lineWidth,
                lineColor = config.lineColor,
                panelWidth = panelWidth,
                panelHeight = panelHeight,
                panelBackgroundColor = config.panelBackgroundColor,
                panelBorderColor = config.panelBorderColor,
                panelBorderWidth = config.panelBorderWidth,
                panelPadding = config.panelPadding,
                fontSize = config.fontSize,
                characterSize = config.characterSize,
                textColor = config.textColor,
                anchor = config.anchor,
                alignment = config.alignment,
                maxNameLength = config.maxNameLength
            };
            
            // 배경 색상 설정
            Renderer renderer = panel.GetComponent<Renderer>();
            if (renderer != null)
            {
                // Unlit/Color shader 사용 (불투명 배경)
                Shader shader = Shader.Find("Unlit/Color");
                if (shader == null)
                {
                    // Unlit/Color가 없으면 Unlit/Transparent 시도
                    shader = Shader.Find("Unlit/Transparent");
                    if (shader == null)
                    {
                        // 둘 다 없으면 Standard 사용
                        shader = Shader.Find("Standard");
                    }
                }
                
                if (shader != null)
                {
                    Material mat = new Material(shader);
                    mat.color = config.panelBackgroundColor;
                    
                    // Standard shader인 경우에만 추가 설정
                    if (shader.name == "Standard")
                    {
                        mat.SetFloat("_Metallic", 0f);
                        mat.SetFloat("_Glossiness", 0.1f);
                    }
                    
                    renderer.material = mat;
                }
            }
            
            // 테두리 생성 (동적 크기 사용)
            CreatePanelBorder(panel, tempConfig);
            
            #if UNITY_EDITOR
            Undo.RegisterCreatedObjectUndo(panel, "Create Panel");
            #endif
            
            return panel;
        }
        
        /// <summary>
        /// 텍스트의 예상 크기를 계산합니다.
        /// </summary>
        private static Vector2 CalculateTextSize(string text, MemoDesignConfig config)
        {
            if (string.IsNullOrEmpty(text))
                return new Vector2(config.panelWidth, config.panelHeight);
            
            // 텍스트 줄 수 계산
            string[] lines = text.Split('\n');
            int lineCount = lines.Length;
            
            // 가장 긴 줄 찾기
            int maxLineLength = 0;
            foreach (string line in lines)
            {
                if (line.Length > maxLineLength)
                    maxLineLength = line.Length;
            }
            
            // TextMesh의 문자 크기와 폰트 크기를 기반으로 크기 추정
            // characterSize는 월드 단위의 문자 크기
            // fontSize는 폰트의 포인트 크기
            float charWidth = config.characterSize * 0.6f; // 대략적인 문자 너비 (비율)
            float charHeight = config.characterSize * 1.2f; // 대략적인 문자 높이 (비율)
            
            // 텍스트 너비 = 가장 긴 줄의 문자 수 * 문자 너비
            float textWidth = maxLineLength * charWidth;
            
            // 텍스트 높이 = 줄 수 * 문자 높이
            float textHeight = lineCount * charHeight;
            
            // 최소 크기 보장
            textWidth = Mathf.Max(textWidth, 0.3f);
            textHeight = Mathf.Max(textHeight, 0.2f);
            
            return new Vector2(textWidth, textHeight);
        }
        
        /// <summary>
        /// 네모창 테두리를 생성합니다.
        /// </summary>
        private static void CreatePanelBorder(GameObject panel, MemoDesignConfig config)
        {
            // 상하좌우 4개의 테두리 생성
            string[] borderNames = { "Top", "Bottom", "Left", "Right" };
            Vector3[] positions = {
                new Vector3(0, config.panelHeight / 2f, -0.001f),  // Top
                new Vector3(0, -config.panelHeight / 2f, -0.001f), // Bottom
                new Vector3(-config.panelWidth / 2f, 0, -0.001f),  // Left
                new Vector3(config.panelWidth / 2f, 0, -0.001f)   // Right
            };
            Vector3[] scales = {
                new Vector3(config.panelWidth, config.panelBorderWidth, 1f),  // Top/Bottom
                new Vector3(config.panelWidth, config.panelBorderWidth, 1f),
                new Vector3(config.panelBorderWidth, config.panelHeight, 1f), // Left/Right
                new Vector3(config.panelBorderWidth, config.panelHeight, 1f)
            };
            
            for (int i = 0; i < 4; i++)
            {
                GameObject border = GameObject.CreatePrimitive(PrimitiveType.Quad);
                border.name = $"Border_{borderNames[i]}";
                border.transform.SetParent(panel.transform, false);
                border.transform.localPosition = positions[i];
                border.transform.localScale = scales[i];
                
                Renderer renderer = border.GetComponent<Renderer>();
                if (renderer != null)
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
                        mat.color = config.panelBorderColor;
                        if (shader.name == "Standard")
                        {
                            mat.SetFloat("_Metallic", 0f);
                            mat.SetFloat("_Glossiness", 0.3f);
                        }
                        renderer.material = mat;
                    }
                }
                
                #if UNITY_EDITOR
                Undo.RegisterCreatedObjectUndo(border, "Create Border");
                #endif
            }
        }
        
        /// <summary>
        /// 네모창 안에 텍스트를 생성합니다.
        /// </summary>
        private static void CreateTextInPanel(GameObject panelObj, string text, MemoDesignConfig config)
        {
            GameObject textObject = new GameObject("Text");
            textObject.transform.SetParent(panelObj.transform, false);
            // 패널보다 훨씬 앞에 위치 (Z-fighting 방지)
            textObject.transform.localPosition = new Vector3(0, 0, -0.2f);
            textObject.transform.localRotation = Quaternion.identity;
            textObject.transform.localScale = Vector3.one;
            
            // TextMesh 컴포넌트 추가 및 디자인 설정 적용
            TextMesh textMesh = textObject.AddComponent<TextMesh>();
            textMesh.text = text;
            textMesh.fontSize = config.fontSize;
            textMesh.characterSize = config.characterSize;
            textMesh.anchor = config.anchor;
            textMesh.alignment = config.alignment;
            textMesh.color = config.textColor;
            textMesh.richText = false;
            textMesh.fontStyle = FontStyle.Bold; // 볼드체로 가시성 향상
            
            // TextMesh 렌더러 설정 (렌더링 순서 보장)
            Renderer textRenderer = textObject.GetComponent<Renderer>();
            if (textRenderer != null)
            {
                // 렌더링 순서를 높여서 항상 앞에 렌더링되도록
                textRenderer.sortingOrder = 100;
                
                // Material 설정 (Edit 모드에서 material 누수 방지를 위해 sharedMaterial 사용)
                Material sharedMaterial = textRenderer.sharedMaterial;
                if (sharedMaterial != null)
                {
                    // 새 Material 인스턴스 생성 (원본 Material 수정 방지)
                    Material newMaterial = new Material(sharedMaterial);
                    
                    // 렌더 큐를 더 높게 설정하여 항상 앞에 렌더링
                    newMaterial.renderQueue = 4000; // Transparent보다 높은 큐
                    
                    // ZTest를 Always로 설정하여 항상 렌더링
                    newMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                    
                    // sharedMaterial에 할당 (Edit 모드에서 안전)
                    textRenderer.sharedMaterial = newMaterial;
                }
            }
            
            // 별도 레이어 설정 (선택사항, 필요시)
            // textObject.layer = LayerMask.NameToLayer("UI");
            
            #if UNITY_EDITOR
            Undo.RegisterCreatedObjectUndo(textObject, "Create Text");
            #endif
        }

        /// <summary>
        /// WatchConfig에서 unitScale을 가져옵니다.
        /// </summary>
        public static float GetUnitScale()
        {
            try
            {
                var configs = Resources.FindObjectsOfTypeAll<WatchConfig>();
                if (configs != null && configs.Length > 0)
                {
                    var config = configs[0];
                    if (config != null)
                    {
                        return config.unitScale;
                    }
                }
            }
            catch (Exception)
            {
                // unitScale 가져오기 실패 시 기본값 사용
            }
            
            return 1000f; // 기본값
        }
    }
}

