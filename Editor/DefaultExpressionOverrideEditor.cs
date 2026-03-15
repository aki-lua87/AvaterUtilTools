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
                            "FXコントローラーが見つかりませんでした。\n" +
                            "VRCAvatarDescriptorのFXレイヤーにコントローラーが設定されているか確認してください。",
                            "OK");
                    }
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

        private static AnimatorController FindFXController(DefaultExpressionOverride comp)
        {
            var descriptor = comp.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null) return null;
            foreach (var layer in descriptor.baseAnimationLayers)
            {
                if (layer.type == VRCAvatarDescriptor.AnimLayerType.FX &&
                    !layer.isDefault &&
                    layer.animatorController != null)
                    return layer.animatorController as AnimatorController;
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
            var clips = CollectAllClipsFromController(fxCtrl);
            if (clips.Length == 0)
            {
                EditorUtility.DisplayDialog("自動検出", "FXコントローラー内にアニメーションクリップが見つかりませんでした。", "OK");
                return;
            }

            ClipSelectWindow.Show(clips, selected =>
            {
                Undo.RecordObject(comp, "AutoDetect FX Clips");
                comp.TargetClips = selected;
                EditorUtility.SetDirty(comp);
            });
        }

        private static AnimationClip[] CollectAllClipsFromController(AnimatorController ctrl)
        {
            var set = new System.Collections.Generic.HashSet<AnimationClip>();
            foreach (var layer in ctrl.layers)
                CollectClipsFromStateMachine(layer.stateMachine, set);
            return set.ToArray();
        }

        private static void CollectClipsFromStateMachine(
            AnimatorStateMachine sm,
            System.Collections.Generic.HashSet<AnimationClip> set)
        {
            foreach (var cs in sm.states)
                CollectClipsFromMotion(cs.state.motion, set);
            foreach (var sub in sm.stateMachines)
                CollectClipsFromStateMachine(sub.stateMachine, set);
        }

        private static void CollectClipsFromMotion(
            Motion motion,
            System.Collections.Generic.HashSet<AnimationClip> set)
        {
            if (motion is AnimationClip clip)
                set.Add(clip);
            else if (motion is BlendTree bt)
                foreach (var child in bt.children)
                    CollectClipsFromMotion(child.motion, set);
        }
    }

    // ====================================================================
    //  クリップ選択ダイアログ
    // ====================================================================

    internal class ClipSelectWindow : EditorWindow
    {
        private AnimationClip[] _clips;
        private bool[] _selected;
        private Vector2 _scroll;
        private System.Action<AnimationClip[]> _onConfirm;

        public static void Show(AnimationClip[] clips, System.Action<AnimationClip[]> onConfirm)
        {
            var win = GetWindow<ClipSelectWindow>(true, "FXクリップ選択", true);
            win._clips     = clips;
            win._selected  = new bool[clips.Length];
            win._onConfirm = onConfirm;
            win.minSize    = new Vector2(400, 400);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("上書きするクリップを選択してください", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("全選択")) for (int i = 0; i < _selected.Length; i++) _selected[i] = true;
                if (GUILayout.Button("全解除")) for (int i = 0; i < _selected.Length; i++) _selected[i] = false;
            }

            EditorGUILayout.Space(4);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            for (int i = 0; i < _clips.Length; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    _selected[i] = EditorGUILayout.Toggle(_selected[i], GUILayout.Width(20));
                    EditorGUILayout.ObjectField(_clips[i], typeof(AnimationClip), false);
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.Space(8);

            int count = _selected.Count(s => s);
            using (new EditorGUI.DisabledScope(count == 0))
            {
                if (GUILayout.Button($"選択した {count} 個を TargetClips に設定", GUILayout.Height(28)))
                {
                    _onConfirm?.Invoke(_clips.Where((_, i) => _selected[i]).ToArray());
                    Close();
                }
            }
        }
    }
}
