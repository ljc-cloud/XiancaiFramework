
namespace XiancaiFramework.Base.Fsm
{
    /// <summary>
    /// 状态机基类
    /// 非泛型，便于集中管理
    /// </summary>
    public abstract class FsmBase
    {
        /// <summary>
        /// 初始化状态机
        /// </summary>
        public abstract void Init();
        /// <summary>
        /// 添加状态
        /// </summary>
        /// <param name="state"></param>
        public abstract void AddState(IFsmState state);
        /// <summary>
        /// 当前状态帧更新
        /// </summary>
        /// <param name="deltaTime"></param>
        public abstract void DoUpdate(float deltaTime);
        /// <summary>
        /// 切换状态
        /// </summary>
        /// <typeparam name="TState"></typeparam>
        public abstract void ChangeState<TState>() where TState : IFsmState;
        /// <summary>
        /// 销毁状态机
        /// </summary>
        public abstract void Destroy();
    }
}