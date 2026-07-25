using System;
using System.Threading;
using System.Threading.Tasks;
using Voxif.IO;

namespace Voxif.Helpers {
    public class HelperTask : IDisposable {

        protected Task task;
        protected CancellationTokenSource tokenSource;
        public CancellationToken token;

        public bool IsCompleted => task?.IsCompleted ?? true;

        protected readonly Logger logger;

        public HelperTask(Logger logger = null) {
            this.logger = logger;
        }

        protected static void Sleep(int millisecondsTimeout = 50) => Thread.Sleep(millisecondsTimeout);

        protected Task Run(Action action) {
            if(!IsCompleted) {
                tokenSource.Cancel();
                task.Wait();
            }
            tokenSource = new CancellationTokenSource();
            token = tokenSource.Token;
            return task = Task.Factory.StartNew(() => {
                try {
                    action();
                    Log("Task terminated");
                } catch(Exception e) {
                    Log("Task aborted" + Environment.NewLine + e.ToString());
                }
            }, token);
        }

        protected virtual void Log(string msg) => logger?.Log("[Task] " + msg);

        public void Dispose() {
            Log("Dispose");
            tokenSource?.Cancel();
        }
    }
}