// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

namespace Metaplay.Server.AdminApi
{
    /// <summary>
    /// Pre-defined default roles for the AdminApi.
    /// </summary>
    public static class DefaultRole
    {
        public const string GameAdmin               = "game-admin";
        public const string GameViewer              = "game-viewer";
        public const string CustomerSupportSenior   = "customer-support-senior";
        public const string CustomerSupportAgent    = "customer-support-agent";

        public static readonly string[] All = new string[] { GameAdmin, GameViewer, CustomerSupportSenior, CustomerSupportAgent };
    }
}
