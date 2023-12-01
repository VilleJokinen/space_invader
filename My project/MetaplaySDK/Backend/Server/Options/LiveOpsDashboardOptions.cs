// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Metaplay.Server
{
    [RuntimeOptions("LiveOpsDashboard", isStatic: false, "Configuration options for this dashboard.")]
    public class LiveOpsDashboardOptions : RuntimeOptionsBase
    {
        [MetaDescription("Set to a hex color like `#3f6730` to change the color of the dashboard header. Defaults to red in production and neutral white elsewhere.")]
        public string DashboardHeaderColorInHex { get; private set; } = IsProductionEnvironment ? "#ef4444" : null;

        [MetaDescription("Display name for the first tab on the player details page.")]
        public string PlayerDetailsTab0DisplayName { get; private set; } = "Game State";

        [MetaDescription("Display name for the second tab on the player details page.")]
        public string PlayerDetailsTab1DisplayName { get; private set; } = "Account Access & Logs";

        [MetaDescription("Display name for the third tab on the player details page.")]
        public string PlayerDetailsTab2DisplayName { get; private set; } = "Purchases & Web3";

        [MetaDescription("Display name for the fourth tab on the player details page.")]
        public string PlayerDetailsTab3DisplayName { get; private set; } = "Segments & Targeting";

        [MetaDescription("Display name for the fifth tab on the player details page.")]
        public string PlayerDetailsTab4DisplayName { get; private set; } = "Technical";

        public override Task OnLoadedAsync()
        {
            // Validate the header color.
            if (!string.IsNullOrEmpty(DashboardHeaderColorInHex))
            {
                if (new Regex(@"^#([\da-fA-F]{3}){1,2}$").Match(DashboardHeaderColorInHex).Success != true)
                    throw new InvalidOperationException("Hex colour must be of the form #xxx or #xxxxxx.");
            }

            return Task.CompletedTask;
        }
    }
}
