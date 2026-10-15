using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    [Collection("Issue22 transport")]
    public class Issue22AdmissionTests
    {
        [Fact]
        public async Task Serialized_API_backlog_is_bounded_even_if_running_host_work_does_not_finish()
        {
            var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var queued = Enumerable.Range(0, DwgApiExecutor.MaxPendingRequests)
                .Select(_ => DwgApiExecutor.InvokeAsync(() => release.Task)).ToArray();
            try
            {
                var rejected = DwgApiExecutor.InvokeAsync(() => 1);
                var error = await Assert.ThrowsAsync<InvalidOperationException>(() => rejected);
                Assert.Contains("not started", error.Message);
            }
            finally { release.TrySetResult(42); await Task.WhenAll(queued); }
            Assert.Equal(7, await DwgApiExecutor.InvokeAsync(() => 7));
        }

        // The main-thread owner cannot await and resume on a different thread.
#pragma warning disable xUnit1031
        [Fact]
        public void Old_listener_queued_host_callback_does_not_start_after_Stop()
        {
            DocumentInvoker.Initialize();
            var accepting = true;
            var mutated = false;
            var callback = Task.Run(() => DocumentInvoker.InvokeSession(() => { mutated = true; return 1; }, () => Volatile.Read(ref accepting)));
            try
            {
                Volatile.Write(ref accepting, false);
                Assert.True(SpinWait.SpinUntil(() => { Application.RaiseIdle(); return callback.IsCompleted; }, 3000));
                Assert.Throws<InvalidOperationException>(() => callback.GetAwaiter().GetResult());
                Assert.False(mutated);
            }
            finally { DocumentInvoker.Shutdown(); }
        }
#pragma warning restore xUnit1031
    }
}
