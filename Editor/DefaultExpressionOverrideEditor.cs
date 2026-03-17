using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace aki_lua87.AvatarUtils.Editor
{
    [CustomEditor(typeof(DefaultExpressionOverride))]
    public class DefaultExpressionOverrideEditor : UnityEditor.Editor
    {
        private SerializedProperty _fxController;
        private SerializedProperty _faceMesh;
        private SerializedProperty _targetClips;

        private bool _showBlendshapePreview;
        private Vector2 _blendshapeScrollPos;

        private void OnEnable()
        {
            _fxController = serializedObject.FindProperty("FXController");
            _faceMesh     = serializedObject.FindProperty("FaceMesh");
            _targetClips  = serializedObject.FindProperty("TargetClips");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var comp = (DefaultExpressionOverride)target;

            // ── ヘッダー ──────────────────────────────────────────────────
            EditorGUILayout.Space(4);
            GUILayout.Label("デフォルト表情 非破壊上書き", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "アバタールートにアタッチしてください。\n" +
                "現在のシェイプキーの状態でFX内の指定クリップをビルド時に差し替えます。\n" +
                "元のアセットは変更されません。",
                MessageType.Info);
            EditorGUILayout.Space(4);

            // ── FX コントローラー ─────────────────────────────────────────
            EditorGUILayout.LabelField("FX AnimatorController", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_fxController, new GUIContent("FX Controller"));

            if (comp.FXController == null)
            {
                EditorGUILayout.HelpBox("未設定です。VRCAvatarDescriptorからFXコントローラーを自動検出できます。", MessageType.Warning);
                if (GUILayout.Button("FX を自動検出してセット"))
                    AutoSetFXController(comp);
            }
            else
            {
                // 生成物が設定されていたら警告
                string fxPath = AssetDatabase.GetAssetPath(comp.FXController);
                bool isGenerated = !string.IsNullOrEmpty(fxPath) &&
                                   fxPath.StartsWith("Assets/aki_lua87_AAU_Generated/DefaultExpressionOverride");
                if (isGenerated)
                {
                    EditorGUILayout.HelpBox(
                        "⚠ 生成物が設定されています！\n" +
                        "このまま使うと以前の状態が引き継がれ、表情・ジェスチャーが正しく動作しません。\n" +
                        "「元のFXを再検出してセット」を押して正しいコントローラーに戻してください。",
                        MessageType.Error);
                    if (GUILayout.Button("元のFXを再検出してセット"))
                        AutoSetFXController(comp);
                }
            }

            EditorGUILayout.Space(6);

            // ── 顔メッシュ ────────────────────────────────────────────────
            EditorGUILayout.LabelField("顔のSkinnedMeshRenderer", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_faceMesh, new GUIContent("Face Mesh"));

            if (comp.FaceMesh == null)
            {
                EditorGUILayout.HelpBox("未設定です。\"Body\" という名前の SkinnedMeshRenderer を自動検出できます。", MessageType.Warning);
                if (GUILayout.Button("Body を自動検出してセット"))
                {
                    var found = FindBodyMesh(comp);
                    if (found != null)
                    {
                        Undo.RecordObject(comp, "Auto-set FaceMesh");
                        comp.FaceMesh = found;
                        EditorUtility.SetDirty(comp);
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("自動検出", "SkinnedMeshRenderer が見つかりませんでした。", "OK");
                    }
                }
            }

            if (comp.FaceMesh != null)
            {
                var mesh = comp.FaceMesh.sharedMesh;
                if (mesh != null)
                {
                    int nonZero = 0;
                    for (int i = 0; i < mesh.blendShapeCount; i++)
                        if (comp.FaceMesh.GetBlendShapeWeight(i) != 0f) nonZero++;

                    EditorGUI.indentLevel++;
                    EditorGUILayout.LabelField(
                        $"シェイプキー: {mesh.blendShapeCount}個  (非ゼロ: {nonZero}個)",
                        EditorStyles.miniLabel);

                    _showBlendshapePreview = EditorGUILayout.Foldout(
                        _showBlendshapePreview, "現在の値を確認", true);
                    if (_showBlendshapePreview)
                        DrawBlendshapePreview(comp);

                    EditorGUI.indentLevel--;
                }
            }

            EditorGUILayout.Space(6);

            // ── 対象クリップ ──────────────────────────────────────────────
            EditorGUILayout.LabelField("上書き対象のアニメーションクリップ", EditorStyles.boldLabel);

            if (comp.FXController != null)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("FXから自動検出", GUILayout.Width(140)))
                        AutoDetectClipsFromFX(comp, comp.FXController as AnimatorController);
                }
            }

            EditorGUILayout.PropertyField(_targetClips, new GUIContent("Target Clips"), true);

            if (comp.TargetClips != null && comp.TargetClips.Length > 0)
            {
                int nullCount = comp.TargetClips.Count(c => c == null);
                if (nullCount > 0)
                    EditorGUILayout.HelpBox($"{nullCount}個のスロットが未設定です。", MessageType.Warning);
            }

            serializedObject.ApplyModifiedProperties();
        }

        // ----------------------------------------------------------------
        //  Blendshape preview
        // ----------------------------------------------------------------

        private void DrawBlendshapePreview(DefaultExpressionOverride comp)
        {
            var mesh = comp.FaceMesh.sharedMesh;
            if (mesh == null) return;

            _blendshapeScrollPos = EditorGUILayout.BeginScrollView(
                _blendshapeScrollPos, GUILayout.MaxHeight(160));

            EditorGUI.indentLevel++;
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                float val = comp.FaceMesh.GetBlendShapeWeight(i);
                if (val == 0f) continue;

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(mesh.GetBlendShapeName(i), GUILayout.MinWidth(180));
                    EditorGUILayout.LabelField(val.ToString("F1"), GUILayout.Width(50));
                }
            }
            EditorGUI.indentLevel--;
            EditorGUILayout.EndScrollView();
        }

        // ----------------------------------------------------------------
        //  Auto-detect
        // ----------------------------------------------------------------

        private static void AutoSetFXController(DefaultExpressionOverride comp)
        {
            var found = FindFXController(comp);
            if (found != null)
            {
                Undo.RecordObject(comp, "Auto-set FXController");
                comp.FXController = found;
                EditorUtility.SetDirty(comp);
            }
            else
            {
                EditorUtility.DisplayDialog("自動検出",
                    "元のFXコントローラーが見つかりませんでした。\n" +
                    "手動で正しいコントローラーを FXController フィールドにセットしてください。",
                    "OK");
            }
        }

        private static AnimatorController FindFXController(DefaultExpressionOverride comp)
        {
            var descriptor = comp.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null) return null;
            const string generatedDir = "Assets/aki_lua87_AAU_Generated/DefaultExpressionOverride";
            foreach (var layer in descriptor.baseAnimationLayers)
            {
                if (layer.type != VRCAvatarDescriptor.AnimLayerType.FX) continue;
                if (layer.isDefault || layer.animatorController == null) continue;
                var ctrl = layer.animatorController as AnimatorController;
                if (ctrl == null) continue;
                // 生成物はスキップ
                string path = AssetDatabase.GetAssetPath(ctrl);
                if (path.StartsWith(generatedDir)) continue;
                return ctrl;
            }
            return null;
        }

        private static SkinnedMeshRenderer FindBodyMesh(DefaultExpressionOverride comp)
        {
            foreach (var smr in comp.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.name == "Body") return smr;
            return comp.GetComponentInChildren<SkinnedMeshRenderer>(true);
        }

        private void AutoDetectClipsFromFX(DefaultExpressionOverride comp, AnimatorController fxCtrl)
        {
            var entries = CollectCategorizedClips(fxCtrl);
            if (entries.Count == 0)
            {
                EditorUtility.DisplayDialog("自動検出", "FXコントローラー内にアニメーションクリップが見つかりませんでした。", "OK");
                return;
            }

            ClipSelectWindow.Show(entries, selected =>
            {
                Undo.RecordObject(comp, "AutoDetect FX Clips");
                comp.TargetClips = selected;
                EditorUtility.SetDirty(comp);
            });
        }

        // ----------------------------------------------------------------
        //  Clip collection (categorized)
        // ----------------------------------------------------------------

        internal struct ClipEntry
        {
            public AnimationClip Clip;
            public string LayerName;
            /// <summary>そのレイヤーのデフォルトステートのクリップか</summary>
            public bool IsDefaultState;
        }

        private static System.Collections.Generic.List<ClipEntry> CollectCategorizedClips(
            AnimatorController ctrl)
        {
            var result = new System.Collections.Generic.List<ClipEntry>();
            var seen   = new System.Collections.Generic.HashSet<AnimationClip>();
            foreach (var layer in ctrl.layers)
                CollectFromStateMachine(layer.stateMachine, layer.name, seen, result);
            return result;
        }

        private static void CollectFromStateMachine(
            AnimatorStateMachine sm,
            string layerName,
            System.Collections.Generic.HashSet<AnimationClip> seen,
            System.Collections.Generic.List<ClipEntry> result)
        {
            foreach (var cs in sm.states)
            {
                bool isDefault = (sm.defaultState != null && cs.state == sm.defaultState);
                CollectFromMotion(cs.state.motion, layerName, isDefault, seen, result);
            }
            foreach (var sub in sm.stateMachines)
                CollectFromStateMachine(sub.stateMachine, layerName, seen, result);
        }

        private static void CollectFromMotion(
            Motion motion, string layerName, bool isDefault,
            System.Collections.Generic.HashSet<AnimationClip> seen,
            System.Collections.Generic.List<ClipEntry> result)
        {
            if (motion is AnimationClip clip)
            {
                if (seen.Add(clip))
                    result.Add(new ClipEntry { Clip = clip, LayerName = layerName, IsDefaultState = isDefault });
            }
            else if (motion is BlendTree bt)
                foreach (var child in bt.children)
                    CollectFromMotion(child.motion, layerName, isDefault, seen, result);
        }
    }

    // ====================================================================
    //  クリップ選択ダイアログ
    // ====================================================================

    internal class ClipSelectWindow : EditorWindow
    {
        private System.Collections.Generic.List<DefaultExpressionOverrideEditor.ClipEntry> _entries;
        private bool[] _selected;
        private Vector2 _scroll;
        private System.Action<AnimationClip[]> _onConfirm;

        private static readonly Color WarningBg   = new Color(1f, 0.92f, 0.6f, 0.4f);
        private static readonly Color DefaultBg    = new Color(0.6f, 1f, 0.7f, 0.25f);

        public static void Show(
            System.Collections.Generic.List<DefaultExpressionOverrideEditor.ClipEntry> entries,
            System.Action<AnimationClip[]> onConfirm)
        {
            var win = GetWindow<ClipSelectWindow>(true, "FXクリップ選択", true);
            win._entries   = entries;
            win._selected  = new bool[entries.Count];
            win._onConfirm = onConfirm;
            win.minSize    = new Vector2(480, 500);

            // デフォルトステートのクリップのみ事前選択
            for (int i = 0; i < entries.Count; i++)
                win._selected[i] = entries[i].IsDefaultState;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("上書きするクリップを選択してください", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "【推奨】デフォルトステートのクリップのみ選択してください。\n" +
                "表情変化・ジェスチャーのクリップを選択すると、それらの表情が正常に動作しなくなります。",
                MessageType.Warning);
            EditorGUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("推奨のみ選択"))
                    for (int i = 0; i < _entries.Count; i++)
                        _selected[i] = _entries[i].IsDefaultState;
                if (GUILayout.Button("全選択"))
                    for (int i = 0; i < _selected.Length; i++) _selected[i] = true;
                if (GUILayout.Button("全解除"))
                    for (int i = 0; i < _selected.Length; i++) _selected[i] = false;
            }

            EditorGUILayout.Space(4);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            // ── デフォルトステート ────────────────────────────────────
            DrawSectionHeader("デフォルトステート (推奨)", DefaultBg);
            bool anyDefault = false;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (!_entries[i].IsDefaultState) continue;
                anyDefault = true;
                DrawRow(i, DefaultBg);
            }
            if (!anyDefault)
                EditorGUILayout.LabelField("  (なし)", EditorStyles.miniLabel);

            EditorGUILayout.Space(6);

            // ── その他のステート ──────────────────────────────────────
            DrawSectionHeader("その他のステート (表情/ジェスチャー — 上書き注意)", WarningBg);
            bool anyOther = false;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].IsDefaultState) continue;
                anyOther = true;
                DrawRow(i, WarningBg);
            }
            if (!anyOther)
                EditorGUILayout.LabelField("  (なし)", EditorStyles.miniLabel);

            EditorGUILayout.EndScrollView();
            EditorGUILayout.Space(8);

            int count = _selected.Count(s => s);
            using (new EditorGUI.DisabledScope(count == 0))
            {
                if (GUILayout.Button($"選択した {count} 個を TargetClips に設定", GUILayout.Height(28)))
                {
                    _onConfirm?.Invoke(
                        _entries.Where((_, i) => _selected[i]).Select(e => e.Clip).ToArray());
                    Close();
                }
            }
        }

        private void DrawSectionHeader(string label, Color bg)
        {
            var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 4);
            EditorGUI.DrawRect(rect, bg);
            EditorGUI.LabelField(rect, "  " + label, EditorStyles.boldLabel);
        }

        private void DrawRow(int i, Color bg)
        {
            var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 2);
            if (_selected[i]) EditorGUI.DrawRect(rect, bg * 1.5f);

            float toggleW = 20f;
            float layerW  = 140f;
            float clipW   = rect.width - toggleW - layerW - 6f;

            var toggleRect = new Rect(rect.x,              rect.y + 1, toggleW,  rect.height - 2);
            var layerRect  = new Rect(rect.x + toggleW,    rect.y + 1, layerW,   rect.height - 2);
            var clipRect   = new Rect(rect.x + toggleW + layerW + 4, rect.y + 1, clipW, rect.height - 2);

            _selected[i] = EditorGUI.Toggle(toggleRect, _selected[i]);
            EditorGUI.LabelField(layerRect,
                _entries[i].LayerName, EditorStyles.miniLabel);
            EditorGUI.ObjectField(clipRect, _entries[i].Clip, typeof(AnimationClip), false);
        }
    }
}
