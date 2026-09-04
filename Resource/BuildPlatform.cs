using UnityEngine;

namespace XiancaiFramework.Resource
{
    /// <summary>
    /// 平台 → 资源包目录名 的统一映射（构建脚本与运行时共用，禁止在别处再手写平台名）
    /// </summary>
    public static class BundlePlatform
    {
        /// <summary>运行时：Player 用 Application.platform，Editor 用当前激活构建目标</summary>
        public static string FolderName
        {
            get
            {
#if UNITY_EDITOR
                // Editor 里跑（预览/测试）：跟随你正在构建的平台
                return FromBuildTarget(UnityEditor.EditorUserBuildSettings.activeBuildTarget);
#else
                return FromRuntimePlatform(Application.platform);
#endif
            }
        }

        /// <summary>构建脚本用：BuildTarget → 目录名</summary>
        public static string FromBuildTarget(UnityEditor.BuildTarget target)
        {
            switch (target)
            {
                case UnityEditor.BuildTarget.StandaloneWindows:
                case UnityEditor.BuildTarget.StandaloneWindows64:
                    return "StandaloneWindows64";
                case UnityEditor.BuildTarget.StandaloneOSX:
                    return "StandaloneOSX";
                case UnityEditor.BuildTarget.StandaloneLinux64:
                    return "StandaloneLinux64";
                case UnityEditor.BuildTarget.Android:
                    return "Android";
                case UnityEditor.BuildTarget.iOS:
                    return "iOS";
                case UnityEditor.BuildTarget.WebGL:
                    return "WebGL";
                default:
                    Debug.LogError($"[BundlePlatform] 未支持的构建目标: {target}");
                    return null;
            }
        }

        /// <summary>运行时用：RuntimePlatform → 目录名（字符串必须与 FromBuildTarget 完全一致）</summary>
        public static string FromRuntimePlatform(RuntimePlatform platform)
        {
            switch (platform)
            {
                case RuntimePlatform.WindowsPlayer: return "StandaloneWindows64";
                case RuntimePlatform.OSXPlayer:        return "StandaloneOSX";
                case RuntimePlatform.LinuxPlayer:      return "StandaloneLinux64";
                case RuntimePlatform.Android:          return "Android";
                case RuntimePlatform.IPhonePlayer:     return "iOS";   // 运行时是 IPhonePlayer，构建时是 iOS
                case RuntimePlatform.WebGLPlayer:      return "WebGL";
                default:
                    Debug.LogError($"[BundlePlatform] 未支持的运行时平台: {platform}");
                    return null;
            }
        }
    }
}