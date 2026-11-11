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

using System;
using UnityEngine;
using Object = UnityEngine.Object;

// NOTE: Folder is Assets/Oculus/VR/Scripts/Util but namespace is Meta.XR.Compat,
// not following folder convention. We intentionally avoid `Meta.XR.Utils` or
// `Meta.XR.Utils.Compat` because any namespace `Meta.XR.Utils.*` creates the parent
// `Meta.XR.Utils`, and unqualified `Utils.X` inside `namespace Meta.XR.*` (e.g.
// Meta.XR.BuildingBlocks, Meta.XR.Simulator) binds to that namespace via parent-namespace
// lookup before using directives, causing CS0234 collisions (BlockPublicTag,
// XRSimUtils, GetBlockData, etc). Using Meta.XR.Compat avoids that and keeps bare
// `Utils` class references working without alias hacks.
namespace Meta.XR.Compat
{
    /// <summary>
    /// Compatibility wrappers for Unity Object find APIs deprecated in 6000.6.
    /// Centralizes version branching so call sites don't need #pragma warnings.
    /// </summary>
    /// <remarks>
    /// Unity 6000+: FindObjectsByType SortMode overloads removed / marked obsolete.
    /// The SortMode-free FindObjectsByType overloads are available from 6000.3 and enforced as
    /// errors from 6000.4 (warnaserror). Use UNITY_6000_4_OR_NEWER for FindObjectsByType so
    /// the compat is clean across the full 6000.0-6000.6 test span.
    /// Minimum supported version is now Unity 6000, so FindFirstObjectByType compat is no longer
    /// needed - call sites use FindAnyObjectByType directly.
    /// </remarks>
    internal static class OVRUnityObjectCompat
    {
        public static T[] FindObjectsByType<T>() where T : Object
        {
#if UNITY_6000_4_OR_NEWER
            return Object.FindObjectsByType<T>();
#else
            return Object.FindObjectsByType<T>(FindObjectsSortMode.None);
#endif
        }

        public static T[] FindObjectsByType<T>(FindObjectsInactive inactive) where T : Object
        {
#if UNITY_6000_4_OR_NEWER
            return Object.FindObjectsByType<T>(inactive);
#else
            return Object.FindObjectsByType<T>(inactive, FindObjectsSortMode.None);
#endif
        }

        public static Object[] FindObjectsByType(Type type, FindObjectsInactive inactive)
        {
#if UNITY_6000_4_OR_NEWER
            return Object.FindObjectsByType(type, inactive);
#else
            return Object.FindObjectsByType(type, inactive, FindObjectsSortMode.None);
#endif
        }

        public static Object[] FindObjectsByType(Type type)
        {
#if UNITY_6000_4_OR_NEWER
            return Object.FindObjectsByType(type);
#else
            return Object.FindObjectsByType(type, FindObjectsSortMode.None);
#endif
        }
    }
}
