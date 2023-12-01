// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Tasks;
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Metaplay.Unity
{
    public static class EditorTask
    {
        /// <summary>
        /// Execute a long-running task in Unity editor. Creates an entry in the Unity editor tasks list that visualizes status
        /// of this task and logs any exceptions from the task into the Unity console.
        /// </summary>
        /// <param name="name">Name of the background task to be shown in Editor UI</param>
        /// <param name="editorTaskWithProgress">The task creation function that accepts a callback for progress reporting</param>
        /// <param name="finalizer">Optional finalzer callback that will be called when task finishes, errors out or is canceled</param>
        public static void Run(string name, Func<Action<float, string>, Task> editorTaskWithProgress, Action finalizer = null)
        {
            RunInternal(name, editorTaskWithProgress, finalizer, Progress.Options.None);
        }

        /// <summary>
        /// Execute a long-running task in Unity editor. Creates an entry in the Unity editor tasks list that visualizes status
        /// of this task and logs any exceptions from the task into the Unity console.
        /// </summary>
        /// <param name="name">Name of the background task to be shown in Editor UI</param>
        /// <param name="editorTask">The task creation function</param>
        /// <param name="finalizer">Optional finalzer callback that will be called when task finishes, errors out or is canceled</param>
        public static void Run(string name, Func<Task> editorTask, Action finalizer = null)
        {
            RunInternal(name, _ => editorTask(), finalizer, Progress.Options.Indefinite);
        }

        static void RunInternal(string name, Func<Action<float, string>, Task> editorTask, Action finalizer, Progress.Options progressOptions)
        {
            // Show progress indicator for background task. Note that the unity task UI seems to only
            // start reporting the progress after some time has passed, so for short tasks there might
            // be no entry shown.
            int progressId = Progress.Start(name, options: progressOptions);

            Action<float, string> progressUpdateFunc = (progress, desc) => Progress.Report(progressId, progress, desc);

            // Actual task run on Unity main thread.
            Task task = MetaTask.Run(() => editorTask(progressUpdateFunc), MetaTask.UnityMainScheduler);

            // Log exceptions to unity console.
            task = task.ContinueWithCtx(t =>
            {
                Debug.LogException(t.Exception.InnerException);
                Debug.LogErrorFormat("{0} failed!", name);
            }, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);

            // Finalization.
            _ = task.ContinueWith(_ =>
            {
                Progress.Remove(progressId);
                if (finalizer != null)
                {
                    try
                    {
                        finalizer();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                        Debug.LogErrorFormat("Finalizer for task {0} failed!", name);
                    }
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, MetaTask.UnityMainScheduler);
        }

    }
}
