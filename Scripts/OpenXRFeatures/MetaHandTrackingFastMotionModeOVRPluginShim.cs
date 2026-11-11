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

#if USING_XR_SDK_OPENXR
namespace Meta.XR
{
    partial class MetaXRFeature
    {
        private bool _fastMotionModeEnabled;

        /// <summary>
        /// OVRPlugin shim for <c>ovrp_RequestFastMotionMode</c>. Records the requested state and applies the hand
        /// tracking frequency hint. The state is re-applied automatically on session begin.
        /// </summary>
        internal OVRPlugin.Result ovrp_RequestFastMotionMode(bool request)
        {
            _fastMotionModeEnabled = request;
            return ApplyFrequencyHint();
        }

        private OVRPlugin.Result ApplyFrequencyHint()
        {
            if (Session == 0)
                return OVRPlugin.Result.Failure_NotInitialized;

            if (!_handTrackingFrequencyHintEnabled)
            {
                if (_fastMotionModeEnabled)
                    LogError("Tried to enable Fast Motion Mode but the " +
                             "'XR_META_hand_tracking_frequency_hint' extension is not enabled.");
                return OVRPlugin.Result.Failure_Unsupported;
            }

            if (Command.xrSetHandTrackingFrequencyHintMETA == null)
            {
                LogError("xrSetHandTrackingFrequencyHintMETA command was not loaded.");
                return OVRPlugin.Result.Failure_Unsupported;
            }

            var hint = _fastMotionModeEnabled
                ? XrHandTrackingFrequencyHintMETA.High
                : XrHandTrackingFrequencyHintMETA.Default;
            var result = Command.xrSetHandTrackingFrequencyHintMETA(Session, hint);
            result.OrLogFormat(LogPrefix + nameof(Command.xrSetHandTrackingFrequencyHintMETA));
            return result.ToOVRPluginType();
        }
    }
}
#endif // USING_XR_SDK_OPENXR
