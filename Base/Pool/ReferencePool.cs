using System;
using System.Collections.Generic;

namespace XiancaiFramework.Base.Pool
{
    /// <summary>
    /// 泛型引用池
    /// </summary>
    public static class ReferencePool<T> where T : IReference, new()
    {
        static ReferencePool()
        {
            ReferencePool.RegisterReleaseAction<T>(Release);
            ReferencePool.RegisterClearAction(Clear);
        }
        
        /// <summary>
        /// 对于单个类型的引用池的最大大小
        /// </summary>
        public static int MaxSize { get; set; } = 200;
        
        /// <summary>
        /// 引用池空闲栈
        /// </summary>
        private static readonly Stack<T> ReferenceStack = new Stack<T>();
        
        /// <summary>
        /// 引用池可用大小
        /// </summary>
        public static int Count => ReferenceStack.Count;

        /// <summary>
        /// 总获取次数
        /// </summary>
        public static int TotalAcquire { get; private set; }
        
        /// <summary>
        /// 总释放次数
        /// </summary>
        public static int TotalRelease { get; private set; }
        
        public static T Acquire()
        {
            TotalAcquire++;
            // 1. 查看引用池空闲队列是否为空
            if (ReferenceStack.Count == 0)
            {
                // 如果没有，直接new并返回
                return new T();
            }

            // 2. 从引用池获取并返回
            T obj = ReferenceStack.Pop();

            return obj;
        }

        public static void Release(T obj)
        {
            if (obj == null)
            {
                throw new ArgumentNullException($"[ReferencePool<{typeof(T).Name}>] Released object is null");
            }

            TotalRelease++;
            obj.Clear();
            if (ReferenceStack.Count >= MaxSize)
            {
                // 如果超过了池的最大大小，直接丢弃
                return;
            }
            
            ReferenceStack.Push(obj);
        }

        public static void Clear()
        {
            while (ReferenceStack.Count > 0)
            {
                ReferenceStack.Pop().Clear();
            }
        }
    }


    /// <summary>
    /// 引用池的非泛型门面类
    /// </summary>
    public static class ReferencePool
    {
        private static readonly Dictionary<Type, Action<IReference>> ReleaseActions = new Dictionary<Type, Action<IReference>>();
        private static readonly List<Action> ClearActions = new List<Action>();
        
        public static T Acquire<T>() where T : IReference, new()
        {
            return ReferencePool<T>.Acquire();
        }

        public static void Release<T>(T obj) where T : IReference, new()
        {
            ReferencePool<T>.Release(obj);
        }

        public static void SetCapacity<T>(int capacity) where T : IReference, new()
        {
            ReferencePool<T>.MaxSize = capacity;
        }

        internal static void RegisterReleaseAction<T>(Action<T> action) where T : IReference
        {
            if (action == null)
            {
                throw new ArgumentNullException($"[ReferencePool<{typeof(T).Name}>] RegisterReleaseAction is null]");
            }

            ReleaseActions[typeof(T)] = obj => action((T)obj);
        }

        public static void ReleaseReference(IReference obj)
        {
            if (ReleaseActions.TryGetValue(obj.GetType(), out Action<IReference> action))
            {
                action(obj);
            }
        }
        
        internal static void RegisterClearAction(Action action)
        {
            ClearActions.Add(action);
        }

        public static void ClearAll()
        {
            foreach (var action in ClearActions)
            {
                action();
            }
        }
    }

}