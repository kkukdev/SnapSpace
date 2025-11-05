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
    // Public entry
    // -------------------------
    public static GameObject LoadObj(string objPath)
    {
        if (string.IsNullOrEmpty(objPath) || !File.Exists(objPath))
            throw new FileNotFoundException("OBJ not found", objPath);

        var ci = CultureInfo.InvariantCulture;
        var objDir = Path.GetDirectoryName(objPath);

        var V = new List<Vector3>();
        var VT = new List<Vector2>();
        var VN = new List<Vector3>();
        var faces = new List<Face>();

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
                    Debug.Log($"[OBJ] usemtl: '{currentMtl}'");
                    break;

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
                    var f = new Face { mat = currentMtl };
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
                    if (f.poly.Count >= 3) faces.Add(f);
                    break;
                }
            }
        }

        var facesWithMat = faces.FindAll(f => !string.IsNullOrEmpty(f.mat));
        Debug.Log($"[OBJ] Total faces: {faces.Count}, mtllib count: {mtlLibPaths.Count}, faces with usemtl: {facesWithMat.Count}");
        
        // usemtl 사용된 메터리얼 이름 목록
        var usedMaterials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in faces)
        {
            if (!string.IsNullOrEmpty(f.mat))
                usedMaterials.Add(f.mat);
        }
        Debug.Log($"[OBJ] Unique materials used in faces: [{string.Join(", ", usedMaterials)}]");

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
        bool anyUseMtl = faces.Exists(f => !string.IsNullOrEmpty(f.mat));
        if (!anyUseMtl && mtlDict.Count > 0)
        {
            string fallback = PickFallbackMaterialName(mtlDict);
            foreach (var f in faces) f.mat = fallback;
            Debug.Log($"[OBJ] No usemtl → applied fallback material '{fallback}' to all faces.");
        }

        // -------- Build Mesh (submeshes by material) --------
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
                Debug.Log($"[Submesh] Added material '{key}' as submesh index {matOrder.Count - 1}");
            }
        }

        foreach (var face in faces)
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
            Debug.Log($"[Submesh] Submesh[{i}] '{matOrder[i]}' → {triCount / 3} triangles");
        }

        mesh.RecalculateBounds();
        mf.sharedMesh = mesh;

        var mats = new Material[matOrder.Count];
        Debug.Log($"[Mesh] Creating {matOrder.Count} materials in order: [{string.Join(", ", matOrder)}]");
        for (int i = 0; i < matOrder.Count; i++)
        {
            var matName = matOrder[i];
            mats[i] = CreateUnityMaterial(matName, mtlDict);
            
            // 메터리얼이 어떤 MTL 정의를 사용하는지 확인
            if (mtlDict.TryGetValue(matName, out var mtlDef))
            {
                Debug.Log($"[Mesh] Material[{i}] '{matName}' → MTL texture: {mtlDef.mapKdPath ?? "NONE"}");
            }
            else
            {
                Debug.LogWarning($"[Mesh] Material[{i}] '{matName}' → MTL definition NOT FOUND");
            }
            
            #if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(mats[i]);
            #endif
        }
        
        mr.sharedMaterials = mats;
        
        // Unity 에디터에서 즉시 반영되도록 강제 업데이트
        #if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(mr);
        UnityEditor.EditorUtility.SetDirty(mf);
        UnityEditor.EditorUtility.SetDirty(go);
        UnityEditor.SceneView.RepaintAll();
        #endif

        Debug.Log($"[Mesh] submeshes={mesh.subMeshCount}, mats={mats.Length}, order=[{string.Join(", ", matOrder)}]");
        Debug.Log($"[UV] outUVs={outUVs.Count}, VT(src)={VT.Count}");
        
        // 메터리얼 할당 확인 및 최종 검증
        for (int i = 0; i < mats.Length; i++)
        {
            if (mats[i] != null)
            {
                var hasTex = mats[i].mainTexture != null;
                var mainTex = mats[i].GetTexture("_MainTex");
                var texName = hasTex ? $"{mats[i].mainTexture.name} ({mats[i].mainTexture.width}x{mats[i].mainTexture.height})" : "NONE";
                var mainTexName = mainTex != null ? $"{mainTex.name} ({mainTex.width}x{mainTex.height})" : "NONE";
                Debug.Log($"[Mesh] Material[{i}] '{mats[i].name}' texture: {texName}, _MainTex: {mainTexName}, shader: {mats[i].shader.name}, color: {mats[i].color}");
                
                // 메터리얼이 제대로 설정되었는지 최종 확인
                if (hasTex && mats[i].mainTexture.width > 0 && mats[i].mainTexture.height > 0)
                {
                    Debug.Log($"[Mesh] Material[{i}] texture validated successfully");
                }
                else if (!hasTex)
                {
                    Debug.LogWarning($"[Mesh] Material[{i}] has no texture - will use color only");
                }
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
            
            Debug.Log($"[MTL] Parsing MTL file: '{mtlPath}', directory: '{dir}'");

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
                        cur.kd = new Color(float.Parse(t[0], ci), float.Parse(t[1], ci), float.Parse(t[2], ci), cur.alpha);
                        break;
                    }
                    case "ks":
                    {
                        if (cur == null) break;
                        var t = tail.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                        cur.ks = new Color(float.Parse(t[0], ci), float.Parse(t[1], ci), float.Parse(t[2], ci), 1f);
                        break;
                    }
                    case "ns":
                        if (cur != null) cur.ns = float.Parse(tail, ci);
                        break;

                    case "d":
                        if (cur != null) { cur.alpha = float.Parse(tail, ci); cur.kd.a = cur.alpha; }
                        break;

                    case "tr":
                        if (cur != null) { cur.alpha = 1f - float.Parse(tail, ci); cur.kd.a = cur.alpha; }
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
                                Debug.LogWarning($"[MTL] map_kd: MTL directory is null, cannot resolve relative path: {last}");
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
                                Debug.LogWarning($"[MTL] Path.GetFullPath failed for: {finalPath}");
                            }
                        }
                        
                        cur.mapKdPath = finalPath;
                        Debug.Log($"[MTL] map_kd parsed: '{tail}' → '{last}' (rel: {!Path.IsPathRooted(last)}, dir: '{dir}') → '{finalPath}' (exists: {File.Exists(finalPath)}, UNC: {isUnc})");
                        break;
                    }
                }
            }

            Debug.Log($"[MTL] Parsed '{Path.GetFileName(mtlPath)}' → materials: {dict.Count}");
        foreach (var kv in dict)
        {
            Debug.Log($"[MTL] - Material '{kv.Key}' → texture: {(string.IsNullOrEmpty(kv.Value.mapKdPath) ? "NONE" : kv.Value.mapKdPath)}");
        }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[MTL] Parse failed: {mtlPath}\n{e}");
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
                Debug.Log($"[MAT] Using cached material '{matName}' with texture");
                return cached;
            }
            else if (cached != null)
            {
                Debug.LogWarning($"[MAT] Cached material '{matName}' has no texture, recreating...");
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
                Debug.LogError("[MAT] URP/Lit and Standard shader not found! Using Diffuse as fallback.");
                shader = Shader.Find("Legacy Shaders/Diffuse");
                if (shader == null) shader = Shader.Find("Diffuse");
            }
        }
        
        Debug.Log($"[MAT] Using shader: {(shader != null ? shader.name : "NULL")}");

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
            if (!string.IsNullOrEmpty(m.mapKdPath))
                Debug.Log($"[MAT] {std.name} map_Kd → {m.mapKdPath}");
            else
                Debug.Log($"[MAT] {std.name} has NO map_Kd (color only)");

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
                        std.color = new Color(1f, 1f, 1f, m.alpha);
                        
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
                        UnityEditor.EditorUtility.SetDirty(std);
                        #endif
                        
                        Debug.Log($"[MAT] Applied texture to {std.name}: {m.mapKdPath}, mainTexture={(std.mainTexture != null ? $"{std.mainTexture.name} ({std.mainTexture.width}x{std.mainTexture.height})" : "NULL")}, _MainTex={std.GetTexture("_MainTex")?.name ?? "NULL"}, color={std.color}");
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[MAT] Texture validation failed for {std.name}: {m.mapKdPath}\n{e}");
                        // 텍스처가 무효하면 색상만 사용
                        std.color = new Color(m.kd.r, m.kd.g, m.kd.b, m.alpha);
                    }
                }
                else
                {
                    // 텍스처가 없을 때만 MTL의 diffuse 색상 사용
                    std.color = new Color(m.kd.r, m.kd.g, m.kd.b, m.alpha);
                    
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
                    
                    Debug.LogWarning($"[MAT] Failed to load texture for {std.name}: {m.mapKdPath}, using color instead");
                }
            }
            else
            {
                // 텍스처 경로가 없을 때는 MTL의 diffuse 색상 사용
                std.color = new Color(m.kd.r, m.kd.g, m.kd.b, m.alpha);

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
        
        // 최종 확인 로그
        if (std.mainTexture != null)
        {
            Debug.Log($"[MAT] Final check - Material '{std.name}' has texture: {std.mainTexture.name} ({std.mainTexture.width}x{std.mainTexture.height}), Color: {std.color}");
        }
        else
        {
            Debug.LogWarning($"[MAT] Final check - Material '{std.name}' has NO texture, Color: {std.color}");
        }
        
        return std;
    }

    static Texture2D LoadTextureSRGB(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogWarning("[Texture] Path is null or empty");
            return null;
        }
        
        Debug.Log($"[Texture] Attempting to load: '{path}' (exists: {File.Exists(path)})");
        
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
                        Debug.Log($"[Texture] Found alternative path: '{actualPath}' (original: '{path}')");
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
                            Debug.Log($"[Texture] Found case-insensitive match: '{actualPath}' (original: '{path}')");
                        }
                    }
                }
            }
            
            if (File.Exists(actualPath))
            {
                var bytes = File.ReadAllBytes(actualPath);
                if (bytes == null || bytes.Length == 0)
                {
                    Debug.LogWarning($"[Texture] File is empty: {actualPath}");
                    return null;
                }

                // LoadImage는 자동으로 크기를 결정하므로 크기를 지정하지 않음
                tex = new Texture2D(1, 1, TextureFormat.RGBA32, true, true);
                
                if (!tex.LoadImage(bytes, false))
                {
                    Debug.LogWarning($"[Texture] LoadImage failed: {actualPath}");
                    UnityEngine.Object.DestroyImmediate(tex);
                    return null;
                }

                // 텍스처가 제대로 로드되었는지 확인
                if (tex.width <= 0 || tex.height <= 0)
                {
                    Debug.LogWarning($"[Texture] Invalid texture dimensions: {tex.width}x{tex.height}");
                    UnityEngine.Object.DestroyImmediate(tex);
                    return null;
                }
            }
            else
            {
                // 2) file:// URI를 통한 로드 (UNC 포함)
                string uri = ToFileUri(path);
                Debug.Log($"[Texture] Trying URI: {uri}");
                
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
                    Debug.LogWarning($"[Texture] Timeout loading: {uri}");
                    return null;
                }

                if (req.result == UnityWebRequest.Result.Success)
                {
                    tex = DownloadHandlerTexture.GetContent(req);
                    if (tex == null || tex.width <= 0 || tex.height <= 0)
                    {
                        Debug.LogWarning($"[Texture] Invalid texture from URI: {uri}");
                        return null;
                    }
                }
                else
                {
                    Debug.LogWarning($"[Texture] Load failed via URI: {uri} (result: {req.result}, error: {req.error})");
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
            catch (Exception e)
            {
                Debug.LogWarning($"[Texture] Texture validation failed: {path}\n{e}");
                UnityEngine.Object.DestroyImmediate(tex);
                return null;
            }

            _texCache[path] = tex;
            Debug.Log($"[Texture] Loaded successfully: {path} ({tex.width}x{tex.height}), format: {tex.format}, mipmap: {tex.mipmapCount}, valid: {tex != null}");
            return tex;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Texture] Load failed: {path}\n{e}");
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
