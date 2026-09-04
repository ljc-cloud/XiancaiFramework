using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XiancaiFramework.Resource;
using Object = UnityEngine.Object;

namespace XiancaiFramework.Scripts.Test
{
    /// <summary>
    /// ResourceManager + ResourcesResourceLoader 真机资源自检（PlayMode）
    /// 目标资源：Assets/Resources/Prefabs/Cube.prefab，key = "Prefabs/Cube"
    /// 挂到场景任意 GameObject 上运行，控制台查看 [RMResourcesTest] 汇总。
    /// 与 FakeLoader 版测试的区别（真实加载器语义）：
    /// 1. ResourcesResourceLoader 对 GameObject 的 Unload 是空操作（引擎禁止 UnloadAsset(GameObject)），
    ///    内存真正回收发生在 Resources.UnloadUnusedAssets 完成后；
    ///    因此"归零卸载"在 Manager 层断言为 IsCached == false（确定性），
    ///    引擎内存回收单独放在"深层卸载"阶段验证（编辑器可能因选中资源而钉住，见该阶段注释）。
    /// 2. "预期错误"场景（缺失 key、重复回收）会打印框架错误日志，属预期噪音。
    /// </summary>
    public class ResourceManagerResourcesTest : MonoBehaviour
    {
        private const string CubeKey = "Prefabs/Cube";

        [SerializeField] private bool _autoRun = true;

        private readonly List<string> _failures = new List<string>();
        private int _checks;
        private GameObject _sceneRoot;

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

            // 前置校验：目标资源必须存在，否则后续断言没有意义
            if (Resources.Load<GameObject>(CubeKey) == null)
            {
                Debug.LogError($"[RMResourcesTest] 目标资源不存在: Assets/Resources/{CubeKey}.prefab，请确认路径");
                return;
            }

            // 场景清理根：避免测试实例残留
            _sceneRoot = new GameObject("RMResourcesTest_Root");

            await RunPhase("同步加载与缓存", PhaseSyncLoad);
            await RunPhase("异步加载与并发合并", PhaseAsyncMerge);
            await RunPhase("资源句柄", PhaseHandle);
            await RunPhase("预热常驻", PhasePreload);
            await RunPhase("实例化与回收", PhaseInstantiate);
            await RunPhase("Shutdown 重置后可重载", PhaseShutdown);
            await RunPhase("深层卸载(引擎回收)", PhaseDeepUnload);

            Destroy(_sceneRoot);

            if (_failures.Count == 0)
            {
                Debug.Log($"[RMResourcesTest] ✅ 全部通过，共 {_checks} 项断言");
            }
            else
            {
                Debug.LogError($"[RMResourcesTest] ❌ {_failures.Count}/{_checks} 项失败：\n{string.Join("\n", _failures)}");
            }
        }

        // ==================== 工具 ====================

        private async UniTask RunPhase(string name, Func<UniTask> phase)
        {
            try
            {
                await phase();
                Debug.Log($"[RMResourcesTest] 阶段通过: {name}");
            }
            catch (Exception e)
            {
                _failures.Add($"{name} 抛异常: {e}");
                Debug.LogError($"[RMResourcesTest] FAIL: 阶段 {name} 抛异常\n{e}");
            }
        }

        private void Check(bool condition, string message)
        {
            _checks++;
            if (condition) return;
            _failures.Add(message);
            Debug.LogError($"[RMResourcesTest] FAIL: {message}");
        }

        private ResourceManager NewManager()
        {
            return new ResourceManager(new ResourcesResourceLoader());
        }

        // ==================== 阶段 1：同步加载与缓存 ====================

        private async UniTask PhaseSyncLoad()
        {
            var manager = NewManager();

            // 1. 首次 Load
            var cube1 = manager.Load<GameObject>(CubeKey);
            Check(cube1 != null, "Resources.Load 应能加载到 Cube");
            Check(manager.IsCached(CubeKey), "Load 后应已缓存");

            // 2. 缓存命中：同实例
            var cube2 = manager.Load<GameObject>(CubeKey);
            Check(cube1 == cube2, "缓存命中应返回同一实例");

            // 3. 释放一次：计数 2→1，缓存仍在
            manager.Release(cube2);
            Check(manager.IsCached(CubeKey), "计数未归零时缓存应保留");

            // 4. 释放第二次：归零 → 缓存清理（Go 资源引擎回收在 UnloadUnusedAssets，见深层卸载阶段）
            manager.Release(cube1);
            Check(!manager.IsCached(CubeKey), "归零后缓存应被清理");

            manager.Shutdown();
        }

        // ==================== 阶段 2：异步加载与并发合并 ====================

        private async UniTask PhaseAsyncMerge()
        {
            var manager = NewManager();

            // 1. 异步加载
            var cube1 = await manager.LoadAsync<GameObject>(CubeKey);
            Check(cube1 != null, "异步加载应成功");

            // 2. 异步命中同步缓存：同一实例
            var cube2 = manager.Load<GameObject>(CubeKey);
            Check(cube1 == cube2, "异步与同步加载应共享同一缓存实例");

            // 3. 同 key 并发合并：结果同一实例
            var t1 = manager.LoadAsync<GameObject>(CubeKey);
            var t2 = manager.LoadAsync<GameObject>(CubeKey);
            var a1 = await t1;
            var a2 = await t2;
            Check(a1 != null && a1 == a2 && a1 == cube1, "并发请求应合并且返回同一实例");

            // 4. 三个持有全部释放后缓存才清
            manager.Release(cube1);
            manager.Release(cube2);
            Check(manager.IsCached(CubeKey), "并发双持有仍在时缓存应保留");
            manager.Release(a1);
            manager.Release(a2);
            Check(!manager.IsCached(CubeKey), "全部释放后缓存应清理");

            manager.Shutdown();
        }

        // ==================== 阶段 3：资源句柄 ====================

        private async UniTask PhaseHandle()
        {
            var manager = NewManager();

            // 1. 同步句柄
            var h1 = manager.LoadHandle<GameObject>(CubeKey);
            Check(h1 != null, "LoadHandle 不应为 null");
            Check(h1.IsValid && h1.Asset != null, "句柄应有效并持有资源");
            Check(h1.Key == CubeKey, "句柄应记录 key");
            Check(h1.As<GameObject>() != null, "As<T>() 应取回资源");

            // 2. Dispose → 失效 + 缓存清理；重复 Dispose 安全
            h1.Dispose();
            Check(!h1.IsValid, "Dispose 后句柄应失效");
            Check(!manager.IsCached(CubeKey), "句柄释放后缓存应清理");
            h1.Dispose();   // 幂等验证

            // 3. 失败路径返回 null 句柄
            var hBad = manager.LoadHandle<GameObject>("not.exist");   // 预期错误日志
            Check(hBad == null, "失败路径应返回 null 句柄");

            // 4. 异步句柄 + manager.Release(handle)
            var h2 = await manager.LoadHandleAsync<GameObject>(CubeKey);
            Check(h2 != null && h2.IsValid, "异步句柄应有效");
            manager.Release(h2);
            Check(!h2.IsValid && !manager.IsCached(CubeKey), "释放句柄后应失效且清缓存");

            // 5. 句柄释放后可重新加载（Resources 语义：可重复 Load）
            var again = manager.Load<GameObject>(CubeKey);
            Check(again != null, "句柄释放后应可重新加载");
            manager.Release(again);

            manager.Shutdown();
        }

        // ==================== 阶段 4：预热常驻 ====================

        private async UniTask PhasePreload()
        {
            var manager = NewManager();

            // 1. 预热成功并常驻
            bool ok = await manager.PreloadAsync<GameObject>(CubeKey);
            Check(ok, "PreloadAsync 应成功");
            Check(manager.IsPreloaded(CubeKey) && manager.IsCached(CubeKey), "预热后应常驻且缓存");

            // 2. 重复预热幂等
            bool ok2 = await manager.PreloadAsync<GameObject>(CubeKey);
            Check(ok2, "重复预热应直接成功");

            // 3. 预热后 Load 并释放：常驻引用使缓存不清理
            var cube = manager.Load<GameObject>(CubeKey);
            manager.Release(cube);
            Check(manager.IsCached(CubeKey), "常驻存在时释放不应清缓存");

            // 4. Unpreload 归还常驻 → 缓存清理
            manager.Unpreload(CubeKey);
            Check(!manager.IsPreloaded(CubeKey), "Unpreload 后不应常驻");
            Check(!manager.IsCached(CubeKey), "Unpreload 后缓存应清理");

            // 5. 预热失败路径
            bool bad = await manager.PreloadAsync<GameObject>("not.exist");   // 预期错误日志
            Check(!bad && !manager.IsPreloaded("not.exist"), "预热缺失资源应失败且不常驻");

            manager.Shutdown();
        }

        // ==================== 阶段 5：实例化与回收 ====================

        private async UniTask PhaseInstantiate()
        {
            var manager = NewManager();

            // 1. 实例化到测试根节点下
            GameObject clone = manager.Instantiate<GameObject>(CubeKey, _sceneRoot.transform);
            Check(clone != null, "实例化不应为 null");
            Check(clone.transform.parent == _sceneRoot.transform, "实例应挂在指定父节点下");
            Check(manager.IsCached(CubeKey), "实例存活期间模板应保持缓存");

            // 2. 回收实例：
            //    Manager 层状态（清映射/清缓存）是同步确定性的，立即断言；
            //    引擎 Destroy 在帧末真正执行，销毁完成点相对帧恢复时序可能差一帧以上，
            //    因此用轮询等待（最多 10 帧）而不是"yield 一帧"后直接断言。
            manager.ReleaseInstance(clone);
            Check(!manager.IsCached(CubeKey), "回收实例后模板缓存应清理");

            bool destroyed = false;
            for (int i = 0; i < 10 && !destroyed; i++)
            {
                await UniTask.Yield();
                destroyed = clone == null;   // 假 null 检测：native 对象已销毁
            }
            Check(destroyed, "回收后实例应已被销毁(等待 10 帧仍未销毁)");

            // 3. 未经 Manager 跟踪的"散装实例"回收：走警告分支（预期警告日志），不崩溃、不销毁
            var template = manager.Load<GameObject>(CubeKey);          // 临时持有一份模板引用
            GameObject stray = Object.Instantiate(template, _sceneRoot.transform);
            manager.ReleaseInstance(stray);                            // 不在实例映射中 → 警告
            Object.Destroy(stray);
            manager.Release(template);

            manager.Shutdown();
        }

        // ==================== 阶段 6：Shutdown 重置后可重载 ====================

        private async UniTask PhaseShutdown()
        {
            var manager = NewManager();

            // 混合状态：常驻 + 裸引用 + 实例
            await manager.PreloadAsync<GameObject>(CubeKey);
            var cube = manager.Load<GameObject>(CubeKey);
            manager.Instantiate<GameObject>(CubeKey, _sceneRoot.transform);

            manager.Shutdown();   // 不应抛异常
            Check(!manager.IsCached(CubeKey), "Shutdown 后缓存应清空");
            Check(!manager.IsPreloaded(CubeKey), "Shutdown 后常驻表应清空");

            // Shutdown 后仍可正常重载
            var cube2 = manager.Load<GameObject>(CubeKey);
            Check(cube2 != null, "Shutdown 后应可重新加载");
            manager.Release(cube2);

            manager.Shutdown();
        }

        // ==================== 阶段 7：深层卸载（引擎内存回收） ====================

        private async UniTask PhaseDeepUnload()
        {
            var manager = NewManager();

            var cube = manager.Load<GameObject>(CubeKey);
            Check(cube != null, "预加载应成功");
            manager.Release(cube);
            Check(!manager.IsCached(CubeKey), "释放后 Manager 缓存应清理");

            // 让引擎真正回收（Resources 加载的 GameObject 只能靠 UnloadUnusedAssets 卸载）
            await Resources.UnloadUnusedAssets().ToUniTask();

            // 注意：编辑器可能因 Project 窗口选中/资源钉住导致资产不被回收，此断言只在真机/非编辑器下生效；
            // 编辑器下输出提示供人工确认。
            if (Application.isEditor)
            {
                Debug.Log($"[RMResourcesTest] (编辑器跳过深层回收断言) cube == null = {cube == null}，" +
                          "如需验证请跑真机或 Standalone 构建");
            }
            else
            {
                Check(cube == null, "UnloadUnusedAssets 完成后资源应被引擎真正卸载(假 null)");
            }

            manager.Shutdown();
        }
    }
}
