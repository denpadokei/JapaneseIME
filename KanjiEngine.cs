using System;
using System.Collections.Generic;
using System.Threading;

namespace JapaneseIME
{
    /// <summary>
    /// Runs the Microsoft IME conversion off the game's main thread and caches the
    /// answers.
    ///
    /// The IME engine takes seconds to start the first time it is used, and a VR
    /// game cannot freeze for that long, so a conversion request only returns what is
    /// already known. The result is picked up by the next frame and the visible
    /// candidate list is refreshed then.
    /// </summary>
    internal static class KanjiEngine
    {
        private const int SuccessLifetimeMinutes = 30;
        private const int FailureLifetimeMinutes = 5;

        private sealed class CacheEntry
        {
            internal string[] Candidates;
            internal DateTime CreatedUtc;
            internal bool Failed;
        }

        internal sealed class Result
        {
            internal string Reading;
            internal string[] Candidates;
        }

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, CacheEntry> Cache =
            new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
        private static readonly HashSet<string> Pending = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Queue<string> Requests = new Queue<string>();
        private static readonly Queue<Result> Completed = new Queue<Result>();

        private static Thread _worker;

        /// <summary>
        /// Returns the cached conversion for a reading, if the engine already
        /// answered it. A false result only means "ask <see cref="Request"/>".
        /// </summary>
        internal static bool TryGet(string reading, out string[] candidates)
        {
            candidates = null;

            if (string.IsNullOrEmpty(reading))
            {
                return false;
            }

            lock (Sync)
            {
                CacheEntry entry;
                if (!Cache.TryGetValue(reading, out entry))
                {
                    return false;
                }

                TimeSpan lifetime = entry.Failed
                    ? TimeSpan.FromMinutes(FailureLifetimeMinutes)
                    : TimeSpan.FromMinutes(SuccessLifetimeMinutes);

                if (DateTime.UtcNow - entry.CreatedUtc > lifetime)
                {
                    Cache.Remove(reading);
                    return false;
                }

                if (entry.Failed || entry.Candidates == null || entry.Candidates.Length == 0)
                {
                    return false;
                }

                candidates = entry.Candidates;
                return true;
            }
        }

        /// <summary>Queues a conversion. Duplicate requests are ignored.</summary>
        internal static void Request(string reading)
        {
            if (string.IsNullOrEmpty(reading))
            {
                return;
            }

            lock (Sync)
            {
                if (Pending.Contains(reading))
                {
                    return;
                }

                CacheEntry entry;
                if (Cache.TryGetValue(reading, out entry) && !entry.Failed &&
                    DateTime.UtcNow - entry.CreatedUtc <= TimeSpan.FromMinutes(SuccessLifetimeMinutes))
                {
                    return;
                }

                Pending.Add(reading);
                Requests.Enqueue(reading);

                if (_worker == null)
                {
                    _worker = new Thread(Worker)
                    {
                        IsBackground = true,
                        Name = "JapaneseIME.KanjiEngine",
                    };

                    _worker.Start();
                }

                Monitor.Pulse(Sync);
            }
        }

        /// <summary>Takes one finished conversion, for refreshing a live candidate list.</summary>
        internal static bool TryTakeCompleted(out Result result)
        {
            lock (Sync)
            {
                if (Completed.Count == 0)
                {
                    result = null;
                    return false;
                }

                result = Completed.Dequeue();
                return true;
            }
        }

        /// <summary>
        /// Loads the native DLL and starts the IME engine ahead of the first
        /// conversion. Must be called off the main thread.
        /// </summary>
        internal static void Prewarm()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    if (NativeKanjiConverter.EnsureAvailable())
                    {
                        NativeKanjiConverter.WarmUp();
                        return;
                    }

                    KanjiConverter.Prewarm();
                }
                catch (Exception)
                {
                }
            });
        }

        private static void Worker()
        {
            while (true)
            {
                string reading;

                lock (Sync)
                {
                    while (Requests.Count == 0)
                    {
                        Monitor.Wait(Sync);
                    }

                    reading = Requests.Dequeue();
                }

                string[] candidates = ConvertNow(reading);

                lock (Sync)
                {
                    Pending.Remove(reading);
                    Cache[reading] = new CacheEntry
                    {
                        Candidates = candidates,
                        CreatedUtc = DateTime.UtcNow,
                        Failed = candidates == null || candidates.Length == 0,
                    };

                    if (candidates != null && candidates.Length > 0)
                    {
                        Completed.Enqueue(new Result { Reading = reading, Candidates = candidates });
                    }
                }
            }
        }

        private static string[] ConvertNow(string reading)
        {
            try
            {
                string[] candidates;

                if (NativeKanjiConverter.EnsureAvailable())
                {
                    return NativeKanjiConverter.TryConvert(reading, out candidates) ? candidates : null;
                }

                return KanjiConverter.TryConvert(reading, out candidates) ? candidates : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
