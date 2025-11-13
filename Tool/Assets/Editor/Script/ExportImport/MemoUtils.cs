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
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[MemoUtils] Failed to read memo file with UTF-8 and CP949: {ex.Message}");
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
            catch (Exception ex)
            {
                Debug.LogError($"[MemoUtils] Failed to parse memo file {filePath}: {ex.Message}");
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
            catch (Exception ex)
            {
                Debug.LogWarning($"[MemoUtils] Failed to parse anchor '{anchor}': {ex.Message}");
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
                
                // anchor 좌표 파싱 (OBJ 파일의 원본 좌표, 예: mm 단위)
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
                        Debug.Log($"[MemoUtils] Using exported Transform for memo with anchor '{memo.anchor}': position={localPosition}, rotation={localRotation.eulerAngles}, scale={localScale}");
                    }
                }
                
                // 메모는 메시 GameObject의 자식이므로 로컬 좌표계를 사용합니다.
                // 메시 GameObject에 unitScale (예: 1000)이 적용되어 있으므로,
                // 메모의 로컬 좌표는 파일의 원본 좌표(변환 후)를 그대로 사용하면 됩니다.
                // 메시 GameObject의 스케일이 자동으로 적용되어 올바른 위치에 표시됩니다.
                
                // 메시 GameObject의 자식으로 3D 텍스트 생성 (로컬 좌표 사용)
                Create3DTextAsChild(parentObj, memo.content, localPosition, localRotation, localScale);
                memoCount++;
            }
            
            if (memoCount > 0)
            {
                Debug.Log($"[MemoUtils] Spawned {memoCount} text memo(s) as children of mesh object");
            }
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
            try
            {
                // TextMesh를 사용하여 3D 텍스트 생성
                GameObject textObject = new GameObject($"Memo_{text.Substring(0, Math.Min(text.Length, 10))}");
                #if UNITY_EDITOR
                Undo.RegisterCreatedObjectUndo(textObject, "Create 3D Text Memo");
                #endif
                
                // 부모 객체의 자식으로 설정
                textObject.transform.SetParent(parentObj.transform, false);
                
                // 로컬 Transform 설정 (부모 객체의 로컬 좌표계)
                textObject.transform.localPosition = localPosition;
                textObject.transform.localRotation = localRotation;
                textObject.transform.localScale = localScale;
                
                // TextMesh 컴포넌트 추가
                TextMesh textMesh = textObject.AddComponent<TextMesh>();
                textMesh.text = text;
                textMesh.fontSize = 20;
                textMesh.characterSize = 0.1f; // 텍스트 크기 (부모 스케일과 함께 적용됨)
                textMesh.anchor = TextAnchor.MiddleCenter;
                textMesh.alignment = TextAlignment.Center;
                textMesh.color = Color.yellow; // 노란색으로 표시
                
                Debug.Log($"[MemoUtils] Created 3D text memo as child at local position {localPosition}, rotation {localRotation.eulerAngles}, scale {localScale}: {text}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MemoUtils] Failed to create 3D text memo as child: {ex.Message}");
            }
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
            catch (Exception ex)
            {
                Debug.LogWarning($"[MemoUtils] Failed to get unitScale from WatchConfig: {ex.Message}");
            }
            
            return 1000f; // 기본값
        }
    }
}

