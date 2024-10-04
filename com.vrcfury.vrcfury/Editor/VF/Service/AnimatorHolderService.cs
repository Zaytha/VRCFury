using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VF.Builder;
using VF.Feature.Base;
using VF.Injector;
using VF.Utils;
using Object = UnityEngine.Object;

namespace VF.Service {
    /**
     * Disabling the Animator during the build has a LOT of benefits:
     * 1. If you clip.SampleAnimation on the avatar while it has a humanoid Avatar set on its Animator, it'll
     *    bake into motorcycle pose.
     * 2. If you change the avatar or controller on the Animator, the Animator will reset all transforms of all
     *    children objects back to the way they were at the start of the frame.
     * 3. If GestureManager (or someone else) started animating our avatar before the build, we need to undo their changes
     *    to get the avatar back into the default position (WriteDefaultValues)
     * 4. If GestureManager (or someone else) changes the controller on the Animator after we build during this same frame,
     *    it would reset all the child transforms back to how they were before we built. To "lock them in," we need to
     *    reset the animator.
     */
    [VFService]
    internal class AnimatorHolderService {
        
        [VFAutowired] private readonly ControllersService controllers;
        private ControllerManager fx => controllers.GetFx();
        [VFAutowired] private readonly VFGameObject avatarObject;

        private class SavedAnimator {
            public RuntimeAnimatorController controller;
            public Avatar avatar;
            public bool applyRootMotion;
            public AnimatorUpdateMode updateMode;
            public AnimatorCullingMode cullingMode;
        }

        private readonly Dictionary<VFGameObject, SavedAnimator> savedAnimators = new Dictionary<VFGameObject, SavedAnimator>();

        [FeatureBuilderAction(FeatureOrder.ResetAnimatorBefore)]
        public void ApplyBefore() {
            VRCFArmatureUtils.ClearCache();
            VRCFArmatureUtils.WarmupCache(avatarObject);
            ClosestBoneUtils.ClearCache();

            foreach (var animator in avatarObject.GetComponentsInSelfAndChildren<Animator>()) {
                savedAnimators.Add(animator.owner(), new SavedAnimator {
                    controller = animator.runtimeAnimatorController,
                    avatar = animator.avatar,
                    applyRootMotion = animator.applyRootMotion,
                    updateMode = animator.updateMode,
                    cullingMode = animator.cullingMode,
                });
                // In unity 2022, calling this when the animator hasn't called Update recently (meaning outside of play mode,
                // just entered play mode, object not enabled, etc) can make it write defaults that are NOT the proper resting state.
                // However, we probably don't even need this anymore since we initialize before the Animator would ever run now.
                // animator.WriteDefaultValues();
                Object.DestroyImmediate(animator);
            }
        }

        [FeatureBuilderAction(FeatureOrder.ResetAnimatorAfter)]
        public void ApplyAfter() {
            foreach (var pair in savedAnimators) {
                var obj = pair.Key;
                if (obj == null) continue;
                var saved = pair.Value;
                
                var animator = obj.AddComponent<Animator>();
                animator.applyRootMotion = saved.applyRootMotion;
                animator.updateMode = saved.updateMode;
                animator.cullingMode = saved.cullingMode;
                animator.avatar = saved.avatar;
                if (obj == avatarObject) {
                    if (saved.controller != null) {
                        animator.runtimeAnimatorController = fx.GetRaw();
                    }
                } else {
                    animator.runtimeAnimatorController = saved.controller;
                }
            }
        }

        public IList<(VFGameObject owner, RuntimeAnimatorController controller)> GetSubControllers() {
            return savedAnimators
                .Where(pair => pair.Key != avatarObject)
                .Where(pair => pair.Value.controller != null)
                .Select(pair => (pair.Key, pair.Value.controller))
                .ToArray();
        }

        public void SetSubController(VFGameObject owner, RuntimeAnimatorController controller) {
            if (owner == avatarObject) return;
            if (savedAnimators.TryGetValue(owner, out var saved)) {
                saved.controller = controller;
            }
        }

        public void RemoveAnimator(VFGameObject owner) {
            if (owner == avatarObject) return;
            savedAnimators.Remove(owner);
        }
    }
}
