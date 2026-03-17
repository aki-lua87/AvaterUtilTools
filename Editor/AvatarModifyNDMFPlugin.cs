using nadena.dev.ndmf;
using UnityEngine;

// 全ての AvatarModify コンポーネントを NDMF ビルドフェーズで処理するプラグイン。
// IVRCSDKPreprocessAvatarCallback が呼ばれる時点では NDMF がアバターのクローンを
// 処理済みのため AvatarModify コンポーネントが見つからない問題を回避する。
// Modular Avatar より前に実行することで、MA のアニメーションリベース前に
// DefaultExpressionOverride のクリップ差し替えが正しく動作する。
[assembly: ExportsPlugin(typeof(aki_lua87.AvatarUtils.AvatarModifyNDMFPlugin))]

namespace aki_lua87.AvatarUtils
{
    internal class AvatarModifyNDMFPlugin : Plugin<AvatarModifyNDMFPlugin>
    {
        protected override void Configure()
        {
            InPhase(BuildPhase.Transforming)
                // MA がインストールされていない環境では無視される
                .BeforePlugin("nadena.dev.modular-avatar")
                .Run("AvatarModify", ctx =>
                {
                    var comps = ctx.AvatarRootObject
                        .GetComponentsInChildren<AvatarModify>(true);

                    foreach (var comp in comps)
                    {
                        if (comp == null) continue;

                        try
                        {
                            comp.Apply(ctx.AvatarRootObject);
                        }
                        catch (System.Exception e)
                        {
                            Debug.LogError(
                                $"[AAU] {comp.GetType().Name}.Apply() で例外:\n{e}", comp);
                        }

                        if (comp != null)
                            Object.DestroyImmediate(comp);
                    }
                });
        }
    }
}
