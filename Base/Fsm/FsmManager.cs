using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = System.Object;

namespace XiancaiFramework.Base.Fsm
{
    /// <summary>
    /// 状态机管理器
    /// 管理所有实例状态机
    /// </summary>
    public class FsmManager
    {
        private readonly Dictionary<Object, FsmBase> _fsmMap = new Dictionary<Object, FsmBase>();

        /// <summary>
        /// 创建Fsm
        /// </summary>
        /// <param name="owner">状态机持有者</param>
        /// <param name="states">持有者的所有状态</param>
        /// <typeparam name="T">持有者类型</typeparam>
        public Fsm<T> CreateFsm<T>(T owner, params FsmState<T>[] states) where T : class
        {
            if (owner == null) return null;
            if (_fsmMap.ContainsKey(owner))
            {
                Debug.LogError($"[FsmManager] 已存在{typeof(T).Name}的状态机");
                return null;
            }
            
            Fsm<T> fsm = new Fsm<T>(owner);
            _fsmMap.Add(owner, fsm);
            foreach (var state in states)
            {
                fsm.AddState(state);
            }

            fsm.Init();
            return fsm;
        }

        /// <summary>
        /// 获取状态机
        /// </summary>
        /// <param name="owner">持有者</param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public Fsm<T> GetFsm<T>(T owner) where T : class
        {
            if (owner == null) return null;
            if (!_fsmMap.TryGetValue(owner, out var fsm))
            {
                Debug.LogError($"[FsmManager] 不存在{owner.GetType().Name}的的状态机");
                return null;
            }

            return fsm as Fsm<T>;
        }

        /// <summary>
        /// 添加状态机状态
        /// </summary>
        /// <param name="owner">持有者</param>
        /// <param name="state">新增状态</param>
        /// <typeparam name="T">持有者类型</typeparam>
        public Fsm<T> AddState<T>(T owner, FsmState<T> state) where T : class
        {
            if (owner == null || state == null) return null;
            if (!_fsmMap.TryGetValue(owner, out var fsm))
            {
                Debug.LogError($"[FsmManager] AddState 该Owner:{typeof(T)}的状态机不存在");
                return null;
            }

            fsm.AddState(state);
            return fsm as Fsm<T>;
        }

        /// <summary>
        /// 状态机更新
        /// </summary>
        /// <param name="deltaTime"></param>
        public void Update(float deltaTime)
        {
            foreach (var fsm in _fsmMap.Values.ToArray())
            {
                fsm.DoUpdate(deltaTime);
            }
        }

        /// <summary>
        /// 状态机切换状态
        /// </summary>
        /// <typeparam name="T">T=Owner</typeparam>
        /// <typeparam name="TState">T=状态类型</typeparam>
        public void ChangeState<T, TState>(T owner) where T : class where TState : IFsmState
        {
            if (owner == null) return;
            if (!_fsmMap.TryGetValue(owner, out var fsm))
            {
                Debug.LogError($"[FsmManager] ChangeState 该Owner:{typeof(T)}的状态机不存在");
                return;
            }

            fsm.ChangeState<TState>();
        }

        /// <summary>
        /// 销毁状态机
        /// </summary>
        public void Destroy<T>(T owner) where T : class
        {
            if (!_fsmMap.TryGetValue(owner, out var fsm))
            {
                Debug.LogError($"[FsmManager] Destroy 该Owner:{typeof(T)}的状态机不存在");
                return;
            }
            fsm.Destroy();
            _fsmMap.Remove(owner);
        }

        public void DestroyAll()
        {
            foreach (var fsm in _fsmMap.Values)
            {
                fsm.Destroy();
            }

            _fsmMap.Clear();
        }
    }
}