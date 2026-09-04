using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace XiancaiFramework.Resource
{
    /// <summary>
    /// 资源管理器
    /// 持有 IResourceLoader（纯 I/O），统一负责缓存、引用计数、实例映射、并发加载合并等生命周期策略
    /// 铁律：失败路径 = 不缓存 + 不计数 + 明确返回 null
    /// </summary>
    public class ResourceManager
    {
        /// <summary>
        /// 加载器（纯 I/O 适配器）
        /// </summary>
        private readonly IResourceLoader _loader;

        /// <summary>
        /// 已缓存的资源字典：key -> 资源
        /// </summary>
        private readonly Dictionary<string, Object> _cachedAssetDict = new Dictionary<string, Object>();

        /// <summary>
        /// 资源引用计数字典：资源 -> 计数
        /// </summary>
        private readonly Dictionary<Object, int> _assetReferenceDict = new Dictionary<Object, int>();

        /// <summary>
        /// 实例 -> 模板资源 映射字典
        /// </summary>
        private readonly Dictionary<Object, Object> _instanceToAsset = new Dictionary<Object, Object>();

        /// <summary>
        /// 资源 -> key 反向映射（引用计数归零时同步清理缓存用）
        /// 假设：一个资源对象只会被一个 key 缓存（Resources 路径下成立）
        /// </summary>
        private readonly Dictionary<Object, string> _assetToKey = new Dictionary<Object, string>();

        /// <summary>
        /// key -> 进行中的异步加载（合并并发请求，避免同 key 重复加载）
        /// </summary>
        private readonly Dictionary<string, UniTask<Object>> _pendingLoads = new Dictionary<string, UniTask<Object>>();

        public ResourceManager(IResourceLoader loader)
        {
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        }

        // ==================== 同步加载 ====================

        /// <summary>
        /// 同步加载资源，失败返回 null
        /// </summary>
        public T Load<T>(string key) where T : Object
        {
            // 1. 缓存命中
            if (_cachedAssetDict.TryGetValue(key, out var cached))
            {
                if (cached is T t)
                {
                    _assetReferenceDict[t]++;
                    return t;
                }

                LogTypeError(key, typeof(T));
                return null;
            }

            // 2. 底层加载
            T asset = _loader.Load<T>(key);
            if (asset == null)
            {
                Debug.LogError($"[{GetType().Name}] 资源不存在: {key}");
                return null;
            }

            CacheAsset(key, asset);
            _assetReferenceDict[asset]++;
            return asset;
        }

        // ==================== 异步加载 ====================

        /// <summary>
        /// 异步加载资源，失败返回 null
        /// 同 key 并发请求会合并为一次底层加载，每个调用方各自持有 +1 引用
        /// </summary>
        public async UniTask<T> LoadAsync<T>(string key) where T : Object
        {
            // 1. 缓存命中
            if (_cachedAssetDict.TryGetValue(key, out var cached))
            {
                if (cached is T t)
                {
                    _assetReferenceDict[t]++;
                    return t;
                }

                LogTypeError(key, typeof(T));
                return null;
            }

            // 2. 合并并发请求
            Object asset;
            if (_pendingLoads.TryGetValue(key, out var pending))
            {
                asset = await pending;          // 等待发起者的加载结果
            }
            else
            {
                UniTask<Object> task = LoadCoreAsync(key);
                _pendingLoads[key] = task;
                try
                {
                    asset = await task;
                }
                finally
                {
                    _pendingLoads.Remove(key);  // 完成即移除，后续调用走缓存
                }
            }

            // 3. 失败路径：不缓存、不计数
            if (asset == null)
            {
                Debug.LogError($"[{GetType().Name}] 资源不存在: {key}");
                return null;
            }

            if (!(asset is T typed))
            {
                LogTypeError(key, typeof(T));
                return null;
            }

            // 4. 每个调用方各自 +1（先验类型再计数，防止计数泄漏）
            _assetReferenceDict[asset]++;
            return typed;
        }

        /// <summary>
        /// 底层异步加载并登记缓存（不计数，计数由每个调用方负责）
        /// </summary>
        private async UniTask<Object> LoadCoreAsync(string key)
        {
            Object asset = await _loader.LoadAsync<Object>(key);
            if (asset == null) return null;

            CacheAsset(key, asset);
            return asset;
        }

        /// <summary>
        /// 登记缓存（只写缓存与反向映射，不碰引用计数）
        /// </summary>
        private void CacheAsset(string key, Object asset)
        {
            _cachedAssetDict[key] = asset;
            _assetToKey[asset] = key;
        }

        // ==================== 卸载 ====================

        /// <summary>
        /// 释放资源引用，引用计数归零时真正卸载底层资源
        /// </summary>
        public void Release(Object asset)
        {
            if (asset == null) return;

            if (!_assetReferenceDict.TryGetValue(asset, out var count))
            {
                Debug.LogWarning($"[{GetType().Name}] 释放未引用的资源: {asset.name}");
                return;
            }

            if (count <= 1)
            {
                // 归零：清计数、清缓存（含反向映射）、卸载底层
                _assetReferenceDict.Remove(asset);
                if (_assetToKey.TryGetValue(asset, out var key))
                {
                    _cachedAssetDict.Remove(key);
                    _assetToKey.Remove(asset);
                }
                _loader.Unload(asset);
            }
            else
            {
                _assetReferenceDict[asset] = count - 1;
            }
        }

        // ==================== 实例化 ====================

        /// <summary>
        /// 加载并实例化资源，失败返回 null
        /// </summary>
        public T Instantiate<T>(string key, Transform parent = null) where T : Object
        {
            T template = Load<T>(key);
            if (template == null) return null;

            T instance = Object.Instantiate(template, parent);
            _instanceToAsset[instance] = template;
            return instance;
        }

        /// <summary>
        /// 回收实例：销毁克隆体并释放其模板资源引用
        /// </summary>
        public void ReleaseInstance<T>(T instance) where T : Object
        {
            if (instance == null) return;

            if (_instanceToAsset.TryGetValue(instance, out var template))
            {
                _instanceToAsset.Remove(instance);
                Object.Destroy(instance);
                Release(template);
            }
            else
            {
                Debug.LogWarning($"[{GetType().Name}] 未找到实例对应的资源模板: {instance.name}");
            }
        }

        // ==================== 查询与全局清理 ====================

        /// <summary>
        /// 该 key 是否已被缓存
        /// </summary>
        public bool IsCached(string key)
        {
            return _cachedAssetDict.ContainsKey(key);
        }

        /// <summary>
        /// 全局清理（fire-and-forget，底层异步执行）
        /// </summary>
        public void UnloadUnusedAssets()
        {
            _loader.UnloadUnusedAssets();
        }

        /// <summary>
        /// 关闭资源管理器：销毁所有托管实例、强制卸载所有资源并清空状态
        /// </summary>
        public void Shutdown()
        {
            foreach (var kv in _instanceToAsset)
            {
                Object.Destroy(kv.Key);
            }
            _instanceToAsset.Clear();

            foreach (var asset in _assetReferenceDict.Keys)
            {
                _loader.Unload(asset);
            }
            _assetReferenceDict.Clear();
            _cachedAssetDict.Clear();
            _assetToKey.Clear();
            _pendingLoads.Clear();

            UnloadUnusedAssets();
        }

        private static void LogTypeError(string key, Type type)
        {
            Debug.LogError($"[ResourceManager] 类型不匹配: {key}:{type.Name}");
        }
    }
}
