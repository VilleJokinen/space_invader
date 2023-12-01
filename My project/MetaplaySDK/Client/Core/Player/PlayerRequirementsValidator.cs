// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;

namespace Metaplay.Core.Player
{
    /// <summary>
    /// Provides base validation logic for various player requirements. Rules may be customized by
    /// inheriting this class.
    /// </summary>
    public class PlayerRequirementsValidator : IMetaIntegrationSingleton<PlayerRequirementsValidator>
    {
        /// <summary>
        /// Min PlayerName string length
        /// </summary>
        public virtual int MinPlayerNameLength => 5;

        /// <summary>
        /// Max PlayerName string length
        /// </summary>
        public virtual int MaxPlayerNameLength => 20;

        /// <summary>
        /// Returns true if given name is a valid display name for a player.
        /// </summary>
        /// <param name="playerName">The name to validate</param>
        /// <returns>True is the name was valid</returns>
        public virtual bool ValidatePlayerName(string playerName)
        {
            if (playerName == null)
                return false;

            // Validate that length of name is between Min and Max characters long
            if (playerName.Length < MinPlayerNameLength || playerName.Length > MaxPlayerNameLength)
                return false;

            // Check that no control characters are inlcuded
            foreach (char ch in playerName)
            {
                if (Char.IsControl(ch))
                    return false;
            }

            // Example rule to filter out semi-colons to mitagate any attempts at sql-injection attacks
            if (playerName.Contains(";"))
                return false;

            // All steps passed - The name is valid
            return true;
        }
    }
}
