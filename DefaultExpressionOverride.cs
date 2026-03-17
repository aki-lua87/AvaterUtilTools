using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
#endif

namespace aki_lua87.AvatarUtils
{
    /// <summary>
    /// アバターのデフォルト表情を非破壊で上書きするコンポーネント。
    /// アバタールート(VRCAvatarDescriptorと同じGameObject)にアタッチする。
    /// FXコントローラー内の指定したアニメーションクリップを
    /// 現在のシェイプキーの状態で上書きしたコピーに差し替える。
    /// </summary>
    [AddComponentMenu("aki_lua87/AAU/DefaultExpressionOverride")]
    public class DefaultExpressionOverride : AvatarModify
    {
        [Header("元のFX AnimatorController")]
        public RuntimeAnimatorController FXController;

        [Header("顔のSkinnedMeshRenderer")]
        public SkinnedMeshRenderer FaceMesh;

        [Header("上書き対象のアニメーションクリップ")]
        public AnimationClip[] TargetClips = new AnimationClip[0];

        private const string OutputRoot = "Assets/aki_lua87_AAU_Generated";
        private const string OutputDir  = OutputRoot + "/DefaultExpressionOverride";

        private void Reset()
        {
#if UNITY_EDITOR
            FXController = FindFXController();
#endif
            FaceMesh = FindBodyMesh();
        }

#if UNITY_EDITOR
        private AnimatorController FindFXController()
        {
            var descriptor = GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null) return null;
            foreach (var layer in descriptor.baseAnimationLayers)
            {
                if (layer.type != VRCAvatarDescriptor.AnimLayerType.FX) continue;
                if (layer.isDefault || layer.animatorController == null) continue;
                var ctrl = layer.animatorController as AnimatorController;
                if (ctrl == null) continue;
                // 生成物はスキップ (以前の実行でDescriptorが書き換わっていても正しい元を返す)
                string path = AssetDatabase.GetAssetPath(ctrl);
                if (path.StartsWith(OutputDir)) continue;
                return ctrl;
            }
            return null;
        }
#endif

        private SkinnedMeshRenderer FindBodyMesh()
        {
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.name == "Body") return smr;
            return GetComponentInChildren<SkinnedMeshRenderer>(true);
        }

        public override void Apply(GameObject avatarRoot)
        {
#if UNITY_EDITOR
            if (!ValidateInputs()) return;

            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                Debug.LogWarning("[AAU] VRCAvatarDescriptor が見つかりません。", this);
                return;
            }

            string facePath = GetRelativePath(avatarRoot.transform, FaceMesh.transform);
            var blendshapeValues = ReadBlendshapeValues();
            EnsureOutputFolder();

            var clipMap = BuildClipReplacementMap(blendshapeValues, facePath);
            if (clipMap.Count == 0) return;

            var generatedFX = CloneAndPatchController(FXController as AnimatorController, clipMap);
            if (generatedFX == null) return;

            SetFXController(descriptor, generatedFX);
            EditorUtility.SetDirty(descriptor);

            Debug.Log($"[AAU] デフォルト表情を上書きしました。({clipMap.Count}クリップ)", this);
#endif
        }

        // ----------------------------------------------------------------
        //  Utility (runtime-safe)
        // ----------------------------------------------------------------

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (target == root) return string.Empty;
            var parts = new List<string>();
            var current = target;
            while (current != null && current != root)
            {
                parts.Insert(0, current.name);
                current = current.parent;
            }
            return string.Join("/", parts);
        }

#if UNITY_EDITOR
        // ----------------------------------------------------------------
        //  Validation
        // ----------------------------------------------------------------

        private bool ValidateInputs()
        {
            if (FXController == null)
            {
                Debug.LogWarning("[AAU] FXController が未設定です。", this);
                return false;
            }

            // 生成物を元として使ってしまうと、以前の壊れた状態が引き継がれる
            string fxPath = AssetDatabase.GetAssetPath(FXController);
            if (fxPath.StartsWith(OutputDir))
            {
                Debug.LogError(
                    "[AAU] FXController に生成物が設定されています！\n" +
                    "必ず元の (生成前の) FXコントローラーを設定してください。\n" +
                    "インスペクターの「FX を自動検出してセット」を押して再設定するか、手動で正しいコントローラーを指定してください。",
                    this);
                return false;
            }

            if (FaceMesh == null)
            {
                FaceMesh = FindBodyMesh();
                if (FaceMesh == null)
                {
                    Debug.LogWarning("[AAU] FaceMesh が未設定です。", this);
                    return false;
                }
            }

            if (TargetClips == null || TargetClips.Length == 0)
            {
                Debug.LogWarning("[AAU] TargetClips が未設定です。", this);
                return false;
            }

            return true;
        }

        // ----------------------------------------------------------------
        //  Blendshape reading
        // ----------------------------------------------------------------

        private Dictionary<string, float> ReadBlendshapeValues()
        {
            var values = new Dictionary<string, float>();
            var mesh = FaceMesh.sharedMesh;
            if (mesh == null) return values;

            for (int i = 0; i < mesh.blendShapeCount; i++)
                values[mesh.GetBlendShapeName(i)] = FaceMesh.GetBlendShapeWeight(i);

            return values;
        }

        // ----------------------------------------------------------------
        //  Clip modification
        // ----------------------------------------------------------------

        private Dictionary<AnimationClip, AnimationClip> BuildClipReplacementMap(
            Dictionary<string, float> blendshapeValues, string facePath)
        {
            var map = new Dictionary<AnimationClip, AnimationClip>();
            foreach (var original in TargetClips)
            {
                if (original == null) continue;
                var modified = CreateModifiedClip(original, facePath, blendshapeValues);
                if (modified != null)
                    map[original] = modified;
            }
            return map;
        }

        private AnimationClip CreateModifiedClip(
            AnimationClip original,
            string facePath,
            Dictionary<string, float> blendshapeValues)
        {
            string originalPath = AssetDatabase.GetAssetPath(original);
            string destPath = OutputDir + "/" + original.name + "_ExpressionOverride.anim";

            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(destPath) != null)
                AssetDatabase.DeleteAsset(destPath);

            AnimationClip modified;
            if (!string.IsNullOrEmpty(originalPath) && File.Exists(originalPath))
            {
                AssetDatabase.CopyAsset(originalPath, destPath);
                AssetDatabase.SaveAssets();
                modified = AssetDatabase.LoadAssetAtPath<AnimationClip>(destPath);
            }
            else
            {
                modified = new AnimationClip { name = original.name + "_ExpressionOverride" };
                foreach (var binding in AnimationUtility.GetCurveBindings(original))
                    AnimationUtility.SetEditorCurve(modified, binding,
                        AnimationUtility.GetEditorCurve(original, binding));
                AssetDatabase.CreateAsset(modified, destPath);
            }

            if (modified == null) return null;

            // 元クリップが既に持っているシェイプキー名を収集する
            // (元クリップにないシェイプキーは追加しない = リップシンク等を破壊しない)
            var existingShapeKeys = new HashSet<string>();
            foreach (var binding in AnimationUtility.GetCurveBindings(modified))
            {
                if (binding.type == typeof(SkinnedMeshRenderer) &&
                    binding.propertyName.StartsWith("blendShape."))
                    existingShapeKeys.Add(binding.propertyName.Substring("blendShape.".Length));
            }

            if (existingShapeKeys.Count == 0)
            {
                Debug.LogWarning(
                    $"[AAU] '{original.name}' にシェイプキーのカーブが存在しませんでした。" +
                    "デフォルト表情として使われているクリップか確認してください。", this);
            }

            // 元クリップが制御しているシェイプキーのみ現在値で上書きする
            float clipLength = modified.length > 0f ? modified.length : 0f;
            foreach (var kvp in blendshapeValues)
            {
                if (!existingShapeKeys.Contains(kvp.Key)) continue;
                var curve = AnimationCurve.Constant(0f, clipLength, kvp.Value);
                modified.SetCurve(facePath, typeof(SkinnedMeshRenderer), "blendShape." + kvp.Key, curve);
            }

            EditorUtility.SetDirty(modified);
            AssetDatabase.SaveAssets();
            return modified;
        }

        // ----------------------------------------------------------------
        //  AnimatorController cloning & clip replacement
        // ----------------------------------------------------------------

        private AnimatorController CloneAndPatchController(
            AnimatorController source,
            Dictionary<AnimationClip, AnimationClip> clipMap)
        {
            string srcPath  = AssetDatabase.GetAssetPath(source);
            string destPath = OutputDir + "/" + source.name + "_ExpressionOverride.controller";

            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(destPath) != null)
                AssetDatabase.DeleteAsset(destPath);

            if (!AssetDatabase.CopyAsset(srcPath, destPath))
            {
                Debug.LogError($"[AAU] コントローラーのコピーに失敗しました: {srcPath}", this);
                return null;
            }

            AssetDatabase.SaveAssets();
            var cloned = AssetDatabase.LoadAssetAtPath<AnimatorController>(destPath);
            if (cloned == null) return null;

            bool anyReplaced = false;
            foreach (var layer in cloned.layers)
                anyReplaced |= ReplaceClipsInStateMachine(layer.stateMachine, clipMap);

            if (!anyReplaced)
                Debug.LogWarning("[AAU] 指定したクリップがFXコントローラー内に見つかりませんでした。TargetClipsを確認してください。", this);

            EditorUtility.SetDirty(cloned);
            AssetDatabase.SaveAssets();
            return cloned;
        }

        private bool ReplaceClipsInStateMachine(
            AnimatorStateMachine sm,
            Dictionary<AnimationClip, AnimationClip> clipMap)
        {
            bool replaced = false;

            foreach (var cs in sm.states)
            {
                // デフォルトステートのみ置換する
                // 同一クリップが表情/ジェスチャーステートでも使われている場合に
                // 意図せず上書きしてしまうのを防ぐ
                bool isDefaultState = sm.defaultState != null && cs.state == sm.defaultState;
                if (!isDefaultState) continue;

                var state = cs.state;
                if (state.motion is AnimationClip clip)
                {
                    if (clipMap.TryGetValue(clip, out var newClip))
                    {
                        state.motion = newClip;
                        EditorUtility.SetDirty(state);
                        replaced = true;
                    }
                }
                else if (state.motion is BlendTree bt)
                {
                    replaced |= ReplaceClipsInBlendTree(bt, clipMap);
                }
            }

            foreach (var sub in sm.stateMachines)
                replaced |= ReplaceClipsInStateMachine(sub.stateMachine, clipMap);

            return replaced;
        }

        private bool ReplaceClipsInBlendTree(
            BlendTree bt,
            Dictionary<AnimationClip, AnimationClip> clipMap)
        {
            bool replaced = false;
            var children = bt.children;

            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].motion is AnimationClip clip &&
                    clipMap.TryGetValue(clip, out var newClip))
                {
                    var child = children[i];
                    child.motion = newClip;
                    children[i] = child;
                    replaced = true;
                }
                else if (children[i].motion is BlendTree sub)
                {
                    replaced |= ReplaceClipsInBlendTree(sub, clipMap);
                }
            }

            if (replaced)
                bt.children = children;

            return replaced;
        }

        // ----------------------------------------------------------------
        //  VRCAvatarDescriptor helpers
        // ----------------------------------------------------------------

        private static void SetFXController(VRCAvatarDescriptor descriptor, AnimatorController controller)
        {
            var layers = descriptor.baseAnimationLayers;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].type == VRCAvatarDescriptor.AnimLayerType.FX)
                {
                    layers[i].animatorController = controller;
                    layers[i].isDefault = false;
                    descriptor.baseAnimationLayers = layers;
                    return;
                }
            }
        }

        // ----------------------------------------------------------------
        //  Folder helpers
        // ----------------------------------------------------------------

        private static void EnsureOutputFolder()
        {
            if (!AssetDatabase.IsValidFolder(OutputRoot))
                AssetDatabase.CreateFolder("Assets", "aki_lua87_AAU_Generated");
            if (!AssetDatabase.IsValidFolder(OutputDir))
                AssetDatabase.CreateFolder(OutputRoot, "DefaultExpressionOverride");
        }
#endif
    }
}
