namespace XiancaiFramework.Base.Pool
{
    public interface IPooledObject
    {
        void OnSpawn();
        
        void OnDespawn();
    }
}