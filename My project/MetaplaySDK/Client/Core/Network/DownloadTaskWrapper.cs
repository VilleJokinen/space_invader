// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Tasks;
using System;
using System.Threading.Tasks;

namespace Metaplay.Network
{
    /// <summary>
    /// Wraps a <see cref="Task{TResult}"/> representing a download into a <see cref="IDownload"/>
    /// </summary>
    public sealed class DownloadTaskWrapper<TResult> : IDownload
    {
        readonly object         _lock;
        Task<TResult>           _task;
        Exception               _lockedException;
        bool                    _lockedTimeout;

        public DownloadStatus Status => GetStatus();

        public DownloadTaskWrapper(Task<TResult> task)
        {
            _lock = new object();
            _task = task ?? throw new ArgumentNullException(nameof(task));
        }

        /// <summary>
        /// Must be in <see cref="StatusCode.Completed"/> state.
        /// </summary>
        public TResult GetResult()
        {
            lock (_lock)
            {
                if (_task == null)
                    throw new ObjectDisposedException("cannot get result, already disposed");
                if (_lockedException != null)
                    throw new InvalidOperationException("cannot get result, status = Error");
                if (_lockedTimeout)
                    throw new InvalidOperationException("cannot get result, status = Timeout");

                return _task.GetCompletedResult();
            }
        }

        DownloadStatus GetStatus()
        {
            lock(_lock)
            {
                // Never change opinion
                if (_lockedException != null)
                    return new DownloadStatus(code: DownloadStatus.StatusCode.Error, error: _lockedException);
                if (_lockedTimeout)
                    return new DownloadStatus(code: DownloadStatus.StatusCode.Timeout);

                bool hasCancelled = false;
                bool hasPending = false;
                Exception someFailure = null;

                switch(_task.Status)
                {
                    case TaskStatus.RanToCompletion:
                        break;

                    case TaskStatus.Faulted:
                        someFailure = _task.Exception.InnerException;
                        break;

                    case TaskStatus.Canceled:
                        hasCancelled = true;
                        break;

                    default:
                        hasPending = true;
                        break;
                }

                if (someFailure != null)
                {
                    if (someFailure is TimeoutException)
                    {
                        _lockedTimeout = true;
                        return new DownloadStatus(code: DownloadStatus.StatusCode.Timeout);
                    }
                    else
                    {
                        _lockedException = someFailure;
                        return new DownloadStatus(code: DownloadStatus.StatusCode.Error, error: _lockedException);
                    }
                }
                else if (hasCancelled)
                {
                    return new DownloadStatus(code: DownloadStatus.StatusCode.Cancelled);
                }
                else if (hasPending)
                {
                    return new DownloadStatus(code: DownloadStatus.StatusCode.Downloading);
                }

                return new DownloadStatus(code: DownloadStatus.StatusCode.Completed);
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_task == null)
                    return;

                switch(_task.Status)
                {
                    case TaskStatus.RanToCompletion:
                        _task.Dispose();
                        break;
                    case TaskStatus.Faulted:
                        _ = _task.Exception;
                        break;
                    default:
                        _ = _task.ContinueWithCtx((Task task) =>
                        {
                            if (task.IsFaulted)
                                _ = task.Exception;
                            task.Dispose();
                        });
                        break;
                }

                _task = null;
            }
        }
    }
}
