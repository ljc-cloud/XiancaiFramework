using System.Collections.Generic;
using UnityEngine;

namespace XiancaiFramework.Base.Pool
{
    /// <summary>
    /// 对象池的实现
    /// </summary>
    public class ObjectPoolManager
    {
        private readonly Dictionary<GameObject, ObjectPool> _pools = new Dictionary<GameObject, ObjectPool>();
        
        public int PoolCount => _pools.Count;

        public GameObject Spawn(GameObject key)
        {
            GameObject spawned = null;
            if (_pools.TryGetValue(key, out var pool))
            {
                spawned = pool.Spawn();
            }
            else
            {
                pool = new ObjectPool(key);
                _pools[key] = pool;
                spawned = pool.Spawn();
            }
            
            if (spawned == null)
            {
                Debug.LogError($"[ObjectPoolManager] Spawn key:{key.name}，Spawn为空");
                return null;
            }

            IPooledObject pooledObject = spawned.GetComponent<IPooledObject>();
            if (pooledObject == null)
            {
                Debug.LogError($"[ObjectPoolManager] Spawn key:{key.name}，没有池化组件");
                return spawned;
            }
            pooledObject.OnSpawn();
            
            return pool.Spawn();
        }

        public void Despawn(GameObject key, GameObject obj)
        {
            if (_pools.TryGetValue(key, out var pool))
            {
                IPooledObject pooledObject = obj.GetComponent<IPooledObject>();
                pooledObject.OnDespawn();
                pool.Despawn(obj);
                return;
            }

            Debug.LogWarning($"[ObjectPoolManager] Despawn key:{key.name}, 不存在该池");
        }
    }
}