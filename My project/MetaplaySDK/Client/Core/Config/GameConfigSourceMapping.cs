// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Collections.Generic;

namespace Metaplay.Core.Config
{
    /// <summary>
    /// Retains mapping information from fully parsed items (types implementing <see cref="IGameConfigData"/>) back to
    /// the game config locations (<see cref="GameConfigSourceLocation"/>). This allows mapping errors from the validation stage
    /// back into the game config sources (eg, Google Sheets).
    /// </summary>
    public class GameConfigSourceMapping
    {
        internal const string                                   BaselineVariantKey  = "Baseline";
        public readonly GameConfigSourceInfo                    SourceInfo;
        readonly Dictionary<string, GameConfigSourceLocation>              _memberToSource     = new Dictionary<string, GameConfigSourceLocation>();
        // \todo Using string-based ConfigKeys as GameConfigValidationResult operates on string -- refactor to use ConfigKey instead?
        readonly Dictionary<(string, string), GameConfigSourceLocation>    _itemToSource       = new Dictionary<(string, string), GameConfigSourceLocation>(); // (configKeyStr, variantId) -> location

        GameConfigSourceMapping(GameConfigSourceInfo sourceInfo, Dictionary<string, GameConfigSourceLocation> memberToSource, Dictionary<(string, string), GameConfigSourceLocation> itemToSource)
        {
            SourceInfo = sourceInfo;
            _memberToSource = memberToSource;
            _itemToSource = itemToSource;
        }

        // \todo Move spreadsheet-specific behavior out from here (SpreadsheetReader should return this?)
        public static GameConfigSourceMapping ForGameConfigLibrary<TKey, TInfo>(SpreadsheetContent sheet, IEnumerable<VariantConfigItem<TKey, TInfo>> items) where TInfo : IGameConfigKey<TKey>, new()
        {
            if (sheet == null)
                throw new ArgumentNullException(nameof(sheet));
            if (items == null)
                throw new ArgumentNullException(nameof(items));

            // Compute column-to-member mapping: each column maps to a full column location
            Dictionary<string, GameConfigSourceLocation> memberToSource = new Dictionary<string, GameConfigSourceLocation>();
            foreach (SpreadsheetCell cell in sheet.Cells[0])
            {
                // Ignore empty header cells
                if (string.IsNullOrEmpty(cell.Value.Trim()))
                    continue;

                // \note Operating on the post-syntax adapter headers so we have unique values for indexed collection columns
                // \note Duplicate values are ignored -- this should get handled better by getting this data from the SpreadsheetReader
                GameConfigSourceLocation columnLocation = GameConfigSpreadsheetLocation.FromColumns(sheet.SourceInfo, cell.Column, cell.Column + 1);
                if (!memberToSource.ContainsKey(cell.Value))
                    memberToSource.Add(cell.Value, columnLocation);
            }

            // Compute per-variant item-to-source mappings
            Dictionary<(string, string), GameConfigSourceLocation> itemToSource = new Dictionary<(string, string), GameConfigSourceLocation>();
            foreach (VariantConfigItem<TKey, TInfo> item in items)
                itemToSource.Add((item.Item.ConfigKey.ToString(), item.VariantIdMaybe ?? BaselineVariantKey), item.SourceLocation);

            return new GameConfigSourceMapping(sheet.SourceInfo, memberToSource, itemToSource);
        }

        // \todo Move spreadsheet-specific behavior out from here (SpreadsheetReader should return this?)
        public static GameConfigSourceMapping ForKeyValueObject(SpreadsheetContent sheet, IEnumerable<VariantConfigStructureMember> members)
        {
            if (sheet == null)
                throw new ArgumentNullException(nameof(sheet));
            if (members == null)
                throw new ArgumentNullException(nameof(members));

            // Compute mapping from each of the members (of the game config object) to the source location
            Dictionary<(string, string), GameConfigSourceLocation> memberToSource = new Dictionary<(string, string), GameConfigSourceLocation>();
            foreach (VariantConfigStructureMember member in members)
                memberToSource.Add((member.Member.MemberInfo.Name, member.VariantIdMaybe ?? BaselineVariantKey), member.SourceLocation);

            // \note We pass the member-to-source mapping as item-to-source to pretend each config object member is an item.
            //       This matches the pre-R25 behavior of inserting object members the same way as full items.
            // \note We leave the memberToSource argument an empty dictionary to match with the pre-R25 behavior as well.
            return new GameConfigSourceMapping(sheet.SourceInfo, new Dictionary<string, GameConfigSourceLocation>(), memberToSource);
        }

        /// <summary>
        /// Try to find the source location of a config item variant (or the location of a member of the item if <paramref name="memberPathHint"/>
        /// is given). The item is matched on the stringified <see cref="IGameConfigKey{TGameConfigKey}.ConfigKey"/> as we don't have access to
        /// the typed object at this point.
        /// </summary>
        /// <param name="configKeyStr">Stringified ConfigKey of the item.</param>
        /// <param name="variantId">Variant of the item that is queried for (or null for baseline).</param>
        /// <param name="memberPathHint">Path of the member that is queried for (eg, 'Array[3].Member').</param>
        /// <param name="location">Resulting location, if true was returned.</param>
        /// <returns>True if the source was found, false otherwise.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public bool TryFindItemSource(string configKeyStr, string variantId, string memberPathHint, out GameConfigSourceLocation location)
        {
            if (configKeyStr is null)
                throw new ArgumentNullException(nameof(configKeyStr));

            // Try to find the variant (if specified) of the item or the baseline
            variantId ??= BaselineVariantKey;
            if (!_itemToSource.TryGetValue((configKeyStr, variantId), out location))
                return false;

            // If member path hint is given, try to narrow the location to it
            if (memberPathHint != null)
            {
                // Try to narrow down to item's member location, if possible. If not, just silently ignore.
                if (TryNarrowItemLocationToMember(location, memberPathHint, out GameConfigSourceLocation memberLocation))
                {
                    location = memberLocation;
                    return true;
                }
            }

            // Was only able to find item's location. It's usually good enough.
            return true;
        }

        /// <summary>
        /// Try to narrow an item's full location (eg, a full row) to the specific member (eg, a column).
        /// The member may also be path to member-of-member, including collection elements (eg, 'Array[1].Cost').
        /// If no location is found with the <paramref name="memberPathHint"/>, the full item location
        /// (<paramref name="itemLocation"/>) is returned.
        /// </summary>
        /// <param name="itemLocation">Location of the item we want to narrow</param>
        /// <param name="memberPathHint">Member path inside the item we're interested in (eg, 'SomeMember' or 'ArrayMember[0].Something')</param>
        /// <returns>True if was able to narrow down the location to the member path, false otherwise.</returns>
        bool TryNarrowItemLocationToMember(GameConfigSourceLocation itemLocation, string memberPathHint, out GameConfigSourceLocation memberLocation)
        {
            // Check if we know the location of the specific member (or path to member)
            // \todo This only does a exact check against the input columns (in case of sheets) -- could be smarter about trying to find the best match (eg, in
            //       case columns specify members of a compount object, we could still match the column range even if memberPathHint is the compount object itself).
            if (_memberToSource.TryGetValue(memberPathHint, out GameConfigSourceLocation memberSourceLocation))
            {
                // Try to narrow to the member location
                return itemLocation.TryNarrowToLocation(memberSourceLocation, out memberLocation);
            }

            // No member location found, just return the full item location
            memberLocation = null;
            return false;
        }
    }
}
