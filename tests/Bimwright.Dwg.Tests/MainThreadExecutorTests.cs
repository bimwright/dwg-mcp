using System;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    // A synchronous host thread is the subject under test; awaiting would let
    // xUnit resume on another thread and invalidate that ownership contract.
#pragma warning disable xUnit1031
    public class MainThreadExecutorTests
    {
        [Fact]
        public void Worker_request_runs_on_host_thread_and_propagates_result()
        {
            var executor = new MainThreadExecutor();
            int host = Thread.CurrentThread.ManagedThreadId;
            var task = Task.Run(() => executor.Invoke(() => Thread.CurrentThread.ManagedThreadId));
            PumpUntilCompleted(executor, task);
            Assert.Equal(host, task.GetAwaiter().GetResult());
        }

        [Fact]
        public void Busy_host_timeout_never_executes_cancelled_work_later()
        {
            var executor = new MainThreadExecutor(10);
            int mutations = 0;
            var task = Task.Run(() => executor.Invoke(() => ++mutations));
            Assert.Throws<TimeoutException>(() => task.GetAwaiter().GetResult());
            executor.RunPending();
            Assert.Equal(0, mutations);
        }

        [Fact]
        public void Exceptions_propagate_and_next_request_can_run()
        {
            var executor = new MainThreadExecutor();
            var task = Task.Run(() => executor.Invoke<int>(() => throw new InvalidOperationException("boom")));
            PumpUntilCompleted(executor, task);
            Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());
            var next = Task.Run(() => executor.Invoke(() => 42));
            PumpUntilCompleted(executor, next);
            Assert.Equal(42, next.GetAwaiter().GetResult());
        }

        [Fact]
        public void Host_calls_are_inline_and_wrong_thread_cannot_pump()
        {
            var executor = new MainThreadExecutor();
            Assert.Equal(42, executor.Invoke(() => 42));
            Assert.Throws<InvalidOperationException>(() => Task.Run(() => executor.RunPending()).GetAwaiter().GetResult());
        }

        private static void PumpUntilCompleted(MainThreadExecutor executor, Task task)
        {
            Assert.True(SpinWait.SpinUntil(() => { executor.RunPending(); return task.IsCompleted; }, 5000));
        }
    }
}
