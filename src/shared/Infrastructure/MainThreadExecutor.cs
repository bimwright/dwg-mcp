using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Bimwright.Dwg.Plugin
{
    // Host-independent queue: the AutoCAD Idle callback is its only consumer.
    internal sealed class MainThreadExecutor
    {
        private readonly ConcurrentQueue<Action> pending = new ConcurrentQueue<Action>();
        private readonly int threadId;
        private readonly int waitMilliseconds;

        internal MainThreadExecutor(int waitMilliseconds = 15000)
        {
            threadId = Thread.CurrentThread.ManagedThreadId;
            this.waitMilliseconds = waitMilliseconds;
        }

        internal T Invoke<T>(Func<T> action)
        {
            if (Thread.CurrentThread.ManagedThreadId == threadId) return action();
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            int state = 0; // 0 queued, 1 running, 2 cancelled before execution
            pending.Enqueue(() =>
            {
                if (Interlocked.CompareExchange(ref state, 1, 0) != 0) return;
                try { completion.SetResult(action()); }
                catch (Exception ex) { completion.SetException(ex); }
            });
            // Cancel only work that has not started. Never report a timeout and
            // then allow a queued drawing mutation to run later.
            if (!((IAsyncResult)completion.Task).AsyncWaitHandle.WaitOne(waitMilliseconds) &&
                Interlocked.CompareExchange(ref state, 2, 0) == 0)
                throw new TimeoutException("AutoCAD main thread is busy; command was not started. Retry when idle.");
            return completion.Task.GetAwaiter().GetResult();
        }

        internal void RunPending()
        {
            if (Thread.CurrentThread.ManagedThreadId != threadId)
                throw new InvalidOperationException("AutoCAD commands must run on the initialized main thread.");
            if (pending.TryDequeue(out var action)) action();
        }
    }
}
