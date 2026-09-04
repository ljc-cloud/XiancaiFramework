using System;
using System.Collections;
using UnityEngine;
using XiancaiFramework.Base.Event;

namespace XiancaiFramework.Scripts.Test
{
    public class EventDispatcherTest : MonoBehaviour
    {
        private void Start()
        {
            EventDispatcher.Global.Subscribe<EventA>(OnEventA);

            StartCoroutine(WaitForSeconds(2f));
        }

        private IEnumerator WaitForSeconds(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            EventDispatcher.Global.Publish(new EventA { SomeArg = 1 });
        }

        public void OnEventA(EventA e)
        {
            Debug.Log($"[EventDispatcherTest] OnEventA Call, {e.SomeArg}!");
        }
        
        public class EventA
        {
            public int SomeArg { get; set; }
        }
    }
}