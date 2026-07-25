using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Voxif.IO {

    public static class NativeMethods {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool FreeConsole();
    }

    public abstract class Logger {

        private Dictionary<string, Stopwatch> swDict;
        private Dictionary<string, Tuple<int, double>> swAvg;

        protected Dictionary<string, Stopwatch> StopwatchDict {
            get => swDict ?? (swDict = new Dictionary<string, Stopwatch>());
        }
        protected Dictionary<string, Tuple<int, double>> StopwatchAverage {
            get => swAvg ?? (swAvg = new Dictionary<string, Tuple<int, double>>());
        }

        public void StartBenchmark(string key) {
            StopwatchDict.Add(key, Stopwatch.StartNew());
        }

        public void StopBenchmark(string key, string prefix = "") {
            StopwatchDict[key].Stop();
            Log(prefix + StopwatchDict[key].Elapsed);
            StopwatchDict.Remove(key);
        }

        public void StartAverageBenchmark(string key) {
            StopwatchDict.Add(key, Stopwatch.StartNew());
            if(!StopwatchAverage.ContainsKey(key)) {
                StopwatchAverage.Add(key, new Tuple<int, double>(0, 0));
            }
        }

        public void StopAverageBenchmark(string key, string prefix = "") {
            StopwatchDict[key].Stop();
            Tuple<int, double> tuple = StopwatchAverage[key];
            StopwatchAverage[key] = new Tuple<int, double>(tuple.Item1 + 1, tuple.Item2 + StopwatchDict[key].Elapsed.TotalMilliseconds);
            Log(prefix + StopwatchDict[key].Elapsed + " Average " + (tuple.Item2 / tuple.Item1));
            StopwatchDict.Remove(key);
        }

        public abstract void StartLogger();
        public abstract void StopLogger();
        public abstract void Log(object value);
    }

    public class ConsoleLogger : Logger {

        public override void StartLogger() {
            NativeMethods.AllocConsole();
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }

        public override void StopLogger() {
            NativeMethods.FreeConsole();
        }

        public override void Log(object value) {
            Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " " + value.ToString());
        }
    }

    public class FileLogger : Logger {
        private const int LinesMax = 5000;
        private const int LinesErase = 500;

        private readonly string filePath;

        private int lineNumber;
        private readonly Queue<string> linesQueue = new Queue<string>();
        private readonly CancellationTokenSource tokenSource = new CancellationTokenSource();
        private readonly ManualResetEvent manualEvent = new ManualResetEvent(false);

        public FileLogger(string filePath) {
            this.filePath = filePath;
        }

        public override void StartLogger() {
            new Thread(() => {
                lineNumber = 0;
                if(!File.Exists(filePath)) {
                    File.Create(filePath);
                } else {
                    using(FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                        using(StreamReader reader = new StreamReader(stream)) {
                            while(!tokenSource.IsCancellationRequested && reader.ReadLine() != null) {
                                lineNumber++;
                            }
                        }
                    }
                }

                if(tokenSource.IsCancellationRequested) {
                    return;
                }

                string line = null;
                while(true) {
                    manualEvent.WaitOne();
                    if(tokenSource.IsCancellationRequested) {
                        return;
                    }
                    lock(linesQueue) {
                        if(linesQueue.Count != 0) {
                            line = linesQueue.Dequeue();
                        } else {
                            manualEvent.Reset();
                            continue;
                        }
                    }
                    WriteLine(line);
                    line = null;
                }
            }).Start();
        }

        public override void StopLogger() {
            tokenSource.Cancel();
            manualEvent.Set();
        }

        public override void Log(object value) {
            lock(linesQueue) {
                linesQueue.Enqueue(DateTime.Now.ToString("HH:mm:ss.fff") + " " + value.ToString());
                manualEvent.Set();
            }
        }

        protected void WriteLine(string msg) {
            if(lineNumber >= LinesMax) {
                string tempLog = filePath + "-temp";
                int linesSkipped = this.lineNumber - LinesMax + LinesErase;
                int lineNumber = 1;
                using(StreamReader reader = File.OpenText(filePath)) {
                    using(StreamWriter writer = File.CreateText(tempLog)) {
                        string line;
                        while((line = reader.ReadLine()) != null) {
                            if(lineNumber <= linesSkipped) {
                                lineNumber++;
                            } else {
                                writer.WriteLine(line);
                            }
                        }
                    }
                }
                try {
                    File.Copy(tempLog, filePath, true);
                    this.lineNumber = LinesMax - LinesErase;
                } catch {
                    Trace.TraceError("Failed replacing log file");
                } finally {
                    try {
                        File.Delete(tempLog);
                    } catch {
                        Trace.TraceError("Failed deleting temp log file");
                    }
                }
            }

            try {
                using(StreamWriter writer = new StreamWriter(filePath, true)) {
                    writer.WriteLine(msg);
                    ++lineNumber;
                }
            } catch(Exception e) {
                Trace.TraceError(e.ToString());
            }
        }
    }
}