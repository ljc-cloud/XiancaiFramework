namespace XiancaiFramework.Base.Fsm
{
    /// <summary>
    /// 状态接口
    /// </summary>
    public interface IFsmState
    {
        /// <summary>
        /// 状态初始化回调
        /// </summary>
        void OnInit();
        /// <summary>
        /// 进入状态回调
        /// </summary>
        void OnEnter();
        /// <summary>
        /// 状态帧更新回调
        /// </summary>
        /// <param name="deltaTime"></param>
        void OnUpdate(float deltaTime);
        /// <summary>
        /// 离开状态回调
        /// </summary>
        void OnLeave();
        /// <summary>
        /// 销毁状态回调
        /// </summary>
        void OnDestroy();
        /// <summary>
        /// 绑定状态机（仅在框架内部调用）
        /// </summary>
        /// <param name="fsmBase"></param>
        void Bind(FsmBase fsmBase);
    }
}