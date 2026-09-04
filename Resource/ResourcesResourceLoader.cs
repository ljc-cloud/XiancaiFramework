using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace XiancaiFramework.Resource
{
    /// <summary>
    /// 基于 Resources 的资源加载器（纯 I/O 实现）
    /// 不做缓存、不做引用计数、不做实例映射，只负责加载与卸载
    /// 生命周期策略统一由 ResourceManager 负责
    /// </summary>
    public class ResourcesResourceLoader : IResourceLoader
    {
        /// <summary>
        /// 同步加载资源，失败返回 null
        /// </summary>
        public T Load<T>(string key) where T : Object
        {
            return Resources.Load<T>(key);
        }

        /// <summary>
        /// 异步加载资源，失败返回 null
        /// </summary>
        public async UniTask<T> LoadAsync<T>(string key) where T : Object
        {
            ResourceRequest request = Resources.LoadAsync<T>(key);
            await request.ToUniTask();
            return request.asset as T;
        }

        /// <summary>
        /// 卸载资源（引用计数归零时由 ResourceManager 调用）
        /// </summary>
        public void Unload(Object asset)
        {
            if (asset == null) return;

            // Unity 禁止对 GameObject / Component / AssetBundle 调用 UnloadAsset，会报错
            // 这些类型的资源交给 UnloadUnusedAssets 回收
            if (asset is GameObject || asset is Component) return;

            Resources.UnloadAsset(asset);
        }

        /// <summary>
        /// 全局清理
        /// </summary>
        public void UnloadUnusedAssets()
        {
            Resources.UnloadUnusedAssets();
        }
    }
}
