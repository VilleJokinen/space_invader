// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Model;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace Metaplay.Cloud.Utility
{
    /// <summary>
    /// Tool for managing multiple asynchronous requests, with retry support,
    /// and with a limit on how many can be simultaneously attempted.
    /// Used with some types of database scanners that send asks to entities.
    ///
    /// Usage:
    ///   - Define a subclass, giving the type parameters:
    ///     - <typeparamref name="TRequestSpec"/>: request descriptor
    ///     - <typeparamref name="TResponse"/>: the response type
    ///     - TRequestSpec should represent whatever information you need
    ///       to start a request. A request is a Task&lt;TResponse&gt;.
    ///   - Define the limit properties, such as <see cref="MaxSimultaneousRequests"/>
    ///   - Call <see cref="AddRequest(TRequestSpec)"/> to add a new request
    ///     - You might want to check <see cref="NumRequestsInBuffer"/> and impose some buffer limit
    ///       before adding new requests.
    ///   - Call <see cref="Update"/> to update the existing requests. This takes callbacks
    ///     for starting a request Task, and for handling a request success or failure.
    ///   - Check <see cref="HasCompletedAllRequestsSoFar"/> to see if all requests
    ///     that have so far been added to the tool have been completed, with
    ///     either success or failure.
    ///
    /// \todo [nuutti] Currently this only exposes a polling interface: the Update method.
    ///                Could maybe extend to a more general Task-based interface. No need currently.
    /// </summary>
    [MetaSerializable]
    public abstract class SimultaneousRequestTool<TRequestSpec, TResponse>
    {
        [MetaMember(1)] List<OngoingRequest>    _ongoingRequests    = new List<OngoingRequest>();
        [MetaMember(2)] Queue<TRequestSpec>     _bufferedRequests   = new Queue<TRequestSpec>();

        // Limits to be defined in subclass
        /// <summary> Maximum number of simultaneously ongoing requests. Note that buffered requests are not counted towards this. </summary>
        public abstract int MaxSimultaneousRequests { get; }
        /// <summary> How many times a request may be attempted in total. A request can be re-attempted if it failed. </summary>
        public abstract int MaxAttemptsPerRequest   { get; }

        /// <summary>
        /// How many requests are buffered, i.e. added with <see cref="AddRequest"/> but not ongoing.
        /// The buffer holds requests that don't yet fit within <see cref="MaxSimultaneousRequests"/>.
        /// </summary>
        public int  NumRequestsInBuffer             => _bufferedRequests.Count;
        /// <summary>
        /// Whether all requests so far added with <see cref="AddRequest"/> have completed.
        /// I.e., there are no ongoing or buffered requests.
        /// </summary>
        public bool HasCompletedAllRequestsSoFar    => _ongoingRequests.Count == 0 && _bufferedRequests.Count == 0;

        /// <summary>
        /// Add a new request.
        /// </summary>
        public void AddRequest(TRequestSpec request)
        {
            _bufferedRequests.Enqueue(request);
        }

        public delegate Task<TResponse> AttemptRequestAsyncFunc (TRequestSpec requestSpec);
        public delegate void            OnRequestSuccessFunc    (TRequestSpec requestSpec, TResponse response);
        public delegate void            OnRequestFailureFunc    (TRequestSpec requestSpec, RequestErrorInfo latestError);

        /// <summary>
        /// Handle finished requests and start new requests if possible.
        /// </summary>
        /// <param name="attemptRequestAsync">A function that creates a new request task from a request spec.</param>
        /// <param name="onRequestSuccess">A function invoked when a request has successfully finished.</param>
        /// <param name="onRequestFailure">A function invoked when a request has failed (and has no more retries left).</param>
        public void Update(AttemptRequestAsyncFunc attemptRequestAsync, OnRequestSuccessFunc onRequestSuccess, OnRequestFailureFunc onRequestFailure)
        {
            if (attemptRequestAsync == null)
                throw new ArgumentNullException(nameof(attemptRequestAsync));
            if (onRequestSuccess == null)
                throw new ArgumentNullException(nameof(onRequestSuccess));
            if (onRequestFailure == null)
                throw new ArgumentNullException(nameof(onRequestFailure));

            UpdateFinishedOngoingRequests(onRequestSuccess, onRequestFailure);
            FillRequestsFromBuffer();
            StartNewAttempts(attemptRequestAsync);
        }

        void UpdateFinishedOngoingRequests(OnRequestSuccessFunc onRequestSuccess, OnRequestFailureFunc onRequestFailure)
        {
            int maxAttemptsPerRequest = MaxAttemptsPerRequest;

            foreach (OngoingRequest ongoingRequest in _ongoingRequests)
            {
                // Handle completed in-flight attempts
                if (ongoingRequest.InFlightAttempt != null
                 && ongoingRequest.InFlightAttempt.Task.IsCompleted)
                {
                    Task<TResponse> ask = ongoingRequest.InFlightAttempt.Task;

                    // If it's a success, record it as such and mark as done.
                    if (ask.IsCompletedSuccessfully)
                    {
                        onRequestSuccess(ongoingRequest.RequestSpec, ask.GetCompletedResult());
                        ongoingRequest.IsDone = true;
                    }
                    else if (ask.IsFaulted)
                    {
                        Exception effectiveException;
                        if (ask.Exception.InnerExceptions.Count == 1)
                            effectiveException = ask.Exception.InnerException;
                        else
                            effectiveException = ask.Exception;

                        ongoingRequest.LatestError = new RequestErrorInfo(MetaTime.Now, effectiveException.ToString());
                    }

                    ongoingRequest.InFlightAttempt = null;
                }

                // On attempt limit, record as failure and mark as done.
                if (!ongoingRequest.IsDone
                    && ongoingRequest.InFlightAttempt == null
                    && ongoingRequest.NumAttemptsStarted >= maxAttemptsPerRequest)
                {
                    RequestErrorInfo errorInfo = ongoingRequest.LatestError ?? new RequestErrorInfo(MetaTime.Now, "Request remains unfinished without an explicit error, perhaps due to crash in the requesting actor.");
                    onRequestFailure(ongoingRequest.RequestSpec, errorInfo);
                    ongoingRequest.IsDone = true;
                }
            }

            // Remove requests that are marked as done.
            // \note IsDone is just scratch for this purpose
            _ongoingRequests.RemoveAll(m => m.IsDone);
        }

        void FillRequestsFromBuffer()
        {
            int maxSimultaneousRequests = MaxSimultaneousRequests;

            while (_ongoingRequests.Count < maxSimultaneousRequests
                && _bufferedRequests.Count > 0)
            {
                TRequestSpec newRequest = _bufferedRequests.Dequeue();
                _ongoingRequests.Add(new OngoingRequest(newRequest));
            }
        }

        void StartNewAttempts(AttemptRequestAsyncFunc attemptRequestAsync)
        {
            foreach (OngoingRequest ongoingRequest in _ongoingRequests)
            {
                if (ongoingRequest.InFlightAttempt != null)
                    continue;

                ongoingRequest.InFlightAttempt = new InFlightAttempt(attemptRequestAsync(ongoingRequest.RequestSpec));
                ongoingRequest.NumAttemptsStarted++;
            }
        }

        [MetaSerializable]
        class OngoingRequest
        {
            [MetaMember(1)] public TRequestSpec RequestSpec         { get; private set; }
            [MetaMember(2)] public int          NumAttemptsStarted  { get; set; }
            [MetaMember(3)] public RequestErrorInfo? LatestError { get; set; }

            [IgnoreDataMember] public InFlightAttempt   InFlightAttempt { get; set; } = null;
            [IgnoreDataMember] public bool              IsDone          { get; set; } = false;

            OngoingRequest(){ }
            public OngoingRequest(TRequestSpec requestSpec)
            {
                RequestSpec         = requestSpec;
                NumAttemptsStarted  = 0;
            }
        }

        class InFlightAttempt
        {
            public Task<TResponse> Task { get; }

            public InFlightAttempt(Task<TResponse> task)
            {
                Task = task ?? throw new ArgumentNullException(nameof(task));
            }
        }
    }

    [MetaSerializable]
    public struct RequestErrorInfo
    {
        [MetaMember(1)] public MetaTime Timestamp;
        [MetaMember(2)] public string Description;

        public RequestErrorInfo(MetaTime timestamp, string description)
        {
            Timestamp = timestamp;
            Description = description;
        }
    }
}
