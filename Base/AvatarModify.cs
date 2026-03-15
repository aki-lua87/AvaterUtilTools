using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace aki_lua87.AvatarUtils
{

    public abstract class AvatarModify : MonoBehaviour
    {
        public abstract void Apply(GameObject avatarRoot);
    }
}
