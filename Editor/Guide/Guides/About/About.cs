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

using Meta.XR.Editor.Id;
using Meta.XR.Editor.RemoteContent;
using Meta.XR.Editor.Settings;
using Meta.XR.Editor.ToolingSupport;
using Meta.XR.Editor.UserInterface;
using Meta.XR.Editor.Utils;
using Meta.XR.Guides.Editor.Nux;
using Meta.XR.Guides.Editor.Welcome;
using UnityEditor;
using UnityEngine;
using static Meta.XR.Editor.UserInterface.Styles.Colors;
using static Meta.XR.Editor.UserInterface.Styles.Contents;

namespace Meta.XR.Guides.Editor.About
{
    [InitializeOnLoad]
    internal static class About
    {
        public const string PackageName = "com.meta.xr.sdk.core";

        // It may be too early to retrieve version, so we use a nullable int to add the flag of
        // whether or not we could retrieve it
        private static int? _version;
        public static int? Version => _version ??= PackageList.ComputePackageVersion(PackageName);

        private static Onboarding _onboarding;
        private static Onboarding Onboarding => _onboarding ??= new Onboarding();

        private static int? _latestVersion;
        public static int? LatestVersion => _latestVersion ??= PackageList.ComputeLatestPackageVersion(PackageName);

        [MenuItem("Window/Meta/About Meta XR SDK", false, 4300)]
        private static void SetupGuide()
        {
            ShowGuide(Origins.Menu, true);
        }

        public static ToolDescriptor ToolDescriptor = new()
        {
            Order = -11,
            Icon = MetaWhiteIcon,
            Name = "Welcome to Meta XR SDK",
            MenuDescription = "Get Started",
            AddToStatusMenu = false,
            AddToMenu = false,
            OnClickDelegate = (origin) => ShowGuide(origin, true),
            InfoTextDelegate = ComputeInfoText,
            PillIcon = ComputePillIcon,
            AvailableVersionDelegate = () => LatestVersion,
            IsStatusMenuItemDarker = true
        };

        // Per project (EditorUserSettings), so projects on different SDK versions don't
        // re-trigger each other. Lowered on a downgrade, so the next upgrade still counts.
        private const string LastSeenVersionKey = "About.LastSeenVersion";

        private static int LastSeenVersion
        {
            get => int.TryParse(EditorUserSettings.GetConfigValue(LastSeenVersionKey), out var version) ? version : 0;
            set => EditorUserSettings.SetConfigValue(LastSeenVersionKey, value.ToString());
        }

        // Set by the removed "Show this window on launch" toggle.
        private const string LegacyShowOnLaunchKey = "Welcome.ShowOnLaunch";

        static About()
        {
            OVRTelemetryConsent.OnLibrariesConsentSet += OnConsentSet;
        }

        private static void OnConsentSet(bool enabled)
        {
            _ = enabled; // ignored, preferring to trust HasUnifiedConsentValue instead

            // delayCall so that the window waits for the full editor to be loaded before popping up.
            // ScheduleShowOnLaunch re-arms its poll on every domain reload (OnConsentSet re-fires
            // from OVREditorStart each reload), which is what makes the show survive the reloads
            // that are common during editor startup, and what catches an SDK upgrade done mid-session.
            EditorApplication.delayCall += ScheduleShowOnLaunch;
        }

        // Seconds to wait for the remote ramp-up keys to load before showing anyway.
        private const double ShowOnLaunchKeysTimeoutSeconds = 10.0;
        private static bool _showOnLaunchPolling;
        private static double _showOnLaunchDeadline;
        private static int _showOnLaunchVersion;

        private static void ScheduleShowOnLaunch()
        {
            if (!OVREditorUtils.IsMainEditor()
             || !OVRTelemetryConsent.HasUnifiedConsentValue
             || Application.isBatchMode
             || _showOnLaunchPolling)
            {
                return;
            }

            var installedVersion = ToolUsage.GetSdkVersion();
            if (!installedVersion.HasValue)
            {
                return;
            }

            MigrateLegacyShowOnLaunch(installedVersion.Value);

            if (installedVersion.Value < LastSeenVersion)
            {
                LastSeenVersion = installedVersion.Value;
            }

            if (installedVersion.Value <= LastSeenVersion)
            {
                return;
            }

            // Poll on EditorApplication.update instead of awaiting the ramp-up key fetch. An awaited
            // async continuation is destroyed by the domain reloads (script compilation / asset
            // import) that are common during editor startup, which left the welcome/NUX window never
            // appearing. We still wait for the keys so nux_flow routes correctly instead of reading
            // its cold-cache default (false); LastSeenVersion is written only when PollShowOnLaunch
            // dispatches the show, so an interrupted poll never burns the upgrade.
            _showOnLaunchPolling = true;
            _showOnLaunchVersion = installedVersion.Value;
            _showOnLaunchDeadline = EditorApplication.timeSinceStartup + ShowOnLaunchKeysTimeoutSeconds;
            EditorApplication.update += PollShowOnLaunch;
        }

        private static void PollShowOnLaunch()
        {
            if (!FeatureRampUpManager.AreKeysReady
             && EditorApplication.timeSinceStartup < _showOnLaunchDeadline)
            {
                return;
            }

            EditorApplication.update -= PollShowOnLaunch;
            _showOnLaunchPolling = false;

            if (!OVREditorUtils.IsMainEditor())
            {
                return;
            }

            LastSeenVersion = _showOnLaunchVersion;
            ShowGuide(Origins.Self, forceShow: true);
        }

        // A project that turned the toggle off starts at the installed version, so the first launch
        // with the new gating doesn't show it the window it opted out of.
        private static void MigrateLegacyShowOnLaunch(int installedVersion)
        {
            var legacyValue = EditorUserSettings.GetConfigValue(LegacyShowOnLaunchKey);
            if (legacyValue == null)
            {
                return;
            }

            if (legacyValue == "false" && EditorUserSettings.GetConfigValue(LastSeenVersionKey) == null)
            {
                LastSeenVersion = installedVersion;
            }

            EditorUserSettings.SetConfigValue(LegacyShowOnLaunchKey, null);
        }

        private const string NuxRampUpKey = "nux_flow";

        // Killswitch: routes straight to WelcomeWindow regardless of NuxFlow completion, without
        // persisting completion, so turning this off again restores the wizard for those users.
        private const string NuxSkipOnboardingRampUpKey = "nux_skip_onboarding";

        private static void ShowGuide(Origins origin, bool forceShow = false)
        {
            if (FeatureRampUpManager.GetRemoteKeysResult(NuxRampUpKey))
            {
                if (NuxFlow.IsNuxCompleted || FeatureRampUpManager.GetRemoteKeysResult(NuxSkipOnboardingRampUpKey))
                {
                    WelcomeWindow.Show(origin);
                }
                else
                {
                    NuxFlow.Instance.ShowNux(origin, forceShow);
                }
            }
            else
            {
                Onboarding.ShowWindow(origin, forceShow);
            }
        }

        private static (string, Color?) ComputeInfoText()
        {
            if (Version < LatestVersion)
            {
                return ($"Version {Version} (New Version {LatestVersion} Available!)", NewColor);
            }
            else
            {
                return ($"Version {Version}", DisabledColor);
            }
        }

        private static (TextureContent, Color?, bool) ComputePillIcon()
        {
            if (Version < LatestVersion)
            {
                return (UpdateIcon, NewColor, true);
            }
            else
            {
                return (null, null, false);
            }
        }
    }
}
