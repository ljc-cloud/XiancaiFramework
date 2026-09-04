using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XiancaiFramework.Resource;
using Object = UnityEngine.Object;

namespace XiancaiFramework.Scripts.Test
{
    /// <summary>
    /// ResourceManager 自检用例（PlayMode 手工测试，用法同 EventDispatcherTest）
    /// 挂到场景任意 GameObject 上运行，控制台查看 [ResourceManagerTest] 汇总。
    /// 说明：
    /// 1. 使用内存假 Loader（FakeLoader），不依赖真实 Resources/AssetBundle 资源；
    /// 2. "预期错误"场景（缺失 key、类型不匹配）会打印 [ResourceManager] 错误日志，属预期噪音；
    /// 3. 断言失败会以 [ResourceManagerTest] FAIL: xxx 输出并计入失败列表。
    /// </summary>
    public class ResourceManagerTest : MonoBehaviour
    {
        [SerializeField] private bool _autoRun = true;

        private readonly List<string> _failures = new List<string>();
        private int _checks;

        private void Start()
        {
            if (_autoRun)
            {
                Run().Forget();
            }
        }

        private async UniTaskVoid Run()
        {
            _failures.Clear();
            _checks = 0;

            await RunPhase("同步缓存与引用计数", PhaseCacheAndRefCount);
            await RunPhase("异步加载与并发合并", PhaseAsyncMerge);
            await RunPhase("资源句柄", PhaseHandle);
            await RunPhase("类型不匹配不计数", PhaseTypeMismatch);
            await RunPhase("预热常驻与取消", PhasePreload);
            await RunPhase("批量预热与进度", PhasePreloadGroup);
            await RunPhase("实例化与回收", PhaseInstantiate);
            await RunPhase("Shutdown 清理后可重载", PhaseShutdown);

            if (_failures.Count == 0)
            {
                Debug.Log($"[ResourceManagerTest] ✅ 全部通过，共 {_checks} 项断言");
            }
            else
            {
                Debug.LogError($"[ResourceManagerTest] ❌ {_failures.Count}/{_checks} 项失败：\n{string.Join("\n", _failures)}");
            }
        }

        // ==================== 工具 ====================

        private async UniTask RunPhase(string name, System.Func<UniTask> phase)
        {
            try
            {
                await phase();
                Debug.Log($"[ResourceManagerTest] 阶段通过: {name}");
            }
            catch (System.Exception e)
            {
                _failures.Add($"{name} 抛异常: {e}");
                Debug.LogError($"[ResourceManagerTest] FAIL: 阶段 {name} 抛异常\n{e}");
            }
        }

        private void Check(bool condition, string message)
        {
            _checks++;
            if (condition) return;
            _failures.Add(message);
            Debug.LogError($"[ResourceManagerTest] FAIL: {message}");
        }

        /// <summary>内存假加载器：记录调用次数，异步路径带一帧真实调度（模拟真异步）</summary>
        private sealed class FakeLoader : IResourceLoader
        {
            public readonly Dictionary<string, Object> Assets = new Dictionary<string, Object>();
            public int LoadCount;          // Load/LoadAsync 实际调用次数
            public int UnloadCount;        // Unload(Object) 调用次数
            public int UnloadUnusedCount;

            public FakeLoader()
            {
                Assets["res.a"] = ScriptableObject.CreateInstance<FakeAssetA>();
                Assets["res.b"] = ScriptableObject.CreateInstance<FakeAssetA>();
                Assets["pre.hero"] = ScriptableObject.CreateInstance<FakeAssetA>();
            }

            public void DestroyFakeAssets()
            {
                foreach (var asset in Assets.Values)
                {
                    Destroy(asset);
                }
                Assets.Clear();
            }

            public T Load<T>(string key) where T : Object
            {
                LoadCount++;
                return Assets.TryGetValue(key, out var asset) ? asset as T : null;
            }

            public async UniTask<T> LoadAsync<T>(string key) where T : Object
            {
                LoadCount++;
                await UniTask.Yield();   // 让出至少一帧，模拟真实异步调度
                return Assets.TryGetValue(key, out var asset) ? asset as T : null;
            }

            public void Unload(Object asset)
            {
                UnloadCount++;
            }

            public void UnloadUnusedAssets()
            {
                UnloadUnusedCount++;
            }
        }

        public abstract class FakeAssetBase : ScriptableObject { }

        public class FakeAssetA : FakeAssetBase { }

        public class FakeAssetB : FakeAssetBase { }

        // ==================== 阶段 1：同步缓存与引用计数 ====================

        private async UniTask PhaseCacheAndRefCount()
        {
            var loader = new FakeLoader();
            var manager = new ResourceManager(loader);

            // 1. 首次 Load：底层加载一次
            var a1 = manager.Load<FakeAssetA>("res.a");
            Check(a1 != null, "首次 Load 不应为 null");
            Check(loader.LoadCount == 1, $"首次 Load 应触发一次底层加载, 实际 {loader.LoadCount}");
            Check(manager.IsCached("res.a"), "Load 后应已缓存");

            // 2. 缓存命中：同实例、不再触底、计数 +1
            var a2 = manager.Load<FakeAssetA>("res.a");
            Check(a1 == a2, "缓存命中应返回同一实例");
            Check(loader.LoadCount == 1, $"缓存命中不应再触底, 实际 {loader.LoadCount}");

            // 3. 释放一次：计数 2→1，缓存仍在、未卸载
            manager.Release(a2);
            Check(manager.IsCached("res.a"), "计数未归零时缓存应保留");
            Check(loader.UnloadCount == 0, $"计数未归零时不应卸载, 实际 {loader.UnloadCount}");

            // 4. 释放第二次：归零 → 卸载 + 清缓存
            manager.Release(a1);
            Check(loader.UnloadCount == 1, $"归零后应卸载一次, 实际 {loader.UnloadCount}");
            Check(!manager.IsCached("res.a"), "归零后缓存应被清理");

            // 5. 缺失 key：null + 不缓存
            var missing = manager.Load<FakeAssetA>("not.exist");   // 预期打一条"资源不存在"
            Check(missing == null, "缺失 key 应返回 null");

            manager.Shutdown();
            loader.DestroyFakeAssets();
        }

        // ==================== 阶段 2：异步加载与并发合并 ====================

        private async UniTask PhaseAsyncMerge()
        {
            var loader = new FakeLoader();
            var manager = new ResourceManager(loader);

            // 1. 同 key 并发：合并为一次底层加载，各持 +1
            var t1 = manager.LoadAsync<FakeAssetA>("res.a");
            var t2 = manager.LoadAsync<FakeAssetA>("res.a");
            var a1 = await t1;
            var a2 = await t2;
            Check(a1 != null && a2 != null, "并发加载结果不应为 null");
            Check(a1 == a2, "并发加载应返回同一缓存实例");
            Check(loader.LoadCount == 1, $"并发同 key 应只触发一次底层加载, 实际 {loader.LoadCount}");

            // 2. 两次释放后才卸载
            manager.Release(a1);
            Check(loader.UnloadCount == 0, "并发双持有释放一次不应卸载");
            manager.Release(a2);
            Check(loader.UnloadCount == 1, "双持有全部释放后应卸载一次");
            Check(!manager.IsCached("res.a"), "卸载后缓存应清理");

            // 3. 卸载后可重新加载（重新走底层）
            var a3 = await manager.LoadAsync<FakeAssetA>("res.a");
            Check(a3 != null, "卸载后重新加载应成功");
            Check(loader.LoadCount == 2, $"重新加载应再次触底, 实际 {loader.LoadCount}");

            manager.Shutdown();
            loader.DestroyFakeAssets();
        }

        // ==================== 阶段 3：资源句柄 ====================

        private async UniTask PhaseHandle()
        {
            var loader = new FakeLoader();
            var manager = new ResourceManager(loader);

            // 1. 同步句柄
            var h1 = manager.LoadHandle<FakeAssetA>("res.a");
            Check(h1 != null, "LoadHandle 成功不应为 null");
            Check(h1.IsValid, "句柄创建后应有效");
            Check(h1.Asset != null, "句柄应持有资源");
            Check(h1.As<FakeAssetA>() != null, "As<T>() 应能取回资源");
            Check(h1.Key == "res.a", "句柄应记录 key");

            // 2. Dispose 后失效；重复 Dispose 安全
            h1.Dispose();
            Check(!h1.IsValid, "Dispose 后句柄应失效");
            h1.Dispose();   // 幂等，不应抛异常
            Check(loader.UnloadCount == 1, $"句柄释放后计数归零应卸载一次, 实际 {loader.UnloadCount}");

            // 3. 失败路径：返回 null 句柄
            var hBad = manager.LoadHandle<FakeAssetA>("not.exist");   // 预期错误日志
            Check(hBad == null, "失败路径应返回 null 句柄");

            // 4. 异步句柄 + manager.Release(handle) 便捷释放
            var h2 = await manager.LoadHandleAsync<FakeAssetA>("res.a");
            Check(h2 != null && h2.IsValid, "异步句柄加载应成功且有效");
            manager.Release(h2);
            Check(!h2.IsValid, "manager.Release(handle) 后句柄应失效");
            Check(loader.UnloadCount == 2, "句柄归还计数应一致");

            manager.Shutdown();
            loader.DestroyFakeAssets();
        }

        // ==================== 阶段 4：类型不匹配不计数 ====================

        private async UniTask PhaseTypeMismatch()
        {
            var loader = new FakeLoader();
            var manager = new ResourceManager(loader);

            var a = manager.Load<FakeAssetA>("res.a");
            Check(a != null, "预加载 FakeAssetA 应成功");

            // 缓存命中但类型不符：应返回 null 且不加计数（预期打一条类型错误日志）
            var wrong = manager.Load<FakeAssetB>("res.a");
            Check(wrong == null, "类型不匹配应返回 null");

            // 释放一次即归零（说明类型错误路径没有污染计数）
            manager.Release(a);
            Check(loader.UnloadCount == 1, $"类型不匹配不应影响计数, 卸载应恰好一次, 实际 {loader.UnloadCount}");
            Check(!manager.IsCached("res.a"), "归零后缓存应清理");

            manager.Shutdown();
            loader.DestroyFakeAssets();
        }

        // ==================== 阶段 5：预热常驻与取消 ====================

        private async UniTask PhasePreload()
        {
            var loader = new FakeLoader();
            var manager = new ResourceManager(loader);

            // 1. 预热成功并常驻
            bool ok = await manager.PreloadAsync<FakeAssetA>("res.a");
            Check(ok, "PreloadAsync 应成功");
            Check(manager.IsPreloaded("res.a"), "预热后应处于常驻状态");
            Check(manager.IsCached("res.a"), "预热后资源应已缓存");
            Check(loader.LoadCount == 1, $"预热应触发一次底层加载, 实际 {loader.LoadCount}");

            // 2. 重复预热幂等：不再触底、不叠计数
            bool ok2 = await manager.PreloadAsync<FakeAssetA>("res.a");
            Check(ok2, "重复预热应直接成功");
            Check(loader.LoadCount == 1, $"重复预热不应再次触底, 实际 {loader.LoadCount}");

            // 3. 预热后 Load 命中缓存：再释放一次后仍因常驻而不卸载
            var a = manager.Load<FakeAssetA>("res.a");
            Check(a != null, "预热后 Load 应命中缓存");
            manager.Release(a);
            Check(loader.UnloadCount == 0, "常驻引用存在时释放不应卸载");

            // 4. Unpreload 归还常驻引用 → 归零卸载 + 清缓存
            manager.Unpreload("res.a");
            Check(!manager.IsPreloaded("res.a"), "Unpreload 后不应再常驻");
            Check(loader.UnloadCount == 1, $"Unpreload 后应卸载一次, 实际 {loader.UnloadCount}");
            Check(!manager.IsCached("res.a"), "Unpreload 后缓存应清理");

            // 5. 预热失败路径：返回 false 且不常驻（预期错误日志）
            bool bad = await manager.PreloadAsync<FakeAssetA>("not.exist");
            Check(!bad, "预热缺失资源应返回 false");
            Check(!manager.IsPreloaded("not.exist"), "预热失败不应常驻");

            // 6. 同步预热
            bool sync = manager.Preload<FakeAssetA>("res.b");
            Check(sync && manager.IsPreloaded("res.b"), "同步预热应成功");

            manager.Shutdown();
            loader.DestroyFakeAssets();
        }

        // ==================== 阶段 6：批量预热与进度 ====================

        private async UniTask PhasePreloadGroup()
        {
            var loader = new FakeLoader();
            var manager = new ResourceManager(loader);

            var keys = new List<string> { "res.a", "res.b", "not.exist" };   // 1 个失败
            float lastProgress = -1f;
            int progressTicks = 0;

            await manager.PreloadGroupAsync<FakeAssetA>(keys,
                new Progress<float>(p =>
                {
                    progressTicks++;
                    lastProgress = p;
                }),
                maxConcurrent: 2);

            Check(lastProgress >= 1f, $"进度应最终到达 1.0, 实际 {lastProgress}");
            Check(progressTicks > 0, "进度回调应被触发");
            Check(manager.IsPreloaded("res.a") && manager.IsPreloaded("res.b"), "批量预热成功项应常驻");
            Check(!manager.IsPreloaded("not.exist"), "批量预热失败项不应常驻");

            manager.Shutdown();
            loader.DestroyFakeAssets();
        }

        // ==================== 阶段 7：实例化与回收 ====================

        private async UniTask PhaseInstantiate()
        {
            var loader = new FakeLoader();
            var manager = new ResourceManager(loader);

            // 1. 实例化成功：克隆体 != 模板，模板 +1
            FakeAssetA instance = manager.Instantiate<FakeAssetA>("pre.hero");
            Check(instance != null, "实例化不应为 null");
            Check(loader.UnloadCount == 0, "实例存活期间模板不应卸载");
            Check(manager.IsCached("pre.hero"), "实例存活期间模板应保持缓存");

            // 2. 回收实例：销毁克隆 + 释放模板 → 归零卸载
            manager.ReleaseInstance(instance);
            Check(loader.UnloadCount == 1, $"回收实例后模板应卸载一次, 实际 {loader.UnloadCount}");
            Check(!manager.IsCached("pre.hero"), "回收实例后模板缓存应清理");

            // 3. 重复回收：找不到映射，仅警告不崩溃
            manager.ReleaseInstance(instance);   // 预期打一条警告
            Check(true, "重复回收不应崩溃");

            manager.Shutdown();
            loader.DestroyFakeAssets();
        }

        // ==================== 阶段 8：Shutdown 清理后可重载 ====================

        private async UniTask PhaseShutdown()
        {
            var loader = new FakeLoader();
            var manager = new ResourceManager(loader);

            // 混合状态：常驻 + 裸引用 + 实例
            await manager.PreloadAsync<FakeAssetA>("res.a");
            var a = manager.Load<FakeAssetA>("res.b");
            manager.Instantiate<FakeAssetA>("pre.hero");

            manager.Shutdown();   // 不应抛异常、不应有残留状态
            Check(loader.UnloadUnusedCount == 1, "Shutdown 应调用底层 UnloadUnusedAssets");
            Check(!manager.IsCached("res.a") && !manager.IsCached("res.b") && !manager.IsCached("pre.hero"),
                "Shutdown 后缓存应全部清空");
            Check(!manager.IsPreloaded("res.a"), "Shutdown 后常驻表应清空");

            // Shutdown 后仍可正常重新加载（Map 已重置）
            var a2 = manager.Load<FakeAssetA>("res.a");
            Check(a2 != null, "Shutdown 后应可重新加载");
            Check(a == a2, "重新加载应拿到同一资产实例(缓存重建)");

            manager.Shutdown();
            loader.DestroyFakeAssets();
        }
    }
}
