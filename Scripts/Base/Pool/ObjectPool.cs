using System.Collections.Generic;
using UnityEngine;

namespace XiancaiFramework.Base.Pool
{
    public class ObjectPool
    {
        public ObjectPool(GameObject key)
        {
            _key = key;
            GameObject go = new GameObject($"{key.name}_PoolRoot");
            Object.DontDestroyOnLoad(go);
            _poolRoot = go.transform;
        }
        
        private readonly GameObject _key;
        private readonly Transform _poolRoot;
        
        private readonly Queue<GameObject> _pool = new Queue<GameObject>();
        
        public int IdleCount => _pool.Count;

        public void Prewarm(int count)
        {
            for (int i = 0; i < count; i++)
            {
                GameObject obj = Object.Instantiate(_key, _poolRoot);
                if (obj.TryGetComponent<IPooledObject>(out var po)) po.OnDespawn();
                obj.SetActive(false);
                _pool.Enqueue(obj);
            }
        }
        
        public GameObject Spawn()
        {
            while (_pool.Count > 0)
            {
                GameObject obj = _pool.Dequeue();
                
                if (obj == null) continue;
                
                obj.transform.SetParent(_poolRoot);
                if (obj.TryGetComponent<IPooledObject>(out var pooledObject)) pooledObject.OnSpawn();
                obj.SetActive(true);
                
                return obj;
            }
            
            GameObject fresh = Object.Instantiate(_key, _poolRoot);
            if (fresh.TryGetComponent<IPooledObject>(out var po)) po.OnSpawn();
            fresh.SetActive(true);                 // 新实例与复用实例同一契约
            return fresh;
        }

        public void Despawn(GameObject obj)
        {
            if (obj == null) return;
            obj.transform.SetParent(_poolRoot);  // 归位，避免随父销毁/父 inactive 污染
            
            if (obj.TryGetComponent<IPooledObject>(out var po)) po.OnDespawn();
            obj.SetActive(false);
            
            _pool.Enqueue(obj);
        }
    }
}