// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;

namespace Metaplay.Unity.CompanyId
{
    public static class CompanyIdLog
    {
        static LogChannel _channel;

        public static LogChannel Log
        {
            get
            {
                if (_channel == null)
                    _channel = MetaplaySDK.Logs.CreateChannel("companyid");
                return _channel;
            }
        }
    }
}
