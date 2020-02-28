﻿using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SocketHook.HostedWpfSample.Threading
{
    // from BCL
    public sealed class TimerAwaitable : IDisposable, ICriticalNotifyCompletion
    {
        private static readonly Action _callbackCompleted = () => { };
        private readonly object _lockObj = new object();

        private readonly TimeSpan _dueTime;
        private readonly TimeSpan _period;

        private Action _callback;
        private Timer _timer;

        private bool _disposed;
        private bool _running = true;


        public TimerAwaitable(TimeSpan dueTime, TimeSpan period)
        {
            _dueTime = dueTime;
            _period = period;
        }

        public void Start()
        {
            if (_timer != null) return;
            lock (_lockObj)
            {
                if (_disposed)
                {
                    return;
                }

                if (_timer != null) return;
                // Don't capture the current ExecutionContext and its AsyncLocals onto the timer
                var restoreFlow = false;
                try
                {
                    if (!ExecutionContext.IsFlowSuppressed())
                    {
                        ExecutionContext.SuppressFlow();
                        restoreFlow = true;
                    }

                    _timer = new Timer(state => ((TimerAwaitable)state).Tick(), this, _dueTime, _period);
                }
                finally
                {
                    // Restore the current ExecutionContext
                    if (restoreFlow)
                    {
                        ExecutionContext.RestoreFlow();
                    }
                }
            }
        }

        public TimerAwaitable GetAwaiter() => this;
        public bool IsCompleted => ReferenceEquals(_callback, _callbackCompleted);

        public bool GetResult()
        {
            _callback = null;
            return _running;
        }

        private void Tick()
        {
            var continuation = Interlocked.Exchange(ref _callback, _callbackCompleted);
            continuation?.Invoke();
        }

        public void OnCompleted(Action continuation)
        {
            if (ReferenceEquals(_callback, _callbackCompleted) ||
                ReferenceEquals(Interlocked.CompareExchange(ref _callback, continuation, null), _callbackCompleted))
            {
                Task.Run(continuation);
            }
        }

        public void UnsafeOnCompleted(Action continuation) => OnCompleted(continuation);

        public void Stop()
        {
            lock (_lockObj)
            {
                // Stop should be used to trigger the call to end the loop which disposes
                if (_disposed)
                {
                    throw new ObjectDisposedException(GetType().FullName);
                }

                _running = false;
            }

            // Call tick here to make sure that we yield the callback,
            // if it's currently waiting, we don't need to wait for the next period
            Tick();
        }

        public void Dispose()
        {
            lock (_lockObj)
            {
                _disposed = true;
                _timer?.Dispose();
                _timer = null;
            }
        }
    }
}