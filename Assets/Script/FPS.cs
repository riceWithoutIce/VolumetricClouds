//------------------------------------------------------------------------
// |                                                                   |
// | by:Qcbf                                                           |
// |                                       |
// |                                                                   |
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;

namespace YoukiaEngine
{
    public class FPS : MonoBehaviour
    {
        private GUIStyle Style;

        void Awake()
        {
            Style = new GUIStyle();
            Style.fontSize = 30;
            Style.normal.textColor = new Color(1f, 0f, 0f, 1f);
        }

        private Queue<float> _timeQueue = new Queue<float>();
        private float _fps;

        public void Update()
        {
            int fNum = Math.Max((int) _fps, 10);

            _timeQueue.Enqueue(Time.deltaTime);
            while (_timeQueue.Count > fNum)
            {
                _timeQueue.Dequeue();
            }
            float t = 0;
            foreach (float dt in _timeQueue)
            {
                t += dt;
            }
            _fps = (int)(_timeQueue.Count / t);

            sb.Clear();
            sb.AppendFormat("FPS：{0}\n", _fps);
#if ENABLE_PROFILER
            sb.AppendFormat("Mono：{0}", (int)(Profiler.GetMonoUsedSizeLong() / 1024 / 1024));
#endif
            info = sb.ToString();
        }

        private StringBuilder sb = new StringBuilder();
        private string info = string.Empty;

        private void OnGUI()
        {
            GUI.Label(new Rect(Screen.width - 200, 30, 100, 20), info, Style);
        }
    }
}