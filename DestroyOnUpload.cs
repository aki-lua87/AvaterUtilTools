using UnityEngine;

namespace aki_lua87.AvatarUtils
{
    [AddComponentMenu("aki_lua87/AAU/DestroyOnUpload")]
    public class DestroyOnUpload : AvatarModify
    {
        public override void Apply(GameObject avatarRoot)
        {
            DestroyImmediate(gameObject);
        }
    }
}
