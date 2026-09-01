using System;
using System.Collections.Generic;
using System.Linq;
using EFT.InventoryLogic.Operations;
using EFT.Visual;
using RootMotion.FinalIK;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using FoldOperation = FoldOperationClass;

namespace FoldThatStock
{
    /// <summary>
    /// Samples a donor clip on a hidden minimal rig, then transfers only the weapon backbone
    /// and selected manipulating arm to the live view. The opposite arm remains anchored to
    /// the live weapon while EFT's normal pose is restored through a final blend.
    /// </summary>
    [DefaultExecutionOrder(32000)]
    public sealed class FoldThatStockArmAnimationOverlay : MonoBehaviour
    {
        private const string LeftCollarboneName = "Base HumanLCollarbone";
        private const string RightCollarboneName = "Base HumanRCollarbone";
        private const float PoseFadeInSeconds = 0.12f;
        private const float PoseFadeOutSeconds = 0.3f;

        // Connects a live transform to its hidden donor-sampled counterpart. RenderTargets
        // contains the actual skinned arm bones when EFT uses a separate proxy Animator rig.
        private sealed class TransformMap
        {
            public Transform Live;
            public Transform Sample;
            public Vector3 DonorReferencePosition;
            public Quaternion DonorReferenceRotation;
            public Vector3 LiveReferencePosition;
            public Quaternion LiveReferenceRotation;
            public bool IsRightArm;
            public readonly List<Transform> RenderTargets = new List<Transform>();
        }

        private sealed class HandPoserState
        {
            public HandPoser Poser;
            public float Weight;
        }

        private sealed class ArmIkState
        {
            public LimbIK Limb;
            public float PositionWeight;
            public float RotationWeight;
            public bool IsRight;
        }

        // Stores a support-arm bone relative to the weapon so it retains the current live grip
        // while the donor clip moves the weapon and opposite manipulating arm.
        private sealed class AnchoredPose
        {
            public Transform Target;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
        }

        private readonly List<TransformMap> _armMaps = new List<TransformMap>();
        private readonly List<TransformMap> _weaponMaps = new List<TransformMap>();
        private readonly List<HandPoserState> _handPosers = new List<HandPoserState>();
        private readonly List<ArmIkState> _armIk = new List<ArmIkState>();
        private readonly List<AnchoredPose> _supportPose = new List<AnchoredPose>();

        private FoldThatStockPlugin _plugin;
        private object _operationState;
        private FoldOperation _foldOperation;
        private AnimationClip _clip;
        private Transform _animatorRoot;
        private TransformMap _weaponRootMap;
        private TransformMap _manipulatingPalmMap;
        private Transform _weaponAnchor;
        private Transform _manipulatingIkMarker;
        private GameObject _sampleRoot;
        private PlayableGraph _sampleGraph;
        private AnimationClipPlayable _samplePlayable;
        private float _elapsed;
        private float _contactSeconds;
        private float _stockTransitionSeconds;
        private string _donorDescription;
        private bool _manipulatesRightArm;
        private bool _visualReleased;
        private bool _operationCompletedForHandoff;
        private bool _running;

        internal bool IsRunning => _running;

        // Builds all transient sampling state for one fold/unfold operation. Nothing created
        // here survives Finish, so changing weapons cannot leak donor transforms or IK weights.
        internal bool Begin(
            FoldThatStockPlugin plugin,
            object operationState,
            FoldOperation foldOperation,
            Animator animator,
            Transform playerTransform,
            AnimationClip clip,
            bool manipulatesRightArm,
            float contactSeconds,
            float stockTransitionSeconds,
            string donorDescription)
        {
            if (_running
                || plugin == null
                || operationState == null
                || foldOperation == null
                || animator == null
                || clip == null)
            {
                return false;
            }

            _plugin = plugin;
            _operationState = operationState;
            _foldOperation = foldOperation;
            _clip = clip;
            _animatorRoot = animator.transform;
            _manipulatesRightArm = manipulatesRightArm;
            _contactSeconds = Mathf.Max(0f, contactSeconds);
            _stockTransitionSeconds = Mathf.Max(0.001f, stockTransitionSeconds);
            _donorDescription = string.IsNullOrWhiteSpace(donorDescription)
                ? "donor"
                : donorDescription;

            if (!BuildSampleRig(animator) || !BuildSampleGraph())
            {
                DestroySampleGraph();
                DestroySampleRig();
                return false;
            }

            CaptureReferencePose();
            BindRenderedArmBones(animator.transform.root, playerTransform);
            BindProceduralControls(playerTransform ?? animator.transform.root);
            CaptureSupportPose();
            _running = true;
            return true;
        }

        internal void Cancel(bool completeOperation)
        {
            Finish(completeOperation);
        }

        private void LateUpdate()
        {
            if (!_running)
            {
                return;
            }

            if (_plugin == null
                || (!_operationCompletedForHandoff
                    && !_plugin.IsFoldOperationCurrent(_operationState, _foldOperation)))
            {
                Finish(false);
                return;
            }

            _elapsed += Mathf.Max(0f, Time.deltaTime);

            // Complete EFT's operation before fade-out begins. The remaining overlay weight
            // then blends into the correct final idle pose instead of the pre-fold idle pose.
            float handoffStart = Mathf.Max(0f, _clip.length - PoseFadeOutSeconds);
            if (!_operationCompletedForHandoff && _elapsed >= handoffStart)
            {
                if (!_plugin.CompleteDonorOperationForHandoff(_operationState, _foldOperation))
                {
                    Finish(false);
                    return;
                }

                _operationCompletedForHandoff = true;
            }

            // FinalIK and HandPoser normally reassert the weapon-holding pose every frame, so
            // keep them suppressed until after the donor transforms have been applied.
            SuppressProceduralControls();
            if (!_visualReleased && _elapsed >= _contactSeconds)
            {
                _visualReleased = true;
                _plugin.ReleaseVisualStock(
                    _foldOperation.Foldable,
                    true,
                    _stockTransitionSeconds);
            }

            try
            {
                float sampleTime = Mathf.Clamp(_elapsed, 0f, _clip.length);
                EvaluateSampleAt(sampleTime);
                float weight = CalculatePoseWeight(sampleTime);
                ApplyWeaponPose(weight);
                ApplyManipulatingArmPose(weight);
                ApplyManipulatingArmIk(weight);
                ApplySupportPose();
            }
            catch (Exception exception)
            {
                _plugin.LogAnimationFailureOnce(
                    "hybrid-sample-animation-" + _donorDescription,
                    $"{_donorDescription} hybrid animation stopped after Unity failed to sample the clip: {exception.Message}");
                Finish(true);
                return;
            }

            if (_elapsed >= _clip.length)
            {
                Finish(!_operationCompletedForHandoff);
            }
        }

        // Recreates only paths addressed by the chosen clip: both arm hierarchies and
        // Weapon_root/Weapon_root_anim/weapon. Donor stock and AK attachment tracks are omitted.
        private bool BuildSampleRig(Animator animator)
        {
            _sampleRoot = new GameObject("FoldThatStock_AnimationSample");
            _sampleRoot.hideFlags = HideFlags.HideAndDontSave;

            Transform[] transforms = animator.GetComponentsInChildren<Transform>(true);
            Transform left = transforms.FirstOrDefault(item => item.name == LeftCollarboneName);
            Transform right = transforms.FirstOrDefault(item => item.name == RightCollarboneName);
            Transform weaponRoot = transforms.FirstOrDefault(item => item.name == "Weapon_root");
            if (left == null || right == null || weaponRoot == null)
            {
                return false;
            }

            CloneArm(left, _sampleRoot.transform, false);
            CloneArm(right, _sampleRoot.transform, true);
            if (!CloneWeaponBackbone(weaponRoot))
            {
                return false;
            }

            string markerName = _manipulatesRightArm
                ? "weapon_R_IK_marker"
                : "weapon_L_IK_marker";
            _manipulatingIkMarker = weaponRoot
                .GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(item => item.name == markerName);
            return _armMaps.Count >= 12
                && _weaponMaps.Count == 3
                && _weaponRootMap != null
                && _manipulatingPalmMap != null
                && _weaponAnchor != null
                && _manipulatingIkMarker != null;
        }

        private void CloneArm(Transform live, Transform sampleParent, bool isRight)
        {
            Transform sample = CreateSampleTransform(live, sampleParent);
            TransformMap map = CreateMap(live, sample);
            map.IsRightArm = isRight;
            _armMaps.Add(map);
            string palmName = _manipulatesRightArm ? "Base HumanRPalm" : "Base HumanLPalm";
            if (isRight == _manipulatesRightArm && live.name == palmName)
            {
                _manipulatingPalmMap = map;
            }

            for (int i = 0; i < live.childCount; i++)
            {
                Transform child = live.GetChild(i);
                if (child != null && child.name.StartsWith("Base Human", StringComparison.Ordinal))
                {
                    CloneArm(child, sample, isRight);
                }
            }
        }

        // Keeping only these three weapon nodes transfers the UMP sway without allowing its
        // receiver, stock, dust cover, or attachment curves to affect the live AK hierarchy.
        private bool CloneWeaponBackbone(Transform weaponRoot)
        {
            Transform weaponRootSample = CreateSampleTransform(weaponRoot, _sampleRoot.transform);
            _weaponRootMap = CreateMap(weaponRoot, weaponRootSample);
            _weaponMaps.Add(_weaponRootMap);

            Transform weaponRootAnim = FindDirectChild(weaponRoot, "Weapon_root_anim");
            Transform weapon = FindDirectChild(weaponRootAnim, "weapon");
            if (weaponRootAnim == null || weapon == null)
            {
                return false;
            }

            Transform weaponRootAnimSample = CreateSampleTransform(weaponRootAnim, weaponRootSample);
            _weaponMaps.Add(CreateMap(weaponRootAnim, weaponRootAnimSample));
            Transform weaponSample = CreateSampleTransform(weapon, weaponRootAnimSample);
            _weaponMaps.Add(CreateMap(weapon, weaponSample));
            _weaponAnchor = weapon;
            return true;
        }

        private static Transform FindDirectChild(Transform parent, string name)
        {
            if (parent == null)
            {
                return null;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child != null && child.name == name)
                {
                    return child;
                }
            }

            return null;
        }

        private static Transform CreateSampleTransform(Transform live, Transform parent)
        {
            GameObject sampleObject = new GameObject(live.name);
            sampleObject.hideFlags = HideFlags.HideAndDontSave;
            Transform sample = sampleObject.transform;
            sample.SetParent(parent, false);
            sample.localPosition = live.localPosition;
            sample.localRotation = live.localRotation;
            sample.localScale = live.localScale;
            return sample;
        }

        private static TransformMap CreateMap(Transform live, Transform sample)
        {
            return new TransformMap
            {
                Live = live,
                Sample = sample,
                LiveReferencePosition = live.localPosition,
                LiveReferenceRotation = live.localRotation
            };
        }

        // A manually evaluated PlayableGraph gives deterministic clip poses without changing
        // the live weapon Animator's controller, parameters, layers, or current state.
        private bool BuildSampleGraph()
        {
            try
            {
                Animator sampleAnimator = _sampleRoot.AddComponent<Animator>();
                sampleAnimator.applyRootMotion = false;
                sampleAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                _sampleGraph = PlayableGraph.Create("FoldThatStock_AnimationSampleGraph");
                _sampleGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                _samplePlayable = AnimationClipPlayable.Create(_sampleGraph, _clip);
                _samplePlayable.SetApplyFootIK(false);
                _samplePlayable.SetApplyPlayableIK(false);
                AnimationPlayableOutput output = AnimationPlayableOutput.Create(
                    _sampleGraph,
                    "FoldThatStock_AnimationSampleOutput",
                    sampleAnimator);
                output.SetSourcePlayable(_samplePlayable);
                _sampleGraph.Play();
                EvaluateSampleAt(0f);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void EvaluateSampleAt(float time)
        {
            _samplePlayable.SetTime(time);
            _sampleGraph.Evaluate(0f);
        }

        // Frame zero is the shared coordinate reference used to retarget donor motion onto the
        // current weapon pose without inheriting the donor weapon's absolute placement.
        private void CaptureReferencePose()
        {
            EvaluateSampleAt(0f);
            CaptureReferencePose(_armMaps);
            CaptureReferencePose(_weaponMaps);
        }

        private static void CaptureReferencePose(List<TransformMap> maps)
        {
            for (int i = 0; i < maps.Count; i++)
            {
                TransformMap map = maps[i];
                map.DonorReferencePosition = map.Sample.localPosition;
                map.DonorReferenceRotation = map.Sample.localRotation;
            }
        }

        // EFT may animate proxy bones while a different hierarchy skins the visible sleeves
        // and gloves. Match by stable bone name so both hierarchies receive the sampled pose.
        private void BindRenderedArmBones(Transform animatorRoot, Transform playerTransform)
        {
            Dictionary<string, TransformMap> mapsByName = _armMaps
                .GroupBy(map => map.Live.name)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            HashSet<SkinnedMeshRenderer> renderers = new HashSet<SkinnedMeshRenderer>();
            AddRenderers(animatorRoot, renderers);
            AddRenderers(playerTransform, renderers);

            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Transform[] bones = renderer.bones;
                for (int i = 0; i < bones.Length; i++)
                {
                    Transform bone = bones[i];
                    TransformMap map;
                    if (bone == null
                        || !mapsByName.TryGetValue(bone.name, out map)
                        || ReferenceEquals(bone, map.Live)
                        || map.RenderTargets.Contains(bone))
                    {
                        continue;
                    }

                    map.RenderTargets.Add(bone);
                }
            }
        }

        private static void AddRenderers(Transform root, HashSet<SkinnedMeshRenderer> renderers)
        {
            if (root == null)
            {
                return;
            }

            SkinnedMeshRenderer[] found = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] != null)
                {
                    renderers.Add(found[i]);
                }
            }
        }

        // Capture the complete visible support-arm chain relative to the moving weapon. For
        // UMP this keeps the left palm on the handguard; for SIG it keeps the right palm on
        // the pistol grip while the left hand performs the fold/collapse gesture.
        private void CaptureSupportPose()
        {
            _supportPose.Clear();
            string supportPalmName = _manipulatesRightArm
                ? "Base HumanLPalm"
                : "Base HumanRPalm";
            if (_weaponAnchor == null
                || !_armMaps.Any(map => map.IsRightArm != _manipulatesRightArm
                    && map.Live.name == supportPalmName))
            {
                return;
            }

            HashSet<Transform> captured = new HashSet<Transform>();
            for (int i = 0; i < _armMaps.Count; i++)
            {
                TransformMap map = _armMaps[i];
                if (map.IsRightArm == _manipulatesRightArm)
                {
                    continue;
                }

                CaptureAnchoredPose(map.Live, captured);
                for (int targetIndex = 0; targetIndex < map.RenderTargets.Count; targetIndex++)
                {
                    CaptureAnchoredPose(map.RenderTargets[targetIndex], captured);
                }
            }
        }

        private void CaptureAnchoredPose(Transform target, HashSet<Transform> captured)
        {
            if (target == null || !captured.Add(target))
            {
                return;
            }

            _supportPose.Add(new AnchoredPose
            {
                Target = target,
                LocalPosition = _weaponAnchor.InverseTransformPoint(target.position),
                LocalRotation = Quaternion.Inverse(_weaponAnchor.rotation) * target.rotation
            });
        }

        // Save every modified procedural weight before suppression so cancellation, errors,
        // and normal completion all restore EFT's original hand/arm control values exactly.
        private void BindProceduralControls(Transform playerTransform)
        {
            if (playerTransform == null)
            {
                return;
            }

            HandPoser[] handPosers = playerTransform.GetComponentsInChildren<HandPoser>(true);
            for (int i = 0; i < handPosers.Length; i++)
            {
                HandPoser poser = handPosers[i];
                if (poser != null)
                {
                    _handPosers.Add(new HandPoserState { Poser = poser, Weight = poser.weight });
                }
            }

            LimbIK[] limbs = playerTransform.GetComponentsInChildren<LimbIK>(true);
            for (int i = 0; i < limbs.Length; i++)
            {
                LimbIK limb = limbs[i];
                if (limb?.solver == null
                    || (limb.solver.goal != AvatarIKGoal.LeftHand
                        && limb.solver.goal != AvatarIKGoal.RightHand))
                {
                    continue;
                }

                _armIk.Add(new ArmIkState
                {
                    Limb = limb,
                    PositionWeight = limb.solver.IKPositionWeight,
                    RotationWeight = limb.solver.IKRotationWeight,
                    IsRight = limb.solver.goal == AvatarIKGoal.RightHand
                });
            }

            SuppressProceduralControls();
        }

        private void SuppressProceduralControls()
        {
            for (int i = 0; i < _handPosers.Count; i++)
            {
                HandPoser poser = _handPosers[i].Poser;
                if (poser != null)
                {
                    poser.weight = 0f;
                }
            }

            for (int i = 0; i < _armIk.Count; i++)
            {
                LimbIK limb = _armIk[i].Limb;
                if (limb?.solver != null)
                {
                    limb.solver.IKPositionWeight = 0f;
                    limb.solver.IKRotationWeight = 0f;
                }
            }
        }

        private void RestoreProceduralControls()
        {
            for (int i = 0; i < _handPosers.Count; i++)
            {
                HandPoserState state = _handPosers[i];
                if (state?.Poser != null)
                {
                    state.Poser.weight = state.Weight;
                }
            }

            for (int i = 0; i < _armIk.Count; i++)
            {
                ArmIkState state = _armIk[i];
                if (state?.Limb?.solver != null)
                {
                    state.Limb.solver.IKPositionWeight = state.PositionWeight;
                    state.Limb.solver.IKRotationWeight = state.RotationWeight;
                }
            }

            _handPosers.Clear();
            _armIk.Clear();
        }

        // Transfer only the donor's shared weapon-motion backbone. Child-part animation and
        // the custom quaternion-driven stock pivot remain under their original owners.
        private void ApplyWeaponPose(float weight)
        {
            Quaternion alignment = GetRootAlignment();
            for (int i = 0; i < _weaponMaps.Count; i++)
            {
                TransformMap map = _weaponMaps[i];
                Vector3 position = map.Sample.localPosition;
                Quaternion rotation = map.Sample.localRotation;
                if (ReferenceEquals(map, _weaponRootMap))
                {
                    position = _weaponRootMap.LiveReferencePosition
                        + alignment * (position - _weaponRootMap.DonorReferencePosition);
                    rotation = alignment * rotation;
                }

                ApplyLocalPose(map.Live, position, rotation, weight);
            }
        }

        // Copy the selected manipulating arm, including wrist and fingers, to proxy and rendered
        // hierarchies. Root alignment converts the donor pose into the live coordinate frame.
        private void ApplyManipulatingArmPose(float weight)
        {
            Quaternion alignment = GetRootAlignment();
            for (int i = 0; i < _armMaps.Count; i++)
            {
                TransformMap map = _armMaps[i];
                if (map.IsRightArm != _manipulatesRightArm)
                {
                    continue;
                }

                bool isRoot = ReferenceEquals(map.Sample.parent, _sampleRoot.transform);
                Vector3 position = map.Sample.localPosition;
                Quaternion rotation = map.Sample.localRotation;
                if (isRoot)
                {
                    position = _weaponRootMap.LiveReferencePosition
                        + alignment * (position - _weaponRootMap.DonorReferencePosition);
                    rotation = alignment * rotation;
                }

                ApplyLocalPose(map.Live, position, rotation, weight);
                for (int targetIndex = 0; targetIndex < map.RenderTargets.Count; targetIndex++)
                {
                    Transform target = map.RenderTargets[targetIndex];
                    if (target == null)
                    {
                        continue;
                    }

                    if (isRoot)
                    {
                        ApplyWorldPose(
                            target,
                            _animatorRoot.TransformPoint(position),
                            _animatorRoot.rotation * rotation,
                            weight);
                    }
                    else
                    {
                        ApplyLocalPose(target, position, rotation, weight);
                    }
                }
            }
        }

        // Run the selected hand solver once against the donor palm target, then suppress it
        // again before EFT can restore the normal weapon-handling pose.
        private void ApplyManipulatingArmIk(float weight)
        {
            Vector3 donorPosition;
            Quaternion donorRotation;
            bool hasDonorPose = TryGetManipulatingPalmWorldPose(out donorPosition, out donorRotation);

            for (int i = 0; i < _armIk.Count; i++)
            {
                ArmIkState state = _armIk[i];
                LimbIK limb = state.Limb;
                if (state.IsRight != _manipulatesRightArm
                    || limb?.solver == null
                    || _manipulatingIkMarker == null)
                {
                    continue;
                }

                Vector3 position = _manipulatingIkMarker.position;
                Quaternion rotation = _manipulatingIkMarker.rotation;
                if (hasDonorPose)
                {
                    position = Vector3.Lerp(position, donorPosition, weight);
                    rotation = Quaternion.Slerp(rotation, donorRotation, weight);
                }

                limb.solver.IKPositionWeight = Mathf.Lerp(state.PositionWeight, 1f, weight);
                limb.solver.IKRotationWeight = Mathf.Lerp(state.RotationWeight, 1f, weight);
                limb.solver.SetIKPosition(position);
                limb.solver.SetIKRotation(rotation);
            }

            try
            {
                for (int i = 0; i < _armIk.Count; i++)
                {
                    ArmIkState state = _armIk[i];
                    if (state.IsRight == _manipulatesRightArm && state.Limb?.solver != null)
                    {
                        state.Limb.solver.Update();
                    }
                }
            }
            finally
            {
                SuppressProceduralControls();
            }
        }

        private bool TryGetManipulatingPalmWorldPose(out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (_manipulatingPalmMap?.Sample == null
                || _sampleRoot == null
                || _animatorRoot == null
                || _weaponRootMap?.Sample == null)
            {
                return false;
            }

            Quaternion alignment = GetRootAlignment();
            Vector3 samplePosition = _sampleRoot.transform.InverseTransformPoint(_manipulatingPalmMap.Sample.position);
            Quaternion sampleRotation = Quaternion.Inverse(_sampleRoot.transform.rotation)
                * _manipulatingPalmMap.Sample.rotation;
            Vector3 alignedPosition = _weaponRootMap.LiveReferencePosition
                + alignment * (samplePosition - _weaponRootMap.DonorReferencePosition);
            Quaternion alignedRotation = alignment * sampleRotation;
            position = _animatorRoot.TransformPoint(alignedPosition);
            rotation = _animatorRoot.rotation * alignedRotation;
            return true;
        }

        private Quaternion GetRootAlignment()
        {
            return _weaponRootMap.LiveReferenceRotation
                * Quaternion.Inverse(_weaponRootMap.DonorReferenceRotation);
        }

        // Reapply the captured support grip last, after donor sampling and manipulating-arm IK.
        private void ApplySupportPose()
        {
            if (_weaponAnchor == null)
            {
                return;
            }

            for (int i = 0; i < _supportPose.Count; i++)
            {
                AnchoredPose pose = _supportPose[i];
                if (pose?.Target != null)
                {
                    ApplyWorldPose(
                        pose.Target,
                        _weaponAnchor.TransformPoint(pose.LocalPosition),
                        _weaponAnchor.rotation * pose.LocalRotation,
                        1f);
                }
            }
        }

        private static void ApplyLocalPose(
            Transform target,
            Vector3 position,
            Quaternion rotation,
            float weight)
        {
            target.localPosition = Vector3.Lerp(target.localPosition, position, weight);
            target.localRotation = Quaternion.Slerp(target.localRotation, rotation, weight);
        }

        private static void ApplyWorldPose(
            Transform target,
            Vector3 position,
            Quaternion rotation,
            float weight)
        {
            target.position = Vector3.Lerp(target.position, position, weight);
            target.rotation = Quaternion.Slerp(target.rotation, rotation, weight);
        }

        // Smooth entry hides the change from EFT's normal grip. The 0.30-second exit blends
        // the donor's final frame into the already-completed folded/unfolded idle state.
        private float CalculatePoseWeight(float sampleTime)
        {
            float fadeIn = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(sampleTime / PoseFadeInSeconds));
            float remaining = Mathf.Max(0f, _clip.length - sampleTime);
            float fadeOut = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(remaining / PoseFadeOutSeconds));
            return Mathf.Min(fadeIn, fadeOut);
        }

        // Centralized teardown is intentionally used by success, cancellation, exceptions,
        // and component destruction so procedural controls and operation state cannot stick.
        private void Finish(bool completeOperation)
        {
            if (!_running)
            {
                return;
            }

            _running = false;
            FoldThatStockPlugin plugin = _plugin;
            object operationState = _operationState;
            FoldOperation foldOperation = _foldOperation;
            RestoreProceduralControls();
            DestroySampleGraph();
            DestroySampleRig();
            plugin?.FinishDonorAnimation(operationState, foldOperation, completeOperation);
            Destroy(this);
        }

        private void OnDestroy()
        {
            if (_running)
            {
                Finish(true);
                return;
            }

            RestoreProceduralControls();
            DestroySampleGraph();
            DestroySampleRig();
        }

        private void DestroySampleGraph()
        {
            if (_sampleGraph.IsValid())
            {
                _sampleGraph.Destroy();
            }
        }

        private void DestroySampleRig()
        {
            _armMaps.Clear();
            _weaponMaps.Clear();
            _supportPose.Clear();
            if (_sampleRoot != null)
            {
                Destroy(_sampleRoot);
                _sampleRoot = null;
            }
        }
    }
}
