using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace aki_lua87.AvatarUtils.Editor
{
    [CustomEditor(typeof(ColorMaskTextureGenerator))]
    public class ColorMaskTextureGeneratorEditor : UnityEditor.Editor
    {
        private const int PreviewMaxSize = 256;

        // ── インスペクタープレビュー ───────────────────────────────────
        private Texture2D _maskPreview;
        private Texture2D _overlayPreview;

        private Texture2D _prevSourceTex;
        private Color _prevColor = new Color(-1, -1, -1, -1);
        private float _prevTolerance = -1f;
        private bool _prevMatchIsWhite;
        private bool _prevUseGradient;

        // スポイト用にキャッシュしたプレビュー解像度のピクセル配列
        private Color[] _cachedSourcePixels;
        private int _cachedPixelW;
        private int _cachedPixelH;

        private bool _eyedropperActive;
        private bool _showSourcePreview = true;
        private bool _showMaskPreview = true;


        // ── Scene プレビュー（AnimationMode 流用）────────────────────
        private bool _scenePreviewActive;
        private Material _scenePreviewMaterial;
        private Texture2D _scenePreviewTexture;
        // プレビュー起動時のマテリアル/プロパティを記録（再起動判定用）
        private Material _scenePreviewTargetMat;
        private string _scenePreviewTargetProp;

        // liltoon が持つ主要テクスチャプロパティ（フォールバック用）
        private static readonly string[] FallbackPropertyNames =
        {
            "_MainTex", "_MainTex2nd", "_MainTex3rd",
            "_EmissionMap", "_Emission2ndMap",
            "_EmissionBlendMask", "_Emission2ndBlendMask",
            "_MasksTex",
            "_ShadowColorTex", "_Shadow2ndColorTex", "_Shadow3rdColorTex",
            "_RimColorTex",
            "_MatCapTex", "_MatCap2ndTex",
            "_MatCapMask", "_MatCap2ndMask",
            "_NormalMap", "_NormalMap2nd",
            "_OutlineTex", "_OutlineMask",
        };

        private static readonly string[] FallbackPropertyLabels =
        {
            "メインテクスチャ (_MainTex)",
            "第2メインテクスチャ (_MainTex2nd)",
            "第3メインテクスチャ (_MainTex3rd)",
            "エミッション (_EmissionMap)",
            "第2エミッション (_Emission2ndMap)",
            "エミッションマスク (_EmissionBlendMask)",
            "第2エミッションマスク (_Emission2ndBlendMask)",
            "マスクRGBA (_MasksTex)",
            "影カラー1 (_ShadowColorTex)",
            "影カラー2 (_Shadow2ndColorTex)",
            "影カラー3 (_Shadow3rdColorTex)",
            "リムカラー (_RimColorTex)",
            "MatCap (_MatCapTex)",
            "MatCap 2nd (_MatCap2ndTex)",
            "MatCapマスク (_MatCapMask)",
            "MatCap 2ndマスク (_MatCap2ndMask)",
            "ノーマルマップ (_NormalMap)",
            "第2ノーマルマップ (_NormalMap2nd)",
            "アウトライン (_OutlineTex)",
            "アウトラインマスク (_OutlineMask)",
        };

        private void OnDisable()
        {
            StopScenePreview();
            DestroyPreviews();
        }

        private void DestroyPreviews()
        {
            if (_maskPreview != null) { DestroyImmediate(_maskPreview); _maskPreview = null; }
            if (_overlayPreview != null) { DestroyImmediate(_overlayPreview); _overlayPreview = null; }
            _cachedSourcePixels = null;
        }

        private bool IsPreviewDirty(ColorMaskTextureGenerator t) =>
            t.SourceTexture != _prevSourceTex ||
            t.TargetColor != _prevColor ||
            t.Tolerance != _prevTolerance ||
            t.MatchIsWhite != _prevMatchIsWhite ||
            t.UseGradient != _prevUseGradient;

        private void RebuildPreview(ColorMaskTextureGenerator t)
        {
            DestroyPreviews();

            _prevSourceTex = t.SourceTexture;
            _prevColor = t.TargetColor;
            _prevTolerance = t.Tolerance;
            _prevMatchIsWhite = t.MatchIsWhite;
            _prevUseGradient = t.UseGradient;

            if (t.SourceTexture == null) return;

            int srcW = t.SourceTexture.width;
            int srcH = t.SourceTexture.height;
            float scale = Mathf.Min(1f, (float)PreviewMaxSize / Mathf.Max(srcW, srcH));
            int pw = Mathf.Max(1, Mathf.RoundToInt(srcW * scale));
            int ph = Mathf.Max(1, Mathf.RoundToInt(srcH * scale));

            _maskPreview = new Texture2D(pw, ph, TextureFormat.RGBA32, false);
            _overlayPreview = new Texture2D(pw, ph, TextureFormat.RGBA32, false);

            // Read/Write 不要で取得（スポイト用にキャッシュも兼ねる）
            Color[] srcPx = ColorMaskTextureGenerator.ReadPixelsSafe(t.SourceTexture, pw, ph);
            _cachedSourcePixels = srcPx;
            _cachedPixelW = pw;
            _cachedPixelH = ph;
            Color[] maskPx = new Color[pw * ph];
            Color[] overlayPx = new Color[pw * ph];
            const float norm = 1.7320508f;

            for (int y = 0; y < ph; y++)
            {
                for (int x = 0; x < pw; x++)
                {
                    // srcPx はすでにプレビュー解像度 (pw×ph) で読み込み済み
                    Color p = srcPx[y * pw + x];
                    float dr = p.r - t.TargetColor.r;
                    float dg = p.g - t.TargetColor.g;
                    float db = p.b - t.TargetColor.b;
                    float d = Mathf.Sqrt(dr * dr + dg * dg + db * db) / norm;

                    float matchVal;
                    if (t.UseGradient)
                    {
                        float tt = t.Tolerance > 0f
                            ? Mathf.Clamp01(1f - d / t.Tolerance)
                            : (d < 0.0001f ? 1f : 0f);
                        matchVal = Mathf.SmoothStep(0f, 1f, tt);
                    }
                    else
                    {
                        matchVal = d <= t.Tolerance ? 1f : 0f;
                    }

                    float outVal = t.MatchIsWhite ? matchVal : 1f - matchVal;
                    maskPx[y * pw + x] = new Color(outVal, outVal, outVal, 1f);
                    overlayPx[y * pw + x] = new Color(1f, 0.35f, 0f, matchVal * 0.62f);
                }
            }

            _maskPreview.SetPixels(maskPx);
            _maskPreview.Apply();
            _overlayPreview.SetPixels(overlayPx);
            _overlayPreview.Apply();
        }

        public override void OnInspectorGUI()
        {
            var t = (ColorMaskTextureGenerator)target;

            // インスペクタープレビュー再構築（パラメーター変化時）
            if (IsPreviewDirty(t))
            {
                RebuildPreview(t);
                // Scene プレビューが起動中なら、マテリアル/プロパティ変更は再起動、それ以外はテクスチャ更新
                if (_scenePreviewActive)
                {
                    if (t.TargetMaterial != _scenePreviewTargetMat || t.TargetPropertyName != _scenePreviewTargetProp)
                        StartScenePreview(t);   // 再起動
                    else
                        UpdateScenePreviewTexture(t);
                }
            }

            // ── ソーステクスチャ ─────────────────────────────────────
            EditorGUILayout.LabelField("ソーステクスチャ", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            var newSrc = (Texture2D)EditorGUILayout.ObjectField(
                "テクスチャ", t.SourceTexture, typeof(Texture2D), false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(t, "Change Source Texture");
                t.SourceTexture = newSrc;
                _eyedropperActive = false;
                EditorUtility.SetDirty(t);
            }

            // ソース画像プレビュー（折りたたみ可能・フルwidth）
            if (t.SourceTexture != null)
            {
                EditorGUILayout.BeginHorizontal();
                _showSourcePreview = EditorGUILayout.Foldout(_showSourcePreview, "テクスチャプレビュー", true);
                EditorGUILayout.LabelField(
                    $"{t.SourceTexture.width} × {t.SourceTexture.height}",
                    EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
                EditorGUILayout.EndHorizontal();

                if (_showSourcePreview)
                {
                    float asp = (float)t.SourceTexture.width / t.SourceTexture.height;
                    float maxW = EditorGUIUtility.currentViewWidth - 24f;
                    const float maxH = 480f;
                    float dispW = maxW;
                    float dispH = dispW / asp;
                    if (dispH > maxH) { dispH = maxH; dispW = maxH * asp; }

                    Rect srcRect = GUILayoutUtility.GetRect(dispW, dispH, GUILayout.ExpandWidth(false));

                    GUI.DrawTexture(srcRect, t.SourceTexture, ScaleMode.ScaleToFit);
                    if (_overlayPreview != null)
                        GUI.DrawTexture(srcRect, _overlayPreview, ScaleMode.ScaleToFit);
                    if (_eyedropperActive)
                        DrawBorder(srcRect, Color.yellow, 2f);

                    HandleEyedropperInput(srcRect, t);

                    EditorGUILayout.LabelField(
                        _eyedropperActive ? "クリックで色を取得 / 右クリックでキャンセル" : "スポイトモード時にクリックして色を取得",
                        EditorStyles.centeredGreyMiniLabel);
                }
            }

            GUILayout.Space(8);

            // ── 色設定 ────────────────────────────────────────────────
            EditorGUILayout.LabelField("色設定", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            var newCol = EditorGUILayout.ColorField("ターゲットカラー", t.TargetColor);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(t, "Change Target Color");
                t.TargetColor = newCol;
                EditorUtility.SetDirty(t);
            }

            GUI.enabled = t.SourceTexture != null && _cachedSourcePixels != null;
            GUI.backgroundColor = _eyedropperActive ? Color.yellow : Color.white;
            if (GUILayout.Button("スポイト", GUILayout.Width(70)))
                _eyedropperActive = !_eyedropperActive;
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (_eyedropperActive)
                EditorGUILayout.HelpBox("テクスチャ上をクリックして色を取得（右クリックでキャンセル）", MessageType.Info);

            EditorGUI.BeginChangeCheck();
            float newTol = EditorGUILayout.Slider("色誤差 (Tolerance)", t.Tolerance, 0f, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(t, "Change Tolerance");
                t.Tolerance = newTol;
                EditorUtility.SetDirty(t);
            }

            GUILayout.Space(8);

            // ── マスクオプション ──────────────────────────────────────
            EditorGUILayout.LabelField("マスクオプション", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            bool newMatchIsWhite = EditorGUILayout.Toggle("一致部分を白にする", t.MatchIsWhite);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(t, "Change Match Is White");
                t.MatchIsWhite = newMatchIsWhite;
                EditorUtility.SetDirty(t);
            }
            EditorGUILayout.LabelField(
                t.MatchIsWhite ? "   一致 → 白 / 非一致 → 黒" : "   一致 → 黒 / 非一致 → 白",
                EditorStyles.miniLabel);

            GUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            bool newUseGrad = EditorGUILayout.Toggle("グラデーション", t.UseGradient);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(t, "Change Use Gradient");
                t.UseGradient = newUseGrad;
                EditorUtility.SetDirty(t);
            }
            if (t.UseGradient)
                EditorGUILayout.HelpBox("色の距離に応じて白黒を滑らかにブレンドします（Tolerance が境界幅）", MessageType.None);

            GUILayout.Space(8);

            // ── マスクプレビュー ──────────────────────────────────────
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("マスクプレビュー", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            _showMaskPreview = GUILayout.Toggle(_showMaskPreview, "表示", GUILayout.Width(50));
            EditorGUILayout.EndHorizontal();

            if (_showMaskPreview && _maskPreview != null)
            {
                float asp2 = (float)_maskPreview.width / _maskPreview.height;
                float maxW2 = EditorGUIUtility.currentViewWidth - 24f;
                const float maxH2 = 480f;
                float dW = maxW2;
                float dH = dW / asp2;
                if (dH > maxH2) { dH = maxH2; dW = maxH2 * asp2; }
                Rect previewRect = GUILayoutUtility.GetRect(dW, dH, GUILayout.ExpandWidth(false));
                DrawCheckerboard(previewRect);
                GUI.DrawTexture(previewRect, _maskPreview, ScaleMode.ScaleToFit);
                DrawBorder(previewRect, new Color(.4f, .4f, .4f), 1f);

                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUI.enabled = t.SourceTexture != null;
                if (GUILayout.Button("※画像として書き出す", EditorStyles.miniButton, GUILayout.Width(60)))
                    ExportMaskTexture(t);
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();
            }

            GUILayout.Space(8);

            // ── 適用先設定 ────────────────────────────────────────────
            EditorGUILayout.LabelField("適用先設定 (liltoon)", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            var newMat = (Material)EditorGUILayout.ObjectField(
                "対象マテリアル", t.TargetMaterial, typeof(Material), false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(t, "Change Target Material");
                t.TargetMaterial = newMat;
                EditorUtility.SetDirty(t);
                if (_scenePreviewActive) StartScenePreview(t); // 対象マテリアル変更時は再起動
            }

            if (t.TargetMaterial != null)
            {
                EditorGUILayout.HelpBox(
                    "ビルド時にこのマテリアルを参照しているアバター内の全 Renderer スロットに適用されます。",
                    MessageType.Info);

                DrawPropertyNameDropdown(t, t.TargetMaterial);
                DrawForceEnableSection(t);
            }
            else
            {
                EditorGUILayout.HelpBox("対象マテリアルを設定してください", MessageType.Warning);
            }

            DrawScenePreviewButton(t);
        }

        private void DrawForceEnableSection(ColorMaskTextureGenerator t)
        {
            bool hasMapping = ColorMaskTextureGenerator.TryGetFeatureEnableInfo(
                t.TargetPropertyName, out var entries);

            EditorGUI.BeginChangeCheck();
            bool newForce = EditorGUILayout.Toggle("機能を強制 ON", t.ForceEnableTextureFeature);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(t, "Change Force Enable");
                t.ForceEnableTextureFeature = newForce;
                EditorUtility.SetDirty(t);
            }

            if (t.ForceEnableTextureFeature)
            {
                if (hasMapping)
                {
                    var desc = string.Join(", ", System.Array.ConvertAll(entries, e => $"{e.prop} = {e.value}"));
                    EditorGUILayout.HelpBox($"ビルド時に設定: {desc}", MessageType.None);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        $"「{t.TargetPropertyName}」に対応する有効化プロパティが定義されていません。\n" +
                        "_MainTex など常時有効のプロパティは設定不要です。",
                        MessageType.Warning);
                }
            }
        }

        private void DrawPropertyNameDropdown(ColorMaskTextureGenerator t, Material mat)
        {
            string[] propNames;
            string[] propLabels;

            if (mat != null && mat.shader != null)
            {
                var shader = mat.shader;
                int count = ShaderUtil.GetPropertyCount(shader);
                var names = new List<string>();
                var labels = new List<string>();

                for (int i = 0; i < count; i++)
                {
                    if (ShaderUtil.GetPropertyType(shader, i) != ShaderUtil.ShaderPropertyType.TexEnv)
                        continue;
                    string n = ShaderUtil.GetPropertyName(shader, i);
                    string desc = ShaderUtil.GetPropertyDescription(shader, i);
                    names.Add(n);
                    labels.Add(string.IsNullOrEmpty(desc) ? n : $"{desc} ({n})");
                }

                if (names.Count > 0)
                {
                    propNames = names.ToArray();
                    propLabels = labels.ToArray();
                }
                else
                {
                    propNames = FallbackPropertyNames;
                    propLabels = FallbackPropertyLabels;
                }
            }
            else
            {
                propNames = FallbackPropertyNames;
                propLabels = FallbackPropertyLabels;
            }

            // 現在の値がリストにない場合は先頭に追加
            int currentIdx = System.Array.IndexOf(propNames, t.TargetPropertyName);
            if (currentIdx < 0)
            {
                var nameList = new List<string>(propNames);
                var labelList = new List<string>(propLabels);
                nameList.Insert(0, t.TargetPropertyName);
                labelList.Insert(0, $"(現在) {t.TargetPropertyName}");
                propNames = nameList.ToArray();
                propLabels = labelList.ToArray();
                currentIdx = 0;
            }

            EditorGUI.BeginChangeCheck();
            int newIdx = EditorGUILayout.Popup("テクスチャプロパティ", currentIdx, propLabels);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(t, "Change Target Property");
                t.TargetPropertyName = propNames[newIdx];
                EditorUtility.SetDirty(t);
                if (_scenePreviewActive) StartScenePreview(t); // プロパティ変更時は再起動
            }

            EditorGUILayout.LabelField($"プロパティ名: {t.TargetPropertyName}", EditorStyles.miniLabel);
        }

        private void HandleEyedropperInput(Rect srcRect, ColorMaskTextureGenerator t)
        {
            if (!_eyedropperActive || _cachedSourcePixels == null) return;
            Event evt = Event.current;

            if (evt.type == EventType.MouseMove && srcRect.Contains(evt.mousePosition))
                Repaint();

            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                Rect dr = GetDisplayedRect(srcRect, t.SourceTexture);
                if (dr.Contains(evt.mousePosition))
                {
                    float u = (evt.mousePosition.x - dr.x) / dr.width;
                    float v = 1f - (evt.mousePosition.y - dr.y) / dr.height;
                    // キャッシュ済みピクセル（プレビュー解像度）から取得（Read/Write 不要）
                    int px = Mathf.Clamp(Mathf.FloorToInt(u * _cachedPixelW), 0, _cachedPixelW - 1);
                    int py = Mathf.Clamp(Mathf.FloorToInt(v * _cachedPixelH), 0, _cachedPixelH - 1);
                    Color c = _cachedSourcePixels[py * _cachedPixelW + px];
                    c.a = 1f;

                    Undo.RecordObject(t, "Eyedropper Color");
                    t.TargetColor = c;
                    _eyedropperActive = false;
                    EditorUtility.SetDirty(t);
                    Repaint();
                    evt.Use();
                }
            }

            if (evt.type == EventType.MouseDown && evt.button == 1)
            {
                _eyedropperActive = false;
                evt.Use();
            }
        }

        private static Rect GetDisplayedRect(Rect container, Texture2D tex)
        {
            float cw = container.width, ch = container.height;
            float ta = (float)tex.width / tex.height, ca = cw / ch;
            float dw, dh;
            if (ta > ca) { dw = cw; dh = cw / ta; }
            else { dh = ch; dw = ch * ta; }
            return new Rect(
                container.x + (cw - dw) * .5f,
                container.y + (ch - dh) * .5f,
                dw, dh);
        }

        private static void DrawBorder(Rect r, Color c, float t)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, t), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - t, r.width, t), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, t, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        // ────────────────────────────────────────────────────────────
        //  テクスチャ書き出し
        // ────────────────────────────────────────────────────────────

        private void ExportMaskTexture(ColorMaskTextureGenerator t)
        {
            if (t.SourceTexture == null) return;

            // デフォルトのファイル名・フォルダーを決定してダイアログを開く
            string defaultPath = BuildDefaultExportPath(t);
            string defaultDir = System.IO.Path.GetDirectoryName(AssetRelToAbsPath(defaultPath));
            string defaultName = System.IO.Path.GetFileNameWithoutExtension(defaultPath);

            string picked = EditorUtility.SaveFilePanel("マスクテクスチャの書き出し先", defaultDir, defaultName, "png");
            if (string.IsNullOrEmpty(picked)) return; // キャンセル

            string absPath = picked.Replace('\\', '/');
            string dir = System.IO.Path.GetDirectoryName(absPath);
            if (!System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            // テクスチャ生成 & PNG 書き出し
            var mask = t.GenerateMaskTexture();
            System.IO.File.WriteAllBytes(absPath, mask.EncodeToPNG());
            Object.DestroyImmediate(mask);

            // Unity にインポートして選択状態にする
            string relPath = AbsToAssetRelPath(absPath);
            if (relPath.StartsWith("Assets"))
            {
                AssetDatabase.ImportAsset(relPath);
                var saved = AssetDatabase.LoadAssetAtPath<Texture2D>(relPath);
                if (saved != null)
                {
                    Selection.activeObject = saved;
                    EditorGUIUtility.PingObject(saved);
                }
            }
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("完了", $"書き出しました:\n{relPath}", "OK");
        }

        private static string BuildDefaultExportPath(ColorMaskTextureGenerator t)
        {
            string srcPath = t.SourceTexture != null ? AssetDatabase.GetAssetPath(t.SourceTexture) : "";
            if (!string.IsNullOrEmpty(srcPath))
            {
                string dir = System.IO.Path.GetDirectoryName(srcPath).Replace('\\', '/');
                string name = System.IO.Path.GetFileNameWithoutExtension(srcPath) + "_mask.png";
                return dir + "/" + name;
            }
            return "Assets/mask.png";
        }

        private static string AssetRelToAbsPath(string path)
        {
            path = path.Replace('\\', '/');
            if (path.StartsWith("Assets/"))
            {
                string data = Application.dataPath.Replace('\\', '/').TrimEnd('/');
                return data + "/" + path.Substring("Assets/".Length);
            }
            return path;
        }

        private static string AbsToAssetRelPath(string path)
        {
            string norm = path.Replace('\\', '/');
            string data = Application.dataPath.Replace('\\', '/').TrimEnd('/');
            if (norm.StartsWith(data + "/"))
                return "Assets/" + norm.Substring(data.Length + 1);
            return path;
        }

        // ────────────────────────────────────────────────────────────
        //  Scene プレビュー (AnimationMode 流用)
        // ────────────────────────────────────────────────────────────

        private void StartScenePreview(ColorMaskTextureGenerator t)
        {
            StopScenePreview();
            if (t.TargetMaterial == null || t.SourceTexture == null) return;

            if (AnimationMode.InAnimationMode())
            {
                Debug.LogWarning("[AAU] 別の AnimationMode が起動中のためプレビューを開始できません。");
                return;
            }

            // テクスチャ生成
            _scenePreviewTexture = t.GenerateMaskTexture();

            // マテリアル複製 & テクスチャ適用
            _scenePreviewMaterial = Object.Instantiate(t.TargetMaterial);
            _scenePreviewMaterial.name = t.TargetMaterial.name + " (Scene Preview)";
            _scenePreviewMaterial.SetTexture(t.TargetPropertyName, _scenePreviewTexture);
            if (t.ForceEnableTextureFeature)
                ColorMaskTextureGenerator.ApplyForceEnableToMaterial(_scenePreviewMaterial, t.TargetPropertyName);

            // AnimationMode 開始（元の参照を自動リバートするために必要）
            AnimationMode.StartAnimationMode();
            AnimationMode.BeginSampling();

            // avatarRoot 以下の全 Renderer でマテリアルを差し替え
            var root = t.gameObject.transform.root.gameObject;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                using var so = new SerializedObject(renderer);
                var matsProp = so.FindProperty("m_Materials");
                bool changed = false;

                foreach (SerializedProperty matProp in matsProp)
                {
                    if (matProp.objectReferenceValue != t.TargetMaterial) continue;

                    // 元の参照を AnimationMode に記録（StopAnimationMode 時に自動復元される）
                    AnimationMode.AddPropertyModification(
                        EditorCurveBinding.PPtrCurve("", renderer.GetType(), matProp.propertyPath),
                        new PropertyModification
                        {
                            target = renderer,
                            propertyPath = matProp.propertyPath,
                            objectReference = t.TargetMaterial,
                        },
                        true);

                    matProp.objectReferenceValue = _scenePreviewMaterial;
                    changed = true;
                }

                if (changed) so.ApplyModifiedPropertiesWithoutUndo();
            }

            AnimationMode.EndSampling();

            _scenePreviewActive = true;
            _scenePreviewTargetMat = t.TargetMaterial;
            _scenePreviewTargetProp = t.TargetPropertyName;
        }

        /// <summary>マスクテクスチャだけ再生成する（マテリアル差し替えは不要な場合）。</summary>
        private void UpdateScenePreviewTexture(ColorMaskTextureGenerator t)
        {
            if (!_scenePreviewActive || _scenePreviewMaterial == null) return;

            if (_scenePreviewTexture != null) Object.DestroyImmediate(_scenePreviewTexture);
            _scenePreviewTexture = t.GenerateMaskTexture();
            _scenePreviewMaterial.SetTexture(t.TargetPropertyName, _scenePreviewTexture);
        }

        private void StopScenePreview()
        {
            if (_scenePreviewMaterial != null) { Object.DestroyImmediate(_scenePreviewMaterial); _scenePreviewMaterial = null; }
            if (_scenePreviewTexture != null) { Object.DestroyImmediate(_scenePreviewTexture); _scenePreviewTexture = null; }

            if (AnimationMode.InAnimationMode())
                AnimationMode.StopAnimationMode(); // 全 Renderer のマテリアル参照が自動リバートされる

            _scenePreviewActive = false;
            _scenePreviewTargetMat = null;
            _scenePreviewTargetProp = null;
        }

        private void DrawScenePreviewButton(ColorMaskTextureGenerator t)
        {
            GUILayout.Space(8);
            EditorGUILayout.LabelField("Scene プレビュー", EditorStyles.boldLabel);

            bool canPreview = t.TargetMaterial != null && t.SourceTexture != null;

            if (_scenePreviewActive)
            {
                var prevBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.4f, 1f, 0.5f);
                if (GUILayout.Button("■ プレビュー停止", GUILayout.Height(30)))
                    StopScenePreview();
                GUI.backgroundColor = prevBg;
                EditorGUILayout.HelpBox("Scene にプレビューを適用中です。設定変更は自動反映されます。\nプレビューは元のマテリアルアセットを一切変更しません。", MessageType.Info);
            }
            else
            {
                GUI.enabled = canPreview && !AnimationMode.InAnimationMode();
                if (GUILayout.Button("▶ Scene プレビュー開始", GUILayout.Height(30)))
                    StartScenePreview(t);
                GUI.enabled = true;

                if (!canPreview)
                    EditorGUILayout.HelpBox("ソーステクスチャと対象マテリアルを設定するとプレビューできます。", MessageType.None);
                else if (AnimationMode.InAnimationMode())
                    EditorGUILayout.HelpBox("別のプレビューが起動中のため使用できません。", MessageType.Warning);
            }
        }

        private static void DrawCheckerboard(Rect r)
        {
            int cols = Mathf.CeilToInt(r.width / 8f);
            int rows = Mathf.CeilToInt(r.height / 8f);
            for (int row = 0; row < rows; row++)
                for (int col = 0; col < cols; col++)
                {
                    bool light = (row + col) % 2 == 0;
                    Color c = light ? new Color(.7f, .7f, .7f) : new Color(.5f, .5f, .5f);
                    float x = r.x + col * 8f;
                    float y = r.y + row * 8f;
                    float w = Mathf.Min(8f, r.xMax - x);
                    float h = Mathf.Min(8f, r.yMax - y);
                    EditorGUI.DrawRect(new Rect(x, y, w, h), c);
                }
        }
    }
}
