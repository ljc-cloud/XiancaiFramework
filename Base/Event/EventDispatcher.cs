using System;
using System.Collections.Generic;
using UnityEngine;
using XiancaiFramework.Base.Pool;

namespace XiancaiFramework.Base.Event
{
    /// <summary>
    /// 事件调度器
    /// </summary>
    public class EventDispatcher
    {
        private static EventDispatcher _global;
        
        /// <summary>
        /// 全局使用的事件实例
        /// </summary>
        public static EventDispatcher Global
        {
            get
            {
                if (_global == null)
                {
                    _global = new EventDispatcher();
                }
                return _global;
            }
        }
        
        /// <summary>
        /// 事件处理器字典
        /// </summary>
        private readonly Dictionary<Type, List<Delegate>> _eventHandlers = new Dictionary<Type, List<Delegate>>();

        /// <summary>
        /// 订阅事件
        /// </summary>
        /// <param name="handler">action处理器</param>
        /// <typeparam name="TEvent">事件键+事件参数</typeparam> 
        public void Subscribe<TEvent>(Action<TEvent> handler)
        {
            var eventType = typeof(TEvent);

            if (_eventHandlers.TryGetValue(eventType, out var handlers))
            {
                if (handlers.Contains(handler))
                {
                    Debug.LogWarning($"[{GetType().Name}] {eventType.Name} 同时订阅两次");
                    return;
                }
                handlers.Add(handler);
            }
            else
            {
                _eventHandlers[eventType] = new List<Delegate> { handler };
            }
        }

        /// <summary>
        /// 取消订阅事件
        /// </summary>
        /// <param name="handler">action处理器</param>
        /// <typeparam name="TEvent">事件键</typeparam>
        public void Unsubscribe<TEvent>(Action<TEvent> handler)
        {
            var eventType = typeof(TEvent);
            if (_eventHandlers.TryGetValue(eventType, out var eventHandlers))
            {
                eventHandlers.Remove(handler);
                if (eventHandlers.Count == 0) _eventHandlers.Remove(eventType);
            }
        }

        /// <summary>
        /// 取消订阅所有事件
        /// </summary>
        /// <typeparam name="TEvent">事件键</typeparam>
        public void UnsubscribeAll<TEvent>()
        {
            var eventType = typeof(TEvent);
            _eventHandlers.Remove(eventType);
        }

        /// <summary>
        /// 发布事件
        /// </summary>
        /// <param name="evt">事件参数</param>
        /// <typeparam name="TEvent">事件键</typeparam>
        public void Publish<TEvent>(TEvent evt)
        {
            if (evt == null)
            {
                throw new ArgumentNullException($"[{GetType().Name}] 事件发布参数 evt 是空");
            }
            
            var eventType = typeof(TEvent);
            if (_eventHandlers.TryGetValue(eventType, out var eventHandlers))
            {
                foreach (var handler in eventHandlers.ToArray())
                {
                    try
                    {
                        Action<TEvent> a = (Action<TEvent>)handler;
                        a(evt);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[{GetType().Name}] 发布事件处理器异常, msg: {e.Message}");
                    }
                }
            }

            if (eventType.IsClass && evt is IReference obj)
            {
                ReferencePool.ReleaseReference(obj);
            }
        }
    }
}