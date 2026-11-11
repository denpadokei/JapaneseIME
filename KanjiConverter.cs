using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace JapaneseIME
{
    /// <summary>
    /// Converts a kana reading to kanji with the Microsoft IME conversion engine.
    ///
    /// Pointer taps cannot use the Windows IME: Beat Saber commits the IME composition
    /// on every UI click, so a reading can never be accumulated for conversion. The
    /// engine is reached through <see cref="NativeKanjiConverter"/> when the native DLL
    /// is present, or through a small helper process otherwise, because Unity's Mono
    /// cannot create COM objects by itself.
    /// </summary>
    internal static class KanjiConverter
    {
        private const string HostFileName = "JapaneseIME.KanjiHost.exe";
        private const int TimeoutMilliseconds = 1500;

        private static readonly object Sync = new object();
        private static readonly Queue<Job> Jobs = new Queue<Job>();

        private static Thread _thread;
        private static bool _unavailable;
        private static Process _process;
        private static StreamWriter _request;
        private static StreamReader _response;

        private sealed class Job
        {
            internal string Reading;
            internal string[] Result;
            internal readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        }

        /// <summary>
        /// Returns the kanji candidates for a reading, best first. The array can be
        /// empty when the engine knows neither form.
        /// </summary>
        internal static bool TryConvert(string reading, out string[] candidates)
        {
            candidates = null;

            if (string.IsNullOrEmpty(reading))
            {
                return false;
            }

            // The native DLL runs the engine in this process; the helper process
            // is only started when it is missing or broken.
            if (NativeKanjiConverter.EnsureAvailable())
            {
                return NativeKanjiConverter.TryConvert(reading, out candidates);
            }

            if (_unavailable)
            {
                return false;
            }

            EnsureThread();
            if (_unavailable)
            {
                return false;
            }

            Job job = new Job { Reading = reading };

            lock (Sync)
            {
                Jobs.Enqueue(job);
                Monitor.Pulse(Sync);
            }

            bool finished;
            try
            {
                finished = job.Done.Wait(TimeoutMilliseconds);
            }
            catch (Exception)
            {
                finished = false;
            }

            if (!finished)
            {
                Plugin.Log?.Warn("JapaneseIME: the kanji conversion helper did not answer in time.");
                Shutdown();
                _unavailable = true;
                return false;
            }

            candidates = job.Result;
            return candidates != null && candidates.Length > 0;
        }

        /// <summary>Stops the helper process.</summary>
        internal static void Shutdown()
        {
            try
            {
                _request?.Close();
                _response?.Close();
                if (_process != null && !_process.HasExited)
                {
                    _process.Kill();
                }
            }
            catch
            {
            }
            finally
            {
                _request = null;
                _response = null;
                _process = null;
            }
        }

        private static void EnsureThread()
        {
            if (_thread != null)
            {
                return;
            }

            lock (Sync)
            {
                if (_thread != null)
                {
                    return;
                }

                Thread thread = new Thread(Worker)
                {
                    IsBackground = true,
                    Name = "JapaneseIME.KanjiConverter",
                };

                thread.Start();
                _thread = thread;
            }
        }

        private static void Worker()
        {
            while (true)
            {
                Job job;

                lock (Sync)
                {
                    while (Jobs.Count == 0)
                    {
                        Monitor.Wait(Sync);
                    }

                    job = Jobs.Dequeue();
                }

                try
                {
                    if (_process == null)
                    {
                        StartHost();
                    }

                    _request.WriteLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(job.Reading)));
                    _request.Flush();

                    string line = _response.ReadLine();
                    if (line == null)
                    {
                        throw new InvalidOperationException("the helper process exited");
                    }

                    // Base64 keeps the pipe ASCII-only, so the console code page
                    // of the redirected streams cannot corrupt the text.
                    string payload = Encoding.UTF8.GetString(Convert.FromBase64String(line));
                    job.Result = payload.Length == 0
                        ? new string[0]
                        : payload.Split('\t');
                }
                catch (Exception ex)
                {
                    job.Result = null;
                    _unavailable = true;

                    Plugin.Log?.Warn(
                        "JapaneseIME: the Microsoft IME conversion helper is unavailable (" +
                        ex.GetType().Name + "). Pointer input stays kana only.");

                    Shutdown();
                }
                finally
                {
                    job.Done.Set();
                }
            }
        }

        private static void StartHost()
        {
            string path = Path.Combine(
                Path.GetDirectoryName(typeof(KanjiConverter).Assembly.Location) ?? string.Empty,
                HostFileName);

            if (!File.Exists(path))
            {
                throw new FileNotFoundException("helper not found", path);
            }

            ProcessStartInfo info = new ProcessStartInfo
            {
                FileName = path,
                WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
            };

            _process = Process.Start(info);
            if (_process == null)
            {
                throw new InvalidOperationException("the helper process did not start");
            }

            _request = _process.StandardInput;
            _request.AutoFlush = true;
            _response = _process.StandardOutput;
        }
    }
}
