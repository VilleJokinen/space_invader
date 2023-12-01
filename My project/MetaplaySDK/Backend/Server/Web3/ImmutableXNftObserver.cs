// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Web3;
using Metaplay.Core;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Metaplay.Server.Web3
{
    /// <summary>
    /// Utility for observing changes to NFT token ownerships.
    /// </summary>
    public sealed class ImmutableXNftObserver : IDisposable
    {
        class State
        {
            public CancellationTokenSource Cts = new CancellationTokenSource();
            public EthereumAddress Contract;
            public DateTime ScanStart;
            public Task Worker;
            public bool IsDisposed = false;

            public object PendingLock = new object();
            public List<NftChange> PendingChanges = new List<NftChange>();
            public DateTime PendingTimestamp;
            public SemaphoreSlim PendingBufferFull;
        }

        State _state;

        /// <summary>
        /// Creates a new observer for events happening after <paramref name="startTime"/>. If no start time
        /// is known, the timestamp should be given somewhere around 10 minutes in the past.
        /// </summary>
        public ImmutableXNftObserver(EthereumAddress nftContract, DateTime startTime)
        {
            if (startTime.Kind != DateTimeKind.Utc)
                throw new ArgumentException("time must be UTC kind", nameof(startTime));

            _state = new State();
            _state.Contract = nftContract;
            _state.ScanStart = startTime;
            _state.PendingTimestamp = startTime;
            _state.Worker = Task.Factory.StartNew(ObserverWorker, state: _state, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default);
        }

        public readonly struct NftChange
        {
            public readonly Erc721TokenId TokenId;
            public readonly EthereumAddress Owner;

            public NftChange(Erc721TokenId tokenId, EthereumAddress owner)
            {
                TokenId = tokenId;
                Owner = owner;
            }
        }

        public readonly struct PollResult
        {
            /// <summary>
            /// The unobserved changes to NFT tokens.
            /// </summary>
            public readonly NftChange[] Changes;

            /// <summary>
            /// The timestamp up to which the NFT timeline has been (mostly) conclusively observed. In order to
            /// suspend and resume observing at a later time, this timestamp may be stored and then given in constructor
            /// to continue.
            /// </summary>
            public readonly DateTime ObservedUpTo;

            public PollResult(NftChange[] changes, DateTime observedUpTo)
            {
                Changes = changes;
                ObservedUpTo = observedUpTo;
            }
        }

        /// <summary>
        /// Retrieves unobserved changes to NFT tokens.
        /// </summary>
        public PollResult PollChanges()
        {
            NftChange[] changes;
            DateTime timestamp;

            lock (_state.PendingLock)
            {
                if (_state.PendingChanges.Count == 0)
                    return new PollResult(Array.Empty<NftChange>(), _state.PendingTimestamp);

                changes = _state.PendingChanges.ToArray();
                _state.PendingChanges.Clear();
                timestamp = _state.PendingTimestamp;

                if (_state.PendingBufferFull != null)
                {
                    _state.PendingBufferFull.Release();
                    _state.PendingBufferFull = null;
                }
            }

            return new PollResult(changes, timestamp);
        }

        static async Task ObserverWorker(object stateObj)
        {
            State state = (State)stateObj;
            DateTime lastObservationTimestamp = MetaTime.Epoch.ToDateTime();

            IMetaLogger log = MetaLogger.ForContext<ImmutableXNftObserver>();

            // Max time length of covered by a single query.
            TimeSpan maxScanLength = TimeSpan.FromMinutes(5);
            // The maximum time we assume the remote server to be behind us.
            TimeSpan maxRemoteServerClockSkew = TimeSpan.FromMinutes(1);
            // How often the remote is polled when scanner is up-to-date with remote (i.e. scanning realtime changes).
            TimeSpan realtimeScanInterval = TimeSpan.FromSeconds(10);
            // How often the remote is polled when scanner is behind the remote.
            TimeSpan historyScanInterval = TimeSpan.FromSeconds(10);

            log.Debug("Polling for NFT changes in contract {Contract}", state.Contract);

            try
            {
                while (!state.IsDisposed)
                {
                    Web3Options web3Options = RuntimeOptionsRegistry.Instance.GetCurrent<Web3Options>();

                    // Scan from [scan, scan + T]. Note (scan + T) might be in the future but we'll handle it later.
                    DateTime nextScanEnd = state.ScanStart + maxScanLength;
                    DateTime scannedAt = DateTime.UtcNow;
                    ImmutableXApi.TokenUpdate[] changes;

                    try
                    {
                        changes = await ImmutableXApi.GetTokenUpdatesInTimeRangeAsync(web3Options, state.Contract, fromDate: state.ScanStart, toDate: nextScanEnd);
                    }
                    catch (Exception ex)
                    {
                        log.Warning("Failed to fetch NFT updates. Will retry later. Error: {ex}", ex);
                        await Task.Delay(TimeSpan.FromSeconds(10), state.Cts.Token);
                        continue;
                    }

                    if (changes.Length > 0)
                        log.Debug("Detected {NumChanges} NFT transactions in {Contract} between {From} .. {To}.", changes.Length, state.Contract, state.ScanStart, nextScanEnd);

                    foreach (ImmutableXApi.TokenUpdate change in changes)
                        lastObservationTimestamp = Util.Max(lastObservationTimestamp, change.Timestamp);

                    // Assume there wont be any updates to timestamps:
                    // * Which are more than T in the past
                    // * Which are older than the lasted update
                    // If these hold, the next scan can start from the after the latter of the two (within the scan range).
                    // (Essentially this is the clock the IMX server is at minimum).

                    DateTime remoteServerClockAtLeast = Util.Max((scannedAt - maxRemoteServerClockSkew), lastObservationTimestamp);
                    DateTime nextScanStart = Util.Clamp(value: remoteServerClockAtLeast, min: state.ScanStart, max: nextScanEnd);

                    SemaphoreSlim bufferFullWait;
                    lock (state.PendingLock)
                    {
                        // If too many changes already in the queue, stop polling until cleared.
                        if (state.PendingChanges.Count > 1000)
                        {
                            log.Warning("Too many pending updates in queue. Consumer is not reading fast enough. Suspending polling.");
                            state.PendingBufferFull = new SemaphoreSlim(initialCount: 0, maxCount: 1);
                            bufferFullWait = state.PendingBufferFull;
                        }
                        else
                        {
                            bufferFullWait = null;
                        }

                        // Copy the results in.
                        foreach (ImmutableXApi.TokenUpdate change in changes)
                        {
                            // Underlying API is open range, we are half-open (to allow resumes without duplicates).
                            if (change.Timestamp > state.ScanStart)
                                state.PendingChanges.Add(new NftChange(change.TokenId, change.User));
                        }
                        state.PendingTimestamp = nextScanStart;
                    }
                    state.ScanStart = nextScanStart;

                    // Throttle. If next scanning wouldn't advance the scan pointer by at least `realtimeScanInterval`,
                    // wait until it does. Otherwise we are in history playback mode, and we use the fixed delay.
                    DateTime now = DateTime.UtcNow;
                    TimeSpan throttleDelay;
                    if (now - state.ScanStart < maxRemoteServerClockSkew + realtimeScanInterval)
                        throttleDelay = (maxRemoteServerClockSkew + realtimeScanInterval) - (now - state.ScanStart);
                    else
                        throttleDelay = historyScanInterval;
                    await Task.Delay(throttleDelay, state.Cts.Token);

                    if (bufferFullWait != null)
                    {
                        await bufferFullWait.WaitAsync(state.Cts.Token);
                        log.Debug("Consumer processed the input queue. Resuming polling.");
                    }
                }
            }
            catch (TaskCanceledException)
            {
                // end
            }
            catch (Exception ex)
            {
                log.Error("Worker failed with error: {ex}", ex);
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        ~ImmutableXNftObserver()
        {
            Dispose(disposing: false);
        }

        void Dispose(bool disposing)
        {
            if (_state != null)
            {
                if (disposing)
                {
                    _state.Cts.Cancel();
                }
                _state.IsDisposed = true;
                _state = null;
            }
        }
    }
}
