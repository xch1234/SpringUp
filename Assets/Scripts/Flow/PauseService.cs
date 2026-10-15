using System;
using System.Collections.Generic;
using UnityEngine;
namespace SpringUp.Flow
{
    // Only the bootstrap owns this service. Tokens are idempotent and survive nested windows.
    public sealed class PauseService : IDisposable
    {
        readonly HashSet<object> reasons = new();
        bool disposed;
        public bool IsPaused => reasons.Count != 0;
        public IDisposable Acquire()
        {
            if (disposed) throw new ObjectDisposedException(nameof(PauseService));
            var key = new object(); reasons.Add(key); Apply();
            return new Lease(() => { if (disposed) return; reasons.Remove(key); Apply(); });
        }
        void Apply() => Time.timeScale = IsPaused ? 0 : 1;
        public void Dispose() { if (disposed) return; reasons.Clear(); Apply(); disposed = true; }
        sealed class Lease : IDisposable
        {
            Action release;
            public Lease(Action action) => release = action;
            public void Dispose() { var action = release; release = null; action?.Invoke(); }
        }
    }
}
