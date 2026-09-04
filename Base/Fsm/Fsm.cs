using System;
using System.Collections.Generic;
using UnityEngine;

namespace XiancaiFramework.Base.Fsm
{
    /// <summary>
    /// 状态机泛型实例，管理一组状态
    /// </summary>
    /// <typeparam name="T">T=Owner</typeparam>
    public class Fsm<T> : FsmBase where T : class
    {
        /// <summary>
        /// 当前状态机持有者
        /// </summary>
        private readonly T _owner;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="owner">持有者引用</param>
        /// <param name="states">所有状态</param>
        public Fsm(T owner, params FsmState<T>[] states)
        {
            _owner = owner;
            foreach (var fsmState in states)
            {
                // if (!_states.TryAdd(fsmState.GetType(), fsmState))
                // {
                //     Debug.LogError($"[Fsm<{typeof(T).Name}>] 重复添加多种类型");
                // }
                AddState(fsmState);
            }
        }

        /// <summary>
        /// 该状态机的所有状态
        /// </summary>
        private readonly Dictionary<Type, FsmState<T>> _states = new Dictionary<Type, FsmState<T>>();
        
        /// <summary>
        /// 待切换的状态
        /// </summary>
        private FsmState<T> _pendingState;
        
        /// <summary>
        /// 是否正在切换状态（OnLeave、OnEnter）
        /// </summary>
        private bool _isTransitioning;

        /// <summary>
        /// 是否已被销毁
        /// </summary>
        private bool _destroyed;

        /// <summary>
        /// 当前的状态
        /// </summary>
        public FsmState<T> CurrentState { get; private set; }

        /// <summary>
        /// 状态机初始化
        /// </summary>
        public override void Init()
        {
            foreach (var fsmState in _states.Values)
            {
                fsmState.OnInit();
            }
        }

        /// <summary>
        /// 添加状态
        /// </summary>
        /// <param name="state">目标状态</param>
        public override void AddState(IFsmState state)
        {
            
            if (!(state is FsmState<T> typed))
            {
                Debug.LogError($"[Fsm<{typeof(T)}>] 状态 {state.GetType().Name} 不属于该持有者");
                return;
            }
            typed.OnInit();
            typed.Bind(this);
            _states[state.GetType()] = typed;
        }

        /// <summary>
        /// 状态机帧更新
        /// </summary>
        /// <param name="deltaTime"></param>
        public override void DoUpdate(float deltaTime)
        {
            if (_destroyed) return;
            CurrentState?.OnUpdate(deltaTime);
        }

        /// <summary>
        /// 切换状态
        /// </summary>
        /// <typeparam name="TState">目标状态类型</typeparam>
        public override void ChangeState<TState>()
        {
            if (_destroyed) return;
            if (!_states.TryGetValue(typeof(TState), out FsmState<T> newState))
            {
                Debug.LogError($"[Fsm<{typeof(T)}>] {typeof(TState).Name}不存在该状态");
                return;
            }
            
            RequestTransition(newState);
        }

        /// <summary>
        /// 请求切换状态
        /// 正在切换时，设置待切换状态
        /// </summary>
        /// <param name="next"></param>
        private void RequestTransition(FsmState<T> next)
        {
            if (CurrentState == next)
            {
                return;
            }

            if (_isTransitioning)
            {
                _pendingState = next;
                return;
            }

            DoTransition(next);
        }

        /// <summary>
        /// 切换状态Core
        /// </summary>
        /// <param name="next"></param>
        private void DoTransition(FsmState<T> next)
        {
            _isTransitioning = true;
            try
            {
                CurrentState?.OnLeave();
                CurrentState = next;
                CurrentState.OnEnter();
            }
            finally
            {
                _isTransitioning = false;
            }
            
            FlushPending();
        }

        /// <summary>
        /// 处理待切换状态
        /// </summary>
        private void FlushPending()
        {
            int guard = 0;
            while (_pendingState != null)
            {
                if (++guard == 30)
                {
                    _pendingState = null;
                    Debug.LogError($"[Fsm<{typeof(T)}>] 状态切换疑似死循环，已熔断");
                    return;
                }
                
                FsmState<T> next = _pendingState;
                _pendingState = null;
                if (CurrentState == next)
                {
                    return;
                }
                DoTransition(next);
            }
        }

        /// <summary>
        /// 销毁状态机
        /// </summary>
        public override void Destroy()
        {
            _destroyed = true;
            foreach (var fsmState in _states.Values)
            {
                fsmState.OnDestroy();
            }
            _states.Clear();
            _pendingState = null;
            CurrentState = null;
        }
    }
}