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

    public sealed class CompositeLogger : Logger {
        private readonly Logger[] loggers;

        public CompositeLogger(params Logger[] loggers) {
            this.loggers = loggers ?? Array.Empty<Logger>();
        }

        public override void StartLogger() {
            foreach(Logger logger in loggers) {
                logger?.StartLogger();
            }
        }

        public override void StopLogger() {
            foreach(Logger logger in loggers) {
                logger?.StopLogger();
            }
        }

        public override void Log(object value) {
            foreach(Logger logger in loggers) {
                logger?.Log(value);
            }
        }
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
        private const int LinesMax = 20000;
        private const int LinesErase = 5000;

        private readonly string filePath;

        private int lineNumber;
        private readonly Queue<string> linesQueue = new Queue<string>();
        private readonly CancellationTokenSource tokenSource = new CancellationTokenSource();
        private readonly ManualResetEvent manualEvent = new ManualResetEvent(false);
        private Thread loggingThread;
        private volatile bool acceptingMessages;

        public FileLogger(string filePath) {
            this.filePath = filePath;
        }

        public override void StartLogger() {
            if(loggingThread != null) {
                return;
            }

            acceptingMessages = true;
            loggingThread = new Thread(() => {
                lineNumber = 0;
                if(!File.Exists(filePath)) {
                    using(File.Create(filePath)) { }
                } else {
                    using(FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                        using(StreamReader reader = new StreamReader(stream)) {
                            while(reader.ReadLine() != null) {
                                lineNumber++;
                            }
                        }
                    }
                }

                StreamWriter writer = OpenWriter();
                try {
                    while(true) {
                        manualEvent.WaitOne();

                        while(true) {
                            string line;
                            lock(linesQueue) {
                                if(linesQueue.Count == 0) {
                                    manualEvent.Reset();
                                    break;
                                }
                                line = linesQueue.Dequeue();
                            }

                            if(lineNumber >= LinesMax) {
                                writer.Flush();
                                writer.Dispose();
                                CompactLog();
                                writer = OpenWriter();
                            }

                            writer.WriteLine(line);
                            lineNumber++;
                        }

                        writer.Flush();
                        if(tokenSource.IsCancellationRequested) {
                            lock(linesQueue) {
                                if(linesQueue.Count == 0) {
                                    return;
                                }
                            }
                        }
                    }
                } finally {
                    writer?.Dispose();
                }
            }) {
                IsBackground = true,
                Name = "LiveSplit.Subnautica2 File Logger"
            };
            loggingThread.Start();
        }

        public override void StopLogger() {
            acceptingMessages = false;
            tokenSource.Cancel();
            manualEvent.Set();
            loggingThread?.Join(2000);
            loggingThread = null;
        }

        public override void Log(object value) {
            if(!acceptingMessages || value == null) {
                return;
            }

            lock(linesQueue) {
                linesQueue.Enqueue(DateTime.Now.ToString("HH:mm:ss.fff") + " " + value.ToString());
                manualEvent.Set();
            }
        }

        private StreamWriter OpenWriter() {
            return new StreamWriter(new FileStream(
                filePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite));
        }

        private void CompactLog() {
            string tempLog = filePath + "-temp";
            int linesToKeep = LinesMax - LinesErase;
            var retained = new Queue<string>(linesToKeep);

            try {
                using(FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                    using(StreamReader reader = new StreamReader(stream)) {
                        string line;
                        while((line = reader.ReadLine()) != null) {
                            if(retained.Count == linesToKeep) {
                                retained.Dequeue();
                            }
                            retained.Enqueue(line);
                        }
                    }
                }

                using(StreamWriter writer = File.CreateText(tempLog)) {
                    foreach(string line in retained) {
                        writer.WriteLine(line);
                    }
                }

                File.Copy(tempLog, filePath, true);
                lineNumber = retained.Count;
            } catch(Exception e) {
                Trace.TraceError(e.ToString());
            } finally {
                try {
                    if(File.Exists(tempLog)) {
                        File.Delete(tempLog);
                    }
                } catch(Exception e) {
                    Trace.TraceError(e.ToString());
                }
            }
        }
    }
}
