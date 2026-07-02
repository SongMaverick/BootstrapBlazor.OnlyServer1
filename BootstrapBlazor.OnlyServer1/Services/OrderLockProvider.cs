using System.Collections.Concurrent;

namespace BootstrapBlazor.OnlyServer1.Services
{
    /// <summary>
    /// 按 OrderId 维度提供进程内互斥锁。
    /// 用于在"没有 DB 唯一约束"前提下,序列化同一订单的并发写入。
    ///
    /// 限制:仅在单实例进程内生效。多实例负载均衡时需要切换到 SERIALIZABLE 事务。
    /// </summary>
    public sealed class OrderLockProvider : IDisposable
    {
        private readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();

        public async Task<IDisposable> AcquireAsync(int orderId, CancellationToken ct = default)
        {
            var sem = _locks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
            await sem.WaitAsync(ct);
            return new Releaser(sem);
        }

        public void Dispose()
        {
            foreach (var sem in _locks.Values)
            {
                sem.Dispose();
            }
            _locks.Clear();
        }

        private sealed class Releaser : IDisposable
        {
            private readonly SemaphoreSlim _sem;
            public Releaser(SemaphoreSlim sem) => _sem = sem;
            public void Dispose() => _sem.Release();
        }
    }
}
