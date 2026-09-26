using System;
using System.Collections.Generic;
namespace Bimwright.Dwg.Plugin.Views.Toast
{
    public sealed class ToastNotificationQueue
    {
        private readonly object _gate = new object();
        private readonly Queue<McpToastViewModel> _pending = new Queue<McpToastViewModel>();
        private bool _enabled = true, _running;
        private long _generation;
        public bool Enabled
        {
            get { lock (_gate) return _enabled; }
            set { lock (_gate) { _enabled = value; if (!value) _pending.Clear(); } }
        }
        public Action<McpToastViewModel> Start()
        {
            lock (_gate)
            {
                _pending.Clear();
                _running = true;
                var generation = ++_generation;
                return vm =>
                {
                    lock (_gate)
                    {
                        if (vm == null || !_enabled || !_running || generation != _generation) return;
                        while (_pending.Count >= 4) _pending.Dequeue();
                        _pending.Enqueue(vm);
                    }
                };
            }
        }
        public void Stop() { lock (_gate) { _running = false; ++_generation; _pending.Clear(); } }
        public McpToastViewModel[] Drain()
        {
            lock (_gate)
            {
                var items = _pending.ToArray();
                _pending.Clear();
                return items;
            }
        }
    }
}
