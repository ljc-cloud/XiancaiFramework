using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace XiancaiFramework.Base.Resource
{
    /// <summary>
    /// 基于AssetBundle实现的资源加载器
    /// </summary>
    public class AssetBundleResourceLoader : IResourceLoader
    {
        private struct BundleAssetInfo
        {
            public string BundleName { get; set; }
            public string AssetName { get; set; }
        }
        
        /// <summary>
        /// 主包
        /// </summary>
        private AssetBundle _mainBundle;
        
        /// <summary>
        /// 
        /// </summary>
        private AssetBundleManifest _mainBundleManifest;
        
        // 构建管线导出的 key → (bundleName, assetPath)
        private readonly Dictionary<string, BundleAssetInfo> _keyMap;
        
        /// <summary>
        /// ab包缓存字典
        /// bundle名 -> 已加载实例
        /// </summary>
        private readonly Dictionary<string, AssetBundle> _bundlesMap = new Dictionary<string, AssetBundle>();

        /// <summary>
        /// ab包引用计数
        /// bundle名 -> 引用计数
        /// </summary>
        private readonly Dictionary<string, int> _bundleRefsMap = new Dictionary<string, int>();
        
        /// <summary>
        /// asset所属bundle名字典
        /// asset -> bundle名
        /// </summary>
        private readonly Dictionary<Object, string> _assetToBundleMap = new Dictionary<Object, string>();
        
        /// <summary>
        /// bundle 名 → 进行中的加载（并发去重）
        /// </summary>
        private readonly Dictionary<string, UniTask<AssetBundle>> _pendingBundlesMap = new Dictionary<string, UniTask<AssetBundle>>();

        /// <summary>
        /// ab包打包的目标平台名
        /// </summary>
        private string PlatformName
        {
            get
            {
#if UNITY_IOS
                return "IOS";
#elif UNITY_ANDROID
                return "Android";
#elif UNITY_WEBGL
                return "WebGL";
#else 
                return "PC";
#endif
            }
        }

        private string Path => $"{Application.streamingAssetsPath}/";
        
        
        public T Load<T>(string key) where T : Object
        {
            BundleAssetInfo bundleAssetInfo = ResolveBundleAssetInfo(key);
            string bundleName = bundleAssetInfo.BundleName;
            LoadBundle(bundleName);
            
            if (_bundlesMap.TryGetValue(bundleName, out var bundle))
            {
                T asset = bundle.LoadAsset<T>(bundleAssetInfo.AssetName);
                _bundleRefsMap[bundleName]++;
                _assetToBundleMap[asset] = bundleName;
                return asset;
            }

            return null;
        }

        private void LoadBundle(string bundleName)
        {
            LoadMainBundleManifest();

            // 需要加载的bundle
            // 获取所有依赖的bundle名
            string[] allDependencies = _mainBundleManifest.GetAllDependencies(bundleName);

            foreach (string dependency in allDependencies)
            {
                if (!_bundlesMap.ContainsKey(dependency))
                {
                    AssetBundle depBundle = AssetBundle.LoadFromFile($"{Path}{dependency}");
                    _bundlesMap[dependency] = depBundle;
                }
            }
            
            if (!_bundlesMap.ContainsKey(bundleName))
            {
                AssetBundle bundle = AssetBundle.LoadFromFile($"{Path}{bundleName}");
                _bundlesMap[bundleName] = bundle;
            }
        }
        
        private async UniTask LoadBundleAsync(string bundleName)
        {
            LoadMainBundleManifest();

            // 需要加载的bundle
            // 获取所有依赖的bundle名
            string[] allDependencies = _mainBundleManifest.GetAllDependencies(bundleName);

            foreach (string dependency in allDependencies)
            {
                if (!_bundlesMap.ContainsKey(dependency))
                {
                    UniTask<AssetBundle> task = AssetBundle.LoadFromFileAsync($"{Path}{dependency}").ToUniTask();
                    AssetBundle assetBundle = await task;
                    _bundlesMap[dependency] = assetBundle;
                }
            }
            
            if (!_bundlesMap.ContainsKey(bundleName))
            {
                AssetBundle bundle = await AssetBundle.LoadFromFileAsync($"{Path}{bundleName}");
                _bundlesMap[bundleName] = bundle;
            }
        }

        private BundleAssetInfo ResolveBundleAssetInfo(string key)
        {
            if (_keyMap.TryGetValue(key, out var bai))
            {
                return bai;
            }
            
            int index = key.LastIndexOf('.');
            string bundleName = key.Substring(0, index);
            string assetName = key.Substring(index + 1);

            BundleAssetInfo bundleAssetInfo = new BundleAssetInfo
            {
                BundleName = bundleName,
                AssetName = assetName
            };

            _keyMap[key] = bundleAssetInfo;
            
            return bundleAssetInfo;
        }

        private void LoadMainBundleManifest()
        {
            if (_mainBundle == null)
            {
                // 通过主包获取依赖信息
                _mainBundle = AssetBundle.LoadFromFile($"{Path}{PlatformName}");
                _mainBundleManifest = _mainBundle.LoadAsset<AssetBundleManifest>(nameof(AssetBundleManifest));
            }
        }

        public async UniTask<T> LoadAsync<T>(string key) where T : Object
        {
            BundleAssetInfo bundleAssetInfo = ResolveBundleAssetInfo(key);
            string bundleName = bundleAssetInfo.BundleName;
            await LoadBundleAsync(bundleName);
            
            if (_bundlesMap.TryGetValue(bundleName, out var bundle))
            {
                T asset = bundle.LoadAsset<T>(bundleAssetInfo.AssetName);
                _bundleRefsMap[bundleName]++;
                _assetToBundleMap[asset] = bundleName;
                return asset;
            }

            return null;
        }

        public void Unload(Object asset)
        {
            if (asset == null) return;
            
            if (_assetToBundleMap.TryGetValue(asset, out var bundleName))
            {
                _assetToBundleMap.Remove(asset);
                if (_bundleRefsMap.TryGetValue(bundleName, out var refCount))
                {
                    if (refCount <= 1)
                    {
                        _bundleRefsMap.Remove(bundleName);
                        if (_bundlesMap.TryGetValue(bundleName, out var bundle))
                        {
                            bundle.Unload(false);
                        }
                    }
                }
            }
        }

        public void UnloadUnusedAssets()
        {
            AssetBundle.UnloadAllAssetBundles(false);
        }
    }
}