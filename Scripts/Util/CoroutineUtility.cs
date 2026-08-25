using System.Collections;
using UnityEngine;

namespace XiancaiFramework.Scripts.Util
{
    /// <summary>
    /// 协程工具类
    /// </summary>
    public static class CoroutineUtility
    {
        private class CoroutineRunner : MonoBehaviour {}
        
        private static CoroutineRunner _runner;

        static CoroutineUtility()
        {
            GameObject go = new GameObject("CoroutineRunner");
            go.hideFlags = HideFlags.HideAndDontSave;
            _runner = go.AddComponent<CoroutineRunner>();
        }

        public static Coroutine StartCoroutine(IEnumerator routine)
        {
            if (routine == null) return null;
            
            return _runner.StartCoroutine(routine);
        }

        public static void StopCoroutine(IEnumerator routine)
        {
            _runner.StopCoroutine(routine);
        }
        
        public static void StopCoroutine(string coroutineName)
        {
            _runner.StopCoroutine(coroutineName);
        }
    }
}