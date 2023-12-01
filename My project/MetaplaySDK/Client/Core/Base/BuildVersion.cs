// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;

namespace Metaplay.Core
{
    [MetaSerializable]
    public struct BuildVersion
    {
        [MetaMember(1)] public string Version;
        [MetaMember(2)] public string BuildNumber;
        [MetaMember(3)] public string CommitId;

        public BuildVersion(string version, string buildNumber, string commitId)
        {
            Version = version;
            BuildNumber = buildNumber;
            CommitId = commitId;
        }
    }
}
