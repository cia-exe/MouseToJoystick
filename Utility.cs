using static System.DateTimeOffset;
using System.Diagnostics;

namespace Cia.Exe
{
    public class LooperHandler : IDisposable
    {
        private readonly PriorityQueue<Action, long> queue = new();
        private readonly AutoResetEvent semaphore = new(false);
        private readonly Thread workerThread;

        public LooperHandler()
        {
            workerThread = new Thread(WorkerThread);
            // Background threads are identical to foreground threads, except that background threads do not prevent a process from terminating.
            workerThread.IsBackground = true;
            workerThread.Start();
        }

        public void PostDelayed(Action action, int delayMilliseconds)
        {
            var executeTime = UtcNow.ToUnixTimeMilliseconds() + delayMilliseconds;
            lock (queue)
            {
                queue.Enqueue(action, executeTime);
                //if (queue.TryPeek(out var _, out var earliestExecuteTime) && earliestExecuteTime == executeTime) semaphore.Set();
                semaphore.Set();
            }
        }

        private volatile bool running = true;
        private void WorkerThread()
        {
            while (running)
            {
                Action? action = null;
                int waitTime = 0;

                lock (queue)
                {
                    if (queue.TryPeek(out _, out var time))
                    {
                        waitTime = (int)(time - UtcNow.ToUnixTimeMilliseconds());
                        if (waitTime <= 0) queue.TryDequeue(out action, out time);
                    }
                    else waitTime = int.MaxValue;
                }

                action?.Invoke();
                if (waitTime > 0) semaphore.WaitOne(waitTime);
            }

            Debug.WriteLine($"{Util.Tid()} WorkerThread exit...");
        }


        #region IDisposable Support

        protected virtual void Dispose(bool disposing) {
            running = false;
            semaphore.Set();
        }

        public void Dispose() => Dispose(true); // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        #endregion

    }

    public static class Util
    {
        public static string Tid() => $"[{Environment.CurrentManagedThreadId:X}]";
    }
}
