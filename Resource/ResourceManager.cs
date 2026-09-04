using System;
using System.Collections.Generic;
using System.Threading;
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

        /// <summary>
        /// 常驻预热句柄表：key -> 常驻持有的句柄（预热期间引用不归零，缓存不清理）
        /// </summary>
        private readonly Dictionary<string, ResourceHandle> _residentHandles = new Dictionary<string, ResourceHandle>();

        /// <summary>
        /// key -> 进行中的预热（并发去重）
        /// </summary>
        private readonly Dictionary<string, UniTask<bool>> _residentPending = new Dictionary<string, UniTask<bool>>();

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
                    AddRefCount(t);
                    // _assetReferenceDict[t]++;
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
            AddRefCount(asset);
            // _assetReferenceDict[asset]++;
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
                    // _assetReferenceDict[t]++;
                    AddRefCount(t);
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
            AddRefCount(asset);
            // _assetReferenceDict[asset]++;
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

        private void AddRefCount(Object asset)
        {
            if (_assetReferenceDict.TryGetValue(asset, out var refCount))
            {
                _assetReferenceDict[asset] = refCount + 1;
            }
            else
            {
                _assetReferenceDict[asset] = 1;
            }
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

        // ==================== 句柄模式 ====================

        /// <summary>
        /// 同步加载并返回资源句柄，失败返回 null（失败不缓存、不计数，无需释放）
        /// 句柄持有期间资源引用 +1；不再使用时调用 handle.Dispose() 归还
        /// </summary>
        public ResourceHandle LoadHandle<T>(string key) where T : Object
        {
            T asset = Load<T>(key);
            return asset == null ? null : new ResourceHandle(this, key, asset);
        }

        /// <summary>
        /// 异步加载并返回资源句柄，失败返回 null
        /// 同 key 并发请求仍合并为一次底层加载；每个成功的调用方各自持有独立句柄与 +1 引用
        /// </summary>
        public async UniTask<ResourceHandle> LoadHandleAsync<T>(string key) where T : Object
        {
            T asset = await LoadAsync<T>(key);
            return asset == null ? null : new ResourceHandle(this, key, asset);
        }

        /// <summary>
        /// 释放句柄（等价于 handle.Dispose()，供 manager 中心化的写法使用）
        /// </summary>
        public void Release(ResourceHandle handle)
        {
            handle?.Dispose();
        }

        // ==================== 预热加载（常驻持有） ====================

        /// <summary>
        /// 同步预热单个资源：加载成功并常驻持有（引用 +1 且不释放），失败返回 false（可重试）
        /// 注意：预热必须"持有引用"——只 Load 后立刻 Release 等于没预热（计数归零会卸载并清缓存）
        /// </summary>
        public bool Preload<T>(string key) where T : Object
        {
            if (_residentHandles.ContainsKey(key)) return true;

            ResourceHandle handle = LoadHandle<T>(key);
            if (handle == null) return false;

            _residentHandles[key] = handle;
            return true;
        }

        /// <summary>
        /// 异步预热单个资源：加载成功并常驻持有，失败返回 false（可重试）
        /// 同 key 并发请求合并为一次加载，不会产生多份常驻引用
        /// </summary>
        public async UniTask<bool> PreloadAsync<T>(string key) where T : Object
        {
            if (_residentHandles.ContainsKey(key)) return true;

            if (_residentPending.TryGetValue(key, out var pending))
            {
                return await pending;
            }

            UniTask<bool> task = PreloadCoreAsync<T>(key);
            _residentPending[key] = task;
            try
            {
                return await task;
            }
            finally
            {
                _residentPending.Remove(key);
            }
        }

        private async UniTask<bool> PreloadCoreAsync<T>(string key) where T : Object
        {
            ResourceHandle handle = await LoadHandleAsync<T>(key);
            if (handle == null) return false;

            _residentHandles[key] = handle;   // 常驻持有：引用不归零、缓存不被清理
            return true;
        }

        /// <summary>
        /// 取消预热：归还常驻引用（计数归零时走正常卸载并清缓存）
        /// </summary>
        public void Unpreload(string key)
        {
            if (_residentHandles.TryGetValue(key, out ResourceHandle handle))
            {
                _residentHandles.Remove(key);
                handle.Dispose();
            }
        }

        /// <summary>
        /// 该 key 是否已常驻预热
        /// </summary>
        public bool IsPreloaded(string key)
        {
            return _residentHandles.ContainsKey(key);
        }

        /// <summary>
        /// 批量预热：按 maxConcurrent 分片并发（限制瞬时 IO/解压峰值），逐片上报进度
        /// 取消粒度为"片之间"：已在途的一片会加载完成，不打断单个加载
        /// </summary>
        /// <param name="keys">待预热 key 集合</param>
        /// <param name="progress">进度回调 0~1</param>
        /// <param name="maxConcurrent">每片并发上限</param>
        /// <param name="token">取消令牌</param>
        public async UniTask PreloadGroupAsync<T>(IEnumerable<string> keys, IProgress<float> progress = null,
            int maxConcurrent = 4, CancellationToken token = default) where T : Object
        {
            List<string> list = new List<string>(keys);
            int total = list.Count;
            int done = 0;

            for (int i = 0; i < total; i += maxConcurrent)
            {
                token.ThrowIfCancellationRequested();

                int count = Mathf.Min(maxConcurrent, total - i);
                UniTask<bool>[] tasks = new UniTask<bool>[count];
                for (int j = 0; j < count; j++)
                {
                    tasks[j] = PreloadAsync<T>(list[i + j]);
                }

                await UniTask.WhenAll(tasks);

                done += count;
                progress?.Report((float)done / total);
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

            // 先归还常驻预热引用（走正常 Release 路径：计数归零即卸载 + 清缓存）
            foreach (var kv in _residentHandles)
            {
                kv.Value.Dispose();
            }
            _residentHandles.Clear();
            _residentPending.Clear();

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
