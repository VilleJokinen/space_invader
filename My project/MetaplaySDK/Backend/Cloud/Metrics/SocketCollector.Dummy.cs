// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

namespace Metaplay.Cloud.Metrics
{
    partial class SocketCollector
    {
        sealed class Dummy : SocketCollector
        {
            protected override void CollectImpl()
            {
            }
            protected override void DisposeImpl()
            {
            }
        }
    }
}
