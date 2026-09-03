using UnityEngine;

namespace XiancaiFramework.Base.Pool
{
    public class PooledObject : MonoBehaviour, IPooledObject
    {
        public void OnSpawn()
        {
            gameObject.SetActive(true);
        }

        public void OnDespawn()
        {
            gameObject.SetActive(false);
        }
    }
}