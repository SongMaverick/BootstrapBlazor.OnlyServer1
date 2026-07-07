using System.Collections.Concurrent;
using System.Diagnostics;

namespace BootstrapBlazor.OnlyServer1.Services
{
    /// <summary>
    /// 按 OrderId 维度提供进程内互斥锁。
    /// 用于在"没有 DB 唯一约束"前提下,序列化同一订单的并发写入。
    ///
    /// 限制:仅在单实例进程内生效。多实例负载均衡时需要切换到 SERIALIZABLE 事务。
    ///
    /// 审计:记录锁的等待时间和持有时间。等待 > 500ms 视为争用告警;
    /// 持有 > 2s 视为长事务告警;AcquireAsync 取消(ct 触发)视为客户端超时信号。
    /// </summary>
    public sealed class OrderLockProvider : IDisposable
    {
        private readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();
        private readonly ILogger<OrderLockProvider> _logger;

        // 阈值常量 — 调参统一改这里
        private const long WaitWarnMs = 500;
        private const long WaitErrorMs = 2_000;
        private const long HoldWarnMs  = 2_000;
        private const long HoldErrorMs = 10_000;

        public OrderLockProvider(ILogger<OrderLockProvider> logger)
        {
            _logger = logger;
        }

        public async Task<IDisposable> AcquireAsync(int orderId, CancellationToken ct = default)
        {
            var sem = _locks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
            var waitSw = Stopwatch.StartNew();
            try
            {
                await sem.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                waitSw.Stop();
                _logger.LogWarning(
                    "OrderLock acquire cancelled (client timeout): orderId={OrderId} waitMs={WaitMs}",
                    orderId, waitSw.ElapsedMilliseconds);
                throw;
            }
            waitSw.Stop();

            var waitMs = waitSw.ElapsedMilliseconds;
            var heldSw = Stopwatch.StartNew();
            if (waitMs >= WaitErrorMs)
            {
                _logger.LogError(
                    "OrderLock acquire 严重争用: orderId={OrderId} waitMs={WaitMs} threshold={Threshold}ms",
                    orderId, waitMs, WaitErrorMs);
            }
            else if (waitMs >= WaitWarnMs)
            {
                _logger.LogWarning(
                    "OrderLock acquire 争用: orderId={OrderId} waitMs={WaitMs} threshold={Threshold}ms",
                    orderId, waitMs, WaitWarnMs);
            }
            return new Releaser(orderId, sem, heldSw, waitMs, _logger);
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
            private readonly int _orderId;
            private readonly SemaphoreSlim _sem;
            private readonly Stopwatch _heldSw;
            private readonly long _waitMs;
            private readonly ILogger _logger;

            public Releaser(int orderId, SemaphoreSlim sem, Stopwatch heldSw, long waitMs, ILogger logger)
            {
                _orderId = orderId;
                _sem = sem;
                _heldSw = heldSw;
                _waitMs = waitMs;
                _logger = logger;
            }

            public void Dispose()
            {
                _heldSw.Stop();
                var heldMs = _heldSw.ElapsedMilliseconds;
                _sem.Release();

                if (heldMs >= HoldErrorMs)
                {
                    _logger.LogError(
                        "OrderLock hold 严重超时: orderId={OrderId} waitMs={WaitMs} heldMs={HeldMs} threshold={Threshold}ms",
                        _orderId, _waitMs, heldMs, HoldErrorMs);
                }
                else if (heldMs >= HoldWarnMs)
                {
                    _logger.LogWarning(
                        "OrderLock hold 超时: orderId={OrderId} waitMs={WaitMs} heldMs={HeldMs} threshold={Threshold}ms",
                        _orderId, _waitMs, heldMs, HoldWarnMs);
                }
            }
        }
    }
}
