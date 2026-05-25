using System;
using System.Threading.Tasks;

namespace Bimwright.Dwg.Plugin
{
    public static class DwgApiExecutor
    {
        private static readonly object Gate = new object();
        private static Task _tail = Task.CompletedTask;

        public static T Invoke<T>(Func<T> work)
        {
            return InvokeAsync(work).GetAwaiter().GetResult();
        }

        public static Task<T> InvokeAsync<T>(Func<T> work)
        {
            if (work == null)
            {
                throw new ArgumentNullException(nameof(work));
            }

            return InvokeAsync(() => Task.FromResult(work()));
        }

        public static Task<T> InvokeAsync<T>(Func<Task<T>> work)
        {
            if (work == null)
            {
                throw new ArgumentNullException(nameof(work));
            }

            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (Gate)
            {
                var predecessor = _tail;
                _tail = RunAfter(predecessor, work, completion);
            }

            return completion.Task;
        }

        private static async Task RunAfter<T>(
            Task predecessor,
            Func<Task<T>> work,
            TaskCompletionSource<T> completion)
        {
            try
            {
                try
                {
                    await predecessor.ConfigureAwait(false);
                }
                catch
                {
                    // Earlier queued failures are reported to their callers and must not block the queue.
                }

                var result = await work().ConfigureAwait(false);
                completion.SetResult(result);
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        }
    }
}
