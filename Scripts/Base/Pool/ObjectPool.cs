using System.Collections.Generic;
using UnityEngine;

namespace XiancaiFramework.Base.Pool
{
    public class ObjectPool
    {
        public ObjectPool(GameObject key)
        {
            _key = key;
        }
        
        private readonly GameObject _key;
        
        private readonly Queue<GameObject> _pool = new Queue<GameObject>();

        public GameObject Spawn()
        {
            if (_pool.Count > 0)
            {
                Debug.Log($"[ObjectPool] Spawn key:{_key.name} 剩余数量：{_pool.Count - 1}");
                return _pool.Dequeue();
            }

            Debug.Log($"[ObjectPool] Spawn key:{_key.name} 池内数量不足");
            return Object.Instantiate(_key);
        }

        public void Despawn(GameObject obj)
        {
            _pool.Enqueue(obj);
            Debug.Log($"[ObjectPool] Despawn key:{_key.name}，剩余数量：{_pool.Count}");
        }
    }
}