// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Activables;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Metaplay.Server.AdminApi.Controllers
{
    public static class ActivablesUtil
    {
        public static ActivablesMetadata GetMetadata()
        {
            CamelCaseNamingStrategy camelCasing = new CamelCaseNamingStrategy();

            IReadOnlyDictionary<MetaActivableCategoryId, MetaActivableRepository.CategorySpec>  categories  = MetaActivableRepository.Instance.AllCategories;
            IReadOnlyDictionary<MetaActivableKindId, MetaActivableRepository.KindSpec>          kinds       = MetaActivableRepository.Instance.AllKinds;

            return new ActivablesMetadata(
                categories.Values.ToOrderedDictionary(
                    category => category.Id,
                    category => new ActivableCategoryMetadata(
                        displayName:                category.DisplayName,
                        shortSingularDisplayName:   category.ShortSingularDisplayName,
                        description:                category.Description,
                        kinds:                      kinds.Values.Where(kind => kind.CategoryId == category.Id).Select(kind => kind.Id).ToList())),

                kinds.Values.ToOrderedDictionary(
                    kind => kind.Id,
                    kind =>
                    {
                        IEnumerable<MemberInfo> gameSpecificGameConfigDataMembers = kind.GameSpecificConfigDataMembers ?? Enumerable.Empty<MemberInfo>();

                        return new ActivableKindMetadata(
                            displayName:                        kind.DisplayName,
                            description:                        kind.Description,
                            category:                           kind.CategoryId,
                            gameSpecificGameConfigDataMembers:  gameSpecificGameConfigDataMembers.Select(memberInfo => camelCasing.GetPropertyName(memberInfo.Name, hasSpecifiedName: false)).ToList());
                    }));
        }

        public class ActivablesMetadata
        {
            public OrderedDictionary<MetaActivableCategoryId, ActivableCategoryMetadata>    Categories;
            public OrderedDictionary<MetaActivableKindId, ActivableKindMetadata>            Kinds;

            public ActivablesMetadata(OrderedDictionary<MetaActivableCategoryId, ActivableCategoryMetadata> categories, OrderedDictionary<MetaActivableKindId, ActivableKindMetadata> kinds)
            {
                Categories = categories ?? throw new ArgumentNullException(nameof(categories));
                Kinds = kinds ?? throw new ArgumentNullException(nameof(kinds));
            }
        }

        public class ActivableCategoryMetadata
        {
            public string                       DisplayName;
            public string                       ShortSingularDisplayName;
            public string                       Description;
            public List<MetaActivableKindId>    Kinds;

            public ActivableCategoryMetadata(string displayName, string shortSingularDisplayName, string description, List<MetaActivableKindId> kinds)
            {
                DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
                ShortSingularDisplayName = shortSingularDisplayName ?? throw new ArgumentNullException(nameof(shortSingularDisplayName));
                Description = description ?? throw new ArgumentNullException(nameof(description));
                Kinds = kinds ?? throw new ArgumentNullException(nameof(kinds));
            }
        }

        public class ActivableKindMetadata
        {
            public string                   DisplayName;
            public string                   Description;
            public MetaActivableCategoryId  Category;
            public List<string>             GameSpecificConfigDataMembers;

            public ActivableKindMetadata(string displayName, string description, MetaActivableCategoryId category, List<string> gameSpecificGameConfigDataMembers)
            {
                DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
                Description = description ?? throw new ArgumentNullException(nameof(description));
                Category = category ?? throw new ArgumentNullException(nameof(category));
                GameSpecificConfigDataMembers = gameSpecificGameConfigDataMembers ?? throw new ArgumentNullException(nameof(gameSpecificGameConfigDataMembers));
            }
        }
    }
}
