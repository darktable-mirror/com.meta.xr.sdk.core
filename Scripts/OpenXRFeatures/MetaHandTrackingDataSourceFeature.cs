/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * Licensed under the Oculus SDK License Agreement (the "License");
 * you may not use the Oculus SDK except in compliance with the License,
 * which is provided at the time of installation or download, or which
 * otherwise accompanies this software in either electronic or hard copy form.
 *
 * You may obtain a copy of the License at
 *
 * https://developer.oculus.com/licenses/oculussdk/
 *
 * Unless required by applicable law or agreed to in writing, the Oculus SDK
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

#if USING_XR_HANDS_1_9_0_OR_NEWER && UNITY_OPENXR_PLUGIN_1_18_0_OR_NEWER

using System;
using UnityEngine;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.OpenXR;
using UnityEngine.XR.Hands.OpenXR.NativeInterop;

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
#endif

// Using Unity native types for XrHandEXT to avoid the need for type conversion
using NativeTypes = UnityEngine.XR.OpenXR.NativeTypes;

namespace Meta.XR
{
#if UNITY_EDITOR
    [MetaOpenXRFeature(
        featureId: featureId,
        uiName: "Meta XR Hand Tracking Data Source",
        desc: "Owns the hand tracking data source and joints motion range for Unity XR Hands, including the " +
              "Meta-only Wide Motion Mode 2 source (inferred poses outside the normal tracking field of view).",
        version: "0.0.1",
        extensions: new[] { dataSourceExtensionString, wideMotionExtensionString, motionRangeExtensionString })]
#endif
    public unsafe class MetaHandTrackingDataSourceFeature : OpenXRHandTrackingFeature
    {
        /// <summary>
        /// The feature ID string. This is used to give the feature a well known ID for reference.
        /// </summary>
        public const string featureId = "com.meta.openxr.feature.handtrackingdatasource";

        /// <summary>
        /// The OpenXR extension string for the hand tracking data source chain.
        /// </summary>
        public const string dataSourceExtensionString = "XR_EXT_hand_tracking_data_source";

        /// <summary>
        /// The OpenXR extension string for the hand joints motion range chain.
        /// </summary>
        public const string motionRangeExtensionString = "XR_EXT_hand_joints_motion_range";

        /// <summary>
        /// The OpenXR extension string for Wide Motion Mode 2 (the Meta-only data source value).
        /// </summary>
        public const string wideMotionExtensionString = "XR_META_hand_tracking_wide_motion_mode2";

        // Type names used to detect Unity's counterpart features at validation time.
        const string k_DataSourceFeatureTypeName =
            "UnityEngine.XR.Hands.OpenXR.HandTrackingDataSourceFeature";
        const string k_MotionRangeFeatureTypeName =
            "UnityEngine.XR.Hands.OpenXR.HandJointsMotionRangeFeature";

        [SerializeField]
        [Tooltip("When enabled, requests the Meta Wide Motion Mode 2 source (inferred poses outside the normal tracking field of view).")]
        bool m_WideMotionModeEnabled;

        [SerializeField]
        [Tooltip("Controller-driven hand poses mode (simultaneous hands and controllers). ConformingToController conforms the motion range to the controller; Natural keeps the unobstructed motion range.")]
        OVRManager.ControllerDrivenHandPosesType m_ControllerDrivenHandPoses;

        NativeArray<XrHandTrackingDataSourceEXT> m_OnlyHandSource;
        NativeArray<XrHandTrackingDataSourceEXT> m_HandAndControllerSource;
        NativeArray<XrHandTrackingDataSourceEXT> m_OnlyHandSourceWMM;
        NativeArray<XrHandTrackingDataSourceEXT> m_HandAndControllerSourceWMM;

        bool m_LeftPosesInferredFromBody;
        bool m_RightPosesInferredFromBody;

        bool m_LeftPosesGeneratedByControllerData;
        bool m_RightPosesGeneratedByControllerData;

        // Mirrors OVRPlugin's m_xrControllerDrivenHandPosesAreNatural: sticky across enable/disable, so re-enabling
        // the controller source restores the previously selected motion range.
        bool m_NaturalRequested;

        bool m_WideMotionExtensionEnabled;
        bool m_MotionRangeExtensionEnabled;

        bool m_TrackerWideMotionModeEnabled;
        bool m_TrackerControllerDrivenHandPoses;

        /// <summary>
        /// The singleton instance of this feature, or <c>null</c>. Set when the OpenXR instance is created
        /// (<see cref="OnInstanceCreate"/>) and cleared when it is destroyed (<see cref="OnInstanceDestroy"/>).
        /// </summary>
        static MetaHandTrackingDataSourceFeature s_featureInstance;

        /// <summary>
        /// Gets the singleton feature instance when it exists and is enabled. Mirrors
        /// <c>Meta.XR.MetaFeatureBase&lt;T&gt;.TryGet</c>.
        /// </summary>
        /// <param name="feature">The feature instance, or <c>null</c> if unavailable.</param>
        /// <returns><c>true</c> if the feature exists and is enabled, otherwise <c>false</c>.</returns>
        public static bool TryGet(out MetaHandTrackingDataSourceFeature feature) =>
            (feature = s_featureInstance) && feature.enabled;

        /// <summary>
        /// Requests (or stops requesting) the Meta Wide Motion Mode 2 source (inferred poses outside the normal
        /// tracking field of view). Applies the hand-pose modes. Mirrors OVRPlugin's
        /// <c>SetWideMotionModeHandPoses</c>.
        /// </summary>
        public void SetWideMotionModeHandPoses(bool enabled)
        {
            m_WideMotionModeEnabled = enabled;
            ApplyWideMotionMode();
        }

        /// <summary>
        /// Returns <c>true</c> when the Meta Wide Motion Mode 2 source (inferred poses outside the normal tracking
        /// field of view) is requested. Mirrors OVRPlugin's <c>IsWideMotionModeHandPosesEnabled</c>.
        /// </summary>
        public bool IsWideMotionModeHandPosesEnabled() => m_WideMotionModeEnabled;

        /// <summary>
        /// The controller-driven hand poses mode. Setting this applies the
        /// hand-pose modes. <c>None</c> requests no controller source; <c>ConformingToController</c> and
        /// <c>Natural</c> both request the controller source, differing only in motion range. Assigning
        /// <c>ConformingToController</c> or <c>Natural</c> also updates the motion range remembered by
        /// <see cref="SetControllerDrivenHandPosesAreNatural"/>.
        /// </summary>
        public OVRManager.ControllerDrivenHandPosesType ControllerDrivenHandPoses
        {
            get => m_ControllerDrivenHandPoses;
            set
            {
                m_ControllerDrivenHandPoses = value;
                if (value != OVRManager.ControllerDrivenHandPosesType.None)
                    m_NaturalRequested = (value == OVRManager.ControllerDrivenHandPosesType.Natural);
                ApplyControllerDrivenHandPoses();
            }
        }

        /// <summary>
        /// Requests (or stops requesting) the controller data source for hand poses (simultaneous hands and
        /// controllers). Mirrors OVRPlugin's <c>SetControllerDrivenHandPoses</c>: enabling restores the motion range
        /// last selected by <see cref="SetControllerDrivenHandPosesAreNatural"/>, defaulting to
        /// <c>ConformingToController</c>; disabling clears the controller source but preserves that selection.
        /// </summary>
        public void SetControllerDrivenHandPoses(bool enabled)
        {
            if (!enabled)
                ControllerDrivenHandPoses = OVRManager.ControllerDrivenHandPosesType.None;
            else if (m_NaturalRequested)
                ControllerDrivenHandPoses = OVRManager.ControllerDrivenHandPosesType.Natural;
            else
                ControllerDrivenHandPoses = OVRManager.ControllerDrivenHandPosesType.ConformingToController;
        }

        /// <summary>
        /// Selects the motion range for controller-driven hand poses. Mirrors OVRPlugin's
        /// <c>SetControllerDrivenHandPosesAreNatural</c>: <c>true</c> keeps the unobstructed (Natural) motion range,
        /// <c>false</c> conforms to the controller. The selection is remembered while the controller source is not
        /// requested and applies the next time it is.
        /// </summary>
        public void SetControllerDrivenHandPosesAreNatural(bool natural)
        {
            m_NaturalRequested = natural;

            if (m_ControllerDrivenHandPoses == OVRManager.ControllerDrivenHandPosesType.None)
                return;

            if (natural)
                ControllerDrivenHandPoses = OVRManager.ControllerDrivenHandPosesType.Natural;
            else
                ControllerDrivenHandPoses = OVRManager.ControllerDrivenHandPosesType.ConformingToController;
        }

        void ApplyWideMotionMode()
        {
            if (m_WideMotionModeEnabled && !m_WideMotionExtensionEnabled)
                Debug.LogError($"MetaHandTrackingDataSourceFeature: Tried to set Wide Motion Mode but " +
                               $"'{wideMotionExtensionString}' extension is not enabled.");

            if (m_WideMotionModeEnabled != m_TrackerWideMotionModeEnabled)
                RequestHandTrackerRestart();
        }


        void ApplyControllerDrivenHandPoses()
        {
            bool includeController = m_ControllerDrivenHandPoses != OVRManager.ControllerDrivenHandPosesType.None;
            if (m_ControllerDrivenHandPoses == OVRManager.ControllerDrivenHandPosesType.ConformingToController
                && !m_MotionRangeExtensionEnabled)
            {
                Debug.LogError($"MetaHandTrackingDataSourceFeature: Tried to conform controller driven hand poses to " +
                               $"the controller but the '{motionRangeExtensionString}' extension is not enabled.");
            }

            if (includeController != m_TrackerControllerDrivenHandPoses)
                RequestHandTrackerRestart();

            if (m_MotionRangeExtensionEnabled)
            {
                var motionRange =
                    m_ControllerDrivenHandPoses == OVRManager.ControllerDrivenHandPosesType.ConformingToController
                        ? XrHandJointsMotionRangeEXT.ConformingToController
                        : XrHandJointsMotionRangeEXT.Unobstructed;
                GetLocateInputChain(NativeTypes.XrHandEXT.Left)?.TryUpdateNode(new XrHandJointsMotionRangeInfoEXT
                {
                    Type = XrHandJointsMotionRangeInfoEXT.StructureType,
                    HandJointsMotionRange = motionRange,
                });
                GetLocateInputChain(NativeTypes.XrHandEXT.Right)?.TryUpdateNode(new XrHandJointsMotionRangeInfoEXT
                {
                    Type = XrHandJointsMotionRangeInfoEXT.StructureType,
                    HandJointsMotionRange = motionRange,
                });
            }
        }

        /// <summary>
        /// Returns <c>true</c> when the given hand's poses are currently inferred from body tracking (wide-motion
        /// poses produced outside the normal tracking field of view) rather than directly tracked. Equivalent to
        /// OVRPlugin's <c>ovrp_GetHandPoseSourceInferred</c> / <c>OVRHand.PoseSourceInferred</c>.
        /// </summary>
        public bool GetHandPoseSourceInferred(Handedness handedness)
        {
            if (handedness == Handedness.Left)
                return m_LeftPosesInferredFromBody;
            if (handedness == Handedness.Right)
                return m_RightPosesInferredFromBody;
            return false;
        }

        /// <summary>
        /// Returns <c>true</c> when the given hand's poses are currently being generated from controller data
        /// (simultaneous hands and controllers) rather than from tracked hands. Equivalent to OVRPlugin's
        /// <c>ovrp_AreHandPosesGeneratedByControllerData</c>. Only ever <c>true</c> when
        /// <see cref="ControllerDrivenHandPoses"/> requests the controller source.
        /// </summary>
        public bool AreHandPosesGeneratedByControllerData(Handedness handedness)
        {
            if (handedness == Handedness.Left)
                return m_LeftPosesGeneratedByControllerData;
            if (handedness == Handedness.Right)
                return m_RightPosesGeneratedByControllerData;
            return false;
        }

        /// <inheritdoc/>
        protected override bool OnInstanceCreate(ulong xrInstance)
        {
            if (!OpenXRRuntime.IsExtensionEnabled(dataSourceExtensionString))
                return false;

            if (!base.OnInstanceCreate(xrInstance))
                return false;

            m_WideMotionExtensionEnabled = OpenXRRuntime.IsExtensionEnabled(wideMotionExtensionString);
            m_MotionRangeExtensionEnabled = OpenXRRuntime.IsExtensionEnabled(motionRangeExtensionString);

            m_OnlyHandSource = new NativeArray<XrHandTrackingDataSourceEXT>(
                new[] { XrHandTrackingDataSourceEXT.Unobstructed }, Allocator.Persistent);
            m_HandAndControllerSource = new NativeArray<XrHandTrackingDataSourceEXT>(
                new[] { XrHandTrackingDataSourceEXT.Unobstructed, XrHandTrackingDataSourceEXT.Controller },
                Allocator.Persistent);
            m_OnlyHandSourceWMM = new NativeArray<XrHandTrackingDataSourceEXT>(
                new[] { XrHandTrackingDataSourceEXT.Unobstructed, XrHandTrackingDataSourceEXT.UnobstructedWideMotion },
                Allocator.Persistent);
            m_HandAndControllerSourceWMM = new NativeArray<XrHandTrackingDataSourceEXT>(
                new[]
                {
                    XrHandTrackingDataSourceEXT.Unobstructed,
                    XrHandTrackingDataSourceEXT.Controller,
                    XrHandTrackingDataSourceEXT.UnobstructedWideMotion,
                },
                Allocator.Persistent);

            m_NaturalRequested = m_ControllerDrivenHandPoses == OVRManager.ControllerDrivenHandPosesType.Natural;

            s_featureInstance = this;
            return true;
        }

        /// <inheritdoc/>
        protected override void OnInstanceDestroy(ulong xrInstance)
        {
            s_featureInstance = null;

            if (m_OnlyHandSource.IsCreated)
                m_OnlyHandSource.Dispose();
            if (m_HandAndControllerSource.IsCreated)
                m_HandAndControllerSource.Dispose();
            if (m_OnlyHandSourceWMM.IsCreated)
                m_OnlyHandSourceWMM.Dispose();
            if (m_HandAndControllerSourceWMM.IsCreated)
                m_HandAndControllerSourceWMM.Dispose();

            base.OnInstanceDestroy(xrInstance);
        }

        /// <inheritdoc/>
        protected override void OnSessionCreate(ulong xrSession)
        {
            base.OnSessionCreate(xrSession);

            var outputSlot = new XrHandTrackingDataSourceStateEXT
            {
                Type = XrHandTrackingDataSourceStateEXT.StructureType,
            };
            GetLocateOutputChain(NativeTypes.XrHandEXT.Left)?.TryAddNode(outputSlot);
            GetLocateOutputChain(NativeTypes.XrHandEXT.Right)?.TryAddNode(outputSlot);

            if (m_MotionRangeExtensionEnabled)
            {
                var motionRange =
                    m_ControllerDrivenHandPoses == OVRManager.ControllerDrivenHandPosesType.ConformingToController
                        ? XrHandJointsMotionRangeEXT.ConformingToController
                        : XrHandJointsMotionRangeEXT.Unobstructed;
                GetLocateInputChain(NativeTypes.XrHandEXT.Left)?.TryAddNode(new XrHandJointsMotionRangeInfoEXT
                {
                    Type = XrHandJointsMotionRangeInfoEXT.StructureType,
                    HandJointsMotionRange = motionRange,
                });
                GetLocateInputChain(NativeTypes.XrHandEXT.Right)?.TryAddNode(new XrHandJointsMotionRangeInfoEXT
                {
                    Type = XrHandJointsMotionRangeInfoEXT.StructureType,
                    HandJointsMotionRange = motionRange,
                });
            }
        }

        /// <inheritdoc/>
        protected override void OnHandTrackingCreateRequest(
            NativeTypes.XrHandEXT hand,
            XrStructureChain extensionChain)
        {
            bool controllerDrivenHandPoses = m_ControllerDrivenHandPoses != OVRManager.ControllerDrivenHandPosesType.None;
            m_TrackerWideMotionModeEnabled = m_WideMotionModeEnabled;
            m_TrackerControllerDrivenHandPoses = controllerDrivenHandPoses;

            var sources = m_OnlyHandSource;
            if (m_WideMotionModeEnabled && m_WideMotionExtensionEnabled)
                sources = controllerDrivenHandPoses ? m_HandAndControllerSourceWMM : m_OnlyHandSourceWMM;
            else if (controllerDrivenHandPoses)
                sources = m_HandAndControllerSource;

            var info = new XrHandTrackingDataSourceInfoEXT
            {
                Type = XrHandTrackingDataSourceInfoEXT.StructureType,
                RequestedDataSourceCount = (uint)sources.Length,
                RequestedDataSources = (XrHandTrackingDataSourceEXT*)sources.GetUnsafePtr(),
            };

            if (!extensionChain.TryAddNode(info))
                Debug.LogWarning("MetaHandTrackingDataSourceFeature: Failed to add data source info to the " +
                                 "extension chain. This can happen when Unity's Hand Tracking Data Source feature " +
                                 "is also enabled. Enable only one of them per project.");
        }

        /// <inheritdoc/>
        protected override void OnHandTrackerDestroyed(NativeTypes.XrHandEXT hand, NativeTypes.XrResult destroyResult)
        {
            if (hand == NativeTypes.XrHandEXT.Left)
            {
                m_LeftPosesInferredFromBody = false;
                m_LeftPosesGeneratedByControllerData = false;
            }
            else
            {
                m_RightPosesInferredFromBody = false;
                m_RightPosesGeneratedByControllerData = false;
            }
        }

        /// <inheritdoc/>
        protected override void OnLocateHandJointsResult(
            NativeTypes.XrHandEXT hand,
            XrStructureChain outputChain,
            NativeTypes.XrResult locateHandJointsResult,
            bool isActive)
        {
            ref var posesInferredFromBody = ref (hand == NativeTypes.XrHandEXT.Left
                ? ref m_LeftPosesInferredFromBody
                : ref m_RightPosesInferredFromBody);
            ref var posesGeneratedByControllerData = ref (hand == NativeTypes.XrHandEXT.Left
                ? ref m_LeftPosesGeneratedByControllerData
                : ref m_RightPosesGeneratedByControllerData);

            posesInferredFromBody = false;
            posesGeneratedByControllerData = false;

            if (locateHandJointsResult != NativeTypes.XrResult.Success || !isActive)
            {
                return;
            }

            if (outputChain.TryGetNode<XrHandTrackingDataSourceStateEXT>(
                NativeTypes.XrStructureType.HandTrackingDataSourceStateEXT,
                out var state))
            {
                if (state.IsActive && state.DataSource == XrHandTrackingDataSourceEXT.UnobstructedWideMotion)
                    posesInferredFromBody = true;

                if (state.IsActive
                    && m_ControllerDrivenHandPoses != OVRManager.ControllerDrivenHandPosesType.None
                    && state.DataSource == XrHandTrackingDataSourceEXT.Controller)
                {
                    posesGeneratedByControllerData = true;
                }
            }
        }

#if UNITY_EDITOR
        /// <inheritdoc/>
        protected override void GetValidationChecks(List<ValidationRule> results, BuildTargetGroup targetGroup)
        {
            results.Add(new ValidationRule(this)
            {
                message = "Meta XR Hand Tracking Data Source requires the Hand Tracking Subsystem feature to be enabled.",
                checkPredicate = () =>
                {
                    var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(targetGroup);
                    if (settings == null)
                        return false;

                    var handTracking = settings.GetFeature<HandTracking>();
                    return handTracking != null && handTracking.enabled;
                },
                fixIt = () =>
                {
                    var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(targetGroup);
                    if (settings == null)
                        return;

                    var handTracking = settings.GetFeature<HandTracking>();
                    if (handTracking != null)
                    {
                        handTracking.enabled = true;
                        EditorUtility.SetDirty(handTracking);
                    }
                },
                fixItAutomatic = true,
                error = true,
            });

            results.Add(new ValidationRule(this)
            {
                message = "Meta XR Hand Tracking Data Source and Unity's Hand Tracking Data Source feature both " +
                          "add a data source to the hand tracker create chain. Enable only one of them per project.",
                checkPredicate = () => !IsFeatureEnabled(targetGroup, k_DataSourceFeatureTypeName),
                fixIt = () => DisableFeature(targetGroup, k_DataSourceFeatureTypeName),
                fixItAutomatic = true,
                error = true,
            });

            results.Add(new ValidationRule(this)
            {
                message = "Meta XR Hand Tracking Data Source and Unity's Hand Joints Motion Range feature both " +
                          "add a motion range to the hand tracker chain. Enable only one of them per project.",
                checkPredicate = () => !IsFeatureEnabled(targetGroup, k_MotionRangeFeatureTypeName),
                fixIt = () => DisableFeature(targetGroup, k_MotionRangeFeatureTypeName),
                fixItAutomatic = true,
                error = true,
            });
        }

        static bool IsFeatureEnabled(BuildTargetGroup targetGroup, string typeName)
        {
            var feature = FindFeature(targetGroup, typeName);
            return feature != null && feature.enabled;
        }

        static void DisableFeature(BuildTargetGroup targetGroup, string typeName)
        {
            var feature = FindFeature(targetGroup, typeName);
            if (feature != null)
            {
                feature.enabled = false;
                EditorUtility.SetDirty(feature);
            }
        }

        static UnityEngine.XR.OpenXR.Features.OpenXRFeature FindFeature(BuildTargetGroup targetGroup, string typeName)
        {
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(targetGroup);
            if (settings == null)
                return null;

            foreach (var feature in settings.GetFeatures())
            {
                if (feature != null && feature.GetType().FullName == typeName)
                    return feature;
            }

            return null;
        }
#endif
    }
}

#endif // USING_XR_HANDS && UNITY_OPENXR_PLUGIN_1_18_0_OR_NEWER
