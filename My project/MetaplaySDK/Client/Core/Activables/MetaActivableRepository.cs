// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Metaplay.Core.Activables
{
    [MetaSerializable]
    public class MetaActivableKindId : StringId<MetaActivableKindId>{ }

    public class MetaActivableCategoryId : StringId<MetaActivableCategoryId>{ }

    /// <summary>
    /// Attribute for declaring a "category" of activables.
    /// Put this attribute on any type (can be just a dummy static class)
    /// in order to make the SDK detect the category declaration.
    ///
    /// A category of activables is a group of "kinds" of activables.
    /// For example, "events" or "offers" could be two categories.
    /// To define what kinds belong to which category,
    /// <see cref="MetaActivableKindMetadataAttribute"/> refers to a category.
    ///
    /// Categories are mainly used for organizing the human-facing
    /// side of activables in the dashboard: for example, each category
    /// has its own top-level page in the dashboard.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class MetaActivableCategoryMetadataAttribute : MetaActivablesMetadataPreserve.PreserveAttribute
    {
        public MetaActivableCategoryId  Id                          { get; }
        public string                   DisplayName                 { get; }
        public string                   ShortSingularDisplayName    { get; }
        public string                   Description                 { get; }

        /// <param name="id">
        /// Technical identifier for this category.
        /// </param>
        /// <param name="displayName">
        /// Name of the game feature, e.g. "In-Game Events".
        /// Used for example in the LiveOps Dashboard in the main
        /// navigation button that goes to the page for this feature.
        /// </param>
        /// <param name="shortSingularDisplayName">
        /// Shorter form of the display name, in singular form, e.g. "Event".
        /// Used for example in the LiveOps Dashboard in links
        /// referencing items under this category, e.g. "View Event".
        /// \todo [nuutti] Better words for this and displayName.
        /// </param>
        /// <param name="description">
        /// Description of the game feature.
        /// Shown for example in the LiveOps Dashboard at the top
        /// of the page for this feature.
        /// </param>
        public MetaActivableCategoryMetadataAttribute(string id, string displayName, string shortSingularDisplayName, string description)
        {
            Id = MetaActivableCategoryId.FromString(id ?? throw new ArgumentNullException(nameof(id)));
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            ShortSingularDisplayName = shortSingularDisplayName ?? throw new ArgumentNullException(nameof(shortSingularDisplayName));
            Description = description ?? throw new ArgumentNullException(nameof(description));
        }

        /// <inheritdoc cref="MetaActivableCategoryMetadataAttribute(string, string, string, string)"/>
        public MetaActivableCategoryMetadataAttribute(string id, string displayName, string description)
            : this(
                  id:                       id,
                  displayName:              displayName,
                  shortSingularDisplayName: id, // \note By default, use id for shortSingularDisplayName.
                  description:              description)
        {
        }
    }

    /// <summary>
    /// Attribute for declaring a "kind" of activables.
    /// Put this attribute on any type (can be just a dummy static class)
    /// in order to make the SDK detect the kind declaration.
    ///
    /// A kind of activables represents a specific activables-based feature.
    /// For example, a "happy hour" feature with specific game functionality
    /// could be one kind.
    ///
    /// Each kind belongs to a category, specified by the CategoryId member
    /// of this attribute.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class MetaActivableKindMetadataAttribute : MetaActivablesMetadataPreserve.PreserveAttribute
    {
        public MetaActivableKindId      Id          { get; }
        public string                   DisplayName { get; }
        public string                   Description { get; }
        public MetaActivableCategoryId  CategoryId  { get; }

        public MetaActivableKindMetadataAttribute(string id, string displayName, string description, string categoryId)
        {
            Id = MetaActivableKindId.FromString(id ?? throw new ArgumentNullException(nameof(id)));
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            Description = description ?? throw new ArgumentNullException(nameof(description));
            CategoryId = MetaActivableCategoryId.FromString(categoryId ?? throw new ArgumentNullException(nameof(categoryId)));
        }
    }

    /// <summary>
    /// Attribute used for marking a Game Config data class
    /// for a specific kind of activable.
    ///
    /// Put this attribute on a class that implements
    /// <see cref="IMetaActivableConfigData"/> that represents
    /// the configuration for this kind of activable.
    /// Additionally, your SharedGameConfig should have a
    /// GameConfigLibrary member whose TInfo type is the type
    /// this attribute is on.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class MetaActivableConfigDataAttribute : Attribute
    {
        public MetaActivableKindId KindId { get; }
        public bool Fallback { get; }
        public bool WarnAboutMissingConfigLibrary { get; }

        /// <param name="fallback">
        /// If true, the class is a "fallback" implementation that
        /// is only used if no non-fallback class exists.
        /// </param>
        public MetaActivableConfigDataAttribute(string kindId, bool fallback = false, bool warnAboutMissingConfigLibrary = true)
        {
            KindId = MetaActivableKindId.FromString(kindId ?? throw new ArgumentNullException(nameof(kindId)));
            Fallback = fallback;
            WarnAboutMissingConfigLibrary = warnAboutMissingConfigLibrary;
        }
    }

    /// <summary>
    /// Attribute used for marking a MetaActivableSet-derived class
    /// for a specific kind of activable.
    ///
    /// Put this attribute on a class derived from
    /// <see cref="MetaActivableSet{TId, TInfo, TActivableState}"/>
    /// that represents the state of activables of this kind.
    /// Additionally, your PlayerModel should have a member
    /// with that type.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class MetaActivableSetAttribute : Attribute
    {
        public MetaActivableKindId KindId { get; }
        public bool Fallback { get; }

        /// <param name="fallback">
        /// If true, the class is a "fallback" implementation that
        /// is only used if no non-fallback class exists.
        /// </param>
        public MetaActivableSetAttribute(string kindId, bool fallback = false)
        {
            KindId = MetaActivableKindId.FromString(kindId ?? throw new ArgumentNullException(nameof(kindId)));
            Fallback = fallback;
        }
    }

    public class MetaActivableRepository
    {
        public class CategorySpec
        {
            public Type                     AttributeHolder             { get; }

            public MetaActivableCategoryId  Id                          { get; }
            public string                   DisplayName                 { get; }
            public string                   ShortSingularDisplayName    { get; }
            public string                   Description                 { get; }

            public List<KindSpec>           Kinds                       { get; } = new List<KindSpec>();

            public CategorySpec(Type attributeHolder, MetaActivableCategoryId id, string displayName, string shortSingularDisplayName, string description)
            {
                AttributeHolder = attributeHolder ?? throw new ArgumentNullException(nameof(attributeHolder));
                Id = id ?? throw new ArgumentNullException(nameof(id));
                DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
                ShortSingularDisplayName = shortSingularDisplayName ?? throw new ArgumentNullException(nameof(shortSingularDisplayName));
                Description = description ?? throw new ArgumentNullException(nameof(description));
            }
        }

        public class KindSpec
        {
            public Type                                                 AttributeHolder                 { get; }

            public MetaActivableKindId                                  Id                              { get; }
            public string                                               DisplayName                     { get; }
            public string                                               Description                     { get; }
            public MetaActivableCategoryId                              CategoryId                      { get; }

            /// <summary> The concrete type derived from <see cref="MetaActivableSet{TId, TInfo, TActivableState}"/>. </summary>
            public Type                                                 ActivableSetType                { get; set; }

            /// <summary> The concrete type implementing <see cref="IMetaActivableConfigData"/>. </summary>
            public Type                                                 ConfigDataType                  { get; set; }
            /// <summary> The TKey type in <see cref="IMetaActivableConfigData{TKey}"/> implemented by <see cref="ConfigDataType"/>. </summary>
            public Type                                                 ConfigKeyType                   { get; set; }
            public List<MemberInfo>                                     GameSpecificConfigDataMembers   { get; set; }
            public bool                                                 WarnAboutMissingConfigLibrary   { get; set; }

            public MemberHelper<ISharedGameConfig, IGameConfigLibraryEntry>      GameConfigLibrary           { get; set; }
            // \note PlayerSubModel is MultiMemberHelper, i.e. stores sub-model info *per concrete player model type*.
            //       Usually, this is not relevant, as there's usually no need for more than one concrete player model type.
            //       However, for example in test code there can be multiple concrete player model types.
            // \todo [nuutti] Do same for GameConfigLibrary?
            public MultiMemberHelper<IPlayerModelBase, IMetaActivableSet>   PlayerSubModel              { get; set; } = new MultiMemberHelper<IPlayerModelBase, IMetaActivableSet>();

            public List<string> GetIncompleteIntegrationErrors()
            {
                List<string> errors = new List<string>();

                if (ConfigDataType == null)
                    errors.Add($"Failed to locate a Config Data type for {DisplayName} (id={Id}). There needs to exist a {nameof(IMetaActivableConfigData)}-implementing type with {nameof(MetaActivableConfigDataAttribute)} specifying the kind {Id}.");
                else if (GameConfigLibrary == null && WarnAboutMissingConfigLibrary)
                    errors.Add($"Failed to locate Game Configs for {DisplayName} (id={Id}). There needs to be a GameConfigLibrary member in your GameConfig with {ConfigDataType.ToGenericTypeString()} type as its TInfo type.");

                if (ActivableSetType == null)
                    errors.Add($"Failed to locate model (state) type for {DisplayName} (id={Id}). There needs to exist a MetaActivableSet<...>-derived type with {nameof(MetaActivableSetAttribute)} specifying the kind {Id}.");
                else if (PlayerSubModel.PerConcreteContainingType.Count == 0)
                    errors.Add($"Failed to locate model (state) in player model for {DisplayName} (id={Id}). There needs to be a member in your PlayerModel with type {ActivableSetType.ToGenericTypeString()}.");

                return errors;
            }

            public KindSpec(Type attributeHolder, MetaActivableKindId id, string displayName, string description, MetaActivableCategoryId categoryId)
            {
                AttributeHolder = attributeHolder ?? throw new ArgumentNullException(nameof(attributeHolder));
                Id = id ?? throw new ArgumentNullException(nameof(id));
                DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
                Description = description ?? throw new ArgumentNullException(nameof(description));
                CategoryId = categoryId ?? throw new ArgumentNullException(nameof(categoryId));
            }
        }

        public class MultiMemberHelper<TContaining, TMember>
        {
            public Dictionary<Type, MemberHelper<TContaining, TMember>> PerConcreteContainingType { get; } = new Dictionary<Type, MemberHelper<TContaining, TMember>>();

            public bool TryGetMemberValue<TConcreteContaining>(TConcreteContaining containing, out TMember memberValue)
                where TConcreteContaining : TContaining
            {
                if (PerConcreteContainingType.TryGetValue(containing.GetType(), out MemberHelper<TContaining, TMember> helper))
                {
                    memberValue = helper.GetMemberValue(containing);
                    return true;
                }
                else
                {
                    memberValue = default;
                    return false;
                }
            }
        }

        public class MemberHelper<TContaining, TMember>
        {
            public MemberInfo                   MemberInfo      { get; }
            public Func<TContaining, TMember>   GetMemberValue  { get; }

            public MemberHelper(MemberInfo memberInfo)
            {
                if (memberInfo == null)
                    throw new ArgumentNullException(nameof(memberInfo));
                if (!typeof(TContaining).IsAssignableFrom(memberInfo.DeclaringType))
                    throw new ArgumentException($"{typeof(TContaining).ToGenericTypeString()} is not assignable from the declaring type of {memberInfo.ToMemberWithGenericDeclaringTypeString()} (declaring type is {memberInfo.DeclaringType.ToGenericTypeString()})");
                if (!typeof(TMember).IsAssignableFrom(memberInfo.GetDataMemberType()))
                    throw new ArgumentException($"{typeof(TMember).ToGenericTypeString()} is not assignable from the type {memberInfo.GetDataMemberType().ToGenericTypeString()} of the data member {memberInfo.ToMemberWithGenericDeclaringTypeString()}");

                Func<object, object> untypedGetter = memberInfo.GetDataMemberGetValueOnDeclaringType();
                if (untypedGetter == null)
                    throw new InvalidOperationException($"{memberInfo.ToMemberWithGenericDeclaringTypeString()} does not have a getter");

                MemberInfo      = memberInfo;
                GetMemberValue  = (TContaining containing) => (TMember)untypedGetter(containing);
            }
        }

        public static MetaActivableRepository Instance { get; private set; }

        public static void InitializeSingleton()
        {
            Instance = new MetaActivableRepository();
        }

        OrderedDictionary<MetaActivableCategoryId, CategorySpec>    _categories = new OrderedDictionary<MetaActivableCategoryId, CategorySpec>();
        OrderedDictionary<MetaActivableKindId, KindSpec>            _kinds      = new OrderedDictionary<MetaActivableKindId, KindSpec>();

        Dictionary<Type, MetaActivableKindId> _concreteActivableStateTypeToKindId = new Dictionary<Type, MetaActivableKindId>();

        public IReadOnlyDictionary<MetaActivableCategoryId, CategorySpec>   AllCategories   => _categories;
        public IReadOnlyDictionary<MetaActivableKindId, KindSpec>           AllKinds        => _kinds;

        public MetaActivableKindId TryGetKindIdForConcreteActivableStateType(Type activableStateType)
        {
            if (_concreteActivableStateTypeToKindId.TryGetValue(activableStateType, out MetaActivableKindId kindId))
                return kindId;
            else
                return null;
        }

        static void FindAttribute<TAttributeType>(List<(Type, TAttributeType)> list, Type type, object[] attrs) where TAttributeType : Attribute
        {
            IEnumerable<TAttributeType> attr = attrs.OfType<TAttributeType>();
            if (!attr.Any())
                return;
            list.Add((type, attr.First()));
        }

        MetaActivableRepository()
        {
            if (GameConfigRepository.Instance == null)
                throw new InvalidOperationException($"{nameof(GameConfigRepository)} must be initialized before {nameof(MetaActivableRepository)}");

            Type sharedGameConfigType = GameConfigRepository.Instance.SharedGameConfigType;
            List<(Type, MetaActivableCategoryMetadataAttribute)> categoryHolders = new List<(Type, MetaActivableCategoryMetadataAttribute)>();
            List<(Type, MetaActivableKindMetadataAttribute)> kindHolders = new List<(Type, MetaActivableKindMetadataAttribute)>();
            List<(Type, MetaActivableConfigDataAttribute)> configDataTypes = new List<(Type, MetaActivableConfigDataAttribute)>();
            List<(Type, MetaActivableSetAttribute)> activableSetTypes = new List<(Type, MetaActivableSetAttribute)>();
            List<Type> concreteActivableStateTypes = new List<Type>();
            List<Type> playerModelClasses = new List<Type>();

            foreach (Type type in TypeScanner.GetAllTypes())
            {
                object[] attrs = type.GetCustomAttributes(false);

                if (attrs != null && attrs.Length > 0)
                {
                    // Populate (type, attr) pairs for the attributes we are interested in
                    FindAttribute(categoryHolders, type, attrs);
                    FindAttribute(kindHolders, type, attrs);
                    FindAttribute(configDataTypes, type, attrs);
                    FindAttribute(activableSetTypes, type, attrs);
                }

                if (type.IsDerivedFrom<MetaActivableState>() && !type.IsAbstract)
                    concreteActivableStateTypes.Add(type);

                if (type.IsPlayerModelClass())
                    playerModelClasses.Add(type);
            }

            foreach ((Type type, MetaActivableCategoryMetadataAttribute attr) in categoryHolders.OrderBy(x => x.Item2.Id))
                RegisterCategory(type, attr);

            foreach ((Type type, MetaActivableKindMetadataAttribute attr) in kindHolders.OrderBy(x => x.Item2.CategoryId).ThenBy(x => x.Item2.Id))
                RegisterKind(type, attr);

            foreach ((Type type, MetaActivableConfigDataAttribute attr) in configDataTypes)
                RegisterConfigDataType(type, attr);

            foreach ((Type type, MetaActivableSetAttribute attr) in activableSetTypes)
                RegisterActivableSetType(type, attr);

            foreach (Type concreteActivableStateType in concreteActivableStateTypes)
                RegisterConcreteActivableStateType(concreteActivableStateType);

            foreach (MemberInfo configEntryMember in GameConfigRepository.Instance.GetGameConfigTypeInfo(sharedGameConfigType).Entries.Values.Select(entry => entry.MemberInfo))
            {
                Type configEntryType = configEntryMember.GetDataMemberType();
                if (!configEntryType.IsGameConfigLibrary())
                    continue;
                Type configDataType = configEntryType.GenericTypeArguments[1];
                MetaActivableConfigDataAttribute configDataAttr = configDataType.GetCustomAttribute<MetaActivableConfigDataAttribute>();
                if (configDataAttr == null)
                    continue;
                RegisterGameConfigLibrary(configEntryMember, configDataAttr);
            }

            foreach (Type playerModelType in playerModelClasses)
            {
                foreach (MemberInfo member in playerModelType.EnumerateInstanceDataMembersInUnspecifiedOrder())
                {
                    Type memberType = member.GetDataMemberType();
                    MetaActivableSetAttribute activableSetAttribute = memberType.GetCustomAttribute<MetaActivableSetAttribute>();
                    if (activableSetAttribute == null)
                        continue;
                    RegisterPlayerSubModel(playerModelType, member, activableSetAttribute);
                }
            }

            foreach (KindSpec kind in _kinds.Values)
                CheckKindTypesConsistency(kind);

            foreach (KindSpec kind in _kinds.Values)
                CheckKindConcreteStateTypeIsKnown(kind);

            foreach (KindSpec kind in _kinds.Values)
            {
                foreach (string error in kind.GetIncompleteIntegrationErrors())
                    DebugLog.Warning("Incomplete integration of activable: {Error}", error);
            }
        }

        void RegisterCategory(Type attributeHolder, MetaActivableCategoryMetadataAttribute categoryMetadata)
        {
            if (_categories.ContainsKey(categoryMetadata.Id))
                throw new InvalidOperationException($"Multiple {nameof(MetaActivableCategoryMetadataAttribute)}s with id={categoryMetadata.Id} found: on {_categories[categoryMetadata.Id].AttributeHolder}, and on {attributeHolder}");

            _categories.Add(categoryMetadata.Id, new CategorySpec(attributeHolder, categoryMetadata.Id, categoryMetadata.DisplayName, categoryMetadata.ShortSingularDisplayName, categoryMetadata.Description));
        }

        void RegisterKind(Type attributeHolder, MetaActivableKindMetadataAttribute kindMetadata)
        {
            if (_kinds.ContainsKey(kindMetadata.Id))
                throw new InvalidOperationException($"Multiple {nameof(MetaActivableKindMetadataAttribute)}s with id={kindMetadata.Id} found: on {_kinds[kindMetadata.Id].AttributeHolder}, and on {attributeHolder}");

            if (!_categories.ContainsKey(kindMetadata.CategoryId))
                throw new InvalidOperationException($"{nameof(MetaActivableKindMetadataAttribute)} for kind {kindMetadata.Id} refers to unknown category {kindMetadata.CategoryId} (no such {nameof(MetaActivableCategoryMetadataAttribute)} was found)");

            KindSpec kind = new KindSpec(attributeHolder, kindMetadata.Id, kindMetadata.DisplayName, kindMetadata.Description, kindMetadata.CategoryId);
            _kinds.Add(kindMetadata.Id, kind);
            _categories[kindMetadata.CategoryId].Kinds.Add(kind);
        }

        void RegisterConfigDataType(Type configDataType, MetaActivableConfigDataAttribute configDataAttribute)
        {
            if (!_kinds.ContainsKey(configDataAttribute.KindId))
                throw new InvalidOperationException($"{nameof(MetaActivableConfigDataAttribute)} on {configDataType.ToGenericTypeString()} refers to unknown activable kind {configDataAttribute.KindId} (no such {nameof(MetaActivableKindMetadataAttribute)} was found)");

            if (!configDataType.ImplementsInterface<IMetaActivableConfigData>())
                throw new InvalidOperationException($"{configDataType.ToGenericTypeString()} has {nameof(MetaActivableConfigDataAttribute)}, but does not implement {nameof(IMetaActivableConfigData)}");

            if (!configDataType.ImplementsGenericInterface(typeof(IMetaActivableConfigData<>)))
                throw new InvalidOperationException($"{configDataType.ToGenericTypeString()} implements {nameof(IMetaActivableConfigData)} but not the generic {typeof(IMetaActivableConfigData<>).Name}<TKey>");
            Type configKeyType = configDataType.GetGenericInterfaceTypeArguments(typeof(IMetaActivableConfigData<>)).Single();

            KindSpec kind = _kinds[configDataAttribute.KindId];
            if (kind.ConfigDataType != null)
            {
                MetaActivableConfigDataAttribute existingConfigDataAttribute = kind.ConfigDataType.GetCustomAttribute<MetaActivableConfigDataAttribute>();
                if (existingConfigDataAttribute.Fallback && !configDataAttribute.Fallback)
                {
                    // Existing fallback gets overridden by non-fallback - that's ok, just proceed to overwrite properties in `kind`.
                }
                else if (!existingConfigDataAttribute.Fallback && configDataAttribute.Fallback)
                {
                    // Non-fallback exists, and this new one is a fallback - that's ok, ignore this new one, just return.
                    return;
                }
                else
                    throw new InvalidOperationException($"Multiple {nameof(MetaActivableConfigDataAttribute)}s found for kind {configDataAttribute.KindId}: on {kind.ConfigDataType.ToGenericTypeString()} and on {configDataType.ToGenericTypeString()}");
            }

            kind.ConfigDataType = configDataType;
            kind.ConfigKeyType = configKeyType;
            kind.GameSpecificConfigDataMembers = ResolveGameSpecificConfigDataMembers(configDataType);
            kind.WarnAboutMissingConfigLibrary = configDataAttribute.WarnAboutMissingConfigLibrary;
        }

        void RegisterActivableSetType(Type activableSetType, MetaActivableSetAttribute activableSetAttribute)
        {
            if (!_kinds.ContainsKey(activableSetAttribute.KindId))
                throw new InvalidOperationException($"{nameof(MetaActivableSetAttribute)} on {activableSetType.ToGenericTypeString()} refers to unknown activable kind {activableSetAttribute.KindId} (no such {nameof(MetaActivableKindMetadataAttribute)} was found)");

            Type baseMetaActivableSetType = activableSetType.EnumerateTypeAndBases().SingleOrDefault(baseType => baseType.IsGenericTypeOf(typeof(MetaActivableSet<,,>)));
            if (baseMetaActivableSetType == null)
                throw new InvalidOperationException($"{activableSetType.ToGenericTypeString()} has {nameof(MetaActivableSetAttribute)}, but is not derived from {typeof(MetaActivableSet<,,>).ToGenericTypeString()}");

            Type activableStateType = baseMetaActivableSetType.GenericTypeArguments[2];
            if (!typeof(MetaActivableState).IsAssignableFrom(activableStateType))
                throw new InvalidOperationException($"Internal error: type argument {activableStateType.ToGenericTypeString()} in {baseMetaActivableSetType.ToGenericTypeString()} (ancestor of {activableSetType.ToGenericTypeString()}) is not derived from {nameof(MetaActivableState)}");

            KindSpec kind = _kinds[activableSetAttribute.KindId];
            if (kind.ActivableSetType != null)
            {
                MetaActivableSetAttribute existingActivableSetAttribute = kind.ActivableSetType.GetCustomAttribute<MetaActivableSetAttribute>();
                if (existingActivableSetAttribute.Fallback && !activableSetAttribute.Fallback)
                {
                    // Existing fallback gets overridden by non-fallback - that's ok, just proceed to overwrite properties in `kind`.
                }
                else if (!existingActivableSetAttribute.Fallback && activableSetAttribute.Fallback)
                {
                    // Non-fallback exists, and this new one is a fallback - that's ok, ignore this new one, just return.
                    return;
                }
                else
                    throw new InvalidOperationException($"Multiple {nameof(MetaActivableSetAttribute)}s found for kind {activableSetAttribute.KindId}: on {kind.ActivableSetType.ToGenericTypeString()} and on {activableSetType.ToGenericTypeString()}");
            }

            kind.ActivableSetType = activableSetType;
        }

        void RegisterConcreteActivableStateType(Type concreteActivableStateType)
        {
            IEnumerable<KindSpec> kinds = _kinds.Values.Where(kind_ => kind_.ActivableSetType != null && kind_.ActivableSetType.GetGenericAncestorTypeArguments(typeof(MetaActivableSet<,,>))[2].IsAssignableFrom(concreteActivableStateType));
            if (kinds.Count() == 0)
                return;
            if (kinds.Count() > 1)
                throw new InvalidOperationException($"Same {nameof(MetaActivableState)}-derived concrete type {concreteActivableStateType.ToGenericTypeString()} used in multiple activable kinds: {kinds.ElementAt(0).Id} and {kinds.ElementAt(1).Id}");

            KindSpec kind = kinds.Single();
            _concreteActivableStateTypeToKindId.Add(concreteActivableStateType, kind.Id);
        }

        void RegisterGameConfigLibrary(MemberInfo gameConfigLibrary, MetaActivableConfigDataAttribute configDataAttribute)
        {
            Type configDataType = gameConfigLibrary.GetDataMemberType().GenericTypeArguments[1];

            if (!_kinds.TryGetValue(configDataAttribute.KindId, out KindSpec kind))
                throw new InvalidOperationException($"Internal error: unregistered kind {configDataAttribute.KindId} for config data type {configDataType.ToGenericTypeString()} (for {gameConfigLibrary.ToMemberWithGenericDeclaringTypeString()})");
            if (kind.ConfigDataType != configDataType)
                throw new InvalidOperationException($"Internal error: registered config data type {kind.ConfigDataType.ToGenericTypeString()} for kind {configDataAttribute.KindId} does not match {configDataType.ToGenericTypeString()} (for {gameConfigLibrary.ToMemberWithGenericDeclaringTypeString()})");

            if (!gameConfigLibrary.DeclaringType.ImplementsInterface<ISharedGameConfig>())
                throw new InvalidOperationException($"GameConfigLibrary having TInfo type with {nameof(MetaActivableConfigDataAttribute)} must implement {nameof(ISharedGameConfig)}; {gameConfigLibrary.DeclaringType.ToGenericTypeString()} (with {gameConfigLibrary.Name}) is not {nameof(ISharedGameConfig)}");

            if (kind.GameConfigLibrary != null)
                throw new InvalidOperationException($"Multiple GameConfigLibrary members found for {configDataAttribute.KindId}: {kind.GameConfigLibrary.MemberInfo.ToMemberWithGenericDeclaringTypeString()} and {gameConfigLibrary.ToMemberWithGenericDeclaringTypeString()}");

            kind.GameConfigLibrary = new MemberHelper<ISharedGameConfig, IGameConfigLibraryEntry>(gameConfigLibrary);
        }

        void RegisterPlayerSubModel(Type playerModelType, MemberInfo playerSubModel, MetaActivableSetAttribute activableSetAttribute)
        {
            Type activableSetType = playerSubModel.GetDataMemberType();

            if (!_kinds.TryGetValue(activableSetAttribute.KindId, out KindSpec kind))
                throw new InvalidOperationException($"Internal error: unregistered kind {activableSetAttribute.KindId} for activable set type {activableSetType.ToGenericTypeString()} (for {playerSubModel.ToMemberWithGenericDeclaringTypeString()})");

            if (kind.ActivableSetType != activableSetType)
                throw new InvalidOperationException($"Registered activable set type {kind.ActivableSetType.ToGenericTypeString()} for kind {activableSetAttribute.KindId} does not match {activableSetType.ToGenericTypeString()} (for {playerSubModel.ToMemberWithGenericDeclaringTypeString()})");

            if (kind.PlayerSubModel.PerConcreteContainingType.ContainsKey(playerModelType))
                throw new InvalidOperationException($"Multiple sub-models for kind {activableSetAttribute.KindId} found in player model {playerModelType.Name}: {kind.PlayerSubModel.PerConcreteContainingType[playerModelType].MemberInfo.ToMemberWithGenericDeclaringTypeString()} and {playerSubModel.ToMemberWithGenericDeclaringTypeString()}");

            kind.PlayerSubModel.PerConcreteContainingType.Add(playerModelType, new MemberHelper<IPlayerModelBase, IMetaActivableSet>(playerSubModel));
        }

        static List<MemberInfo> ResolveGameSpecificConfigDataMembers(Type configDataType)
        {
            // Resolve the members of configDataType that are not mapped to from any of the relevant core interfaces.
            // The relevant core interfaces are those implemented by IMetaActivableConfigData<...>, where this
            // IMetaActivableConfigData<...> is the instantiation of IMetaActivableConfigData<> implemented by configDataType.

            Type                    rootCoreInterfaceType   = configDataType.GetGenericInterface(typeof(IMetaActivableConfigData<>));
            List<InterfaceMapping>  coreInterfaceMappings   = rootCoreInterfaceType.GetInterfaces().Select(iFace => configDataType.GetInterfaceMap(iFace)).ToList();

            IEnumerable<MemberInfo> configDataMembers       = configDataType.EnumerateInstanceDataMembersInUnspecifiedOrder();

            return configDataMembers
                    .Where(configDataMember =>
                    {
                       bool isCoreMember;

                        if (configDataMember is PropertyInfo configDataProperty)
                        {
                            MethodInfo configDataPropertyGetter = configDataProperty.GetGetMethod();
                            isCoreMember = coreInterfaceMappings.Any(mapping => mapping.TargetMethods.Contains(configDataPropertyGetter));
                        }
                        else
                            isCoreMember = false;

                        return !isCoreMember;
                   }).ToList();
        }

        static void CheckKindTypesConsistency(KindSpec kind)
        {
            if (kind.ConfigDataType == null
             || kind.ActivableSetType == null)
                return;

            Type activableSetBaseType       = kind.ActivableSetType.EnumerateTypeAndBases().Single(baseType => baseType.IsGenericTypeOf(typeof(MetaActivableSet<,,>)));
            Type activableSetConfigDataType = activableSetBaseType.GenericTypeArguments[1];

            if (activableSetConfigDataType != kind.ConfigDataType)
                throw new InvalidOperationException($"Activable kind {kind.Id}'s model {kind.ActivableSetType.ToGenericTypeString()} specifies config data type {activableSetConfigDataType.ToGenericTypeString()} which differs from {kind.Id}'s declared config data type {kind.ConfigDataType.ToGenericTypeString()}");
        }

        void CheckKindConcreteStateTypeIsKnown(KindSpec kind)
        {
            if (kind.ActivableSetType == null)
                return;

            if (!_concreteActivableStateTypeToKindId.Values.Contains(kind.Id))
                throw new InvalidOperationException($"Activable kind {kind.Id} uses {kind.ActivableSetType.GetGenericAncestor(typeof(MetaActivableSet<,,>)).ToGenericTypeString()}, but no concrete type was found for {kind.ActivableSetType.GetGenericAncestorTypeArguments(typeof(MetaActivableSet<,,>))[2].ToGenericTypeString()}");
        }
    }

    namespace MetaActivablesMetadataPreserve
    {
        /// <summary>
        /// Attribute for preserving activable kind and category metadata declarations
        /// when the attributes are used on a dummy static class.
        ///
        /// Normally Unity's managed code stripping on Medium and higher levels would
        /// remove such seemingly-unused classes, but Metaplay SDK relies on reflection
        /// to locate them.
        ///
        /// Any type with an attribute named (or inheriting from an attribute named)
        /// precisely PreserveAttribute is preserved by the stripping. In particular,
        /// it doesn't need to be Unity's predefined PreserveAttribute.
        /// </summary>
        public class PreserveAttribute : Attribute{ }
    }
}
