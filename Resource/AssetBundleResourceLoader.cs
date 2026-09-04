using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace XiancaiFramework.Resource
{
    /// <summary>
    /// 基于AssetBundle实现的资源加载器
    /// key = 包名.资源名
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
        /// 包依赖信息
        /// </summary>
        private AssetBundleManifest _mainBundleManifest;
        
        // 构建管线导出的 key → (bundleName, assetPath)
        private readonly Dictionary<string, BundleAssetInfo> _keyMap = new Dictionary<string, BundleAssetInfo>();
        
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

//         /// <summary>
//         /// ab包打包的目标平台名
//         /// </summary>
//         private string PlatformName
//         {
//             get
//             {
// #if UNITY_IOS
//                 return "IOS";
// #elif UNITY_ANDROID
//                 return "Android";
// #elif UNITY_WEBGL
//                 return "WebGL";
// #else 
//                 return "PC";
// #endif
//             }
//         }
//
//         /// <summary>
//         /// bundle包所在路径
//         /// </summary>
//         private string Path => $"{Application.streamingAssetsPath}/";

        private string BuildPath => $"{Application.streamingAssetsPath}/{BundlePlatform.FolderName}/";
        
        /// <summary>
        /// 同步加载资源
        /// </summary>
        /// <param name="key">包名.资源名</param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public T Load<T>(string key) where T : Object
        {
            BundleAssetInfo bundleAssetInfo = ResolveBundleAssetInfo(key);
            if (bundleAssetInfo.BundleName  == null)
            {
                return null;
            }
            
            string bundleName = bundleAssetInfo.BundleName;
            LoadBundle(bundleName);
            
            if (_bundlesMap.TryGetValue(bundleName, out var bundle))
            {
                T asset = bundle.LoadAsset<T>(bundleAssetInfo.AssetName);
                if (asset == null)
                {
                    Debug.LogError($"[AssetBundleResourceLoader] Load 加载asset:{key} 为空！");
                    return null;
                }
                AddBundleRefCount(bundleName);
                
                _assetToBundleMap[asset] = bundleName;
                return asset;
            }

            return null;
        }

        /// <summary>
        /// 加载bundle包
        /// </summary>
        /// <param name="bundleName"></param>
        private void LoadBundle(string bundleName)
        {
            if (!LoadMainBundleManifest())
            {
                Debug.LogError($"[AssetBundleResourceLoader] LoadBundle 加载主包失败！");
                return;
            }

            // 需要加载的bundle
            // 获取所有依赖的bundle名
            string[] allDependencies = _mainBundleManifest.GetAllDependencies(bundleName);

            foreach (string dependency in allDependencies)
            {
                if (!_bundlesMap.ContainsKey(dependency))
                {
                    AssetBundle depBundle = AssetBundle.LoadFromFile($"{BuildPath}{dependency}");
                   
                    if (depBundle == null)
                    {
                        Debug.LogError($"[AssetBundleResourceLoader] 加载依赖包{dependency}失败！");
                        return;
                    } 
                    
                    _bundlesMap[dependency] = depBundle;
                }
                AddBundleRefCount(dependency);
            }
            
            if (!_bundlesMap.ContainsKey(bundleName))
            {
                AssetBundle bundle = AssetBundle.LoadFromFile($"{BuildPath}{bundleName}");
                if (bundle == null)
                {
                    Debug.LogError($"[AssetBundleResourceLoader] 加载包{bundleName}失败！");
                    return;
                }
                _bundlesMap[bundleName] = bundle;
            }
        }
        
        /// <summary>
        /// 异步加载bundle包
        /// </summary>
        /// <param name="bundleName"></param>
        private async UniTask LoadBundleAsync(string bundleName)
        {
            if (!LoadMainBundleManifest())
            {
                Debug.LogError($"[AssetBundleResourceLoader] LoadBundle 加载主包失败！");
                return;
            }

            if (_bundlesMap.ContainsKey(bundleName)) return;

            if (_pendingBundlesMap.TryGetValue(bundleName, out var pendingTask))
            {
                await pendingTask;
                return;
            }

            UniTask<AssetBundle> task = LoadBundleCoreAsync(bundleName);
            _pendingBundlesMap[bundleName] = task;
            try
            {
                await task;
            }
            finally
            {
                _pendingBundlesMap.Remove(bundleName);
            }
        }

        /// <summary>
        /// 异步加载bundle包实际逻辑
        /// </summary>
        /// <param name="bundleName"></param>
        private async UniTask<AssetBundle> LoadBundleCoreAsync(string bundleName)
        {
            // 需要加载的bundle
            // 获取所有依赖的bundle名
            string[] allDependencies = _mainBundleManifest.GetAllDependencies(bundleName);

            foreach (string dependency in allDependencies)
            {
                if (!_bundlesMap.ContainsKey(dependency))
                {
                    if (_pendingBundlesMap.TryGetValue(dependency, out var pendingTask))
                    {
                        await pendingTask;
                        continue;
                    }
                    UniTask<AssetBundle> task = AssetBundle.LoadFromFileAsync($"{BuildPath}{dependency}").ToUniTask();
                    _pendingBundlesMap[dependency] = task;
                    try
                    {
                        AssetBundle depBundle = await task;
                        if (depBundle == null)
                        {
                            Debug.LogError($"[AssetBundleResourceLoader] 加载依赖包{dependency}失败！");
                            return null;
                        }
                        
                        _bundlesMap[dependency] = depBundle;
                    }
                    finally
                    {
                        _pendingBundlesMap.Remove(dependency);
                    }
                }
                AddBundleRefCount(dependency);
            }
            
            if (!_bundlesMap.ContainsKey(bundleName))
            {
                AssetBundle bundle = await AssetBundle.LoadFromFileAsync($"{BuildPath}{bundleName}");
                if (bundle == null)
                {
                    Debug.LogError($"[AssetBundleResourceLoader] 加载依赖包{bundleName}失败！");
                    return null;
                }
                _bundlesMap[bundleName] = bundle;
            }
            return _bundlesMap[bundleName];
        }

        /// <summary>
        /// 根据key解析包名和资源名
        /// </summary>
        /// <param name="key">包名.资源名(资源名内禁止 . )</param>
        /// <returns></returns>
        private BundleAssetInfo ResolveBundleAssetInfo(string key)
        {
            if (_keyMap.TryGetValue(key, out var bai))
            {
                return bai;
            }
            
            int index = key.LastIndexOf('.');

            if (index <= 0)
            {
                Debug.LogError($"[AssetBundleResourceLoader] BundleAssetInfo, 解析错误：{key} ");
                return default;
            }
            
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

        /// <summary>
        /// 加载主包以及依赖信息
        /// </summary>
        private bool LoadMainBundleManifest()
        {
            if (_mainBundle == null)
            {
                // 通过主包获取依赖信息
                _mainBundle = AssetBundle.LoadFromFile($"{BuildPath}{BundlePlatform.FolderName}");
                if (_mainBundle == null)
                {
                    Debug.LogError($"[AssetBundleResourceLoader] LoadMainBundleManifest 加载主包失败！");
                    return false;
                }
                _mainBundleManifest = _mainBundle.LoadAsset<AssetBundleManifest>(nameof(AssetBundleManifest));
            }

            return true;
        }

        /// <summary>
        /// 异步加载资源
        /// </summary>
        /// <param name="key"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public async UniTask<T> LoadAsync<T>(string key) where T : Object
        {
            BundleAssetInfo bundleAssetInfo = ResolveBundleAssetInfo(key);
            string bundleName = bundleAssetInfo.BundleName;
            await LoadBundleAsync(bundleName);
            
            if (_bundlesMap.TryGetValue(bundleName, out var bundle))
            {
                AssetBundleRequest request = bundle.LoadAssetAsync<T>(bundleAssetInfo.AssetName);
                T asset = await request.ToUniTask() as T;

                if (asset == null)
                {
                    Debug.LogError($"[AssetBundleResourceLoader] LoadAsync 加载asset:{key} 为空！");
                    return null;
                }
                AddBundleRefCount(bundleName);
                _assetToBundleMap[asset] = bundleName;
                return asset;
            }

            return null;
        }

        private void AddBundleRefCount(string bundleName)
        {
            if (_bundleRefsMap.TryGetValue(bundleName, out var refCount))
            {
                refCount++;
                _bundleRefsMap[bundleName] = refCount;
            }
            else
            {
                _bundleRefsMap[bundleName] = 1;
            }
        }

        /// <summary>
        /// 卸载资源
        /// </summary>
        /// <param name="asset"></param>
        public void Unload(Object asset)
        {
            if (asset == null) return;
            
            if (_assetToBundleMap.TryGetValue(asset, out var bundleName))
            {
                _assetToBundleMap.Remove(asset);
                if (_bundleRefsMap.TryGetValue(bundleName, out var refCount))
                {
                    if (refCount > 1)
                    {
                        refCount--;
                        _bundleRefsMap[bundleName] = refCount;
                    }
                    else
                    {
                        // 卸载bundle包
                        _bundleRefsMap.Remove(bundleName);
                        if (_bundlesMap.TryGetValue(bundleName, out var bundle))
                        {
                            bundle.Unload(false);
                            _bundlesMap.Remove(bundleName);
                        }
                        // 获取所有依赖的bundle名
                        string[] allDependencies = _mainBundleManifest.GetAllDependencies(bundleName);
                        // 依赖bundle包引用计数-1
                        foreach (string dependency in allDependencies)
                        {
                            if (_bundleRefsMap.TryGetValue(dependency, out var bundleRef))
                            {
                                if (bundleRef > 1)
                                {
                                    bundleRef--;
                                    _bundleRefsMap[dependency] = bundleRef;
                                }
                                else
                                {
                                    _bundleRefsMap.Remove(dependency);
                                    // 卸载依赖包
                                    if (!_bundlesMap.TryGetValue(dependency, out var depBundle)) continue;
                                    depBundle.Unload(false);
                                    _bundlesMap.Remove(dependency);
                                }
                            }
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// 卸载内存中无用资源
        /// 需要上层ResourcesManager先行Shutdown，避免_cachedAssetDict等缓存错误
        /// </summary>
        public void UnloadUnusedAssets()
        {
            AssetBundle.UnloadAllAssetBundles(false);
            _mainBundle = null;
            _mainBundleManifest = null;
            _bundlesMap.Clear();
            _bundleRefsMap.Clear();
            _pendingBundlesMap.Clear();
            _assetToBundleMap.Clear();
            Resources.UnloadUnusedAssets();
        }
    }
}