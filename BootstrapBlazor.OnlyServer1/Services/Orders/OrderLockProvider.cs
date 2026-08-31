using System.Collections.Concurrent;
using System.Diagnostics;

namespace BootstrapBlazor.OnlyServer1.Services.OrderManagement
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

        // (2026-07-09 Review #L1):后台清理节流(每 5 分钟扫一次,移除空闲 key)
        private const long CleanupIntervalMs = 5 * 60 * 1000;
        private long _lastCleanupTicks;  // 初始为 0,首次 AcquireAsync 立即触发清理

        public OrderLockProvider(ILogger<OrderLockProvider> logger)
        {
            _logger = logger;
        }

        public async Task<IDisposable> AcquireAsync(int orderId, CancellationToken ct = default)
        {
            // (2026-07-09 Review #L1) 节流式后台清理:防 _locks 字典无限增长
            MaybeCleanup();

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
            return new Releaser(orderId, sem, heldSw, waitMs, _logger, this);
        }

        /// <summary>
        /// (2026-07-09 Review #L1) 节流式后台清理:5 分钟一次,扫描 _locks,
        /// 把"当前没人占"(CurrentCount==1)的 SemaphoreSlim 移除并 Dispose。
        /// 锁竞争(被持有)状态不会误删,因为 CurrentCount 此时为 0。
        /// </summary>
        private void MaybeCleanup()
        {
            var nowTicks = Stopwatch.GetTimestamp();
            var lastTicks = Interlocked.Read(ref _lastCleanupTicks);
            var elapsedTicks = nowTicks - lastTicks;
            var intervalTicks = (long)(CleanupIntervalMs * Stopwatch.Frequency / 1000.0);
            if (elapsedTicks < intervalTicks) return;

            // 抢占式:只有一个线程真清理,其他线程跳过这一轮
            if (Interlocked.CompareExchange(ref _lastCleanupTicks, nowTicks, lastTicks) != lastTicks)
            {
                return;
            }

            int removed = 0;
            try
            {
                foreach (var key in _locks.Keys.ToList())
                {
                    if (_locks.TryGetValue(key, out var sem) && sem.CurrentCount == 1)
                    {
                        if (_locks.TryRemove(key, out var oldSem))
                        {
                            oldSem.Dispose();
                            removed++;
                        }
                    }
                }
                if (removed > 0)
                {
                    _logger.LogInformation(
                        "OrderLock 后台清理:移除 {Count} 个空闲 SemaphoreSlim,剩余 {Remaining} 个活跃 key",
                        removed, _locks.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OrderLock 后台清理失败,忽略(下次 5 分钟后重试)");
            }
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
            private readonly OrderLockProvider _owner;

            public Releaser(int orderId, SemaphoreSlim sem, Stopwatch heldSw, long waitMs, ILogger logger, OrderLockProvider owner)
            {
                _orderId = orderId;
                _sem = sem;
                _heldSw = heldSw;
                _waitMs = waitMs;
                _logger = logger;
                _owner = owner;
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

                // (2026-07-09 Review #L1) 释放后顺手检查一次后台清理
                _owner.MaybeCleanup();
            }
        }
    }
}
