namespace XiancaiFramework.Base.Pool
{
    /// <summary>
    /// 引用池接口
    /// </summary>
    public interface IReference
    {
        /// <summary>
        /// 入池前的清理方法
        /// </summary>
        void Clear();
    }
}