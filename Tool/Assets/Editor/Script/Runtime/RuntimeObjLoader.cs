using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public static class RuntimeObjLoader
{
    // -------------------------
    // Internal data structures
    // -------------------------
    class MtlDef
    {
        public string name;
        public Color kd = Color.white;  // diffuse
        public Color ks = Color.black;  // specular
        public float ns = 0f;           // shininess
        public float alpha = 1f;        // d(1=opaque) / Tr(1-alpha)
        public string mapKdPath;        // albedo texture path (absolute or combined)
    }

    struct Idx { public int v, vt, vn; }

    class Face
    {
        public string mat;          // from 'usemtl'
        public List<Idx> poly = new();
    }

    static readonly Dictionary<string, Material> _matCache = new();
    static readonly Dictionary<string, Texture2D> _texCache = new();

    // -------------------------
    // Internal data structures for object/group handling
    // -------------------------
    class ObjectGroup
    {
        public string name;
        public List<Face> faces = new();
        public string currentMtl;
    }

    // -------------------------
    // Public entry
    // -------------------------
    public static GameObject LoadObj(string objPath, bool preserveOriginalCoordinates = false)
    {
        if (string.IsNullOrEmpty(objPath) || !File.Exists(objPath))
            throw new FileNotFoundException("OBJ not found", objPath);

        var ci = CultureInfo.InvariantCulture;
        var objDir = Path.GetDirectoryName(objPath);

        var V = new List<Vector3>();
        var VT = new List<Vector2>();
        var VN = new List<Vector3>();
        
        // 객체/그룹별로 faces를 분리하여 저장
        var objectGroups = new List<ObjectGroup>();
        ObjectGroup currentGroup = null;

        var mtlLibPaths = new List<string>();
        string currentMtl = null;

        // -------- Parse OBJ --------
        foreach (var raw in File.ReadLines(objPath))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;

            int sp = line.IndexOf(' ');
            string headRaw = (sp < 0) ? line : line[..sp];
            string tail = (sp < 0) ? "" : line[(sp + 1)..].Trim();
            string head = headRaw.ToLowerInvariant();

            switch (head)
            {
                case "mtllib":
                {
                    foreach (var token in SplitRespectQuotes(tail))
                    {
                        var p = token.Trim('"');
                        var full = Path.IsPathRooted(p) ? p : Path.Combine(objDir, p);
                        if (File.Exists(full)) mtlLibPaths.Add(full);
                    }
                    break;
                }
                case "usemtl":
                    currentMtl = tail.Trim().Trim('"');
                    if (currentGroup != null) currentGroup.currentMtl = currentMtl;
                    break;

                case "o":  // object 명령어
                case "g":  // group 명령어
                {
                    // 새로운 객체/그룹 시작
                    string groupName = string.IsNullOrEmpty(tail) ? "default" : tail.Trim();
                    currentGroup = new ObjectGroup { name = groupName, currentMtl = currentMtl };
                    objectGroups.Add(currentGroup);
                    break;
                }

                case "v":
                {
                    var t = tail.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    // 오른손→왼손 좌표계 변환: Z축 뒤집기 (좌우 반전 방지)
                    V.Add(new Vector3(float.Parse(t[0], ci), float.Parse(t[1], ci), -float.Parse(t[2], ci)));
                    break;
                }
                case "vt":
                {
                    var t = tail.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    float u = float.Parse(t[0], ci);
                    float v = (t.Length > 1) ? float.Parse(t[1], ci) : 0f;
                    // UV 좌표 원본 그대로 사용 (일부 OBJ는 뒤집을 필요 없음)
                    VT.Add(new Vector2(u, v));
                    break;
                }
                case "vn":
                {
                    var t = tail.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    // 오른손→왼손 좌표계 변환: Z축 뒤집기 (좌우 반전 방지)
                    VN.Add(new Vector3(float.Parse(t[0], ci), float.Parse(t[1], ci), -float.Parse(t[2], ci)));
                    break;
                }
                case "f":
                {
                    // 현재 그룹이 없으면 기본 그룹 생성
                    if (currentGroup == null)
                    {
                        currentGroup = new ObjectGroup { name = "default", currentMtl = currentMtl };
                        objectGroups.Add(currentGroup);
                    }
                    
                    var f = new Face { mat = currentGroup.currentMtl ?? currentMtl };
                    var tokens = tail.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var tok in tokens)
                    {
                        // vt 기본값 -1로(미존재 구분)
                        int v = 0, vt = -1, vn = -1;
                        var fields = tok.Split('/');
                        if (fields.Length >= 1 && !string.IsNullOrEmpty(fields[0])) v = ParseIndex(fields[0], V.Count);
                        if (fields.Length >= 2 && !string.IsNullOrEmpty(fields[1])) vt = ParseIndex(fields[1], VT.Count);
                        if (fields.Length >= 3 && !string.IsNullOrEmpty(fields[2])) vn = ParseIndex(fields[2], VN.Count);
                        f.poly.Add(new Idx { v = v, vt = vt, vn = vn });
                    }
                    if (f.poly.Count >= 3) currentGroup.faces.Add(f);
                    break;
                }
            }
        }
        
        // 객체/그룹이 없으면 모든 faces를 기본 그룹으로 처리 (하위 호환성)
        // 주의: currentGroup이 null이면 faces가 파싱되지 않았을 수 있음
        // 하지만 파싱 중에 currentGroup이 null이면 자동으로 생성되므로, 
        // objectGroups가 비어있다는 것은 faces도 없다는 의미일 수 있음

        // 전체 faces 수집 (로깅용)
        var allFaces = new List<Face>();
        foreach (var group in objectGroups)
        {
            allFaces.AddRange(group.faces);
        }
        
        var facesWithMat = allFaces.FindAll(f => !string.IsNullOrEmpty(f.mat));
        int totalFaces = allFaces.Count;
        
        // usemtl 사용된 메터리얼 이름 목록
        var usedMaterials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in allFaces)
        {
            if (!string.IsNullOrEmpty(f.mat))
                usedMaterials.Add(f.mat);
        }

        // -------- Parse MTL(s) --------
        var mtlDict = new Dictionary<string, MtlDef>(StringComparer.OrdinalIgnoreCase);

        if (mtlLibPaths.Count == 0)
        {
            foreach (var m in Directory.GetFiles(objDir, "*.mtl", SearchOption.TopDirectoryOnly))
                ParseMtlFile(m, mtlDict);
        }
        else
        {
            foreach (var m in mtlLibPaths) ParseMtlFile(m, mtlDict);
        }

        // -------- Fallback material when no 'usemtl' --------
        bool anyUseMtl = allFaces.Exists(f => !string.IsNullOrEmpty(f.mat));
        if (!anyUseMtl && mtlDict.Count > 0)
        {
            string fallback = PickFallbackMaterialName(mtlDict);
            foreach (var group in objectGroups)
            {
                foreach (var f in group.faces) f.mat = fallback;
            }
        }

        // -------- Build Mesh (submeshes by material) --------
        // 모든 그룹의 faces를 하나의 메시로 통합 (기존 동작 유지)
        var outVerts = new List<Vector3>();
        var outUVs = new List<Vector2>();
        var outNorms = new List<Vector3>();

        var submeshTris = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        var matOrder = new List<string>();

        void EnsureMat(string m)
        {
            var key = string.IsNullOrEmpty(m) ? "_default" : m;
            if (!submeshTris.ContainsKey(key))
            {
                submeshTris[key] = new List<int>();
                matOrder.Add(key);
            }
        }

        // 모든 그룹의 faces를 하나로 통합
        foreach (var group in objectGroups)
        {
            foreach (var face in group.faces)
            {
                EnsureMat(face.mat);
                var tris = submeshTris[string.IsNullOrEmpty(face.mat) ? "_default" : face.mat];

                // fan-triangulation: (0, i, i+1)
                // Z축 뒤집기로 인해 와인딩 순서 반전 필요
                for (int i = 1; i < face.poly.Count - 1; i++)
                {
                    // 순서 반전: [0, i+1, i]
                    var tri = new[] { face.poly[0], face.poly[i + 1], face.poly[i] };
                    foreach (var idx in tri)
                    {
                        int ni = outVerts.Count;
                        outVerts.Add(V[idx.v]);

                        // UV 안전 처리
                        if (idx.vt >= 0 && idx.vt < VT.Count) outUVs.Add(VT[idx.vt]);
                        else outUVs.Add(Vector2.zero);

                        // Normal 안전 처리
                        if (idx.vn >= 0 && idx.vn < VN.Count) outNorms.Add(VN[idx.vn]);
                        else outNorms.Add(Vector3.zero);

                        tris.Add(ni);
                    }
                }
            }
        }

        // -------- Pivot Adjustment (align lowest Y to 0) --------
        // preserveOriginalCoordinates가 true이면 원본 좌표 시스템을 유지 (pivot adjustment 비활성화)
        if (!preserveOriginalCoordinates && outVerts.Count > 0)
        {
            float minY = float.PositiveInfinity;
            for (int i = 0; i < outVerts.Count; i++)
                if (outVerts[i].y < minY) minY = outVerts[i].y;

            if (!Mathf.Approximately(minY, 0f) && !float.IsInfinity(minY))
            {
                var offset = new Vector3(0f, -minY, 0f);
                for (int i = 0; i < outVerts.Count; i++)
                    outVerts[i] += offset;
            }
        }

        var go = new GameObject(Path.GetFileNameWithoutExtension(objPath));
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();

        var mesh = new Mesh
        {
            indexFormat = (outVerts.Count > 65000)
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16
        };
        mesh.SetVertices(outVerts);
        if (outUVs.Count == outVerts.Count) mesh.SetUVs(0, outUVs);
        if (outNorms.Count == outVerts.Count) mesh.SetNormals(outNorms);
        else mesh.RecalculateNormals();

        mesh.subMeshCount = matOrder.Count;
        for (int i = 0; i < matOrder.Count; i++)
        {
            var triCount = submeshTris[matOrder[i]].Count;
            mesh.SetTriangles(submeshTris[matOrder[i]], i, true);
        }

        mesh.RecalculateBounds();
        mf.sharedMesh = mesh;

        var mats = new Material[matOrder.Count];
        for (int i = 0; i < matOrder.Count; i++)
        {
            var matName = matOrder[i];
            mats[i] = CreateUnityMaterial(matName, mtlDict);
            
            // 메터리얼이 어떤 MTL 정의를 사용하는지 확인
            
            #if UNITY_EDITOR
            SafeSetDirty(mats[i]);
            #endif
        }
        
        mr.sharedMaterials = mats;
        
        // Unity 에디터에서 즉시 반영되도록 강제 업데이트
        #if UNITY_EDITOR
        SafeSetDirty(mr);
        SafeSetDirty(mf);
        SafeSetDirty(go);
        UnityEditor.SceneView.RepaintAll();
        #endif
        
        // 메터리얼 할당 확인 및 최종 검증
        for (int i = 0; i < mats.Length; i++)
        {
            if (mats[i] != null)
            {
                var hasTex = mats[i].mainTexture != null;
                var mainTex = mats[i].GetTexture("_MainTex");
                var texName = hasTex ? $"{mats[i].mainTexture.name} ({mats[i].mainTexture.width}x{mats[i].mainTexture.height})" : "NONE";
                // 메터리얼이 제대로 설정되었는지 최종 확인
            }
        }

        return go;
    }

    // -------------------------
    // Helpers
    // -------------------------
    static int ParseIndex(string token, int count)
    {
        int x = int.Parse(token, CultureInfo.InvariantCulture);
        if (x > 0) return x - 1;
        if (x < 0) return count + x;
        return 0;
    }

    static void ParseMtlFile(string mtlPath, Dictionary<string, MtlDef> dict)
    {
        try
        {
            var ci = CultureInfo.InvariantCulture;
            MtlDef cur = null;
            var dir = Path.GetDirectoryName(mtlPath);

            foreach (var raw in File.ReadLines(mtlPath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                int sp = line.IndexOf(' ');
                string headRaw = (sp < 0) ? line : line[..sp];
                string tail = (sp < 0) ? "" : line[(sp + 1)..].Trim();
                string head = headRaw.ToLowerInvariant();

                switch (head)
                {
                    case "newmtl":
                        cur = new MtlDef { name = tail.Trim().Trim('"') };
                        dict[cur.name] = cur;
                        break;

                    case "kd":
                    {
                        if (cur == null) break;
                        var t = tail.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                        cur.kd = new Color32((byte)(float.Parse(t[0], ci) * 255), (byte)(float.Parse(t[1], ci) * 255), (byte)(float.Parse(t[2], ci) * 255), (byte)(cur.alpha * 255));
                        break;
                    }
                    case "ks":
                    {
                        if (cur == null) break;
                        var t = tail.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                        cur.ks = new Color32((byte)(float.Parse(t[0], ci) * 255), (byte)(float.Parse(t[1], ci) * 255), (byte)(float.Parse(t[2], ci) * 255), 255);
                        break;
                    }
                    case "ns":
                        if (cur != null) cur.ns = float.Parse(tail, ci);
                        break;

                    case "d":
                        if (cur != null) { cur.alpha = float.Parse(tail, ci); cur.kd = new Color32((byte)(cur.kd.r * 255), (byte)(cur.kd.g * 255), (byte)(cur.kd.b * 255), (byte)(cur.alpha * 255)); }
                        break;

                    case "tr":
                        if (cur != null) { cur.alpha = 1f - float.Parse(tail, ci); cur.kd = new Color32((byte)(cur.kd.r * 255), (byte)(cur.kd.g * 255), (byte)(cur.kd.b * 255), (byte)(cur.alpha * 255)); }
                        break;

                    case "map_kd":
                    {
                        if (cur == null) break;
                        var tokens = SplitRespectQuotes(tail);
                        var last = tokens.Count > 0 ? tokens[^1].Trim('"') : tail.Trim('"');
                        
                        // 경로 정규화 (백슬래시/슬래시 통일)
                        last = last.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                        
                        string finalPath;
                        if (Path.IsPathRooted(last))
                        {
                            // 절대 경로 (UNC 포함)
                            finalPath = last;
                        }
                        else
                        {
                            // 상대 경로 - MTL 파일의 디렉토리 기준
                            if (string.IsNullOrEmpty(dir))
                            {
                                finalPath = last; // 경고만 하고 원본 경로 사용
                            }
                            else
                            {
                                finalPath = Path.Combine(dir, last);
                            }
                        }
                        
                        // 경로 정규화 (.. 처리 등)
                        // UNC 경로는 GetFullPath가 제대로 작동하지 않을 수 있음
                        bool isUnc = finalPath.StartsWith(@"\\");
                        if (isUnc)
                        {
                            // UNC 경로는 그대로 사용 (정규화는 제한적)
                            finalPath = finalPath.Replace('/', '\\');
                        }
                        else
                        {
                            try
                            {
                                finalPath = Path.GetFullPath(finalPath);
                            }
                            catch
                            {
                                // GetFullPath 실패 시 원본 경로 사용
                            }
                        }
                        
                        cur.mapKdPath = finalPath;
                        break;
                    }
                }
            }
        }
        catch (Exception)
        {
            // MTL 파싱 실패 시 무시
        }
    }

    static List<string> SplitRespectQuotes(string s)
    {
        var list = new List<string>();
        if (string.IsNullOrEmpty(s)) return list;

        bool inQ = false;
        var cur = new System.Text.StringBuilder();
        foreach (var ch in s)
        {
            if (ch == '"') { inQ = !inQ; cur.Append(ch); continue; }
            if (char.IsWhiteSpace(ch) && !inQ)
            {
                if (cur.Length > 0) { list.Add(cur.ToString()); cur.Clear(); }
            }
            else cur.Append(ch);
        }
        if (cur.Length > 0) list.Add(cur.ToString());
        return list;
    }

    static string PickFallbackMaterialName(Dictionary<string, MtlDef> dict)
    {
        // 1순위: map_Kd가 있고 실제 파일이 존재하는 재질
        foreach (var kv in dict)
        {
            var m = kv.Value;
            if (!string.IsNullOrEmpty(m.mapKdPath) && File.Exists(m.mapKdPath))
                return kv.Key;
        }
        // 2순위: 임의의 첫 재질
        foreach (var kv in dict) return kv.Key;
        return "_default";
    }

    // -------------------------
    // Helpers for Unity Editor
    // -------------------------
    static void SafeSetDirty(UnityEngine.Object obj)
    {
        if (obj == null) return;
        #if UNITY_EDITOR
        try
        {
            // DontSaveInEditor 플래그가 있으면 SetDirty 호출하지 않음 (assertion 오류 방지)
            if ((obj.hideFlags & HideFlags.DontSaveInEditor) == 0)
            {
                UnityEditor.EditorUtility.SetDirty(obj);
            }
        }
        catch (System.Exception)
        {
            // SetDirty 실패 시 무시 (이미 파괴된 오브젝트 등)
        }
        #endif
    }

    // -------------------------
    // Material / Texture
    // -------------------------
    static Material CreateUnityMaterial(string matName, Dictionary<string, MtlDef> mtlDict)
    {
        string key = $"__std__{matName}";
        if (_matCache.TryGetValue(key, out var cached))
        {
            // 캐시된 메터리얼이 텍스처를 가지고 있는지 확인
            if (cached != null && cached.mainTexture != null)
            {
                return cached;
            }
            else if (cached != null)
            {
                _matCache.Remove(key);
            }
        }

        // URP용 셰이더 사용 (Built-in의 Standard는 URP에서 작동하지 않음)
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            // URP 셰이더가 없으면 대체 시도
            shader = Shader.Find("Standard");
            if (shader == null)
            {
                shader = Shader.Find("Legacy Shaders/Diffuse");
                if (shader == null) shader = Shader.Find("Diffuse");
            }
        }

        var std = new Material(shader)
        {
            name = string.IsNullOrEmpty(matName) ? "Default" : matName
        };

        // Defaults (URP/Lit 속성 사용)
        std.color = Color.white;
        std.SetFloat("_Smoothness", 0.3f);  // URP에서는 _Glossiness 대신 _Smoothness
        std.SetFloat("_Metallic", 0.0f);

        if (!string.IsNullOrEmpty(matName) && mtlDict.TryGetValue(matName, out var m))
        {
            // MTL 기본 색상 설정 (텍스처가 없을 때 사용)
            std.color = m.kd;

            // 투명도 너무 낮아 완전 투명되는 경우 보정
            if (m.alpha < 0.05f) m.alpha = 1f;
            
            // Rough approximations from Ns/Ks (텍스처 유무와 관계없이 설정)
            float smooth = Mathf.Clamp01(m.ns / 1000f);
            std.SetFloat("_Smoothness", Mathf.Lerp(0.0f, 0.9f, smooth));  // URP에서는 _Smoothness 사용
            float ksAvg = (m.ks.r + m.ks.g + m.ks.b) / 3f;
            std.SetFloat("_Metallic", Mathf.Clamp01(ksAvg));

            if (!string.IsNullOrEmpty(m.mapKdPath))
            {
                var tex = LoadTextureSRGB(m.mapKdPath);
                if (tex != null && tex != Texture2D.whiteTexture && tex.width > 0 && tex.height > 0)
                {
                    // 텍스처 유효성 재확인
                    try
                    {
                        var testPixel = tex.GetPixel(0, 0);
                        
                        // Standard 셰이더의 모든 텍스처 슬롯 명시적으로 설정
                        std.SetTexture("_MainTex", tex);
                        std.mainTexture = tex; // 호환성을 위해 둘 다 설정
                        
                        // 텍스처가 있을 때는 색상을 흰색으로 설정하여 텍스처가 제대로 보이도록 함
                        std.color = new Color32(255, 255, 255, (byte)(m.alpha * 255));
                        
                        // 텍스처 타일링 설정
                        std.SetTextureScale("_MainTex", Vector2.one);
                        std.SetTextureOffset("_MainTex", Vector2.zero);
                        
                    // 투명도 설정 (URP 방식)
                    if (m.alpha < 0.999f)
                    {
                        // URP/Lit의 Transparent Surface Type 설정
                        std.SetFloat("_Surface", 1);  // 0=Opaque, 1=Transparent
                        std.SetFloat("_Blend", 0);    // 0=Alpha, 1=Premultiply, 2=Additive, 3=Multiply
                        std.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                        std.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        std.SetInt("_ZWrite", 0);
                        std.SetInt("_AlphaClip", 0);
                        std.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                        std.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                        std.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    }
                    else
                    {
                        // 불투명일 때는 기본 렌더 큐 사용
                        std.SetFloat("_Surface", 0);  // Opaque
                        std.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
                    }
                        
                        // Unity 에디터에서 즉시 반영
                        #if UNITY_EDITOR
                        SafeSetDirty(std);
                        #endif
                    }
                    catch (Exception)
                    {
                        // 텍스처가 무효하면 색상만 사용
                        std.color = new Color32((byte)(m.kd.r * 255), (byte)(m.kd.g * 255), (byte)(m.kd.b * 255), (byte)(m.alpha * 255));
                    }
                }
                else
                {
                    // 텍스처가 없을 때만 MTL의 diffuse 색상 사용
                    std.color = new Color32((byte)(m.kd.r * 255), (byte)(m.kd.g * 255), (byte)(m.kd.b * 255), (byte)(m.alpha * 255));
                    
                    // 투명도 설정 (URP 방식)
                    if (m.alpha < 0.999f)
                    {
                        std.SetFloat("_Surface", 1);  // Transparent
                        std.SetFloat("_Blend", 0);    // Alpha
                        std.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                        std.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        std.SetInt("_ZWrite", 0);
                        std.SetInt("_AlphaClip", 0);
                        std.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                        std.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                        std.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    }
                    else
                    {
                        std.SetFloat("_Surface", 0);  // Opaque
                        std.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
                    }
                }
            }
            else
            {
                // 텍스처 경로가 없을 때는 MTL의 diffuse 색상 사용
                std.color = new Color32((byte)(m.kd.r * 255), (byte)(m.kd.g * 255), (byte)(m.kd.b * 255), (byte)(m.alpha * 255));

                if (m.alpha < 0.999f)
                {
                    std.SetFloat("_Surface", 1);  // Transparent
                    std.SetFloat("_Blend", 0);    // Alpha
                    std.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    std.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    std.SetInt("_ZWrite", 0);
                    std.SetInt("_AlphaClip", 0);
                    std.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    std.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                    std.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                }
                else
                {
                    std.SetFloat("_Surface", 0);  // Opaque
                    std.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
                }
            }
        }

        _matCache[key] = std;
        
        return std;
    }

    static Texture2D LoadTextureSRGB(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }
        
        if (_texCache.TryGetValue(path, out var t))
        {
            if (t != null && t != Texture2D.whiteTexture && t.width > 0 && t.height > 0)
            {
                // 캐시된 텍스처가 유효한지 확인
                try
                {
                    var test = t.GetPixel(0, 0);
                    return t;
                }
                catch
                {
                    // 텍스처가 무효하면 캐시에서 제거
                    _texCache.Remove(path);
                    if (t != null) UnityEngine.Object.DestroyImmediate(t);
                }
            }
        }

        try
        {
            Texture2D tex = null;
            
            // 1) 로컬/네트워크 파일 직접 시도
            string actualPath = path;
            if (!File.Exists(actualPath))
            {
                // 경로가 없으면 파일명만으로 찾기 시도
                var fileName = Path.GetFileName(actualPath);
                var dir = Path.GetDirectoryName(actualPath);
                
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    // 같은 디렉토리에서 파일명으로 검색
                    var found = Directory.GetFiles(dir, fileName, SearchOption.TopDirectoryOnly);
                    if (found.Length > 0)
                    {
                        actualPath = found[0];
                    }
                    else
                    {
                        // 대소문자 구분 없이 검색
                        var allFiles = Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly);
                        var match = Array.Find(allFiles, f => 
                            string.Equals(Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase));
                        if (match != null)
                        {
                            actualPath = match;
                        }
                    }
                }
            }
            
            if (File.Exists(actualPath))
            {
                var bytes = File.ReadAllBytes(actualPath);
                if (bytes == null || bytes.Length == 0)
                {
                    return null;
                }

                // LoadImage는 자동으로 크기를 결정하므로 크기를 지정하지 않음
                tex = new Texture2D(1, 1, TextureFormat.RGBA32, true, true);
                
                if (!tex.LoadImage(bytes, false))
                {
                    UnityEngine.Object.DestroyImmediate(tex);
                    return null;
                }

                // 텍스처가 제대로 로드되었는지 확인
                if (tex.width <= 0 || tex.height <= 0)
                {
                    UnityEngine.Object.DestroyImmediate(tex);
                    return null;
                }
            }
            else
            {
                // 2) file:// URI를 통한 로드 (UNC 포함)
                string uri = ToFileUri(path);
                
                using var req = UnityWebRequestTexture.GetTexture(uri);
                var op = req.SendWebRequest();
                
                // Unity 에디터에서 동기 대기 - 최대 10초 타임아웃
                float timeout = 10f;
                float elapsed = 0f;
                while (!op.isDone && elapsed < timeout)
                {
                    System.Threading.Thread.Sleep(10);
                    elapsed += 0.01f;
                }

                if (!op.isDone)
                {
                    return null;
                }

                if (req.result == UnityWebRequest.Result.Success)
                {
                    tex = DownloadHandlerTexture.GetContent(req);
                    if (tex == null || tex.width <= 0 || tex.height <= 0)
                    {
                        return null;
                    }
                }
                else
                {
                    return null;
                }
            }

            if (tex == null) return null;

            // 텍스처 설정
            tex.name = Path.GetFileName(path);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            tex.anisoLevel = 1;
            
            // GPU에 업로드 - 여러 번 시도하여 확실히 업로드
            tex.Apply(true, false); // mipmap 생성, GPU 업로드
            
            // 텍스처가 실제로 유효한지 픽셀 읽기로 확인
            try
            {
                var pixel = tex.GetPixel(0, 0);
                if (pixel.a == 0 && pixel.r == 0 && pixel.g == 0 && pixel.b == 0)
                {
                    // 검은색/투명 픽셀은 정상일 수 있으므로 다른 위치도 확인
                    var midPixel = tex.GetPixel(tex.width / 2, tex.height / 2);
                }
            }
            catch (Exception)
            {
                UnityEngine.Object.DestroyImmediate(tex);
                return null;
            }

            _texCache[path] = tex;
            return tex;
        }
        catch (Exception)
        {
            return null;
        }
    }

    static string ToFileUri(string p)
    {
        // \\server\share\path → file:////server/share/path
        if (p.StartsWith(@"\\"))
        {
            var s = p.Replace("\\", "/").TrimStart('/');
            return "file:////" + s; // 4개의 슬래시 + server/...
        }

        // C:\path → file:///C:/path
        if (Path.IsPathRooted(p))
        {
            var s = p.Replace("\\", "/");
            return "file:///" + s;
        }

        // 상대경로는 현재 디렉토리 기준
        var abs = Path.GetFullPath(p).Replace("\\", "/");
        return "file:///" + abs;
    }
}
