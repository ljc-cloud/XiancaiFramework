using System.Collections.Generic;
using UnityEngine;

namespace XiancaiFramework.Base.Pool
{
    /// <summary>
    /// 对象池
    /// </summary>
    public class ObjectPoolManager
    {
        /// <summary>
        /// 池字典
        /// </summary>
        private readonly Dictionary<GameObject, ObjectPool> _pools = new Dictionary<GameObject, ObjectPool>();
        
        public int PoolCount => _pools.Count;
        
        public void Prewarm(GameObject key, int count)
        {
            if (_pools.TryGetValue(key, out var pool))
            {
                pool.Prewarm(count);
                return;
            }
            pool = new ObjectPool(key);
            _pools[key] = pool;
            pool.Prewarm(count);
        }
        
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
            
            return spawned;
        }

        public void Despawn(GameObject key, GameObject obj)
        {
            if (obj == null) return;
            if (_pools.TryGetValue(key, out var pool))
            {
                pool.Despawn(obj);
                return;
            }
            Debug.LogWarning($"[ObjectPoolManager] Despawn key:{key.name}, 不存在该池");
            Object.Destroy(obj);
        }
    }
}