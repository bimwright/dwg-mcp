using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class DwgApiExecutorTests
    {
        [Fact]
        public async Task InvokeAsync_SerializesConcurrentWork()
        {
            var active = 0;
            var maxActive = 0;

            var tasks = Enumerable.Range(0, 12)
                .Select(i => DwgApiExecutor.InvokeAsync(async () =>
                {
                    var now = Interlocked.Increment(ref active);
                    maxActive = Math.Max(maxActive, now);
                    await Task.Delay(10);
                    Interlocked.Decrement(ref active);
                    return i;
                }))
                .ToArray();

            var results = await Task.WhenAll(tasks);

            Assert.Equal(1, maxActive);
            Assert.Equal(Enumerable.Range(0, 12), results);
        }

        [Fact]
        public async Task InvokeAsync_ExceptionDoesNotBlockQueue()
        {
            var first = DwgApiExecutor.InvokeAsync(() => Task.FromException<int>(new InvalidOperationException("boom")));
            var second = DwgApiExecutor.InvokeAsync(() => Task.FromResult(42));

            await Assert.ThrowsAsync<InvalidOperationException>(() => first);
            Assert.Equal(42, await second);
        }

        [Fact]
        public async Task InvokeAsync_PreservesFifoOrder()
        {
            var order = new List<int>();
            var tasks = Enumerable.Range(0, 8)
                .Select(i => DwgApiExecutor.InvokeAsync(() =>
                {
                    order.Add(i);
                    return Task.FromResult(i);
                }))
                .ToArray();

            await Task.WhenAll(tasks);

            Assert.Equal(Enumerable.Range(0, 8), order);
        }
    }
}
