using UnityEngine;

namespace XiancaiFramework.Base.Fsm
{
    /// <summary>
    /// 抽象基类
    /// </summary>
    /// <typeparam name="T">T=Owner</typeparam>
    public abstract class FsmState<T> : IFsmState where T : class
    {
        protected T Owner { get; }
        protected Fsm<T> Fsm { get; private set; }
        public FsmState(T owner)
        {
            Owner = owner;
        }
        
        public abstract void OnInit();

        public abstract void OnEnter();

        public abstract void OnUpdate(float deltaTime);

        public abstract void OnLeave();

        public abstract void OnDestroy();

        public void Bind(FsmBase fsmBase)
        {
            if (!(fsmBase is Fsm<T> fsm))
            {
                Debug.LogError($"[FsmState<{typeof(T).Name}>] 该状态{fsmBase.GetType().Name}不属于状态机{Fsm.GetType().Name}");
                return;
            }
            Fsm = fsm;
        }
    }
}