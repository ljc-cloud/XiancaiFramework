using Cysharp.Threading.Tasks;

namespace XiancaiFramework.Resource
{
    /// <summary>
    /// 资源加载器接口（纯 I/O 适配器）
    /// 只负责实际的加载与卸载，不关心缓存、引用计数等生命周期策略（由 ResourceManager 负责）
    /// 约定：加载失败一律返回 null，不抛异常
    /// </summary>
    public interface IResourceLoader
    {
        /// <summary>
        /// 同步加载资源，失败返回 null
        /// </summary>
        T Load<T>(string key) where T : UnityEngine.Object;

        /// <summary>
        /// 异步加载资源，失败返回 null
        /// </summary>
        UniTask<T> LoadAsync<T>(string key) where T : UnityEngine.Object;

        /// <summary>
        /// 卸载资源
        /// 由 ResourceManager 在引用计数归零时调用；实现需容忍未跟踪/无法卸载的资源（如 GameObject 模板交给 UnloadUnusedAssets 回收）
        /// </summary>
        void Unload(UnityEngine.Object asset);

        /// <summary>
        /// 全局清理（如 Resources.UnloadUnusedAssets / Addressables.CleanBundleCache）
        /// </summary>
        void UnloadUnusedAssets();
    }
}
