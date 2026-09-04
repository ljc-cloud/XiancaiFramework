using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace XiancaiFramework.Resource
{
    /// <summary>
    /// 资源句柄：一次资源引用的显式载体（对齐 Addressables.AsyncOperationHandle 的设计思路）
    /// 创建时机：LoadHandle / LoadHandleAsync 成功返回时，底层资源引用计数已 +1；
    /// 释放时机：句柄持有多久，引用就持有多久；不再使用调用 Dispose() 归还（-1，归零由 ResourceManager 真正卸载）。
    /// 铁律：
    /// 1. 加载失败返回 null 句柄，无需（也无法）释放；
    /// 2. Dispose 幂等，重复调用安全；
    /// 3. 句柄只负责一次引用的进出，多次加载同一 key 应持有多个句柄，各自独立释放。
    /// 推荐用法：字段持有句柄 + 持有者 OnDestroy 中 Dispose（组件生命周期即资源引用周期）。
    /// </summary>
    public sealed class ResourceHandle : IDisposable
    {
        private ResourceManager _manager;

        /// <summary>
        /// 资源 key
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// 资源本体（若资源被外部销毁，读到的将是 Unity 假 null）
        /// </summary>
        public Object Asset { get; }

        /// <summary>
        /// 句柄是否仍持有引用（Dispose 后为 false，可用作"资源是否还在使用"的判定）
        /// </summary>
        public bool IsValid { get; private set; }

        internal ResourceHandle(ResourceManager manager, string key, Object asset)
        {
            _manager = manager;
            Key = key;
            Asset = asset;
            IsValid = true;
        }

        /// <summary>
        /// 强类型访问资源本体，类型不匹配返回 null
        /// </summary>
        public T As<T>() where T : Object
        {
            return Asset as T;
        }

        /// <summary>
        /// 归还资源引用（幂等：第二次调用起为空操作，不会重复 -1）
        /// </summary>
        public void Dispose()
        {
            if (!IsValid) return;

            IsValid = false;
            _manager?.Release(Asset);
            _manager = null;
        }

        public override string ToString()
        {
            return IsValid ? $"[ResourceHandle] {Key}" : $"[ResourceHandle] {Key} (已释放)";
        }
    }
}
