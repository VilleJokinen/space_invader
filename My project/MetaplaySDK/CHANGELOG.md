# Change Log

## Release 25 (2023-11-14)

### Breaking

- SDK: Minimum required Unity version is now 2021.3.
- SDK: `ExportEntityHandler` and `ImportEntityHandler` are Obsolete.
  - **Action Required**: All classes inheriting from `ImportEntityHandler` should inherit `SimpleImportEntityHandler` instead.
- SDK: `MultiplayerEntityClientContext` constructor now takes `ClientMultiplayerEntityContextInitArgs`.
  - **Action Required**: When a context is generated, create init args with `MultiplayerEntityClientBase.DefaultInitArgs` and pass it into the constructor.
- SDK: The fixed-point `F32`/`F64` methods `ToString()` and `Parse()` have been rewritten to be deterministic and faster, but the outputs are not identical to the earlier releases. This applies parsing fixed-point values during game config building as well.
  - **Action Required**: Check that the tiny differences in the game config build outputs don't cause any meaningful changes. If you need the behavior of the parser, talk to us and we'll help you restore it.
  - **Action Required**: If you're converting fixed-points to/from strings, you must perform a full synchronized update of the client and the server to avoid desyncs.
  - **Action Required**: If you're converting fixed-points to JSON, make sure the changed results (from the updated rounding rules) do not affect any system consuming the said JSON documents.
- SDK: The game config base classes `SharedGameConfigBase` and `ServerGameConfigBase` have been refactored to make SDK side config libraries (Languages, InAppProducts, PlayerSegments, Offers, OfferGroups, PlayerExperiments) optional and to not require specifying custom info classes for the SDK libraries via template parameters. The old base classes have been preserved with `Legacy` prefix to aid in the migration.
  - **Action Required**: If using any of the SDK side features requiring config libraries, introduce the associated game config entries into your `SharedGameConfigBase` and `ServerGameConfigBase` derived classes and tag the entries with `GameConfigEntryAttribute`. Alternatively you can switch the base class to the `Legacy` variants to retain the old behavior of having the entries declared on the SDK side.
- SDK: `PersistedParticipantDivisionAssociation` has a new field `LeagueStateRevision` to help restore the league's state in case of shutdowns and crashes.
  - **Action Required**: If the leagues feature is enabled, this change requires a new database migration.
- SDK: `ConfigArchive` has been refactored to be read-only to reduce unnecessary copies of the data, `ConfigArchiveEntry`s are now decompressed on load.
  - **Action Required**: Replace calls to `ConfigArchiveEntry.Uncompress()` and `ConfigArchiveEntry.RawBytes` with `ConfigArchive.GetEntryBytes()` or `ConfigArchiveEntry.Bytes`.
- SDK: Remove the `LocalizationStorageFormat` and `LocalizationStorageFormat.LegacyCsv`. The binary format is now always assumed.
  - **Action Required**: Remove any references to `LocalizationStorageFormat` and the legacy format from your code.
- SDK: `MetaPlayerReward.Consume` now takes the `IRewardSource` parameter. `MetaPlayerReward.InvokeConsume` is now sealed and exists only as a helper to call `Consume` with the concrete `PlayerModel` type instead of `IPlayerModelBase`.
  - **Action Required**: In your custom player reward classes, change your existing `Consume` overrides to take the `IRewardSource` parameter. Also, change your existing `InvokeConsume` overrides to be `Consume` overrides instead.
- SDK: Social authentication code now tolerates the case where there was a conflicting player account, but the server failed to get the conflicting player's state, for example due to a deserialization error. The social login API was updated to reflect this. Previously, the current game session would be terminated in this scenario.
  - **Action Required**: If you implement `IMetaplayClientSocialAuthenticationDelegate` in your client code: Add an implementation of `OnSocialAuthenticationConflictWithFailingOtherPlayer`. The minimal implementation can be empty (though logging an error is recommended), but ideally the error should be communicated to the user, with the option of contacting customer support. See the comments in `IMetaplayClientSocialAuthenticationDelegate` for more information.
  - **Action Required**: If you handle `SocialAuthenticateResult` directly in your client code: Member `ConflictingPlayer` has been renamed to `ConflictingPlayerIfAvailable`, and `ConflictingPlayerId` has been added. Unlike before, `ConflictingPlayerIfAvailable` can now deserialize to null even if there was a conflict. The presence of a conflict is now determined by checking if `ConflictingPlayerId` is available (i.e. is not `EntityId.None`). Therefore the new case you will need to handle is that where `ConflictingPlayerIfAvailable` deserializes to null yet `ConflictingPlayerId` is available. You should handle this case in the same way as described above for `IMetaplayClientSocialAuthenticationDelegate.OnSocialAuthenticationConflictWithFailingOtherPlayer`.
- SDK: `ConfigParser`'s method `TryGetParseFunc()` has been replaced with `TryParse()`.
  - **Action Required**: Calls to `ConfigParser.TryGetParseFunc()` should be changed according to the use case:
    - Calls to `TryGetParseFunc()` immediately followed by a call to the returned parse func should be replaced by a call to `TryParse()` instead.
    - Calls to `TryGetParseFunc()` made for the purpose of checking if a custom parse func has already been registered should be replaced with a call to `HasRegisteredParseFunc()`.
    - Other kinds of calls to `TryGetParseFunc()` are unlikely to exist, but if they do then they need to be considered case by case.
- SDK: In `PersistedMultiplayerEntityActorBase`, `TimeSpan TickUpdateInterval` is replaced with `TickRateSetting TickRate` and has a new default behavior.
  - **Action Required**: If there is existing `TickUpdateInterval`, convert it into `TickRate`. See `TickRateSetting` constructor for details.
  - **Action Required**: If there was no previously defined `TickUpdateInterval`, the actor now by default uses the Model's tick rate when there is an ongoing Session. This may significantly increase the tick execution rate. To keep the previous tick rate, set `TickRate` to `new TickRateSetting(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5))`.
- SDK: `GameConfigBuilderServices` has been removed in favor of the new Game Config build sources system. Integrations need to update to specifying the config build sources using in the `GameConfigBuildParameters` input parameter and configuring source fetchers with the `IGameConfigSourceFetcherConfig` input parameter.
  - **Action Required**: Refer to the Release 25 game configs migration guide to update existing build pipeline configuration.
  - **Action Required**: `ConfigArchive.FromBytes()`, `ConfigArchive.GetEntryBytes()`, and `ConfigArchiveEntry.Bytes` APIs have been updated to use `Memory<byte>`, please update usages.
  - **Action Required**: Replace `ReadOnlyConfigArchive` with `ConfigArchive`.
  - **Action Required**: Replace `ConfigArchive.ToBytes()` calls with `ConfigArchiveBuildUtility.ToBytes()`.
  - **Action Required**: `FolderEncoding` is now a child class or `ConfigArchiveBuildUtility`.
  - **Action Required**: Replace calls to `ConfigArchive.WriteToFile()` with `ConfigArchiveBuildUtility.WriteToFile()`.
  - **Action Required**: Replace uses of the `IConfigArchive` and `IConfigArchiveEntry` interfaces with `ConfigArchive` and `ConfigArchiveEntry`.
- SDK: Runtime option `GoogleSheets:GameConfigSheetId` has been removed in favor of the new Game Config build sources system. Integrations should now specify the available source spreadsheets by deriving from `GameConfigBuildIntegration` and overriding its `GetAllAvailableBuildSources`.
  - **Action Required**: Remove `GoogleSheets:GameConfigSheetId` from your runtime option `yaml` files. Refer to the Release 25 game configs migration guide to update existing build pipeline configuration.
- SDK: `MetaplayClientOptions` and `MetaplaySDKConfig` have been refactored and several options including `ServerEndpoint` are moved to the new `EnvironmentConfig` class.
  - **Action Required**: Refer to the Release 25 environment configs migration guide to start using the default environment config provider or to implement a custom IEnvironmentConfigProvider.
- Unity: Support for `METAPLAY_UNITY_DEFINES` override file has been removed.
  - **Action Required**: For overriding app target environment in CI (i.e. setting the target server it connects to), use `METAPLAY_ACTIVE_ENVIRONMENT_ID` environment variable, or `-MetaplayActiveEnvironmentId env` command line parameter.
- BotClient: `StartSession` doesn't need to be called and is removed.
  - **Action Required**: Remove call to `StartSession`.
- Dashboard: `b-tooltip` has been removed in favor the newer `MTooltip` component.
  - **Action Required**: Replace `b-tooltip` with `MTooltip` in your Vue components.
- Dashboard: `b-badge` has been removed in favor the newer `MBadge` component.
  - **Action Required**: Replace `b-badge` with `MBadge` in your Vue components.
- Dashboard: `MetaApiErrorAlert` has been removed and replaced with the `MErrorCallout` component.
  - **Action Required**: Replace `MetaApiErrorAlert` with `MErrorCallout` in your Vue components.

### Added

- SDK: Added virtual `IPlayerModelBase.UpdateTimeZone()` method to allow restricting player time zone changes. Sample implementation is included in the Idler sample.
- SDK: MetaSerialization now reports the type and member name when encountering an unexpected type.
- SDK: MetaSerialization now reports the ConfigKey and spreadsheet Url when encountering an invalid `MetaRef` in a game config item.
- SDK: MetaSerialization now supports deserializing objects by invoking the constructor, this enables support for Records and read-only structs. You can use this by either passing `MetaSerializableFlags.AutomaticConstructorDetection` to `[MetaSerializable]` or by add the `[MetaDeserializationConstructor]` attribute to your constructor.
- SDK: MetaSerialization now supports serializing `Tuple` and `ValueTuple` up to 7 elements.
  - Note that the order of parameters has to stay the same, similarly to `MetaSerializableFlags.ImplicitMembers`.
  - `Tuple` and `ValueTuple` are compatible with a class and a struct respectively, meaning you can change the `Tuple`/`ValueTuple` to an instance of a class/struct provided you keep the same TagId order of the members.
- SDK: MetaSerialization now supports `DateTimeOffset`, `Version`, `Guid`, `TimeSpan`, `ReadOnlyCollection<T>`, and `ReadOnlyDictionary<Key, Value>`.
- SDK: `[BigQueryAnalyticsName("...")]` and `[FirebaseAnalyticsName("...")]` attributes for overriding the name of an analytics event or a parameter in an analytics event, which would by default be determined by the C# class or member name.
- SDK: It is now possible to have multiple config libraries with the same item type, or different item types inheriting from the same base class which implements `IGameConfigData<>`. Reference resolving will look for the item in each library whose item type is compatible with the reference.
- SDK: Analytics events "keywords" for convenient classification and filtering of analytics events in the dashboard.
- SDK: Added `LeagueManagerOptions.AllowDivisionBackFill` option to turn off division backfilling when removing a participant from a division.
- SDK: Added `LeagueManagerOptions.ConcludeSeasonMaxDelayMilliseconds` option to introduce a delay to divisions concluding.
- SDK: Added a new abstraction for game config build sources communicated in `GameConfigBuildParameters`.
- SDK: Added integration extendable abstract `IGameConfigSourceFetcher` system for implementing fetching of game config source data, and SDK implementations of google sheets and local file fetching.
- SDK: Added integration hook `GameConfigBuildIntegration` to encapsulate various extension points related to game config building.
- SDK: Added `configBuildSource` parameter to `GameConfigEntryAttribute` for specifying the used game config build source per entry.
- SDK: Added server-side limit of 1000 on the list of individually targeted players in broadcasts and notification campaigns, configurable in `ServerOptions`.
- SDK: Added new `EnvironmentConfig` system and the `DefaultEnvironmentConfigProvider` that replaces the Idler sample's `DeploymentConfig` and similar systems in other samples. Refer to the Release 25 environment configs migration guide for more information on migrating to the new system, or implementing a simpler minimal integration.
- Dashboard: Added a new unified way to handle errors on the dashboard. Using the `MErrorCallout` component, you can now display both technical and non-technical errors in a consistent and user-friendly way.
- Dashboard: `LeagueDetailView`now has a migration progress indicator during preview phase using new `MProgressBar` component.
- Dashboard: Event log cards (for example the player analytics event log) can now be filtered based on the newly added keywords in addition to the existing event types and general search. The underlying `MetaEventStreamCard` component has hew properties to both set and freeze the filters to easily create cards that show a specific subset of all the events.
- Dashboard: New sidebar section for building and managing localizations as OTA updates to clients. This is a first pass on the UI with focus on viewing the localization contents and any missing keys to help with release management workflows on large games.
- Dashboard: Added the `LiveOpsDashboard.DashboardHeaderColorInHex` and `LiveOpsDashboard.DashboardHeaderLightTextColor` runtime options to customize the dashboard header color and text color in your environments.
- Dashboard: New `MetaUiNext` components. Run `pnpm storybook` in the `MetaplaySDK/NodePackages/MetaUiNext` folder to see the components in action.
  - `MBadge` - A colored box/pill to highlight information. Replaces `b-badge`.
  - `MTooltip` - A utility to show tooltips.
  - `MInputSimpleSelect` - A minimalistic select input for string values.
  - `MInputSingleCheckbox` - A single checkbox input for boolean values.
  - `MInputTextArea` - A textarea input for string values.
  - `MErrorCallout` - A specialized callout to display technical errors.
- Dashboard: The `add<listName>OverviewListItem` and the `addUiComponent` have been extened to support hiding of overview list items and UI placement components based a user's permissions.
- Dashboard: Pinned Vue version to `3.3.4` to avoid withDefault boolean type bug with vue bootstrap.

### Changed

- SDK: Change solution files to use "Any CPU" and remove explicit x64 and arm64 from C# project files. This fixes build issues when using the solution files.
- SDK: It is now an error for `PlayerModel` or other `ISchemaMigratable` classes to have extraneous `[MigrationFromVersion(fromVersion: x)]` methods where `fromVersion` is outside the range specified in the `[SupportedSchemaVersions(...)]` attribute. This is intended to guard against mistakes in the specified version range.
- SDK: Game config libraries no longer register as config resolvers to the GameConfig instance on import (or library build), the operation of registering active resolvers now happens in `GameConfigBase.OnConfigEntriesPopulated()`.
- SDK: The server no longer uses the config build timestamp for figuring out if the current game config should be auto-updated to the builtin config found on filesystem. Config auto-update on server boot now always happens if the builtin gameconfig hasn't been seen in the environment before. This fixes cases where auto-update did not take place due to the live gameconfig having a newer timestamp than the config associated with a server update.
- SDK: It is now possible to extend `GlobalState` with game-specific data via introducing a game side implementation of `GlobalStateManager`.
- SDK: Updated the client-side `IAPManager` to use the `IDetailedStoreListener` interface added in Unity Purchasing 4.8.0 when available. This fixed a warning about an obsoleted `UnityPurchasing` method.
- SDK: Game config data now properly supports the `Hidden` and `ServerOnly` MetaMember flags for stripping data from the client visible config.
- SDK: The `IMetaActivableConfigData` fields are now conditionally compiled for server only, which allows implementations to omit them from client visible config data.
- SDK: The "full protocol hash" used for warning about client and server generated serializer potential incompatibility now ignores any meta-members tagged with flag `MetaMemberFlags.Hidden` or `MetaMemberFlags.ServerOnly`. This allows hidden and server-only flags to be conditionally compiled only in server builds without resulting in compatibility warnings during the login handshake.
- SDK: A memory-expensive debug check (added in release 24) done at config build time is now disabled in non-local server environments. The check guards against certain kinds of bugs in `SharedGameConfig.OnLoaded` and is likely only worthwhile during development, not during live operation.
- SDK: TCP backlog is now inspected with `sock_diag` instead of `/proc/net/tcp` for reduced overhead.
- SDK: JSON Serialization in the Admin API now ignores properties that throw exceptions, these properties are excluded from the output of the endpoint. Exceptions are deduplicated (by message, to prevent spam) and are still logged to the server output after serialization is complete.
  - We recommend that these exceptions are still fixed, as it can lead to degraded performance and error fatigue.
- SDK: The game config classes (implementations of `ISharedGameConfig` and `IServerGameConfig`) are no longer required to contain config library entries associated with SDK features. Runtime stub libraries are exposed via the base class implementation when no concrete entry is declared by the game integration config class. Additionally the following uses of SDK config libraries have been updated:
  - The config data exposed to dashboard via `SystemStatusController` is now augmented with stub data when no config entry exists.
  - MetaActivableRepository check for config data associated to offer groups existing has been removed.
- SDK: Change the config archive content hash for an empty archive (no entries present) to not conflict with `ContentHash.None` for the ability to refer to an empty archive separately from a missing archive.
- SDK: Removed `GameConfigBase.ArchiveVersion`. With the introduction of ServerOnly game config data the game config content hash is no longer the same for server and client, and therefore relying on the `ArchiveVersion` property of the in-memory game config is prone to error.
- Unity Editor: Model Inspector now displays `IEnumerable` types as collections.
- Metrics: Added `metaplay_in_maintenance` gauge which has the value of 1 when the game server is in maintenance.
- Metrics: Entity subscribe durations and counts are now represented separtely per target entity in `game_entity_ask_duration` and `game_entity_asks_total` metrics. This allows identifying which `SubscribeToAsync()` call is causing elevated metrics.
- Dashboard: `MetaUsername` component no longer needs URL or permissions to be passed in.
- Dashboard: The core page layout has been rebuilt with the new MetaUiNext components.
- Dashboard: The quick links section is now a popover instead of a modal for better usability.
- Dashboard: `SegmentDetailView`, `OfferGroupDetailView`, `OfferDetailView`, `ActivableDetailView` now using single endpoint subscription instead of deconstructing all endpoint data.
- Dashboard: The subscriptions API has changed to become significantly easier to use. `useStaticSubscription` and `useDynamicSubscription` are now deprecated. Update all callsites to use the new `useSubscription` API instead. Check the "Working with Subscriptions" documentation for how to do this.
- Dashboard: Improved display of game configs that contain errors or that can no longer be deserialized due to changes in game code.
- Dashboard: A UX pass on the individual player targeting form in broadcasts and notifications. It should now be easier to use and understand.
- Dashboard: Unified error handling and displaying on the dashboard. Using the `MErrorCallout` component, you can now display and share both technical and non-technical error information in a consistent and user-friendly way.
- Dashboard: Event stream cards (like the player analytics events log) filtering utilities are now OR instead of the previous AND between the different filters. This makes it easier to drill down into specific events. There is also a new text label to make it clearer how the filters act as you enable them.
- Dashboard: Game config building from dashboard is now enabled by default if the integration provides available game config sources by overriding `GameConfigBuildIntegration.GetAvailableBuildSources`.
- Dashboard: Removed the "Overview of changes" section links from the game config diff page due to a regression in how the page rendering works. This page is due for a re-design that will introduce a better way to navigate large diffs. Tentatively planned for release 26.
- Tests: The integration tests now log all docker invocation outputs by default. Added the -q/--quiet flag to get the old behavior.

### Fixed

- SDK: Fixed the player re-deletion feature.
- SDK: Fixed the racy build failures when building the solution files with `dotnet build`. Previously, the build could fail with `error CS2012: Cannot open ... for writing`.
- SDK: `model.CurrentTime` now has a valid value (instead of epoch) already during model schema migrations. This applies to players, guilds, and multiplayer entities such as divisions. This also fixes the `PlayerEventModelSchemaMigrated` and `GuildEventModelSchemaMigrated` analytics events having an invalid timestamp and thus being rejected from BigQuery.
- SDK: Loss of websocket socket without clean shutdown is no longer logged on error level.
- SDK: Session resume failures after restoring application from background is more robust and interprets this more reliably as a SessionLostInBackground error.
- SDK: The fixed-point `ToString()` methods now produce deterministic string outputs that can be parsed to the original value losslessly. The output is also the shortest such string for better human readability.
- SDK: The fixed-point `Parse()` methods now handle rounding properly to achieve lossless fixed->string->fixed conversions.
- SDK: Fix handling of Akka.NET log message parameters.
- SDK: The leagues system season migration no longer drops participants from season migration in case of a crash.
- SDK: Fixed crash in Android client builds on Android 6. The SDK code was using an API added in API level 24 which is only available in Android 7 and above.
- SDK: Fixed iOS build failing if `Symlink Sources` build setting was enabled.
- SDK: File reading via `FileUtil` methods now allows the file to be open in other applications. This is done by opening the file with the `FileAccess.Read` and `FileShare.ReadWrite` flags.
- SDK: Fixed async matchmaker player deserialization and added better error reporting.
- SDK: `ConfigParser` can now parse all `StringId<>`, `enum`, and `Nullable<>` types (assuming the `Nullable<>`'s underlying type is parseable), regardless of whether those types appear as key types in `GameConfigLibrary<,>`s.
- SDK: Fix network thread hang detection (thread pool starvation) to also detect hangs in early login.
- Unity Editor: Fixed a rare deadlock of Unity Editor that could happen when 1) Game was in Play-mode, 2) Editor was being put into background, 3) and client was at a very precise moment in processing a network message.
- Unity Editor: Fixed Model Inspector leaving an empty child proxy to collections whose last member was removed.
- Dashboard: Entity Import and Export error messages now display the whole error message.
- Dashboard: Improved sluggish segment selector in audience targeting components when there are a large number of segments.
- Dashboard: Improved explictness of boolean checks on all dashboard alerts enhancing readability and maintainability of the code.
- Dashboard: Fixed issue where events without descriptions would cause the event log cards (eg: the "Latest Player Events" card) to error out.
- Dashboard: Fixed issue where `fetchSubscriptionDataOnceOnly` could hang forever if the requested endpoint returned no data.
- Dashboard: Fixed error handling on the player overwrite form. An error message is now shown when an overwrite request fails.
- Dashboard: Fixed links to logs displaying a Deprecation Notice in Grafana.
- Dashboard: Entity archive import and player import now display error messages more clearly and use clearer language on the changes applied or to be applied.
- Dashboard: Fixed an issue in the broadcast and notification time picker that would sometimes give unexpected results when changing the start date into the future.

## Release 24.4 (2023-10-11)

### Fixed

- SDK: Fixed crash in Android client builds on Android 6. The SDK code was using an API added in API level 24 which is only available in Android 7 and above.
- Dashboard: Fixed the broadcast start date picker. It did not react to user input and always remained at the current date.

## Release 24.3 (2023-09-20)

### Changed

- Samples: Disabled push notifications and geolocation features from Idler sample to remove dependencies on external services.

## Release 24.2 (2023-09-14)

### Fixed

- Dashboard: The "upload from file" functionality in the player overwrite form has been fixed. R24 introduced a regression where the overwrite would fail when the data had been input via the file upload instead of pasting into the text box.
- Dashboard: Fixed searching in `MetaInputSelect` components. R24 introduced a regression where searching would sometimes not work.

## Release 24.1 (2023-08-30)

### Changed

- Installer: The installer now uses a pre-generated template for importing files into the target project instead of requiring the HelloWorld sample to be present.

### Fixed

- Installer: The backend solution file generated now has correct paths to MetaplaySDK projects.

## Release 24 (2023-08-25)

### Breaking

- All `IBlobStorage.PutAsync` methods now throw on failure rather than returning `false`.
  - **Action Required**: All usages of `PutAsync` should handle the potential exception by catching rather than checking the return value.
  - **Action Required**: All ignores such as `_ = await Foo.PutAsync(..)` should be turned into `try { await Foo.PutAsync(..) } catch {}`.
- The deprecated logging to file (`LoggingOptions:FileLogPath` and friends) has been removed.
  - **Action Required**: If you are using the feature, you need to migrate to use the primary logging channel.
- The entities persisted in the database are now compressed by default.
  - **Action Recommended**: If you have your own custom entity types, we recommend using the `SerializeToPersistedPayload()` and `DeserializePersistedPayload()` for serializing the entity payloads for database. This enables the compression for the custom entities as well. Note that `SerializeToPersistedPayload()` calls `ValidatePersistedState()` automatically so any explicit calls can be removed.
- Server: `DatabaseScanJobManager` OnJob* methods are now async
  - **Action Required**: Update method definitions to async syntax.
  - **Action Required**: return `Task.CompletedTask` where necessary.
- Client credential management is now async and `CredentialsStore.TryGetCredentials` has been removed.
  - **Action Required**: If you are using `TryGetCredentials` to synchronously get the current PlayerId, you should use `MetaplaySDK.PlayerId` instead.
- `ExecuteInBackground` has been removed and replaced with `ContinueTaskOnActorContext`.
  - **Action Required**: If you have `ExecuteInBackground(() => Task.Run(...))`, you should replace it with `ContinueTaskOnActorContext(Task.Run(...))`. This executes the task on thread pool.
  - **Action Required**: If you have `ExecuteInBackground(() => DoSomethingAsync(...))`, you should replace it with `ContinueTaskOnActorContext(DoSomethingAsync(...))`. This executes the task on actor background.
- Dashboard: `MetaInputSelect` has been refactored to support generics. This makes it easier to use and type-safe.
  - **Action Required**: If you are using `MetaInputSelect`, update all callsites to include a type hint for the `value` and `options` props. This may also reveal some bugs in your code that were previously hidden by the lack of type safety.
- Dashboard: ESLint configs updated to the latest standard TS Vue versions used by the community.
  - **Action Required**: You may see new errors in your code that were previously not raised by the old ESLint config. Please review and address them as needed.
- Dashboard: Simplified the ui placements integration api to use a union type instead of enum for the placements. This makes all the call sites easier to read and use.
  - **Action Required**: Update your calls to `addUiPlacement` to use a plain string instead of the enum. TypeScript will guarantee that the string is one of the valid placements.
- Removed the deprecated `AuthenticationType.Auth0`.
  - **Action Required**: Replace all usage of `AuthenticationType.Auth0` with the more general `AuthenticationType.JWT`.
- `Util.SanitizeString` has been removed.
  - **Action Required**: Replace all calls to `SanitizeString` with a custom sanitization approach that is suitable for the particular type of sensitive data. Mapping all strings to "xxx" is recommended.
- `MetaInputDateTime` has been removed in favour of `MInputDateTime` from the MetaUiNext package.
  - **Action Required**: Replace all usages of `MetaInputDateTime` with `MInputDateTime`.
- Game configs have been refactored to allow in-memory instances of config items to be shared across multiple experiment specializations of a config. This changes some interfaces and introduces restrictions to how config objects can be mutated.
  - **Action Required**: It is now forbidden in many circumstances to mutate config library items in `SharedGameConfig.OnLoaded()` (and same in `ServerGameConfig`). This is because a config item might be shared with other `SharedGameConfig` instances which are different experiment specializations of the same config. A config should not be allowed to mutate items used by another config.
    - There is a debug check which runs at config build time and produces an error if `OnLoaded` was detected to modify the contents of another `SharedGameConfig`. Note that the debug check is best-effort and can only detect misbehavior that is actually triggered by the existing config contents.
    - `OnLoaded` is often used to compute derived data from the config and augment the runtime config item instances with said data, such that the derived data can be more efficently or conveniently used at runtime.
    - Going forward, the recommended way is to store the derived data outside the config items, such as in separate lookups (which aren't shared across `SharedGameConfig` instances) stored at the top-level of `SharedGameConfig`. This refactoring may involve passing around the `SharedGameConfig` instance in game code, where previously just an individual config item instance was sufficient.
    - Note that the `IGameConfigPostLoad` mechanism is still supported and is allowed to mutate the config item. It can be used for computing (and assigning into the config item) derived data which depends only on the config item and items that are reachable via from it via `MetaRef`s.
  - **Action Required**: If you override the `Import(GameConfigImporter)` method in `SharedGameConfig` (or `ServerGameConfig`), update the method name to `PopulateConfigEntries`.
  - **Action Required**: If you use `GameConfigFactory.Instance.ImportSharedGameConfig(PatchedConfigArchive.WithNoPatches(archive))` (or `ImportServerGameConfig`) for loading a game config from an archive, change that to `GameConfigUtil.ImportSharedConfig(archive)` (or `ImportServerConfig`). The `GameConfigFactory` API has been changed and `PatchedConfigArchive` has been removed.
  - **Action Required**: If you use the `GameConfigLibrary.Infos` property to access the library as a `IReadOnlyDictionary` or to loop through its key-value entries, remove the `.Infos` property access and instead use the library object directly. The `Infos` property has been removed and `GameConfigLibrary` itself now implements `IReadOnlyDictionary`.
  - **Action Required**: If you refer by name to the concrete type of the `GameConfigLibrary.Values` property, you must now use `GameConfigLibrary.ValuesEnumerable` instead of `OrderedDictionary.ValueCollection`. Similarly, for `GameConfigLibrary.Keys`, use `GameConfigLibrary.KeysEnumerable` instead of `OrderedDictionary.KeyCollection`.
- Added API to collect messages during config build.
  - **Action Required**: `GameConfig.BuildTimeValidate` now takes a `GameConfigValidationResult` parameter.
  - **Action Required**: `IGameConfigBuilder.Assign*` methods now optionally requires a `GameConfigBuildDebugInfo` parameter, this should be set to provide debug information to the config validation reporting system.
  - **Action Required**: `IGameConfigSourceItem` classes now need to define a `TGameConfigKey` generic parameter that is the same as the ConfigKey type of `IGameConfigData`.
- Game config variant overrides no longer supports merging column and row overrides into a single row.
  - **Action Required**: Game Config libraries that use value types as config keys need to be updated to use the new patch by Id feature.
  - This means that you have to fill in the columns used to create the config key for the variant rows in the offending sheets.
- `PlayerSessionParamsBase` now takes `SessionProtocol.ISessionStartRequestGamePayload` in its constructor.
  - **Action Required**: If params class is modified, pass in the payload in `GameCreatePlayerSessionParams` from `sessionStart.Meta.SessionGamePayload`.
- `Metaplay.Core.Message.SessionProtocol.ISessionStartRequestGamePayload` is now `Metaplay.Core.Message.ISessionStartRequestGamePayload`.
  - **Action Required**: Remove `SessionProtocol.` prefix from all usages.
- The league system no longer exposes player ids to the client. The new identifier for a participant is `ParticipantIndex`.
  - **Action Required**: Change any equality checks of a participant with the current player by comparing the `ParticipantIndex` to the value returned by `DivisionClientContext.GetParticipantIndex()` or the `IDivisionClientState.CurrentDivisionParticipantIdx` in the player model.
- `MultiplayerEntityClientContext` constructor now takes in a `MetaplayClientStore` parameter.
  - **Action Required** Change any usages of `MultiplayerEntityClientContext` to include the client store parameter.

### Added

- SDK: The release package now contains the .version file.
- SDK: Current PlayerID (if known) is now available in `MetaplaySDK.PlayerId`.
- SDK: On Android devices with Google Mobile Services installed, the used game account is restored when application is uninstalled and reinstalled.
- SDK: On iOS devices, the used game account is restored when application is uninstalled and reinstalled.
- SDK: In client Firebase Analytics, nullable variants of the supported scalar types are now supported by omitting the field if the value is null.
- SDK: Allow customizing server analytics sinks via overrideable `AnalyticsDispatcherSinkFactory`.
- SDK: Added support to patch config entries by Id.
- SDK: Added `SpreadsheetParseOptions.IgnoreCollectionElementValue` to allow ignoring collection elements during parsing.
- SDK: Created `GameConfigValidationResult` API to collect build time validation messages, the `ConfigKey` can be provided to the API to automatically generate source links.
- SDK: In entity event logs, event deserialization failures now display the type of the event that failed to deserialize.
- SDK: Added `MaxCollectionSizeAttribute` to allow changing the maximum collection for struct/class members (by default, the maximum size is 16384).
- Server: Entity payloads are now compressed when persisting into database. The Zstandard compression is used by default. Alternatively, LZ4 can be used or compression disabled.
- Server: Added HTTP API endpoint to get all players marked as developers.
- Server: Added GET `gameConfig/{configId}/count` endpoint that returns a list of libraries and top level items.
- Server: Added POST `gameConfig/{configId}/details` endpoint that returns config library contents for the given libraries and experiments.
- Server: Added POST `gameConfig/diff/{baselineConfigId}/{newConfigId}` endpoint that returns the diff between the given configIds and libraries.
- Server: WebGL and UnityEditor platforms can now be included or excluded when maintenance mode is set.
- Server: Separated session resume and failure counts to their own metrics timeseries (`game_session_resumes_total` and `game_session_resume_fails_total`). Resumes are no longer counted in `game_player_logins_total` and `game_player_login_fails_total`.
- Server: Added `SessionProtocol.ISessionStartRequestGamePayload` into `PlayerSessionParamsBase`.
- Server: Added a new Server model concept to divisions, which allows storing server-only data and running hidden logic on the server.
- Dockerfile: Introduce `entrypoint.sh` which provides a cleaner API for invoking the gameserver or the botclient from the image.
- Dockerfile: The nuget packages are now properly cached between the build steps. In local builds, they are also cached between builds via docker cache mounts.
- Dashboard: Added support for Tailwind CSS and a custom plugin to easily manage the configuration to prevent it from colliding with Bootstrap. Use the `tw-` prefix for all Tailwind CSS classes.
- Dashboard: Added Storybook for developing and testing dashboard components in the `Core`, `MetaUi` and `MetaUiNext` packages.
- Dashboard: Added more information to the "Latest Logins" card in the player details page.
- Dashboard: Added a new copy-to-clipboard button to the stack trace section of the incident reports.
- Dashboard: Extracted timeout promise function to a global helper function called sleep.
- Dashboard: Added more consistent API error handling to most dashboard pages via a new `metaApiError` prop in the `MetaPageContainer` component.
- Dashboard: Incident reports now contain client's Git commit id.
- Dashboard: Incident reports now show a warning when the deletion date is in less than 3 days.
- Dashboard: Player details error page now allows exporting the player model for debugging when serialization or migrations of the model fail.

### Changed

- Server: Added combination data to the `experiments` endpoint.
- Server: Upgraded Akka.NET to v1.5.7, Parquet.Net to v4.12.0.
- Server: Option `AppleStore:AcceptSandboxPurchases` is now `true` by default, making all environments accept valid Apple sandbox purchases. This is the behavior recommended by Apple, and is required for Apple's reviewers to be able to test IAPs in production environments.
- SDK: The `integration-tests.py` has been rewritten and is now simpler and more robust.
- SDK: HTTP server authentication domains now register their own 404 (NotFound) handlers so that the 404 handlers set correct CORS headers.
- SDK: PrettyPrint in Compact mode is more compact for lists by omitting array indices.
- SDK: All server-side projects now use the latest supported C# version instead (i.e., `<LangVersion>Latest</LangVersion>`).
- SDK: Incident reports are now retained up to 14 days in production and staging environments and 30 days in other environments.
- SDK: Added cell information to config build error reporting.
- SDK: Upgrade to IronCompress which fixes the LZ4 compression level handling. Use sensible compression level with each IronCompress algorithm.
- SDK: Nullable structs in ConfigEntries are now null if the cell is empty.
- SDK: PrettyPrint in Compact mode is more compact for lists by omitting array indices.
- SDK: Game config libraries have been rewritten to use a "deduplicating" implementation in which items' in-memory instances may be shared across multiple config specializations. The purpose is to reduce server memory usage when many experiments are active at the same time. However, this may also have other effects on the performance of game config usage, such as more costly config access operations. We strongly recommend keeping an eye on performance metrics and testing the changes in a testing environment before production.
- SDK: Admin API permissions: by default, customer support roles now have permission to view experiments and player incidents.
- Dashboard: Changed the Metaplay logo and icons to the new branding.
- Dashboard: Audit log details page now has a nicer display for game-specific log data.
- Dashboard: Migrated final core dashboard components to the composition API:
  - `PlayerLoginHistoryCard`
  - `AuditLogDetailView`
  - `EnvironmentView`
  - `DatabaseEntityDetailView`
  - `SegmentDetailView`
  - `MetaInputNumber`
  - `MetaTime`
  - `MetaDuration`
  - `OfferGroupsOffersCard`
  - `OfferDetailView`
  - `OfferGroupDetailView`
  - `MetaActivablesBaseCard`
  - `ActivableDetailView`
- Dashboard: Improved loading page layout with better error messages.
- Dashboard: Improved `MetaClipboardCopy` to support unsubtle button with text in it.
- Dashboard: Changed technical text labels to monospace font in `PlayerDeviceHistoryCard` and `PlayerLoginMethodsCard`.
- Dashboard: Active "players" and "guilds" are now hidden from non-developers viewing the `PlayerListView` and `GuildListView` pages.
- Dashboard: Added a "Developers" page in the sidebar. Initially, it has a `MetaListCard` that renders all developer players from a new API endpoint.
- Dashboard: Improved the overall layout, style and typing of the `MetaInputSelect` wrapper component and updated all related components.
- Dashboard: Config Diffs now work if the new value is `0` or `false` instead of not showing or being an empty value.
- Dashboard: Added `MetaApiErrorAlert` to most views and a couple of components for error handling. Updated the destructured data, error etc naming scheme in those files to match the subscription naming.
- Dashboard: Improved error reporting during the initialization of Cypress test runs.
- Dashboard: Added generics support for `MetaInputSelect` and updated all callsites to include a type hint for the `value` and `options` props.
- Dashboard: Maintenance mode date time picker now defaults to one hour from the current time.
- Dashboard: Broadcast form now has a smarter duration picker that allows for more human-friendly inputs in both duration and exact time.
- Dashboard: New components in the MetaUiNext package:
  - `MInputDateTime` - a date and time picker that improves on and replaces `MetaInputDateTime`.
  - `MInputText` - a general purpose form input for all text.
  - `MInputNumber` - a form input for numbers.
  - `MInputSwitch` - a checkbox that looks like a switch.
  - `MInputSegmentedSwitch` - a radio button that looks like a switch.
  - `MInputDuration` - an opinionated input for Luxon durations.
  - `MInputDurationOrEndDateTime` - a combo picker for both exact date time or duration inputs but always outputs a duration.
  - `MInputStartDateTimeAndDuration` - a combo picker for both exact date time or duration inputs but always outputs a start date time and a duration. Replaces `MetaInputDateTimeRange`.
  - `MCollapse` - an opinionated and performant component for making collapsible sections.
  - `MCollapseCard` - a variant of `MCard` that has a body that starts off collapsed.
- Dashboard: ESLint configs updated to the latest standard TS Vue versions used by the community.
- Dashboard: Added a new sentence-case format option to the `MetaDuration` and `MetaTime` components.
- Dashboard: Player entities with no active session are no longer added to the recently active players list.
- Dashboard: Changed the font stack to use the default UI system font families for faster loading and better legibility across platforms. This also reduced the need for monospace fonts in most cases and they have been converted to sans-serif.
- Dashboard: Removed automatic uppercasing of headers to make technical labels, such as player IDs, more legible.
- Dashboard: Reduced the use of purple color across the whole UI for a more uniform and calm look.
- Dashboard: Added warnings in experiment details page and confirmation popup if the amount of combinations exceeds a threshold.
- Dashboard: Added a new `MCollapse` component that has significantly better performance under complex conditions than the `BCollapse` component. Migrated most of the existing use-cases to use the new component.
- Dashboard: Simplified the ui placements integration api to use a union type intead of enum for the placements. This makes all the call sites easier to read and use.
- Dockerfile: BotClient images no longer include `StreamingAssets/` folder, which was used to bake initial game configs into the image. BotClients now fetch the game config from CDN on use.
- Dockerfile: Install Cypress dependencies earlier in the Dockerfile to avoid code changes from invalidating the caching of the steps.
- Dockerfile: Add caching of PNPM store and Cypress installation for speeding up running of integration tests. Drop the build stage `test-dashboard` in favor of `build-dashboard`.
- Dashboard: Advance leagues dashboard from MVP to production quality:
  - Improved the overall look and feel and user flow of the league, season and division views.
  - Improved navigation between the league, season and division views.
  - Improved overall UI and UX flow for manually advancing a season.
  - Added ability to manually add/modify a player to a rank or division on the `PlayerDetailView`.
  - Added player audit log events for league actions that affect players.
  - Improved overall audit log events descriptions for the league and division actions.
  - Removed ability to manually advance a single division in the division manager.
- Dashboard: `MetaRawData` now shows the size of object and arrays.
- Dashboard: Updated the `MetaApiErrorAlert` component UI to improve readability of displayed errors.
- Dashboard: Improved error handling of entity exists checks used in Inspect entity dialog. Changed `EntityExistsController.Get` API endpoint and removed `EntityExistsController.Head` endpoint.
- Samples: All samples have been upgraded to Unity 2022.3.7f1 (the latest LTS).
- Idler sample: Switch to using Unity logging directly instead of the legacy `ClientLog`.

### Fixed

- SDK: PrettyPrint for `IEnumerable`s now produces sensible results and now longer produces recursion warnings.
- SDK: Fix a case where non-compiling serializer code was generated for certain patterns involving `MetaRef`s and "plain" (non-`MetaRef`) config references.
- SDK: Fix API returning wrong HTTP response code when trying to access endpoint when user doesn't have permission. Now returning 403 as intended.
- SDK: Fixed WebSocket close sequence to allow all preceding packets to be sent out before socket is closed. This fixes communicating connection error codes to client.
- SDK: Fixed unobserved exceptions being thrown when a network diagnostics report is cancelled due to timing out.
- SDK: Fix a memory leak in the WebApiBridge on the client: the buffer for the return value of a synchronous JavaScript->C# call was not released.
- SDK: Fix issues in websocket code causing connections to be abruptly terminated and incident reports generated.
- Server: Fixed push notification jobs running when push notifications are disabled (this can happen when restoring backups to the dev environment, or when scheduling notifications and subsequently disabling push notifications).
- Server: Running notification campaigns are now cancelled if the server is started with push notifications disabled.
- Server: Admin API game config JSON serialization now includes the values of player segment property requirements. This fixes game config diff display sometimes erroneously not finding differences.
- Server: The HTTP 404 handler for non-existent endpoints now reports errors properly as 404 instead of CORS errors for authentication domains where CORS is enabled.
- Dockerfile: Server runtime image no longer contains dummy sqlite files. These were unintentionally generated as a part of the serializer code generation.
- Dockerfile: Fix the Cypress installation to not happen when not building for tests.
- Dashboard: Fixed game config diff select boxes having an unexpectedly small click area.
- Dashboard: Fixed `MetaListCard` not showing a missing permissions message when the user doesn't have permission to view the list.
- Dashboard: Resolved the error on the guild search form. You no longer need to select a `search type` as it is automatically handled by the generated form system.
- Unity: Fixed Model Inspector's handling of recursive object references.
- Unity: Fixed Model Inspector's handling of `HashSet<T>`.
- Unity: Fixed Model Inspector's handling of fixed point vector types (`F32Vec2`, `F32Vec3`, `F64Vec2`, `F64Vec3`).
- Unity: Fixed Model Inspector sometimes not updating children of an object (when the value changed to null).
- WebGL: Fix app hang if application tried to Close the connection while flushing any pending messages.

## Release 23 (2023-05-26)

### Breaking

- Removed the deprecated `tests.py` integration tests.
  - **Action Required**: Use the new better dockerized `integration-tests.py` instead.
- Upgraded Metaplay to use .NET 7.
  - **Action Required**: Upgrade all your C# projects to use `<TargetFramework>net7.0</TargetFramework>`.
  - **Action Required**: All build systems, CI pipelines, etc. must be upgraded to .NET 7 as well.
- Renamed `GameLogicPath` to `SharedCodePath` in C# projects and `Directory.Build.props` files.
  - **Action Required**: Replace all usage of `GameLogicPath` in your .csproj files with `SharedCodePath`.
- `SegmentedIOBuffer` no longer has a contructor taking in the `MemoryAllocator`. It will now use the Memory Pool if available.
  - **Action Required**: Remove allocator argument from `new SegmentedIOBuffer(...)` calls.
- Players will no longer join the player leagues automatically on startup. This is now done by calling `TryJoinPlayerLeagues` in the player actor.
  - **Action Required**: If using leagues, add a call to `TryJoinPlayerLeagues` in the game's `PlayerActor`.
- Removed the `PlayerActorBase._currentAssociatedPlayerDivision` property.
  - **Action Required**: Use the `CurrentDivision` property stored in `Model.PlayerSubClientStates[ClientSlotCore.PlayerDivision]` instead.
- Renamed `UInt128` to `MetaUInt128` to avoid confusion with `System.UInt128` (introduced in .NET 7).
  - **Action Required**: Replace any usage of `UInt128` with `MetaUInt128`.
- `FlatIOBuffer` no longer has a contructor taking in the `MemoryAllocator`. It will now use the Memory Pool if available.
  - **Action Required**: Remove allocator argument from `new FlatIOBuffer(...)` calls.
- `LoginProtocolVersion` is no longer configurable
  - **Action Required**': Replace `MetaplayCore.Options.LoginProtocolVersion` with `MetaplayCore.LoginProtocolVersion`
  - **Action Required**': Remove `loginProtocolVersion` from `new MetaplayCoreOptions` constructors.
- The server option `InAppProductionMode` in section `AppleStore` has been renamed to `AcceptProductionPurchases`.
  - **Action Required**: If any of your `Options.*.yaml` files specify this option, rename it there.
- Dashboard: The subscription module's interfaces have changed. The built-in subscriptions were moved into the core module.
  - **Action Required**: Update any custom subscriptions to use the new interfaces as well as any custom dashboard components that call the subscription API.
- Synchronous `FileUtil` methods are removed from WebGL builds.
  - **Action Required**: If WebGL build fails due to a call to such a method, the call site should be inspected. If the file access is needed in WebGL, async API should be used. If file access is not needed, the call should be conditionally disabled in WebGL.
- Logging has been simplified to use `IMetaLogger` and the compile-time defines names are now `METAPLAY_` prefixed.
  - **Action Required**: If using the old `COMPILE_LOG_LEVEL_XYZ`, switch to use the equivalent `METAPLAY_LOG_LEVEL_XYZ` instead. Note that `INFO` is now `INFORMATION`.
  - **Action Required**: Replace any use of `LogLevel.Info` with `LogLevel.Information`.
- The `Dockerfile.botclient` has been removed as `Dockerfile.server` now includes the BotClient.
  - **Action Required**: Update the `metaplay-loadtest` Helm chart to v0.3.0 or later.

### Added

- SDK: Added the ability to override a league's normal recurring scheduling with a custom schedule.
- SDK: Added member `InAppPurchaseEvent.PaymentType`, which can be used to check if the purchase was a normal real-money purchase or a sandbox (test) purchase. This is available after the purchase has been validated. For Google Play, this involves an additional request to the Android Publisher API, and thus requires that a service account for accessing the Android Publisher API has been configured on the server (otherwise `PaymentType` will remain null).
- SDK: Added `CsvReader.ReadStruct()` variant that parses struct members from key-value string stream.
- SDK: Added helper method `CsvEntryBuilder` for reducing boilerplate when dealing with CSV config files in the `GetEntryBuilder()` method in a custom config builder.
- Unity: Add custom build preprocessor which allows specifying custom scripting #defines via a `METAPLAY_UNITY_DEFINES` file at the project root. Useful for forcing specific environment in CI jobs. Only supported in Unity 2021.2 and newer.
- Unity: `EditorTask` utility for launching long running tasks in Unity editor, used by game config building in the samples.
- Unity: Added 'Copy value to clipboard' functionality to the Model Inspector.
- Server: Add support for reading Runtime Options as base64-encoded yaml from an Environment variable. Such sources are declared with `env-yaml-base64:VARIABLE_NAME` path.
- Dashboard: Added audit log events for leagues and divisions.
- Dashboard: Added the ability to manually advance a season in the league manager.

### Changed

- Dockerfile: Retain the relative project structure within the docker builds to loosen the requirements for how the SDK and project can be placed relative to each other.
- Dockerfile: Simplify the C# builds by removing the separate `*.csproj` copying and `dotnet restore`.
- Server: Remove the #defines for `TRACE` and `DEBUG` from the C# project files. These are automatically defined by the build tools.
- Server: Renamed `InternalPlayerDivisionJoinRequest` to `InternalPlayerDivisionJoinOrUpdateAvatarRequest` to better reflect the intended usage.
- Server: CORS policies can now be assigned for each authentication domain using the `[EnableCors()]` header on the root controller.
- Server: On server startup, the critical CDN resources of the currently active GameConfig and Localizations are reuploaded if they are missing. This allows moving server and its state to another environment without needing to also manually bring in CDN state.
- Server: More aggressive sanitization of user supplied paths in log messages.
- Sample projects: Use `rollForward = latestFeature` in `global.json` files so that the latest feature-band gets used (eg, 7.0.2xx).
- HelloWorld: Put all the code under `Metaplay.Sample` namespace to avoid conflicts with userland code and to better communicate that the code can be adapted or removed.
- SDK: Minimum required Unity version is now 2020.3.
- SDK: `DebugLog` in Unity now logs with the correct log level, when possible, according to the logging method used. Unity does not have Verbose and Debug log levels, so for those the basic log level (same as Info) is used.
- SDK: Added server option `AppleStore:AcceptSandboxPurchases` which controls whether App Store sandbox IAPs are accepted; it is no longer controller by `InAppProductionMode` (now called `AcceptProductionPurchases`) being `false`. `AcceptSandboxPurchases` defaults to `false` in production and to `true` elsewhere, which retains previously-existing default behavior. You may choose to set it to `true` in all environments if you wish to always accept valid App Store sandbox IAPs in order to allow TestFlight users to perform sandbox purchases without needing to mark them as developers in the dashboard.
- SDK: Localizations are now stored compressed in CDN and client cache.
- SDK: Synchronous FileUtil methods are removed from WebGL builds.
- SDK: Logging simplification and optimizations:
  - All logging (client and server) should now use the `IMetaLogger` interface.
  - The server no longer uses message passing for log events, making them faster and reduce memory consumption, and arrive more reliably in the debugger output.
  - Logging now avoids `object[]` allocations and boxing for formatted strings with up to 6 arguments when the used log level is not enabled.
  - Prefer `Information` over `Info` to align with Microsoft's naming conventions.
  - Remove various old adapter classes. Gets replaced by `IMetaLogger`.
  - `MetaReceiveActor` now has a `_log` member to reduce code duplication.
  - Client no longer has a separate `PreInitLogger` as `UnityLogger` now works before log levels have been initialized, making it more robust against threading race conditions.
  - The compile-time `METAPLAY_LOG_LEVEL_<level>` switches now work the same on the client and the server, and across all the `IMetaLogger` based logging methods.
- SDK: Communicate entity import processing errors appropriately.
- SDK: `GameConfigHelper.ParseCsvToSpreadsheet` now removes trailing empty cells from each row. In particular, rows with nothing but empty cells will produce output rows with 0 cells. This behavior is more in line with Google Sheet fetching.
- SDK: Notification content changed to a type that uses `LocalizedString` to align with mails and broadcasts, allowing the use of `MetaGeneratedForm`. Migrates existing NotificationCampaigns in `DatabaseScanCoordinatorState`.
- Unity: `TimelineHistory` now only tracks the Model tick/action history when the Timeline Debugger window is active. Optimize the timeline's memory allocations: it no longer allocates when not active and re-uses memory when it is active.
- Unity: Optimize domain reload speed in Unity Editor.
- Dashboard: The subscription system has been re-written in Vue 3 primitives for significantly improved performance when handling large data structures. Use the new `useStaticSubscription` and `useDynamicSubscription` composables to subscribe to data.
- Dashboard: Migrated multiple components from the `Options API` SFC based syntax to the `Composition API` Setup based syntax.
- Dashboard: The `NotificationForm` and `BroadcastForm` components have been replaced by the `NotificationFormButton` and the `BroadcastFormButton` respectively to take advantage of our custom `MetaActionModalButton` modal component for more consistent modal behaviour. The files and related components have also been rewritten to use the `Composition Api` setup syntax.
- Dashboard: Notification forms and views now use `MetaGeneratedForm` to display localized content.

### Fixed

- SDK: `WebConcurrentDictionary` now matches `System.ConcurrentDictionary` behaviour in the case of dictionary being modified during value creation in `GetOrAdd()`.
- SDK: The client-side WebGL pitfall code analyzer no longer warns about the usage of `ContinueWith` when using an explicit `TaskScheduler` argument.
- SDK: Dynamic creation of `StringId` interned values for types that are not tagged `MetaSerializable` now works correctly.
- SDK: Fix errors from background thread logging after the SDK has been deinitialized on the client, e.g., from the incident report uploader.
- SDK: Fixed `PlayerModel.DeviceHistory[].LoginMethods` potentially containing `null` login method.
- Unity: Fixed Model Inspector footer overlapping and hiding rows of the tree view.
- Unity: Fixed Model Inspector search results not respecting active filters.
- Unity: Model Inspector now shows the name of `enum` members instead of their numeric value.
- Server: Creating new EFCore migrations now longer prints `IHost` errors and warnings.
- Server: Using `InternalPlayerDivisionJoinOrUpdateAvatarRequest` to update the division avatar of an existing player after a division has concluded no longer results in an `InvalidEntityAsk` error.
- Server: The local development `VueCorsPolicy` now only applies to the AdminApi endpoints instead of all the HTTP endpoints.
- Server: Removed potential LOH allocations in `HttpUtil` when Http request returned non-200 response with a long error message.
- Server: Fixed the `/gracefulShutdown` to expect a HTTP POST request instead of GET.
- Server: If server is no longer compatible with the current game config, it will fall back to the config it the server was built with instead of failing to start the server.
- Server: `FileUtil.DeleteAsync()` now works also on server builds.
- Dockerfile: Fix skipping of Cypress installation in non-test builds.
- Dashboard: Significantly reduced browser memory pressure on pages with a lot of components and large data structures, such as the player details page.
- Dashboard: Fixed search function for user permissions on the `My Profile` page.
- Dashboard: Links to Grafana logs no longer auto-inject the JSON decoding as part of the log processing as logs are generally plain-text.
- Dashboard: Fixed and improved searching and filtering on `Analytics Events` list page.
- Dashboard: Fixed sidebar entries not always highlighting the active route correctly.
- Dashboard: Fixed segment targeting in the `MessageAudienceForm` where the segments were listed incorrectly when editing or duplicating an existing broadcast.
- Dashboard: Fixed a bug that would cause the game config contents UI to fail to render when an experiment is selected and that experiment adds new array items.

## Release 22 (2023-03-14)

### Breaking

- SDK: The `MetaplayClient` SDK default integration class is now abstract and integrations should declare a derived class for type-safe access to the current player model.
  - **Action Required**: Introduce a `MetaplayClient` class in the integration that inherits from `MetaplayClientBase<TPlayerModel>`, substituting your concrete `PlayerModel` class as the template parameter. Reusing the name `MetaplayClient` should make the impact on using code minimal. The implementation of the class can be empty, the static `MetaplayClient` API methods are preserved in the base class.
- SDK: `ISharedGameConfig`'s library accessors have been restructured to require less boilerplate, using an `IGameConfigLibrary<,>` interface.
  - **Action Required**: If your project implements `ISharedGameConfig` directly instead of via `SharedGameConfigTemplate` or `SharedGameConfigBase`, update the library accessors to adhere to the new interface.
- SDK: The Unity localizations build is now expected to place a single-archive copy of localizations as `GameConfig/Localizations.mpa`.
  - **Action Required**: Update your `GameConfigBuilder` to build localizations into a single file and use the file when uploading the localizations to the server:

  ```csharp
      const string ServerLocalizationsPath = "Backend/Server/GameConfig/Localizations.mpa";

      public static async Task TryBuildLocalizations()
      {
          // Build Localizations & write each language in its own file
          await BuildArchiveAsync(ServerLocalizationsPath,
              ArchiveStorageFormat.SingleFile,
              () => BuildLocalizationsArchiveAsync(MetaTime.Now),
              onSuccessHandler: fullArchive =>
              {
                  // Export Localizations into StreamingAssets/ in FolderEncoding format
                  Debug.Log($"Writing Localizations archive as multiple files into {ClientLocalizationsPath}");
                  ConfigArchive.FolderEncoding.WriteToDirectory(fullArchive, ClientLocalizationsPath);
              });
      }

      public static async Task PublishLocalizationsToLocal()
      {
          ConfigArchive localizationsArchive = await ConfigArchive.FromFileAsync(ServerLocalizationsPath);
          await GameConfigBuildUtil.PublishLocalizationArchiveToServerAsync("http://localhost:5550/api/", localizationsArchive, authorizationToken: null, confirmDialog: false);
      }
  ```

- SDK: As the localizations archive is now longer sourced from within the Unity assets folder the server docker build no longer includes the `Assets/StreamingAssets/` directory.
  - **Action Required**: Make sure that you don't rely on any other data being copied to the server build from the directory! Or if you do, modify `Dockerfile.server` to copy them explicitly.
- SDK: `GameConfigBuildUtil.PublishArchiveToServerAsync()` has been replaced with `PublishGameConfigArchiveToServerAsync()` and `PublishLocalizationArchiveToServerAsync()`.
  - **Action Required**: Replace any usage of the old method with the new ones, depending on which type of archive is being published.
- SDK: `SocialAuthenticationClaimGooglePlay` is now `SocialAuthenticationClaimGooglePlayV1` to clearly communicate it is the legacy version.
  - **Action Required**: Rename any usage of the old type with the new.
- SDK: `playerModel.HasMetaOffersToRefresh()` has been replaced with `playerModel.GetMetaOfferGroupsRefreshInfo()` which returns a list of refreshable offer groups. This is used to reduce workload on the server by reducing the number of offer groups it needs to consider when refreshing.
  - **Action Required**: Instead of calling `HasMetaOffersToRefresh()`, you should now use `GetMetaOfferGroupsRefreshInfo()` and call the `HasAny()` method on the result, and also pass the result to the `PlayerRefreshMetaOffers` action.
- SDK: To enable WebGL support in the client, it is now required to define the preprocessing symbol `METAPLAY_ENABLE_WEBGL`. This also enables warnings about features which are poorly-supported in Unity WebGL builds. See the documentation page **Metaplay & WebGL** for details.
  - **Action Required**: If you make WebGL builds of your project, add a file called `csc.rsp` in your `Assets` folder, with a line saying `-define:METAPLAY_ENABLE_WEBGL`.
- SDK: `DatabaseScanJobSpec`'s API has been changed: added method `ComputeAggregateStatistics` which combines multiple workers' statistics into one, and changed method `CreateSummary` to take only one statistics object instead of multiple.
  - **Action Required**: If you define custom database scan jobs, update their `DatabaseScanJobSpec`s to adhere to the new API. Note that you likely already have the necessary code for `ComputeAggregateStatistics` in your existing `CreateSummary` method, so this is likely a matter or minor refactoring.
- Dashboard: Major dependency updates with each their own braking changes: Vue 2 -> 3 (compatibility mode), Vue Router 3 -> 4, Vite 3 -> 4, Cypress 11 -> 12. This is a complex update but most of the changes are in the core SDK. We will work with the affected customers to migrate their code as needed.
  - **Action Required:** You may need to delete your `node_modules` folder and reinstall dependencies with `npm ci`. NPM can get confused with major updates like this.
- Dashboard: Vue 3 compatibility mode has been enabled. This means that Vue 2 and Vue 3 APIs can be used interchangeably, but there are some breaking changes and warnings about deprecated APIs.
  - **Action Required:** Update your custom dashboard components to use the new Vue 3 APIs where possible. See the [Vue 3 migration guide](https://v3-migration.vuejs.org/) for more information.
- Dashboard: Vue 3 has a new component registration API that is incompatible with Vue 2.
  - **Action Required:** Update your custom dashboard components to use `app.component()` instead of `Vue.component()`.
- Dashboard: Vue 3 has a new `v-model` syntax that is incompatible with Vue 2. The current compatibility mode allows both syntaxes to be used, but this will be removed in a future release.
  - **Action Required:** Form inputs using the `v-model` property and an input event like `@input` or `@update` at the same time may have a race condition where the input event is fired before the `v-model` is updated. Update your custom dashboard components to use the input event's value instead of the `v-model` value to guarantee the updated value is used.
- Dashboard: Vue 3 has changed the execution order of `v-for` and `v-if` directives. The official recommendation is to not use `v-if` and `v-for` on the same element as the order is ambiguous to the reader.
  - **Action Required:** Update your custom dashboard components to not use `v-if` and `v-for` on the same element and instead use `v-if` on a child element to make the order explicit.
- Dashboard: Vue Router v4 now has built-in support for accessing the current `route` and `router` instances via the `useRoute()` and `useRouter()` composition APIs. Our previous workaround for this has been removed.
  - **Action Required:** Migrate all imports of `useRoute()` and `useRouter()` to import from `vue-router`.
- Dashboard: Vue Router v4 has changed the route props API. Props can now be `string[]` in addition to `string`.
  - **Action Required:** Wrap all route props with the new `routeParamToSingleValue()` utility function from the core dashboard module to continue with the old behavior.
- Dashboard: Vue Router v4 has changed how routes are added to the router. The `addNavigationEntry()` integration API has been updated to work with the new router by moving the content of `route.meta` parameter into it's own top-level parameter in the `addNavigationEntry()` function.
  - **Action Required:** Update all calls to `addNavigationEntry()` to use the new, separate route metadata parameter.
- Dashboard: The `displayName` field of `GameSpecificReward` was removed because it was obsolete.
  - **Action Required:** Update any calls to `addPlayerRewards()` to remove the `displayName` field.
- Dashboard: Cypress 12 has enabled isolated testing by default. This means that each `it()` command runs in a separate, clean browser session. This is a breaking change for tests that rely on the old behavior.
  - **Action Required:** Update you existing tests to use the old behavior by adding `{ testIsolation: false }` to incompatible tests or update the tests to run in one `it()` command instead of multiple, as needed.
- Dashboard: `Meta-Teleport` component has been removed in favour of a new type-safe API to dynamically update the headerbar title.
  - **Action Required:** Use the new `useHeaderbar()` composition API of the core dashboard module to update the title.
- Dashboard: The unit tests are now run explicitly by the Dockerfile.
  - **Action Required:** Remove the `npm run test:unit` from your `Backend/Dashboard/package.json` from the `build` command.

### Added

- SDK: Added virtual `PlayerSegmentInfoBase.MatchesPlayer` for the ability to customize the player segment matching logic in the integration.
- SDK: Support in-memory SQLite databases with the `--Database:SqliteInMemory=true` option. Useful for benchmarking locally with BotClient.
- SDK: Added `MetaOfferGroupInfoBase.MaxOffersActive` that can be used to limit the number of simultaneously active offers in an offer group.
- SDK: Erroneous activable states in player model (states that no longer have an associated config item) are now automatically purged by the server after a configurable delay from the latest game config publish.
- SDK: New `AuthenticationConflictAutoResolver` integration allows customizing social login conflict auto-resolution rules.
- SDK: Added ranks support to leagues. The league manager can now handle promotions and demotions between seasons.
- SDK: `MetaTask.Run()` allows executing tasks on a chosen Scheduler without the pitfalls of `Task.Factory.StartNew()` (no need to call `Unwrap()`).
- SDK: `MetaTask.UnityMainScheduler` is a convenience getter of the Unity Main thread scheduler. With above `MetaTask.Run()`, posting work on the correct thread is easy.
- SDK: `MetaTask.BackgroundScheduler` is a convenience getter of the best available background scheduler. On WebGL this is the main thread.
- SDK: `Task.GetCompletedResult()` is a convenience helper for `Task.Result` which throws (instead of blocking) if `Result` is not yet available. Convenient for avoiding accidental deadlocks especially on WebGL.
- SDK: Player incident reports now produce `PlayerEventIncidentRecorded` analytics events.
- SDK: Added a client code analyzer for WebGL projects, warning about usage of features which are known to be poorly supported in Unity's WebGL builds.
- SDK: UDP Passthrough allows hosting custom UDP servers on embedded within the game server.
- SDK: New docker-based integration tests in `Scripts/integration-tests.py`. The old `Scripts/tests.py` is now deprecated and will be removed in the future.
- SDK: Support specifying custom userland `Backend/` directory to `Dockerfile.server` via `--build-arg BACKEND_DIR=xyz` to avoid needing to modify the Dockerfile itself when using a custom directory.
- SDK: Add ARM64 platform support to server project files.
- Dashboard: Added ability to pause updates of MetaListCards.
- Dashboard: Added pausing to Incident Reports so that they can be examined more easily.
- Dashboard: Added a toggle to Incident Reports for enabling or disabling the rich text styling of the Unity client logs.
- Dashboard: Added ability to pass props directly into components that are created by UiPlacements.
- Dashboard: Added new component entry points to the `OverviewView`, `GameConfigListView`, `GameConfigDetailsView` and `GameConfigDiffView` pages. You can now add or remove injected components to these views.
- Dashboard: Improved leagues support and added the ability to see the promotion and demotion statistics for historical seasons.
- Dashboard: The following components have been added as a part of migrating to Vue 3:
  - `MetaInputSelect` - A dropdown component that supports searching, async options loading, auto-completion, multiple selection, and more.
  - `MetaInputPlayerSelect` - A dropdown component for searching and selecting a player.
  - `MetaInputGuildSelect` - A dropdown component for searching and selecting a guild.
  - `MetaInputDateTime` - A date and time picker component.
  - `MetaInputDateTimeRange` - A date and time range (start + end time) picker component.
- Dashboard: Entity refresher and schema migrator jobs now report failed entities in more detail: entity ids, failure timestamps, and error messages.

### Changed

- Sample projects: The `Sample-Server.sln` files have been moved to the project `Backend/` directory in all sample projects.
- Sample projects: The contents of `Assets/SharedCode` is now built into a separate `SharedCode` assembly that does not reference the default assemblies or UnityEngine assemblies.
- Sample projects: Upgrade to Unity 2019.3.19f1.
- SDK: Separated mutable state from `MetaplayClient` into `MetaplayClientState` accessed via the static accessors in `MetaplayClient`.
- SDK: Removed `EmptyGameLogic.cs` previously used for providing minimal integration for client project to build.
- SDK: Use Unity's `AssemblyBuilder` to compile the generated serializer code. On some machines was measured to be about 4x faster.
- SDK: Directory `Temp/Metaplay/` is now used for Unity temporary files instead of `MetaplayTemp/` to avoid clutter in the project root.
- SDK: Prevent generation of runtime serializable type information into the Unity editor version of `Metaplay.Generated.dll` for improved performance.
- SDK: Remove `curl` from the Docker images due to frequent vulnerabilities in the package. Also remove the `curl static.gc.apple.com` test in Dockerfile as it's covered by the unit tests.
- SDK: The runtime option `AdminApi:Type = AuthenticationType.Auth0` is deprecated and will be removed in a future release. Use the more general `AuthenticationType.JWT` instead.
- SDK: Optimized the allocations associated with config specialization in server by introducing `ReadOnlyConfigArchive`.
- SDK: Improve the serialization buffer handling by re-using the memory of individual `SegmentedIOBuffer` segments and then using recycled buffers in the `MetaSerialization` APIs directly. This considerably reduces the number of memory allocations done by the serializers and reduces the amount of memory retained by the various server-side actors.
- SDK: Complete rewrite of the `IOWriter` class for improved performance.
- SDK: Optimized generated serializer code by using modern C# `Span<T>` APIs.
- SDK: Allow supplying GoogleSheet credentials (option `GoogleSheets.CredentialsPath`) as AWS secrets manager URL in addition to local file.
- SDK: Checksum mismatches now produce significantly shorter messages. Full state dumps are no longer done.
- SDK: `PlayerActor.OnClientSessionHandshake()` is deprecated in favor of `OnClientSessionHandshakeAsync()`.
- SDK: `PlayerActor.OnSessionStart()` is deprecated in favor of `OnSessionStartAsync()`.
- SDK: `EntityActor.CastMessage()`, `EntityAskAsync()`, and other messaging functions are now thread-safe also within a single actor and may be used from background threads.
- SDK: `LogicVersionMismatch` errors are no longer reported as player incidents. Logic version mismatches are often expected, especially after a server update.
- SDK: Remove `RecyclingSerializer` and replace all usage with calls to `MetaSerialization` as it now uses recycled buffers internally.
- SDK: Increase DB access timeouts to tolerate load spikes better. This improves behavior when a large number of players disconnect at the same time, such as in the case of enabling maintenance mode.
- SDK: Device identity is now tracked separately from per-device authentication. On client side device identity is represented as `MetaplaySDK.DeviceGuid` which is negotiated at session startup and doesn't depend on the `DeviceId` authentication method. The legacy per-device book keeping in PlayerModel has been repurposed as `DeviceHistory` and additional statistics are tracked per device on login. Existing device identification based on `DeviceId` authentication is migrated to the new scheme and partial stats are migrated from recent login history.
- SDK: Updated the client-side `IAPManager` to support Unity Purchasing 4.6.0 (regarding changes to `IStoreListener.OnInitializeFailed()` and `IAppleExtensions.RestoreTransactions()`).
- SDK: Added preservation for more assemblies in `link.xml`: `Metaplay.Unity` and the game-specific shared code assembly. This should avoid issues caused by Unity's code stripping, and reduce the need for explicit `[Preserve]` attributes in code.
- SDK: Some non-mutating struct methods have been changed to be readonly to improve performance.
- SDK: `Dockerfile.server` now builds the C# game server before building the dashboard. Dashboard build uses Debian (bullseye-slim) -based base image.
- SDK: `Dockerfile.server` now includes BotClient in the built image. In a future release, the BotClient docker images will be replaced with the server image, the ECR repository and `Dockerfile.botclient` deprecated and removed.
- SDK: `Dockerfile.server` now generates the serializer code by starting the server after the build.
- Dashboard: The `CoreUiPlacement` component now forwards all props and listeners to child components. Additionally removed small wrapper components that were used to forward props when using UiPlacements for example for the `PlayerAuditLogCard`.
- Dashboard: Migrated multiple components from the `Options API` SFC based syntax to the `Composition API` Setup based syntax.
- Dashboard: Creating a new notification campaign by duplicating an old one has been moved from the list page to the detail page. Also various other minor changes to make the notification pages behave more consistently with the broadcast pages.

### Fixed

- SDK: Fix the process to properly exit after adding an EFCore database migration by delaying the registration of the `SIGTERM` handler.
- SDK: Fixed OpenId issuer verification used with Facebook limited login.
- SDK: POST, PATCH, and DELETE http request to unknown AdminApi paths are now logged, just like GETs were.
- SDK: Incorrectly declared AdminApi controller methods are now detected early in server startup.
- SDK: Fix `IAPManager`'s behavior when using Unity's development-mode fake IAP store, which would erroneously cause purchases to be displayed as duplicates in the dashboard.
- SDK: Fix rare race condition in client `MetaplayConnection` tear down by handling cancel request before any other operations.
- SDK: Fix large integer values being incorrectly rounded on the dashboard database entity inspector.
- SDK: Network diagnostics now use URLs to existing files to avoid generating 404s on CDN.
- SDK: Player incident reports caused by `PlayerChecksumMismatchConnectionError` are now grouped more appropriately in "top incidents" views. This is done by omitting noisy details from the incident's "fingerprint".
- SDK: AdminApi entity search no longer throws an internal server error when an empty search string is used and the total entity count is very low.
- SDK: AdminApi: When JSON-parsing `MetaTime` from a string with a numeric time offset specifier, the offset is now correctly applied, instead of erroneously applying also the server's local time offset.
- SDK: AdminApi: Endpoints that configure audience targeting now disallow null Ids.
- SDK: Game Config cache directory cleaning now works on WebGL builds.
- SDK: The Model Inspector tool in the Unity editor now automatically switches to an up-to-date Model reference in case the old reference goes stale. For example, when a new session starts, the inspector will now switch to the new `PlayerModel`.
- SDK: Google Play Refunds now make the request using the order id (as expected by Google's API), instead of erroneously using the transaction id.
- Dashboard: Fixed usability issue when displaying extra long text on the `MessageAudienceForm`, `SegmentsCard` and `TargetingCard`.
- Dashboard: The player incident report view now parses and applies the client logs' styling elements explicitly. Previously it parsed them as HTML, which produced incorrect results because Unity's "rich text" styling is not actually HTML.
- Dashboard: Fixed displaying of the `TargetingCard.vue` when a new game config where a target segment has been removed is published.

## Release 21.1+CId_Bugfix1 (2023-01-24)

### Breaking

- SDK: PlayerModel internal representation of AuthenticationMethods has changed.
  - *Action Required*: If your game has custom authentication platforms, you MUST implement `PlayerModel.FixupLegacyPlayerAuthEntry` for them. Implementation may use convenience helper type `PlayerAuthEntryBase.Default` for the entries.

### Fixed

- SDK: Fixed deadlock in localization download in WebGL builds due to missing poll checks.

## Release 21.1 (2023-01-13)

### Breaking

- SDK: A new C# project `MetaplaySDK/Backend/ServerShared/Metaplay.ServerShared.csproj` has been added.
  - **Action Required:** Add a reference to this new project to your solution (`.sln`) files.
- SDK: Simplify `ServiceShardingStrategy` constructor to no longer require the `ClusterConfig` argument.
  - **Action Required:** Remove the argument from any call sites for the method.
- SDK: `MetaplayApiController` has been split to `GameAdminApiController` and `MetaplayAdminApiController`.
  - **Action Required:** Replace all inheritance from `MetaplayApiController` with `GameAdminApiController`.
- SDK: AdminApi permission requirements for controller endpoints are now specified with `[RequirePermission(<permissionName>)]` attribute.
  - **Action Required:** Replace all uses of `[Authorize(<permissionName>)]` with `[RequirePermission(<permissionName>)]` from your controllers.
- SDK: All API endpoints used by the LiveOps Dashboard must now specify the required permissions to avoid accidentally allowing anonymous access of endpoints.
  - **Action Required:** Specify `[RequirePermission(<permissionName>)]` for any endpoints missing them, or use `[AllowAnomymous]` to allow access by anyone who has access to the Dashboard.

### Added

- The built-in web server (`AdminApiActor`) now properly supports multiple authentication domains. These are configured by implementing the `AuthenticationDomainConfig` for each desired domain. These classes can configure the type of authentication required as well as any additional services required.

### Changed

- SDK: The database functionality has been moved to the `Metaplay.ServerShared` project to allow applications other than game servers use the functionality.
- SDK: The dashboard form generation code has been moved to the `Metaplay.ServerShared` project to allow applications other than game servers to use the functionality.
- SDK: The `MetaDatabase` class has been split to `MetaDatabaseBase` (in `Metaplay.ServerShared`) for the application-agnostic functionality and `MetaDatabase` for all game server-related functionality.
- SDK: `MetaplayAdminApiController` has been split from `GameAdminApiController` and has all the application-agnostic functionality.
- SDK: `ServerOptions.WebRootPath` is replaced by `AdminApiOptions.WebRootPath`.
- SDK: `DefaultRole.cs` is now in the `Metaplay.ServerShared` project.
- SDK: `MetaDatabaseBase` and `MetaDbContext` classes are now non-abstract to allow using it as the default implementation.
- SDK: `AdminApiActor` now uses more modern hosting APIs (`CreateWebServerApp()` instead of `Host.CreateDefaultBuilder()`) and enables use of MVC Views.
- SDK: The configuration of the `/api` HTTP endpoints (used by the LiveOps Dashboard) are now configured in `AdminApiAuthenticationConfig` instead of being hard-coded in `AdminApiStartup` (which is global to all types of HTTP endpoints).
- SDK: `AnonymousAuthenticationHandler` replaces old `NoAuthMiddlewareHandler` and implements role assumes by injecting them as claims into the HTTP request context.

## Release 21 (2022-12-16)

### Breaking

- SDK: `MetaplaySDK/CloudCore` is renamed to `MetaplaySDK/Cloud`. SDK C# projects now start with `Metaplay.` prefix.
  - **Action Required:** Project references to `MetaplaySDK/Cloud` must be updated.
- SDK: Guild and other non-Player entity model `FastForwardTime` is removed and replaced with `OnFastForwardTime`, which is called *after* the model clock is already fast forwarded.
  - **Action Required:** Each `FastForwardTime` should be renamed to `OnFastForwardTime` and should no longer modify clock and instead assume clock is already updated.
- SDK: METAPLAY_ENABLE_GUILDS has been removed in favor of the new METAPLAY_DISABLE_GUILDS, by default the SDK now compiles with guilds code included. Remove any traces of METAPLAY_ENABLE_GUILDS in integration code unless there are explicit reasons to disable guilds code compilation in the project.
- SDK: `MetaTime.ToLocalDateTime` has been removed as it is misleading and not safe to use in shared game logic, since its result depends on the machine it is executed on.
  - **Action Required:** Existing usage in game code should be reviewed. Shared code should calculate local times using `playerModel.TimeZoneInfo.CurrentUtcOffset` instead. Legitimate usage, if any, of `MetaTime.ToLocalDateTime` should be addressed either by adding it as a game-specific extension method, or inlining as `metaTime.ToDateTime().ToLocalTime()`.
- SDK: `MetaplaySDK.LocalizationManager` and `IMetaplayLocalizationDelegate` APIs have been simplified:
  - **Action Required:** `LocalizationManager.SetCurrentLanguage` no longer takes in the version. Call-sites should no longer compute versions and pass it in.
  - **Action Required:** `IMetaplayLocalizationDelegate.AutoFlags` should be replaced with `bool AutoActivateLanguageUpdates { get }`. Old flag `DownloadActiveLanguageUpdates` is always enabled, `DownloadSomeVersionOfAllLanguages` is not supported.
  - **Action Required:** Following methods should be removed in `IMetaplayLocalizationDelegate` implementations: `EnqueueSelectLanguageAction`,  `OnLanguagesChanged`, `ValidateLanguage`, `GetAppStartDefaultLanguage`, `SessionContext`, `BuiltinFormat`.
- SDK: `api.system.maintenance_jobs` permission has been renamed to `api.scan_jobs.manage` for consistency.
  - **Action Required:** Please update your custom roles as needed.
- SDK: `MaintenanceJobSpec`'s abstract properties `JobDescription` and `KindDiscriminator` have been moved to the base `DatabaseScanJobSpec`, and the latter property renamed to `JobKindDiscriminator` and made optional (virtual).
  - **Action Required:** If you have custom `DatabaseScanJobSpec` types, they now need to implement `JobDescription`. If you have custom `MaintenanceJobSpec` types, their `KindDiscriminator` needs to be renamed to `JobKindDiscriminator`.
- SDK: `DatabaseScanJobManager` now has abstract method `GetUpcomingJobs` for reporting upcoming jobs to the dashboard.
  - **Action Required:** If you have custom `DatabaseScanJobManager` types, they now need to implement this method. At simplest, this may return an empty enumerable, but in that case the manager's upcoming jobs won't be visible in the dashboard.
- SDK: NFT support has been extended and modified since the Web3 preview releases (20+Web3, 20.1+Web3).
  - **Action Required:** If your project is using the NFT features from the preview release, you will need to update your C# code and server options. Please see the release 21 notes and NFT documentation for details.
- SDK: Default language is now defined in `MetaplayCoreOption`s and removed from `GameConfig`.
  - **Action Required:** `MetaplayCoreOption` constructor should define `defaultLanguage`. It is most commonly `LanguageId.FromString("en")`.
  - **Action Required:** Default language is retrieved with `MetaplayCore.Options.DefaultLanguage` instead of `gameConfig.DefaultLanguage.LanguageId`.
- Dashboard: Global access to `gameServerApi` deprecated.
  - **Action Required:** Migrate your custom components to call `useGameServerApi` to get access to `gameServerApi`.
- Dashboard: Added helper functions for the `OverviewListItem` types to make it easier to add new items to the overview lists.
  - **Action Required:** TBD
- Dashboard: Removed two deprecated properties from the `addPlayerRewards` integration API.
  - **Action required:** Update your calls to `addPlayerRewards`. The compiler should give a helpful error.
- Dashboard: Removed custom Cypress test navigation commands in favour of using `cy.visit()` directly.
  - **Action required:** Replace any calls to `cy.navigateToPlayer()`, `cy.navigateToOverview()`, `cy.navigateToSystem()` and `cy.navigateToSidebarPageByName()` with `cy.visit()`.

### Added

- SDK: `[MetaSerializable]` attribute can be conditionally toggled with `[MetaplayFeatureEnabledCondition]` attribute. Serialization type data or code is not emitted for disabled features.
- SDK: `PlayerModel.GetTimeOnTick(int)` is removed. Time should be retrieved with `ModelUtil.TimeAtTick(...)` utility instead.
- SDK: `MetaplayOAuth2Client` configuration now defaults to Metaplay Managed Game Servers OAuth Provider.
- SDK: Guild and other non-Player entity model `FastForwardTime` is removed and replaced with `OnFastForwardTime`, which is called *after* the model clock is already fast forwarded.
- SDK: `ModelJournal<>` type no longer has the Action type as a generic argument. Actions are exposed as `ModelAction` base class.
- SDK: OfflineServer guilds implementation has been moved to the Idler sample OfflineServer implementation.
- SDK: The included Dockerfiles now perform an 'apt update && apt upgrade' to ensure the latest packages are installed.
- SDK: NFT support, including: modeling NFTs as C# classes, tracking players' NFT ownerships, and publishing NFT metadata to an S3 bucket. An earlier version was delivered to some customers in a previous preview release, but 21 is the first mainline release with these features.
- SDK: Sending push notifications now takes in optional `Dictionary<string, string> Data` which is delivered as the Firebase Messaging optional data.
- SDK: `AnalyticsDispatcherSinkJsonBlobStorage` can now be overridden by integration to override the json serialization of analytics events.
- SDK: The `PlayerAnalyticsContext` class can now be inherited by the integration to add game-specific player analytics context data.
- SDK: Added the Model Inspector, a Unity editor window that visualizes the real-time state of any Model.
- SDK: Added experimental Leagues support, with a sample integration in Idler.
- Dashboard: Core guild components migrated to subscriptions for their data fetching and refresh. This should fix potential guild-to-guild links not updating the page properly.
- Dashboard: Added object lengths into `MetaRawData` and changed preview font into monospace for legibility.
- Dashboard: Integration API split into sub-files for easier maintenance as the API grows.
- Dashboard: Tests now fail if they emit warnings or errors to the console. This can be disabled per-test if necessary.
- Dashboard: Improved API error component to include and copy-to-clipboard and stack display.
- Dashboard: Added a button to copy client logs from player incident reports.
- Dashboard: Added a new `MetaActionModalButton` component to make it easier to create buttons that spawn modal screens.
- Dashboard: Added a new `PlayerResourcesList` component to make it easier to add to or replace the default resources list on the `PlayerOverviewCard`.
- Dashboard: Added a new `MetaAlert` component to make it easier to create consistent looking alert messages.
- Dashboard: `MetaPageContainer` component now has a slot and and a property to show optional alerts on the page with consistent formatting. This reduces page layout copy/paste.
- Dashboard: `MetaPageContainer` component also got new slots like `overview` and `center-column` to make it easier to create consistent-looking pages. All core views have been migrated into using this component.
- Dashboard: Added unit tests to core modules.
- Dashboard: Added support for unit tests in customer projects.
- Dashboard: `MetaPageHeaderCard` and `MetaListGroupItem` now support an optional avatar parameter to display an image.
- Dashboard: Database scan jobs can now be globally paused and resumed.
- Dashboard: Added ability to pause entity event streams.
- Dashboard: Added `PlayerOverviewTitle` and `PlayerOverviewSubtitle` components to make it easier to override the default title and/or subtitle on the `PlayerOverviewCard`.
- Dashboard: Cypress tests now support TypeScript. All core tests and configuration have been migrated. The game-specific tests have typings for our custom commands to get code completion and type checking.
- Dashboard: Added new component entry points to the `Players, Guilds, Matchmaking, Broadcasts, Scan Jobs and System` details and List views. Using the `addUiComponent` or `removeUiComponent` hooks you can now add or remove custom components to these views.

### Changed

- SDK: Upgraded to Akka.NET 1.4.46.
- SDK: `IPlayerModelBase` now implements `IMetaIntegrationConstructible`, mandating that an implementation is expected to be discovered during MetaplayCore initialization.
- SDK: Game config build no longer attempts to validate experiment variants that have no overrides associated to them.
- SDK: EntityActor now implements `IEntityAsker` interface that allows providing access to the entity ask mechanism to code outside of `EntityActor` classes.
- SDK: EntityArchive import and export from dashboard no longer does an extra hop through `GlobalStateManager` but communicates with `EntityActor` instances that are part of the operation directly.
- SDK: Server-side runtime caching of experiment-specialized game configs now retains each config for at least 20 seconds, with the intention of reducing resource usage (by avoiding unnecessary repeated config instantiation) in player maintenance jobs which wake up each player only for a short moment.
- SDK: Player state overwrite now preserves authentication and login history members by copying the values from the destination data instead of the auth reattach action.
- SDK: Optimized Util.ArrayEqual() to use SequenceEqual().
- SDK: Don't require running `npm install` to get Docker builds to work. The Dockerfile.server now runs `npm install` automatically if `package-lock.json` is not present.
- Dashboard: Changed header alerts to stick to the top of the screen for better visibility when scrolled down.
- Dashboard: Removed two deprecated properties from the `addPlayerRewards` integration API.
- Dashboard: Player resources list on the player details page now shows an error badge if the resource amount could not be resolved.
- Dashboard: The global backend error notifications now display more helpful messages from the game server.
- Dashboard: Greatly improved speed of game configs and experiments pages.
- Dashboard: Database scan jobs page has received an UI redesign.
- Dashboard: Reduced the length of some lists (eg: all experiments, all game configs) from 100 to 20 to make them more readable.
- Dashboard: Player exports now have the player ID in the file name when downloading as a file.
- Sample projects: Updated Unity version to 2021.3.14f1.
- Idler sample project: Nicer default values to in-game mail reward attachments.

### Fixed

- SDK: Fixed a build performance regression introduced in R20.1, where Unity WebGL builds took an unexpectedly long time due to the generated serializer code containing a very large method. The large method is now split into multiple smaller methods.
- SDK: MetaplaySDK.PersistentDataPath is now available at all times in Editor which fixes the various clear/delete operations in the 'Metaplay' Editor menu.
- SDK: Refactor JSON converters that modified JsonSerializer state. Modifying the state in converters is not thread safe and can cause sporadic JSON serialization errors when JsonSerializer is used in parallel.
- SDK: In WebGL, fixed "TypeError cannot read property 'length' of null" error seen sometimes in logs during startup.
- SDK: Fix the security scan CI job to properly fail the job when vulnerable dependencies are found.
- SDK: Incident reporting now works in WebGL builds.
- SDK: Localization now works in WebGL builds.
- SDK: The SDK now includes a root-level `.gitignore` that overrides any patterns from the parent directories. Typical Unity .gitignores in the parent directory no longer cause SDK .csproj and .sln files to be ignored from git.
- SDK: Server/Client serializer protocol hash checking that was disabled in R20 has been re-enabled.
- SDK: Fixed bug in game config parsing for sheets that have multiple experiment variant override columns.
- SDK: BotClient now uses a random available port for its Akka.NET remoting to avoid conflicts on earlier port 7000 (the remoting is not actually used, but there seems to be no easy way to disable it).
- SDK: Log the actual internal exception from `Model` schema migration errors.
- SDK: JSON parsing of `F32` and `F64` now allows also JSON numbers without the decimal period.
- SDK: Reduced the probablity of hitting a rare Unity deadlock on IL2CPP if GC was issued while a background thread was initializing class static initializers.
- SDK: Initialize Serilog console loggers with `CultureInfo.InvariantCulture` to ensure locale-independent logging (fixes warning CA1305).
- SDK: On Editor, reduced the number of temporary Editor-specific allocations in Serialization code.
- SDK: Fixed rare race condition leading into session start failures if session start (loading a player) took exactly `1 + N * 5` seconds.
- SDK: Added `[Preserve]` attribute on `DefaultOfflineServer` to avoid it getting stripped in IL2CPP builds.
- SDK: The Javascript plugin metaplay-imx-link-sdk-plugin (used in web3-enabled WebGL client builds to interface with Immutable X Link SDK) is now included pre-built in the Metaplay SDK package to avoid needing to build it at client build time. The plugin is still only included in the client build if web3 features are enabled.
- SDK: Fix rare occurrence of client exceeding PlayerFlushActions.MaxTicksPerFlush ticks in flush batches.
- Dashboard: Fixed guild invites list to not show "X/0" as usage count for unlimited invites.
- Dashboard: Fixed player details page not always updating immediately after admin actions.
- Dashboard: Fixed the sidebar open/close button not having a hover state.
- Dashboard: Fixed the response reporting of the player social auth remove dialog.
- Dashboard: Fixed experiment control groups sometimes showing 0 participants instead of the actual count.

## Release 20.1+Web3 (2022-10-11)

### Added

- Dashboard: Show on dashboard recent tokens that have been minted on IMX but haven't been initialized on the server yet.

## Release 20.1 (2022-10-11)

### Breaking

- Dockerfile dotnet base-images are no longer pinned and can become out-of-date if an old base-image is cached on build agent.
  - Action: Make sure docker builds are invoked with `--pull` switch to keep base-images up-to-date.

### Added

- Samples: Wordle sample project has been extended to showcase the integration and the usage of Game Configs.

### Changed

- Target .NET SDK 6.0 in Docker builds instead of specific patch release to make sure each server build is made with the latest security patches for the platform.
- SDK: `MetaSerializerTypeRegistry` contents are now preprocessed as part of the `Metaplay.Generated.dll` generation and game builds initialize the registry from the preprocessed data. This eliminates the bulk of the need to use C# reflection for Metaplay-specific type info. As a result, the Metaplay SDK initialization time is significantly reduced especially with Unity versions that suffer from bad performance of `GetCustomAttribute` calls.

### Fixed

- SDK: Fixed websocket connection failure if TLS was enabled.
- SDK: Fix accidental usage of WebGL-only code in Unity Editor when the WebGL platform is selected.
- Dashboard: Fixed Dashboard End-to-end Cypress tests failing with a `mkdir` failure on Windows.
- Dashboard: Added a dedicated modal to alert users when a session has expired.
- Dashboard: Fixed SDK installer generating a non-functional dashboard project folder if SDK was installed on an empty Unity project.
- Dashboard: Fixed a validation error in Push Notification Campaign page preventing the creation of new campaigns.

## Release 20+Web3 (2022-10-03)

### Added

- SDK: Added support to log in with ImmutableX account.
- SDK: Basic NFT support, including: modeling NFTs as C# classes, tracking players' NFT ownerships, and publishing NFT metadata to an S3 bucket.

## Release 20 (2022-09-29)

### Breaking (list anything here that needs related changes in the customer projects)

- SDK: Server-side csproj files need to be updated to depend on the SDK projects.
- SDK: Server types are now found in assemblies prefixed with "Metaplay.". Dashboard type references might need to be updated to reflect this.
- SDK: `GuildModel.Tick` is now `GuildModel.OnTick`.
- SDK: The ASP.NET dependencies are now included in `CloudCore.csproj` instead of `Server.csproj`.
  - Action: All customer projects need to be updated.
- SDK: SupportedSchemaVersions has been moved to the respective persisted objects (eg, `PlayerModel` or `GlobalState`).
  - Action: The relevant models must specify the `[SupportedSchemaVersions(oldestSupportedVersion, currentVersion)]` attribute and the individual migration methods the `[[MigrationFromVersion(fromVersion)]` attribute.
- SDK: `METAPLAY_ENABLE_JSON` define is removed and JSON support is assumed.
  - Action: `METAPLAY_ENABLE_JSON` definitions should be removed.
- SDK: `GameJsonContractResolver` is removed. Custom JSON formatters should be registered with the normal `[JsonConverter(type-of-converter)]` on the target type.
- SDK: On Client, JSON serialization now maps property names into lower camelCase (`FooProperty` is serialized into `fooProperty`) to match with server behavior.
  - Action: On client, all uses of `JsonSerialization` need to be checked for potential regressions.
- SDK: `[IncludeOnlyInJsonSerializationMode(JsonSerializationMode.Default)]` is no longer included into AdminAPI (Dashboard) json serialization. For these cases `JsonSerializationMode.AdminApi` should be used instead.
- SDK: `PlayerModel.ServerOptions` is now `MetaplaySDK.Connection.ServerOptions`.
- SDK: Google authentication configuration has been changed. `EnableGoogleAuthentication` is now `false` by default and IT MUST BE MANUALLY SET TRUE IN PRODUCTION. `GooglePlayClientId` is not longer defined directly but instead with `GooglePlayOAuth2ClientCredentialsPath` which is a path or secretsmanager url into a OAuth client configuration JSON exported from Google Cloud Console. Additionally, a new option `GooglePlayApplicationId` must contain the Google Play Application ID from the Google Play developer console.
- SDK: The `MetaDbContext` no longer has generics parameters for the persisted player and guild types. The concrete persisted type is automatically discovered from code.
  - Action: Remove generics parameters for `MetaDbContext` in `GameDbContext` declarations.
- SDK: Removed integration-side DB initialization in server startup.
  - Action: Remove the `InitializeDatabase()` function in `ServerMain.cs`.
- SDK: `Platform` options is split to `GooglePlayStore` and `AppleStore` and the structure has changed:
  - `EnableGoogleAuthentication` is now ´false` by default and IT MUST BE MANUALLY SET TRUE IN PRODUCTION.
  - `GooglePlayClientId` is not longer defined directly but instead with `GooglePlayOAuth2ClientCredentialsPath` which is a path or secretsmanager url into a OAuth client configuration JSON exported from Google Cloud.
  - A new option `GooglePlayApplicationId` must contain the Google Play Application ID from the Google Play developer console.
  - `EnableAppleAuthentication` is now ´false` by default and IT MUST BE MANUALLY SET TRUE IN PRODUCTION.
- SDK/Samples: Removed the `GAME_SERVER` define.
  - Use `NETCOREAPP` (or one of the `UNITY_xxxx_y_OR_NEWER`) instead if you want to compile some code in client vs cloud projects (server or botclient).
- SDK: `[TypeConverter(typeof(StringIdTypeConverter<T>))]` is no longer required or allowed in StringId type definitions, as string conversion is now enabled for all StringId types.
  - Action: Any such attribute annotations should be removed.
- SDK: Added listener method `IPlayerModelClientListenerCore.InAppPurchaseValidated` which is called when purchase validation completes either with status ValidReceipt or ReceiptAlreadyUsed.
  - Action: If the game implements `IPlayerModelClientListenerCore`, an implementation for this method needs to be added. The implementation can be empty if the game has no use for it.
- SDK: `F64.ToString` now uses the normal `double` formatting, instead of using the `n6` format which produces thousand separators (commas).
- SDK: All `Config.env.yaml` files must parse without errors and warnings.
  - Action: Manually test each YAML with `METAPLAY_OPTIONS` to validate.
- SDK: If an array is declared in multiple `Config.*.yaml` or other config sources, the last array definition is now chosen in its entirety. Previously all arrays were merged such that for each declared array element, the last element declaration for the array element index was chosen, and the array length was the maximum of all arrays. The resulting by-index merged array was then concatenated into the property's default value.
  - Action: Avoid array declarations in config.
- SDK: By default, `[MetaReservedMembers(...)]` now forbids using member tagIds that haven't been reserved. This can be relaxed to the old behavior with `[MetaAllowNonReservedMembers]`.
  - Action: If any violations are detected at SDK init time, either adjust the reserved ranges, add `[MetaAllowNonReservedMembers]`, or remove all `[MetaReservedMembers(...)]` attributes from the type, as appropriate.
- SDK: `InAppPurchaseEvent`'s methods `Clone` and `CloneForHistory` now take a `IGameConfigDataResolver` parameter, which should be the resolver in the containing `PlayerModel`.
- SDK: `MetaOfferGroupInfoBase` and `MetaOfferInfoBase` now provide convenience constructors for populating data from the corresponding source config item base types. Integrations that customize the source types should now derive from `MetaOfferGroupSourceConfigItemBase` and `MetaOfferSourceConfigItemBase` rather than from the "Default" types.
- Server: AdminAPI `$type` fields in Dashboard no longer carry Assembly information.
  - Action: All hard-coded types of form "Type.Name, Assembly" should be replaced with just "Type.Name".
- Dashboard: `$backendApi` deprecated -> migrate to `gameServerApi` module.
- Dashboard: Subscriptions refactored into a module -> refactor imports.
- Dashboard: Game server API, UI components, authentication and permission checker refactored into modules with their own Pinia stores -> refactor imports.
- Dashboard: Event stream refactored into a module -> refactor imports.
- Dashboard: Vue 2.7 upgrade -> `this.root` was removed and other potential issues in customer code -> test well.
- Dashboard: `$backendApi` deprecated -> migrate to `gameServerApi` module.
- Dashboard: Reusable core components refactored into a module -> refactor imports.
- Dashboard: `auth-tooltip` component renamed to `meta-auth-tooltip` -> refactor imports.
- Dashboard: Integration API has been rewritten -> refactor uses.
- Dashboard: Project structure has been completely redone and the game-specific code now has its own root project that imports core SDK modules -> migrate existing implementations.
- Dashboard: Node updated to 18.x in anticipation to the next LTS cycle after this release is out -> upgrade local version and CI systems.
- Dashboard: `mainStore` renamed to `coreStore` for consistency with the other modules -> refactor imports.
- Dashboard: Local dashboard is now started from `<project>/Backend/Dashboard> npm run serve` instead of `<project>/MetaplaySDK/Backend/Dashboard> npm run serve`
- Dashboard: `meta-generated-view` has been refactored completetly and split into `meta-generated-content`, `meta-generated-card` and `meta-generated-section`. Most related APIs have changed.
- Dashboard: `makeVModelForm` helper method is no longer available.

### Added

- SDK: Offline state persistence now supports schema migrations and atomic file operations to make the feature more robust.
- SDK: Support for nested arrays and/or collections in serialization (eg, `int[][]` or `List<int[]>[]`).
- SDK: Game-specific StringId types can derive from StringId<> indirectly. This can be used to make families of StringId types.
- SDK: Json serialization supports serializing tuples as arrays.
- SDK: Added support for Play Games Services v2 Social Authentication.
- SDK: Added new variants of `PlayerActorBase` and `GuildActorBase` that default the corresponding persisted type to the SDK type. This allows removing some boilerplate in the common case of not needing to customize the persisted type.
- SDK: EntityAsk handler methods may optionally accept EntityId of the sender entity alongside the message.
- SDK: Allow passing in game-specific context data to the `MetaplayerReward.Consume()` function for analytics purposes.
- SDK: "Deserialization converter" mechanism to facilitate changing the serialization format of an existing type: for example, changing a concrete class to abstract (and deserializing existing data as a specific child class).
- SDK: Config sheet parsing now supports declaring Experiment Variant overrides in variant override columns in addition to variant rows.
- SDK: GameConfigBuildTemplate now implements overrideable default building of config entries. Sample projects have been updated to use this new API.
- SDK: Added DefaultGameConfigBuild implementation that is used for config building when no integration exists.
- SDK: Player progress is now persisted when running in Offline Mode.
- SDK: Added support for WebSockets in WebGL builds and in the server.
- Dashboard: Added a new MetaClipboardCopy button to overview card to make copying of target ID's easier and more consistent.
- Dashboard: Added a new button on the broadcast overview card, to make it easier to duplicate an exisiting broadcast.
- Dashboard: Added a new link into Database Entity Inspector if Player view data loading fails to make is easier to inspect the broken player state.
- Dashboard: Game-specific code now supports TypeScript.
- Dashboard: Migrated to a new toast notification plugin and added new custom hooks for adding toasts to the dashboard.
- Dashboard: Added support for `MetaRef`s and `StringId`s in generated forms. These will show a dropdown list based on the currently active gameConfig.

### Changed

- SDK: Upgraded Dockerfiles to .NET 6.0.8 (and SDK 6.0.400).
- SDK: Messages marked as internal (`MessageDirection.ClientInternal` and `ServerInternall`) may no longer be in the shared code.
- SDK: Allow specifying `Environment:SystemHttpListenHost`. Defaults to '127.0.0.1' when running locally (to only allow access from localhost) and '0.0.0.0' in the cloud (to allow Kubernetes to reach it).
- SDK: Player name validation moved from `PlayerActorBase.ValidatePlayerName` into `PlayerRequirementsValidator`.
- SDK: In Offline Mode, the player is now persisted on disk by default. You can disable this by setting `MetaplayOfflineOptions.PersistState` to false in your `MetaplayClientOptions`.
- SDK: AdminAPI no longer deserializes arbitrary types from JSON objects. Instead, type must be statically defined at deserialization site, or the dynamic type must be `[MetaSerializable]`.
- SDK: Database table mappings for `IPersistedItem` classes are now automatically discovered from code, removing the need for declaring entries for the mapped classes in `MetaDbContext` (or `GameDbContext` for game-specific classes). The old style configuration of `DbSet<>` fields in the context class has been preserved for backwards compatibility but will likely be deprecated in the future. Any `IPersistedItem` concrete classes that don't have a corresponding old style `DbSet<>` entry in the context class need to declare the database table name via a `Table` attribute.
- SDK: Removed the need of declaring a game-specific `GameDatabase` class when no custom queries are needed.
- SDK: Removed throttling database queries per MetaDatabase context and instantiating new context objects per use.
- SDK: Introduced SDK-side server projects (Metaplay.Cloud, Metaplay.Server, Metaplay.BotClient) and modified game projects to depend on them rather than importing sources from SDK folders.
- SDK: BotClient main entrypoint moved from BotClientMain (SDK-side) to integration side BotClient.cs.
- SDK: `METAPLAY_ENABLE_GUILDS` is deprecated. The guilds feature is now always built into the SDK assemblies.
- SDK: `OrderedDictionary<>` error messages were improved to include the key/value of interest to make debugging easier.
- SDK: `PlayerStateRequest/Response` has been replaced with generic `InternalEntityStateRequest/Response`.
- SDK: The initial PlayerModel is no longer logged when starting the client to improve performance. The log lines are commented out in code. In case you want to re-enable them, search for `{InitialPlayerModel}` in `MetaplayClient.cs` and `DefaultOfflineServer.cs`.
- SDK: Server RuntimeOptions (`Configs/Config.yaml` files) no longer tolerate and ignore unrecognized definitions. Unknown properties or subproperties are now a fatal error. Failing to parse a property value to the target datatype is a fatal error.
- SDK: The server now runs schema migrations on to-be-imported player models before generating the diff view for dashboard.
- SDK: JSON serialization for Game Config contents now shares the serializer with the envelope. This makes the customized serialization style consistent in the whole JSON document.
- SDK: `AtomicFileReadWriteUtil` has been renamed to `AtomicBlobStore` and includes a WebGL implementation on top of the browser's localStorage. localStorage is used to allow robust persisting of the player state when the tab/browser is being closed.
- SDK: In WebGL builds, `MetaplayCoreOptions.ProjectName` is now included in the IndexedDB database name to reduce the chance of conflicts across multiple projects.
- SDK: The check that protocol hashes match on client and server has been temporarily disabled due to the deprecation of the compile-time guilds feature toggle on server side. The check will be re-enabled in Release 21.
- Dashboard: Vite updated to 3.x for faster compilation times.
- Dashboard: Node update to 18.x in anticipation of the next LTS cycle.
- Samples: Idler now uses asynchronous scene loading, managed in `ApplicationStateManager.cs`.
- Misc: tests.py script argument `--backend-dir` now defaults to `Backend` rather than `Server`, update CI scripts in light of this!

### Fixed

- SDK: Switch `MetaplaySystemHttpServer` to use ASP.NET. This fixes the `ArgumentNullException`s that `System.Net.HttpListener` can throw if requests are made during initialization.
- SDK: Fixed broken Unity build with certain `MetaRef<>` patterns.
- SDK: Fix unit tests to run from within the Rider IDE.
- SDK: Google Sheet fetcher error messages are less cryptic and have more relevant error description for the most common error cases.
- SDK: ActiveLogicVersion is now correctly set to the minimum version on server start of the supported range when force-updating from older version.
- SDK: Fixed `SpreadsheetContent.FilterColumns` throwing IndexOutOfRangeErrors on sparse Spreadsheet data.
- SDK: If a localization sheet contains the same language multiple times, a clear error message reported instead of failing internally to a constraint violation.
- SDK: Indexer properties (`object[param1, param2]`) no longer break JSON serialization. The properties are automatically ignored and no longer need to be manually marked as Ignored.
- SDK: Limit the number of incident reports being purged at a time to avoid SQL timeouts when a large number of reports have accumulated.
- SDK: Any social login claims made between application start and session start are no longer lost. The claims are buffered and flushed on session start.
- SDK: Fixed a reflection-related failure in Unity game config building, caused by an apparent regression in Unity. The failure was present at least in Unity 2021.3.9f1 and 2021.3.10f1, but not yet in 2021.3.5f1.
- Dashboard: Fixed display of user details when logging in to the Dashboard without any roles assigned.
- Dashboard: Updated the action-delete-player modal, to clearly communicate when a player is scheduled to be deleted.
- Dashboard: Fixed player experiment details to better reflect and communicate the different experiment stages.
- Dashboard: Fixed player save file size to display the correct size.
- Dashboard: Fixed searching audit event logs by username.
- Dashboard: Fixed player's "Remove Authentication Method" dialog not closing after successfully removing the selected method.
- Dashboard: When building a game config, the default sheet id again uses the `GoogleSheets:GameConfigSheetId` runtime option instead of a hardcoded id mistakenly added in release 19.
- Dashboard: Errors during initialization are now reported with more information in Firefox. Previously Firefox was missing pertinent information that was only shown in Chrome.
- Server: Harmless warnings of long HTTP requests to `/sse` have been silenced.

## Release 19 (2022-06-22)

### Breaking (list anything here that needs related changes in the customer projects)

- SDK: Unity build now uses link.xml from the MetaplaySDK client folder and refers to it by GUID. Make sure that you have the file checked in to version
  control and that the GUID declared in link.xml.meta has not been changed.
- SDK: Due to the serializer initialization change in Unity editor it is no longer necessary to explicitly initialize MetaplayCore or MetaplaySerializer in
  editor context. Specifically, the function `MetaplaySDK.InitSerialization()` is now private and calls to it from project editor code should be removed.
- SDK: Added `MetaplayCoreOptions.ProjectName` that replaces the old `EnvironmentOptions.ApplicationName`.
- SDK: Application initialization should now use `ServerMainBase.RunServerAsync()` or `BotClientMain.RunBotsAsync()` instead of the old methods (those are now protected to avoid accidental usage). The EFCore detection logic was also moved into ServerMainBase, so it no longer needs to be done in the userland ServerMain.
- SDK: A new environment variable `METAPLAY_ENVIRONMENT_FAMILY` must now be specified when running the server in a container. Helm chart v0.4.0 sets this.
- SDK: Most of the SDK-specific contents of Options.yamls were migrated into the SDK code.
- SDK: Default permissions are now defined in the SDK code. If a project is happy with the defaults, they should delete their config from the .yamls.
- SDK: In local environments, the server will now by default exit immediately if unknown options are given in the option .yaml files. This is controlled by the option `Environment:ExitOnUnknownOptions`, and is intended for catching errors during local development. In cloud environments, it is disabled by default, and should be kept that way to allow the server and infra to be briefly out of date with each other during backend updates.
- SDK: Client-side Social Authentication failure handler in `IMetaplayClientSocialAuthenticationDelegate` receives information if failure due to a temporary issue and the potential error message.
- SDK: Client-side Social Authentication conflict handler is given `ConflictResolutionId` which it must pass in into `MetaplayClient.SocialAuthManager.ResolveConflict`.
- SDK: `GuildActor.CreateGuildDiscoveryInfo` now returns a pair of public and server-only info without using `GuildDiscoveryGuildData`.
- SDK: Guild Name and Description requirements and validation is moved from `GuildModelConstants` and `GuildActor[Base]` into a `GuildRequirementsValidator` integration which the game may override with a custom implementation.
- SDK: Game-specific permissions should now provide default permissions and use [MetaDescription] for the description.
- SDK: Renamed `AdminApiPermissionRegistryAttribute` to `AdminApiPermissionGroupAttribute`.
- SDK: CDN emulator is now served at <http://localhost:5552> instead of old <http://localhost:5550/api/cdnEmulator>. Must change all usage to new url. The dedicated server listens to connections from all networks.
- SDK: Format of Authentication runtime options changed and renamed to AdminApi. This affects the definition of roles, permissions and API authentication options. Changes may need to be made to options yaml files.
- SDK: `IMetaplayLocalizationDelegate.AllowAppStartWithoutLocalizations` is removed and is now controlled by `MetaplayFeatureFlags.EnableLocalizations` given during init.
- SDK: When config-parsing `StringId`s containing dots or hyphens, the parsing stops at whitespace instead of ignoring whitespaces. In the unlikely case a project has whitespace in `StringId`s in configs, they'll need to be adjusted.
- SDK: Metaplay SDK core options are now provided via implementing interface `IMetaplayCoreOptionsProvider`, replacing the older mechanism using the `MetaplayCoreOptionsProvider` attribute.
- SDK: Must pass `MetaplayFeatureFlags` to `MetaplayCoreOptions` constructor. Specify `EnableLocalizations` based on whether the project uses the Metaplay localization feature and `EnableGuilds` if the Metaplay guilds feature is used.
- SDK: `SessionActorBase` has `_state.GameState` renamed into `_gameState`. Session is established if `_phase == ActorPhase.InSession` (instead of `_state != null`).
- SDK: If `METAPLAY_ENABLE_FIREBASE_ANALYTICS` is defined, all members in all Analytics Events must be scalar types, or explicitly ignored with `[FirebaseAnalyticsIgnore]`. Previously non-scalar types were automatically ignored.
- SDK: BotClient should no longer call `PlayerContext.Update` or `GuildContext.Update`. The updates are handled automatically.
- SDK: `MetaSerializableFlags.AllowNoMembers` is removed. The types must instead be marked with `[MetaAllowNoSerializedMembers]`. This allows consistent usage with `[MetaSerializableDerived]` types.
- SDK: Guild Recommender requires temporary `ParseLegacyVersion1PoolPage` implementation for one-time data migration. Reference implementation is available in `idler` sample project.
- SDK: Client Listeners for GuildModel are now set with MetaplayClient.GuildClient.SetGuildClientListeners at SDK init time or IMetaplayLifecycleDelegate.OnSessionStarted callback.
- SDK: Removed the IGameConfigDataResolver parameter from config parse functions and the config build function.
- SDK: `IMetaplayClientConnectionDelegate.CreateOfflineTransport()` is now `CreateOfflineServer()`. Offline server instance is moved from `MetaplayConnection.Delegate.OfflineServer` into `MetaplayConnection.OfflineServer`.
- SDK: The serializer now more strictly requires that when a class derives from a `[MetaSerializable]` base class, the subclass needs to specify either `[MetaSerializable]` (if the class isn't intended to be serialized via the base class) or `[MetaSerializableDerived(...)]`. Previously, the subclass would inherit the `[MetaSerializable]` from the base class.
- SDK: Guild functionality in `PlayerActorBase` has been moved to separate `PlayerActorGuildComponent`. Integrations need to be updated accordingly by moving PlayerActor guild-related overrides into a game-specific PlayerActorGuildComponent derived class.
- BotClient: `BotClient` actor should no longer implement `IPlayerModelClientListenerCore` directly as it is already no-op implemented in the base class.
- Dashboard: ESLint 8 may explode the dashboard if the customer project root doesn't have an .eslintrc.json that, for example, disables linting for customer files.
- Dashboard: Integration API got re-structured -> migrate old integration files and check that customer routes put all extra data inside the route meta object instead of route root.
- Dashboard: Routes now need a new `meta: { sidebarOrder: number }` prop to be placed in a specific position -> migrate old integration files.
- Dashboard: Custom player details components got refactored to use the new subscriptions API -> migrate existing game-specific components.
- Dashboard: MetaListCard filter and sort options refactore to `uiUtils.ts` -> migrate existing game-specific components.

### Added

- SDK: New MetaRequest/MetaResponse client-server communication primitive for more convient implementation of client session messages that expect a response.
- SDK: Support for GameCenter team-scoped TeamPlayerIds. Additionally client may pass both the team-scoped TeamPlayerId and the legacy id to log in with both authentication methods with one query.
- SDK: Support for overwriting player state from EntityArchive json provided by client (available in development environments and for players tagged as developers).
- SDK: Added `MetaplayFeatureFlags` that can be used to enable/disable features across the client and the server. Initially, localization feature can be controlled via the `EnableLocalizations` member and the guilds feature via `EnableGuilds`. The feature flags must be passed to `MetaplayCoreOptions`.
- SDK: The game config parsing of `MetaTime` now supports date-time syntax, such as `2022-05-20 12:34:56.789`.
- SDK: New `ChecksumGranularity` `PerActionSingleTickPerFrame` limits the number of tick checksums to at most one tick per frame which reduces the performance impact when resuming from pause. This used by default.
- SDK: Added Guild Recommender metrics to allow observing update frequency and pool sizes.
- SDK: Added support for adding game code sub clients in the default Unity integration MetaplayClient. They can be passed in with MetaplayClientOptions.
- SDK: InitializeMetaplayProject() now copies the userland Backend/ directory & Server.sln as well.
- SDK: Serialization of a null config reference with a non-nullable key type (e.g. enum) is now supported when the `IGameConfigData<>`-implementing class defines a static property called `ConfigNullSentinelKey`. This reserves a key for representing null references in serialized data.
- SDK: Added `MetaplayGameCenterPlugin` helper for retrieving the Apple GameCenter authentication tokens.
- SDK: Experimental support for WebGL. Has some significant limitations still: only supports offline mode (networking is not implemented), doesn't support localizations or incident reporting.
- SDK: Added the ability to define additional bucketing requirements for asynchronous matchmakers.
- SDK: New EntityComponent framework for defining modular EntityActor components. Currently used for splitting the guild-related actor behaviour of PlayerActor to PlayerActorGuildComponent.
- SDK: Added `PlayerActorBase.OnPlayerIncidentRecorded()` for game-specific data gathering of player incidents.
- Dashboard: Added a new function into integration.js to easily register custom Vue plugins without modifying code SDK code.
- Dashboard: Added tooltip that shows the required permissons when a sidebar item is greyed out.
- Dashboard: Added a new 'safety lock' feature that can be toggled from the sidebar and is on by default in production. It prevents accidentally committing actions in the dashboard.
- Dashboard: Added a new "swap" button to game config diffs page to quickly swap the diffing order.
- Dashboard: Added a new modal on the sidebar header that provides quick links to other environments.
- Dashboard: Added filtering options to the experiments list card. Defaults to hiding concluded experiments.
- Idler: Added some unit test examples for testing game-specific PlayerActions in Samples/Idler/CloudCore.Tests/PlayerActionTests.cs.
- Idler: Added an example AsyncMatchmaker integration.
- Samples: Added Wordle clone sample.

### Changed

- SDK: Upgraded to .NET 6.0.6 (SDK 6.0.301).
- SDK: Upgraded dependencies, mainly Akka v1.4.37, EFCore v6.0.4, MySqlConnector v2.1.8, prometheus-net v6.0.0, MaxMind GeoIP v5.1.0.
- SDK: Default to MySQL 8.0 database. Reduces number of roundtrips to database for improved performance. MySQL 5.7 is considered deprecated now.
- SDK: Refactor of Admin API authentication code to make it easier to add new auth providers.
- SDK: Added new (faster and smoother) Admin API authentication provider based on JWTs.
- SDK: The [MetaDescription] has been moved into the SDK core, and is intended to be used for all new features requiring dashboard-visible descriptions for classes or members.
- SDK: Client-side consistency checks for ticks are throttled to at most 2 per frame. This limits the performance impact when resuming from pause.
- SDK: Mapping of DbSet<> entries in DbContext classes can now be toggled via `MetaplayFeatureEnabledConditionAttribute` attributes.
- Dashboard: Upgraded Cypress to version 10.
- Dashboard: Made player details page properly reactive to changes in player ID.
- Dashboard: Upgraded ESLint, related plugins and configuration to new major 8.x.
- Dashboard: Significant restructuring of how routes are registered and how route metadata is passed around. More aligned with the underlying vue-router types.
- Dashboard: Significant restructuring of the integration API to make it easier to maintain and extend. No more need for game config callbacks during init!
- Dashboard: Sidebar routes now have a meta property `sidebarOrder` to easily inject new routes into specific positions from integration code.
- Dashboard: Developer mode toggle has been moved to the users page and is on by default in non-production environments.
- Dashboard: Overview card lists now uses a customizable `MetaOverviewList` component to display overview card data.
- Dashboard: Game config diffs details are now lazy loaded and closed by default to ease with browser performance when opening very large diffs.
- Dashboard: MetaListCard sort and filter options refactored to `uiUtils.ts` and TypeScript. This fixes unnecessary and noisy HMR warnings when in local development mode.
- Dashboard: API subscription system overhauled. Improvements include better reactivity and a standardized client API, both leading to more consistent usage patterns.
- Dashboard: Moved server build and commit ID from the sidebar to the overview page.
- SDK: Remove the need of declaring Metaplay assembly preserve directives in project link.xml
- SDK: Unity Editor now always has Metaplay serializer initialized.
- SDK: Successful duplicate IAPs (such as restorations of non-consumables or subscriptions) are now stored in a bounded-length `DuplicateInAppPurchaseHistory` list instead of the unbounded `InAppPurchaseHistory` to avoid bloating the `PlayerModel` in case of repeated purchase restorations.
- SDK: DeploymentVersion was changed from class to struct.
- SDK: By default, server<->client messages larger than 10kB are now deflate-compressed for transmission. This is controlled by the server option `ClientConnection:EnableWireCompression`.
- SDK: The effective `PlayerModel` size limit (until session start fails) has been increased from 1MB to 5MB. The size limit is no longer enforced on the client-side, and can thus now be adjusted with just a server update.
- SDK: Change ModelJournals for PlayerModel and GuildModel to work with the SDK-side interfaces rather than the concrete types. This enabled further cleanups of the SDK API.
- SDK: Player Actor analytics events are now flushed when the Model is persited into the DB. This reduces the desync between the model and the analytics stream if the player actor crashes.
- SDK: When config-parsing `StringId`s, they're now allowed to contain dots (`.`) and hyphens (`-`) after the first character. Previously, only dots were allowed, and only as separators between normal identifiers.
- SDK: Unity com.unity.nuget.newtonsoft-json has been upgraded to v3.0.2 which includes Newtonsoft.Json v13.x.
- SDK: `Client/Unity/Editor/GoogleApis` libraries were upgraded to core apis v1.57.0 and sheets v4.1.57.0 for compatibility with Newtonsoft.Json v13.x.
- SDK: Client-side Metaplay Connection no longer supports automatic reconnect after connection loss during session.
- SDK: Spreadsheet parsing now supports empty value cells for key-value structure configs, when empty input makes sense for the member type.
- SDK: Controlled exceptions form EntityAsk handlers are only handled if the exception originates from the entity itself. Forwarding/Rethrowing another entity's controlled exceptions are no longer supported.
- SDK: `EntityAskError` base class used for controlled exceptions from EntityAsk handlers has been renamed to `EntityAskRefusal` to clearly differentiate it from entity-terminating errors.
- SDK: The variant column syntax in config spreadsheets now supports declaring multiple variants per row as a comma-separated list.
- SDK: Incident reports now prefer truncating the client log (by dropping least recent log entries) to avoid the entire report being dropped due to exceeding the report size limit (4MB).
- SDK: Provide more accurate error info for failed dashboard-initiated GameConfig builds, to fix missing failure string in dashboard gameconfig views.
- Samples: Idler and HelloWorld samples now use Unity 2021.3.2f1.
- Samples: HelloWorld active environment is now specified with an enum (instead of just boolean) to easily support cloud-based environments, too.

### Fixed

- SDK: Server tolerates multiple parallel social logins of a same platform.
- SDK: Large incident reports (>1MB) during session start are no longer rejected.
- SDK: Log spam reduces if JWKS server is temporarily unavailable by throttling queries after sequential failures.
- SDK: The admin API can now be used to query player states up to 6MB in size, allowing the dashboard to be used to view players whose state moderately exceeds the session-start size limit of 5MB. Previously, in multi-node environments, the admin API query would fail for player states larger than 1MB.
- SDK: Analytics events and their contents are now deep copied when issued. Previously events could share references which allowed inadvertly mutating event payload after being issued.
- SDK: When an abstract class implements a `[MetaSerializable]` interface, classes inheriting that base class can now be serialized via the base class as the static type.
- SDK: It is now permitted to put the `[MetaSerializable]` attribute on multiple interface types in the same inheritance hierarchy, as long as they all specify the same `MetaSerializableFlags`.
- SDK: Firebase Analytics integration now supports StringId-typed parameters.
- SDK: Server Action deadlines no longer trigger unexpectedly early if client is flushing past ticks when resuming from pause.
- SDK: Null config references are now properly serialized as the null value of the key type. Previously, only string and StringId key types were serialized properly for null references.
- SDK: Certain kinds of cyclic type constructions no longer break the serializer initialization.
- SDK: On client, old gameconfigs and localizations are pruned from download cache automatically.
- SDK: System:ClientPorts default value is no longer appended when explicitly specifying the option.
- Dashboard: Fixed display of missing variants in Game Config page when different experiments shared the same variant names.
- Dashboard: Table content adjusts smoothly in smaller screen sizes.
- Dashboard: Player reset and overwrite operations now force the entity event log to refresh appropriately.

## Release 18 (2022-04-14)

### Added

- Dashboard: Users who connect to the Dashboard without any assigned roles now get a message to contact the administrator.
- Dashboard: The project now supports TypeScript!
- Dashboard: Players that are online but the app is paused have amber presence indicator instead of green.
- Dashboard: Added a new `meta-language-label` component to display language labels.
- Dashboard: Added new `meta-input-number` and `meta-input-checkbox` components as we start to migrate away from Bootstrap components.
- Dashboard: Added a new card in the player details page to view and manage the new IAP subscriptions.
- Dashboard: Added descriptions to the RuntimeOptions classes and values.
- SDK: New `EntityActorRegisterCallback` method attribute for EntityActor classes to receive EntityActor metadata as part of the EntityActorRegistry initialization.
- SDK: Support auto-renewing subscription IAPs. The server checks subscription renewal after the initial purchase.
- SDK: For Google Play IAPs, store OrderId (in addition to TransactionId == PurchaseToken) in the IAP history.
- SDK: Added `EntityActor.ScheduleExecuteOnActorContext` helper for cancellable, short-term actor-local scheduling needs.
- SDK: Added support for logging in with Social Authentication credentials using the new SocialAuthenticationLoginRequest login message.
- SDK: Centralized integration registry for maintaining game integrations for API classes (supports singleton and default constructible integration API classes).
- SDK: Server now checks all necessary `DbSet<>`s are declared for all `PersistedEntityActor`s at application launch time instead of upon first use.
- SDK: AsyncMatchmakerActorBase that games can derive from to create matchmaker actors for async matchmaking needs.
- SDK: Player force reset functionality to be able to clear player state when reading the current player state fails.
- SDK: Support for persisting the player state in Offline Mode, only intended as development tool for now.
- SDK: Improved tooling for creating new Multiplayer Entities: `PersistedMultiplayerEntityActorBase`, `MultiplayerModelBase`, and `MultiplayerEntityClientBase` provide base code for server-side ticking entities, and for their Model and the client side state management.
- SDK: Server now emits on launch `metaplay_build_info` metric that contains build number and commit id.
- Idler: New experimental command-line GameConfigTool that can be used to build, publish (to localhost for now), and print the contents of Game Configs from outside Unity.

### Changed

- SDK: Upgraded to .NET 6.0.3 (SDK 6.0.201), which addresses some known vulnerabilities.
- SDK: Moved tracking of live entity counts and recently active entities into StatsCollector.
- SDK: New GameConfigs in Server's GameConfig history are now stored in compressed format.
- SDK: Require `GameConfigLibrary<TKey, TInfo>` key type to implement `IEquatable<TKey>` to avoid potential performance pitfalls.
- SDK: StringId<> class is now abstract.
- SDK: Disallow by default non-empty `[MetaSerializable]` types that have no serializable (i.e. `[MetaMember()]`) fields as they are most likely an error.
- SDK: Entity archive exports now contain the schema versions of the exported entities and importing entity archives supports running migrations on the imported models.
- SDK: Removed `DatabaseShardState` as obsolete.
- SDK: Replace old database item count estimations with new APIs `MetaDatabase.GetTableItemCountsAsync()` for querying exact counts (can take time) and `MetaDatabase.EstimateTableItemCountsAsync()` for quickly getting estimates.
- SDK: Replace the old `[EphemeralEntityActor]` and `[PersistedEntityActor]` attributes with a more general `[EntityConfig]` attribute and classes for specifying how the EntityKinds should behave on the server.
- SDK: Rename `LogicVersionRange` to `MetaVersionRange`. Use it for specifying `PersistedEntityConfig.SupportedSchemaVersions`.
- SDK: Cleanups to Dockerfiles to reduce divergence: don't rely on .sln files, target dotnet operations against specific project, and use more explicit paths.
- SDK: Optimize docker builds by skipping unnecessary build and restore steps. Drop unit tests as they're replaced by CI jobs.
- SDK: Updated the client SDK init/deinit code to properly support tearing down state on Editor playmode exit. This allows projects to disable domain reloading on playmode transitions for faster iteration times.
- SDK: Don't generate the serializer DebugStream append code by default. Speeds up serializer generation by about 0.5s in HelloWorld.
- SDK: Split server runtime option `EntityEventLog` by entity type into `EventLogForPlayer` and `EventLogForGuild`. This reduces nesting in the options, allowing them to better benefit from validation as well as the new `[MetaDescription]`s for options.
- SDK: Upgrade FixPointCS to v0.3: switch from ctor to static FromXyz(), backward-breaking changes to Pow(x, 0) and SqrtPrecise() for negative values, callbacks for invalid inputs (disabled by default), some new helper methods, improved performance.
- SDK: Byte buffer representation in `IOBuffer`s is simplified which improves serialization performance by 10-15% in microbenchmarks.
- SDK: Migrate CodeAnalyzers project into the SDK.
- SDK: Improve serializer speed on the server by about 2x by inlining some methods.
- SDK: Dockerfiles are now part of the SDK, to enable easier merging of updates for them.
- Dashboard: User page now only shows roles that are relevant to the active deployment and removes the deployment prefix string to make them easier to read.
- Dashboard: Highlighted own username in audit logs and other pages.
- Dashboard: Major refactoring of the project bundler and development server from `vue-cli` to `Vite`.
- Dashboard: `utils.js` refactored to named exports in individual files to make individual named imports and thus, tree shaking, possible.
- Dashboard: Replaced assumed role input with a checkbox selector.
- Dashboard: Changed the integration dashboard tests to run against the production build, served by the game server.
- Dashboard: Major refactoring of dashboard core code from vanilla JavaScript into TypeScript. Ongoing effort.
- Dashboard: Completed the refactoring of all state management into Vuex that was started during R17. `this.$root.state` is no more!
- Dashboard: Refactored NPM scripts that depended on environment variables into their own new utility tool to get rid of one deprecated dependency.
- Dashboard: Added ability to pause rollout of experiments.
- Dashboard: All IAPs are now listed in a single card instead of two to make room for the new subscriptions card.
- Dashboard: All objects returned from the API now include a $type property by default.
- Dashboard: Generated views can now use the $type property included in objects to dynamically infer the type of the object.
- Dashboard: Generated forms boolean fields now default to false.
- Dashboard: Reworded the logic version redirect dialogue to be more specific about how it works.
- Dashboard: The browser now reloads the dashboard when publishing a new config to "activate" it immediately without manual reloads. The intention is to make this less intrusive in future releases.
- Unity: The generated serializer assembly `Metaplay.Generated.dll` no longer needs to be manually deleted after a game build. The file has been moved from under `Assets/Metaplay` to `Assets` and is automatically cleaned up
  on a successful build. A failed or aborted build will result in the file not being deleted but the existence of it no longer interferes with Editor operation.
- Unity: Network thread log messages are now written to log even if even main loop is stalled. Buffered messages are forcibly flushed after 5 seconds if main thread is not responsive.
- Unity: The generated serializer assembly Metaplay.Generated.dll is now loaded dynamically in game builds as well. This removes the static dependency on generated code and allows scripts to be build outside of the main player build flow, including Addressables builds from editor.
- Unity: Enable usage of `Span<>` in Unity 2021.2 and above for various serializer code paths, reducing the number of allocations during (de)serialization.
- Sample projects: Upgrade to Unity version 2020.3.29f1. Upgrade Unity dependencies.
- HelloWorld: Use 'en' as the default language instead of previous 'none'.
- Idler: Enable player state persistence in Offline Mode.

### Fixed

- SDK: The serializer now allows types in different namespaces to have the same name.
- SDK: Better error messages for when `GameConfigLibrary<>` key type is missing either `ToString()` or `Equals()`.
- SDK: Replace some usage of deprecated UnityWebRequest with HttpClient to avoid deprecation warnings.
- SDK: Metaplay primitives that implement `IComparable<>` now also implement untyped `IComparable`.
- SDK: Serializer private member access now works when building with Mono scripting backend.
- SDK: The database item counts shown in the dashboard and used for estimations of segment sizes etc., are now more accurate, but are only updated every 15 minutes due to the operation being fairly expensive.
- SDK: Copy the .editorconfig in Dockerfiles to silence some benign warnings in docker builds.
- SDK: For Google Play purchases, the server validation now checks that the purchase is in the "purchased" state (`purchaseState` = 0). In particular, don't accept Google Play's "deferred" purchases until they are completed.
- SDK: For Google Play purchases, the client-side `IAPManager` utility now ignores purchases in the "deferred" state (`purchaseState` = 4).
- SDK: Don't log errors on the server when a large incident report is sent by the client at session start failure.
- Dashboard: The number of Concurrents shown on Overview page no longer counts clients that have not yet completed session handshake. This fixes the confusingly high number when Maintenance Mode is enabled.
- Dashboard: Fixed guild pages not having header icons (broken route meta data object).
- Idler: Configuring backend gateways in DeploymentSpec no longer allows overriding `EnableTLS` but will use the value from the primary configuration.

## Release 17 (2022-02-18)

### Added

- SDK: Added concept of `Client Platform` communicated by game clients to the server during the login flow.
- SDK: Support routing analytics events on the client to the application via the new `IMetaplayClientAnalyticsDelegate`. This allows easy streaming of events to 3rd party analytics libraries.
- SDK: Add `AnalyticsEventFirebaseConverter` helper class to easily convert analytics events for Firebase Analytics in the client. Only simple parameter types are supported for now.
- SDK: Add `PlayerModel.ServerOptions.DeletionRequestSafetyDelay` to allow inspecting deletion delay before issuing a deletion request.
- SDK: The SDK-side `MetaplayClient` now provides player-facing English-language error messages in `ConnectionLostEvent`.
- SDK: The SDK-side `MetaplayClient` now registers Firebase messaging tokens to the server if the Firebase Messaging package is detected.
- SDK: Added support for hot-loading GameConfigs when running in offline mode.
- SDK: Analytics events for client-side connection errors are routed to the new `IMetaplayClientAnalyticsDelegate`, and are also available in `ConnectionLostEvent`.
- SDK: Added incident reporting for exceptions thrown from client's `MessageDispatcher` listeners.
- SDK: Added `GameConfigBuildUtil` class that contains some useful utility methods for building GameConfigs (see Idler for usage examples).
- SDK: Add PlayerSynchronizedServerActions which are like Unsynchronized server actions, except they may modify checksummed field but the execution of the action may be delayed. These actions are issued with `PlayerActor.EnqueueServerAction`.
- SDK: New `DeploymentOptions` (requires Helm chart v0.3.1 to be populated): `AdminUri` for the public dashboard url and `ApiUri` for the public admin API uri (eg, for uploading game configs).
- SDK: Support for using MySQL 8.0 databases. The database instances must be migrated with in infra update.
- SDK: Add `DatabaseOptions.MaxConnectionsPerShard` for controlling the database connection pool size. The pool is applied to each shard separately, so total number of connections is NumShards*MaxConnectionsPerShard.
- SDK: If session negotiation fails due to client-side error, the error report is delivered to server immediately rather than having to wait for a successful session later on.
- SDK: Added LoginProtocolVersion to MetaplayCoreOptions to support possible future changes in login protocol.
- SDK: Added `MetaOfferInfoBase.RequireInAppProduct` that can be set to false for implementing soft-currency MetaOffers.
- Server: Added new metric `game_session_start_fails_total` for observing the number of succesfully logged-in connection that fail to start a session.
- Server: Fallback error page for the missing pre-built dashboard when trying to access it (by default at <http://localhost:5550>).
- Unity: Added debugging utility for spoofing client platform for the Unity editor.
- Unity: Add support for OAuth2 in Admin Api Authorization (e.g. GameConfig publishing).
- Dashboard: Added Entity Inspector for inspecting the raw data of a Persisted Entity. The tool can be used for inspecting how many bytes individual fields take in the database.
- Dashboard: Offers and offer groups now display total revenue, based on configured IAP reference price.
- Dashboard: Added new wrapper component MetaLazyLoader that stops components from loading and rendering when they are outside the viewport, reducing resource usage when the wrapped component is not visible to the user.
- Dashboard: Added support for automatically generating forms and views from C# types.
- Dashboard: Added page for safely viewing raw player details. This can be useful for viewing players with damaged data.

### Changed

- SDK: Upgraded dependencies, mainly: .NET 6.0.1, EFCore 6.0.1, Akka.NET 1.4.32.
- SDK: Add explicit references to packages `System.Net.Http` and `System.Text.RegularExpressions` to ensure new enough versions that have known vulnerabilities fixed.
- SDK: `FacebookLoginService` is split into `FacebookLoginService` which manages only Login API and `FacebookAppService` which manages only App Access Token.
- SDK: Facebook Limited Login key validation no longer blindly trusts keyset cache durations but attempts renewal every 5 minutes on cache miss. This improves reliablity on scheduled and unscheduled key rotations.
- SDK: Maintenance mode can now be enabled on a client platform basis.
- SDK: `AnalyticsEventRegistry` has been moved to shared code. The `AnalyticsEventSpec` is now a top-level class.
- SDK: Player-requested account deletions are now separated from Admin-initiated account deletions.
- SDK: A push notification campaign targeted only for a selected list of players no longer requires a full database scan.
- SDK: Replace RandomPCG constructors with static factory methods `RandomPCG.CreateNew()` for initializing to non-deterministic random state, and `RandomPCG.CreateFromSeed(ulong)` to initialize to a specific seed.
- SDK: Removed `NumEntriesToKeepInModel` from event log configuration in runtime options to simplify the implementation. Always keeping a number of entries in the entity model has turned out to be unnecessary.
- SDK: Reduce event log related bloat in player and guild models. The amount of metadata in the model no longer scales linearly with the number of persisted events.
- SDK: Persisted entity event log segments are now compressed.
- SDK: Changed entity event log retention to be primarily time-based. Maximum and minimum retention counts still exist for safety.
- SDK: To improve SDK vs user separation, new `PlayerModel`s are now initialized with parameterless constructor followed by an initializer method, instead of a parameter-taking constructor.
- SDK: `IPlayerModelServerListenerBase` has been renamed to `IPlayerModelServerListenerCore` and is no longer intended to be inherited by the game-specific `IPlayerModelServerListener`, but rather to exist alongside it in the model. Same applies to client listeners, and for `GuildModel`'s corresponding listeners.
- SDK: `MetaplayClient` is now a static class, with the `Initialize` method replacing the old constructor.
- SDK: Clients now support a `WireProtocolVersion` range. This allow migrating all projects to the new standard WireProtocolVersion 10 in the future.
- SDK: Action eligibility for execution by client and server is now marked with `ModelActionExecuteFlags` and is no longer dependent on type hierarchy. This enables actions that may be issued by both the server and the client.
- SDK: New player inbox implementation: mail item state is now stored separately from contents in `PlayerMailItem` classes. Existing mails are migrated to the new inbox in PlayerActor migration. The base `PlayerMailItem` class maintains generally useful state info about mail items in the form of timestamps.
- SDK: Player inbox mutating actions now use the new `PlayerSynchronizedServerAction` execution strategy and therefore the inbox data is no longer tagged `NoChecksum`.
- SDK: The `PlayerConsumeMail` action no longer deletes a mail item. The previously server-only action `PlayerDeleteMail` can now be issued from client as well instead.
- SDK: Mail items are now identifier by a globally unique ID of the mail contents in favor of the local inbox running integer.
- SDK: Development environments are by default limited to 5 database connections per shard. The intention is to detect problems with excessive database connections in dev environments before they go into production.
- SDK: Legacy uncompressed GameConfig delivery mechanism has been removed.
- SDK: Reordered login flow to check maintenance and developer bypass before LogicVersion.
- SDK: CloudCoreVersion.cs was moved from userland to SDK `MetaplaySDK/Backend/CloudCore/`.
- AdminAPI: If server rejects Localization update on `/api/localizations`, it now returns 409 with an error message instead of a generic 500.
- AdminAPI: If server rejects authenticated request due to a missing permission, it now returns 403 with an error message describing which permission it lacked.
- AdminAPI: When running a local dev server, the API port has been moved from 5000 to 5550. This was due to conflicts on port 5000. You can now find a locally running API server at <http://localhost:5550/api>.
- Dashboard: When running a local dev server, the Dashboard server port has been moved from 8080 to 5551. This was due to potential conflicts on port 8080. You can now find a locally running Dashboard server at <http://localhost:5551>.
- Dashboard: Cypress tests now have improved test skipping (by feature or by filename) for easier game specific forking and merging.
- Dashboard: Added new `MetaTeleport` component to dynamically change page header bar titles based on page content. Initially wired up to always display the current player name in the player details page.
- Dashboard: Subscriptions can now mutate data (eg: to fix issues with the data in one central place) before distributing it to the rest of the Dashboard.
- Dashboard: Segments and experiments that don't have display names or descriptions set now use placeholder text instead of showing nothing.
- Dashboard: Startup and authentication rewritten to improve initial loading experience.
- AdminAPI: Entity event log requests now use a cursor-based scanning API instead of specifying a range of log entry IDs.
- Idler: Migrate sample project to use the new streamlined integration APIs.
- Idler: Cleanups to the GameConfigBuilder workflows.
- Dashboard: Changed mails and broadcasts to use new generated views and forms.

### Fixed

- SDK: Fixed bug in session resource proposal code that sometimes caused a non-existing localization (id, version) pair to be reported.
- SDK: Fixed server delivering malformed GameConfig Archives if client did not support compression.
- SDK: Database throttle queues are now replica-specific. A single replica degrading will no longer cause throttling on other replicas.
- SDK: When a dynamic-content IAP purchase does not fully complete during a single session, it is now more robustly completed on a subsequent session, instead of leaving the store purchase in pending state and producing repeated warnings about missing dynamic content.
- SDK: Fixed an issue that prevents developer bypass during maintenance if client had been previously redirected to another environment.
- SDK: The `update-cloudcore-versions.sh` now gets invoked with an explicit `/bin/bash` prefix so that it doesn't need the executable bit set (it is easily lost when working on the files in Windows).
- Dashboard: Fixed the link to Grafana on the dashboard overview page.
- Dashboard: Improved medium-narrow browser window layouts to be more readable.
- Dashboard: Game Config detail page loads more quickly.
- Dashboard: Improved performance of entity event log cards when displaying a very large number of events.
- Unity: OfflineServer uses latest language version in source tree.

## Release 16 (2021-12-22)

### Added

- SDK: Introduce `EntityActorRegistry` which keeps metadata about all `EntityActors` and can be used to query it.
- SDK: Added `[JsonSerializeNullCollectionAsEmpty]` attribute to allow serializing null arrays and dictionaries as `[]` and `{}` in JSON.
- SDK: For server-side analytics, added support for setting custom labels (key-value pairs) to all emitted analytics events of an Entity.
- SDK: The connection fails with a new `TerminalError.PlayerIsBanned` error if attempting to log in with a player that is banned. Ongoing sessions are terminated with a new `SessionForceTerminateReason.PlayerBanned` reason. In `LifecycleDelegate` both are exposed as `ConnectionLostReason.PlayerIsBanned`.
- SDK: Added a client-side `IAPManager` for managing the flow of IAPs between the game, Metaplay, and Unity IAP. Previously, `IAPManager` existed in the Idler reference project.
- SDK: Development functionality for forcing activables (e.g. in-game events) into a specific phase (e.g. active) for individual players, for testing purposes.
- SDK: Mechanism for registering custom database maintenance jobs for `PersistedEntityActor`s using the `[EntityMaintenanceJob(..)]` attribute.
- SDK: GameConfigs are automatically compressed by server before delivering them to clients. This is backwards compatible. Compression can be controlled with `ContentDelivery` Runtime Option.
- SDK: Added an ExtendableEvent utility built on top of activables, for implementing in-game events whose activations can be extended while in review phase.
- SDK: Include client-facing game config version and experiment memberships in player incident reports.
- SDK: Added the possibility of marking a player as a developer, enabling them to log in during maintenance mode, execute development actions in prodcution environments and validate iOS sandbox IAPs.
- SDK: Added option of targeting an intersection of segments (must match ALL segments) in addition to previous behaviour of union (must match ANY segment) in broadcasts, notifications and experiments.
- SDK: Preliminary support for triggering broadcasts based on gameplay (analytics) events. Not enabled in dashboard by default.
- Dashboard: Added ability to view the differences between arbitrary game configs on the game configs diff page.
- Dashboard: Difference chunks on the game config diff page can now be collapsed to reduce noise.

### Changed

- SDK: Upgraded to .NET 6 and C# 10.0, ASP.NET 6, and EFCore 6.
- SDK: Switched to using `[Index]` attribute for the database index definitions.
- SDK: Split core-specific parts from `GameDbContext` into `MetaDbContext<TPersistedPlayer, TPersistedGuild>`.
- SDK: Move `PersistedPlayerSearch` into core where it belongs.
- SDK: Move core-required members from `PersistedPlayer` to `PersistedPlayerBase`.
- SDK: Move most of `ServerMain` into SDK core class `ServerMainBase`.
- SDK: When parsing multi-column arrays or lists in GameConfigs (noted with [ColumnNames] in brackets), fill empty cells between non-empty cells by parsing the empty string to the correct type. Empty cells at the end of collections are still ignored.
- SDK: Missing translation texts are now represented as "#missing#{TranslationId}" to distinguish missing translations from untranslated text.
- SDK: Removed `PlayerActor.CreatePlayerStateResponse()` as it's no longer needed.
- SDK: Support virtual database items in the core to allow untangling SDK code from userland when accessing `PersistedPlayers` or `PersistedGuilds`.
- SDK: Removed all `CreateDataResolver()` methods -- use the relevant `ISharedGameConfig` instead.
- SDK: Moved `PlayerActor.RegisterPlayerAsActive()` into `PlayerActorBase`.
- SDK: Introduced `PlayerActor.CreatePersisted()` for creating the game-specific `PersistedPlayer` object.
- SDK: Log the AdminApi request query strings as well.
- SDK: All `EntityActor` types must now have either `[PersistedEntityActor]` or `[EphemeralEntityActor]` attribute.
- SDK: `PersistedEntityActor` schema version is now defined via `[PersistedEntityAttribute(..)]`.
- SDK: Segment targeting for broadcasts, notifications and experiments now uses a PlayerCondition to be able to express segment and property requirement combinations.
- SDK: On Client, `Connection.Reconnect()` emits `DisconnectedFromServer` and processes its handler synchronously before starting to open the new connection.
- SDK: Banning a player now prevents logging in rather than executing actions.
- SDK: Configure deployment sharding topology via `ClusteringOptions.ShardingTopologies` and `TopologyId`.
- SDK: Remove some boilerplate from `GameDbContext` and remove `DatabaseBackendXyz` dependency on `GameDbContext`.
- SDK: `EntityRefresherUtil` and `EntitySchemaMigratorUtil` now query `EntityActorRegistry` instead of including metadata about EntityKinds.
- SDK: Entities eligible for Archive Importing and Exporting are marked by setting `[EntityArchiveImporterExporter]` for Actor. Player and Guild Actor are eligible automatically with the default Import & Export handlers.
- SDK: Entity Id remapping logic (during EntityArchive import) is moved from `ImportEntityHandler` into `(Player|Guild|*)Model.RemapEntityIdsAsync`.
- SDK: Game-specific AdminApi `GamePermissions` must now have `[AdminApiPermissionRegistry]`.
- SDK: Replaced the statically-referred game-specific `MetaDefines` by a `MetaplayCoreOptions` which the SDK discovers by reflection at initialization time.
- SDK: Entities eligible for Refresh and Schema Migration scan jobs are marked by setting `[EntityMaintenanceRefreshJob]` and `[EntityMaintenanceSchemaMigratorJob]` for Actor. Player and Guild Actor are eligible automatically.
- SDK: Register player deletion maintenance job with the new generic maintenance job mechanism: `[EntityMaintenanceJob("Delete", typeof(ScheduledPlayerDeletionJobSpec))]`.
- SDK: Moved most boilerplate of an empty `SessionActor` to `SessionActorBase`.
- SDK: Make AdminApi request logging configurable: by default, only log requests that take a long time when running server locally to quiet down spammy logging.
- SDK: Renamed `PlayerActor.OnNewOwnerSubscriber` and `OnOwnerSubscriberLost` to `OnNewOwnerSession` and `OnOwnerSessionEnded`. `OnOwnerSessionEnded` is now also called when session ends due to player being kicked.
- SDK: Removed `GuildActor.ValidateGuildCreationAsync` and replaced it with overridable `PlayerActor.ValidateGuildCreationAsync`.
- SDK: Renamed `ServerAction` into `UnsynchronizedServerAction` to better describe its behavior.
- SDK: Network threads no longer synchronously call Unity log handlers. Low-level network logs are instead buffered and flushed later on Unity thread.
- SDK: `ConfigArchiveEntry.Bytes` is now accessed via `ConfigArchiveEntry.Uncompress()`.
- SDK: `IJournalModelAdapter` (and the game-side implementations like `PlayerAdapter`) has been removed and its responsibilities refactored to various places.
- SDK: `GuildModelBase<...>` now implements `IGuildModelBase` and some of its methods. Accordingly, various methods in `GuildModel` are now defined as overrides instead of direct interface method implementations.
- SDK: Make offers in offer groups "sticky" by default: during an offer group's activation, an offer won't become unavailable if its offer-specific conditions become unfulfilled.
- SDK: Changed how connection rejection during maintenance mode works to allow for checking if a player is a developer first. This deprecates `ProtocolStatus.InMaintenance`.
- AdminApi: Moved time consuming fetching of database item counts from /status endpoint to its own endpoint.
- AdminApi: `[Consumes("application/json")]` is now applied on per-endpoint basis as ASP.NET 6 no longer matched any of the routes without explicit specifying the 'Content-Type: application/json' header, even where no content exists.
- Dashboard: Major overhaul of how time-based events (eg: player actions, audit logs, etc.) are shown.
- Dashboard: Cypress tests now fail if error or warning messages are printed out to the console while the test is running.

### Fixed

- SDK: Game-config-build-time debug validation of `MetaRef`s: Ignore indexers and getterless properties in the object tree traversal.
- SDK: When parsing enum-typed config keys, produce properly enum-typed objects instead of integers.
- SDK: In Unity editor, don't report a player incident when the connection is closed due to exiting play mode.
- SDK: Add check for GameConfigLibrary keys producing non-unique values from ToString(), which causes the duplicates to not show properly on the dashboard.
- SDK: Fix SDK initialization when a kind of activable is missing its MetaActivableSet type.
- SDK: Unity network error messages sometimes contain null bytes, which ended up in incident reports. They are now trimmed when generating the report.
- SDK: Replace deprecated crypto primitives with the .NET6 recommended `SHA256.Create()` etc., and `RandomNumberGenerator.GetBytes()`.
- SDK: More robust checks to not allowed PlayerActor or GuildActor to spawn for non-existent entities. At least an empty state must exist in the database.
- SDK: When JSON-serializing entity event log or audit log events, tolerate exceptions thrown from the `EventDescription` property getter.
- SDK: Fix case where Json-to-S3 Analytics sink would report an error and drop overflowing events if write buffer got more events than could fit into a single S3-written blob (by default 100k events).
- SDK: Database re-sharding fixed for items without a `[PrimaryKey]`.
- SDK: Fixed a responsiveness bug with database scan workers when work ticks took a longer time than the desired tick interval. This would sometimes manifest as the notification campaign system becoming temporarily unavailable after cancelling a campaign.
- SDK: Small miscellaneous fixes to incident report collecting & uploading: better amortization of uploads over time, better buffering of report headers in client, and global throttle for collecting all report types.
- SDK: Use shared `HttpClient` when generator network diagnostics reports, to reduce the chances of exceptions in its `Dispose()` method (caused by a known bug in Mono version used by Unity, should be innocuous).
- Dashboard: Fixed issue with MetaListCard sometimes showing an empty page when searching or applying filters on a list that spans multiple pages.
- Dashboard: Fix visualization of network latencies in incident reports.
- Dashboard: Developer mode toggle is now synced across browser tabs.

## Release 15 (2021-11-05)

### Added

- SDK: Admin API now includes '/api/echo' endpoint to aid in debugging API connection issues.
- SDK: Added development-only Admin API entrypoint '/api/testEntityAskFailure' for testing EntityAsk error propagation.
- SDK: Added support for writing server-side analytics events into a BigQuery table.
- SDK: Added support for custom context data to IAP analytics events.
- SDK: Added analytics events for IAP flow start on client, IAP flow abort on client, validation start on server, validation completion on server.
- SDK: Throwing an `EntityAskError` from `OnNewSubscriber` handler rejects the subscription and the error is thrown on `SubscribeToAsync` call site.
- SDK: Ability to configure whether individual analytics events should go into the owning entity's event log and/or external analytics event sink.
- SDK: Added an SDK-side MetaOffers feature, which improves on the Shop Offers example feature.
- SDK: Additional server-side logging of connection information, such as local port and total bytes received during a connection.
- SDK: Add SDK-side default implementations of various client-side integration components, such as `MetaplayClient` and `PlayerClientContext`.
- SDK: Added information about the TLS layer in network related Player Incident Reports.
- SDK: Improved client and server-side detection and incident reporting of a connectivity issue where session communication is unhealthy despite connection handshakes succeeding.
- SDK: Dashboard-related HTTP requests are now logged on the server, including their duration, to better monitor their performance.
- SDK: Add Unity Package Manager compatible package.json entry in the Client folder for ability to import Metaplay SDK from local directory.
- Reference project: Support Auth0 as authentication provider for the Unity Editor GameConfig workflow.
- Dashboard: `meta-time` split into separate `meta-time` and `meta-duration` components.
- Dashboard: Added an API subscription and caching feature to simplify and speed-up access to server-side data.
- Dashboard: `meta-list-card` now has a permission-property to show a message when permissions are missing.
- Dashboard: Added new, easier to read view for game configs. The same view also highlights variant differences for experiments.
- Dashboard: Add link to Grafana Logs on the main page (with sensible query string pre-filled).
- Dashboard: Integration hook for being able to customize reward types per context.

### Changed

- SDK: Experiments workflow streamlined and improved.
- SDK: Upgrade to Akka.NET v1.4.25: includes significant performance boost to Akka.Remote messaging.
- SDK: Don't allow null items or keys in GameConfigLibraries. Accidental nulls cause problems that are tricky to debug, so it's safer to not allow them.
- SDK: Removed Newtonsoft.Json.dll from Unity assets in favor of declaring the dependency as a Unity package.
- SDK: `PlayerEventInAppPurchased` schema bumped to version 2. Added new fields for store, product and context information.
- SDK: Segment size estimation now uses a single sample of players to evaluate instead of sampling the player pool separately for each segment.
- SDK: Segment size estimator is now a separate singleton actor. Asking for current segment estimate happens via EntityAsk-messages.
- SDK: Use more informative labels for expensive database queries to make it easier to pinpoint which systems are causing anomalies.
- SDK: In server logs, leave private-range IPv4 addresses (such as 10.X.Y.Z) unredacted for client connections.
- SDK: Add boolean argument `allowMissingTranslations` to `GameConfigHelper.SplitLanguageSheets()` to not throw errors on missing localizations.
- SDK: Game Config spreadsheet parser: throw an error when multiple columns have the same name.
- SDK: Game Config spreadsheet parser: throw an error when a column has no name yet is not fully empty.
- SDK: Added public setters for IPlayerModelBase GameConfig, IGuildModelBase GameConfig and IModel LogicVersion.
- SDK: Config data references contained within config data must now be wrapped in the new `MetaRef<>` type, which allows also forward references between config items.
- SDK: Use coarser histogram buckets for database latency metrics to keep number of metrics more manageable.
- SDK: Moved Metaplay SDK source files for backend components to the MetaplaySDK folder.
- Dashboard: Increased broadcast components reusability by allowing changing the item name ("broadcast" by default) via properties.
- Dashboard: Core tests refactored to utilise `data-cy` properties for finding elements.
- Dashboard: New notification and broadcast list page layouts for more consistent usability.
- Dashboard: Game config detail view's configuration list now looks more consistent and is a bit easier to use.
- Dashboard: Incident reports are now displayed more prominently in the player details page.
- Dashboard: Incident reports have an additional description about `NullReferenceException`s.
- Dashboard: The filtering and sorting utilities of various cards are now easier to open with a bigger click area.
- Dashboard: Cypress test runner updated to 8.6.0.
- Dashboard: Nicer error message when there is no game server connectivity.
- Dashboard: Major iteration on experiment pages to show more relevant information at-a-glace and easily control the various experiment states.
- Dashboard: Major iteration on the `meta-raw-data` component for better usability and rougly 300x performance improvement.
- Dashboard: The Dashboard NPM project has been moved to MetaplaySDK/Backend/Dashboard. Game-specific integration files remain in Server/Dashboard.
- AdminApi: Audit logs search endpoint split into simple and advanced versions, requiring different permissions.
- AdminApi: Always specify listen host/interface via runtime option `AuthenticationOptions.ListenHost` and `ListenPort` (launchsettings.json is ignored).
- Reference project: Reset database schema migration history.
- Reference project: Upgraded to Unity 2020.3.17f1.
- Reference project: Removed the Shop Offers example feature in favor of the new MetaOffers.

### Fixed

- SDK: Client incident reporting system now recovers from corrupted incident report database.
- SDK: Better error message for OrderedDictionary<> constructing from IEnumerable<> when there are multiple items with the same key.
- SDK: The generated serializer now gets recompiled when a field is changed to a property or vice versa. Previously, a runtime error might happen when using the serializer after such a change.
- SDK: HttpUtil now avoids large temporary allocations when executing large JSON requests.
- SDK: When writing analytics events to an external S3 bucket, an extra leading slash is no longer generated.
- SDK: In SessionActorBase, tolerate other actors' CastMessage-sent messages arriving outside a session.
- SDK: Fix notification campaign getting stuck when a single player's notification token count exceeds the send batch limit (100 tokens).
- SDK: Always assume DateTimes are read/written to database as UTC. Previously DateTimes were returned with DateTimeKind.Unspecified.
- SDK: Specify MaxLength for PlayerIncidents.Fingeprint, so it can be used in MySQL indexes.
- SDK: Add new database index for PlayerIncidents that makes querying incident reports by type much faster.
- SDK: Unhandled expections from Actions executed for Player Consistency Checks are no longer fatal and instead log a warning.
- SDK: Don't require ASP.NET developer certs to be generated by only listening on the HTTP interface. HTTPS is terminated in the load balancer.
- SDK: In AdminApi endpoints for GameConfigs, when json-serializing config patches, use the config entries' C# member names instead of the names given in the `[GameConfigEntry]` attribute. This fixes dashboard's visualization of experiments in cases where the two names differ.
- SDK: MetaActivableState.IsInReview: If a config change has moved the schedule into the past beyond the latest activation, don't erroneously treat it as in-review. This fixes a AdminApi/dashboard error for players with such activations.
- Reference project: Fix some NREs when shutting down the game.
- Dashboard: Fixed `meta-raw-data` previews showing unhelpful data for non-object data types.
- Dashboard: Fixed "Select Individual Players" targeting form text box being too narrow on most screen sizes.
- Dashboard: Incident loading state is now properly visualized instead of indicating that there are no incidents.
- Dashboard: Show incident report upload time instead to get a reliable clock for them (instead of device clock-based time of occurrence).
- Dashboard: Fixed right borders not always hiding on narrow screens when the parent container reorders from horizontal to vertical list.
- Dashboard: Fixed an issue when very long IAP receipt ID's would sometimes truncate instead of wrap on a new line.
- Dashboard: Fix 'View Logs' link in player login history.

## Release 14 (2021-09-02)

### Added

- SDK: Initial support for A/B tests, with the ability to define the content changes via the GameConfigs, ability to configure the Experiments from the dashboard, and all the scaffolding to assign players into the Experiments and Variants.
- SDK: History of uploaded GameConfigs in the dashboard, including the ability to diff between the versions and choose the currently active one.
- SDK: ServerGameConfig that contains GameConfig data that is available only on the server.
- SDK: GameConfigs now include Patches alongside the baseline data. Patches can be used to override the baseline contents and are used by the A/B tests.
- SDK: GameConfig archives can now be partially built, meaning that some parts of the archive are fetched from the server (and remain untouched) while other parts are built from the source data (eg, Google Sheets).
- SDK: Added a convenience PlayerId property to GuildMemberBase for conveniently getting the member's player id.
- SDK: [EntityAskHandler] methods now have the option of returning the value to automatically have it routed to the caller (previously, `ReplyToAsk()` had to be used). In that case, the method may not take the `EntityAsk` as an argument to avoid accidental double-replies.
- SDK: `[MetaOnDeserialized]` methods now can optionally take a `MetaOnDeserializedParams` parameter, which contains some deserialization context such as the `IGameConfigDataResolver`.
- SDK: Added support to MetaActivables for removing activables' config items without breaking the deserialization of player states containing those activables. Enabling this requires minor changes to the game-specific classes inheriting `MetaActivableState`.
- SDK: Ongoing activations in players' existing MetaActivable states can be configured to be affected by config changes. For example, changing a lifetime parameter can adjust the end time of ongoing activations, instead of the lifetime being fixed when the activation starts.
- SDK: In development mode, exception stack traces during login (for example due to deserialization error) are now delivered to client via `Message` string in `TransientError.SessionError`.
- SDK: Added `EntityAskError` serializable exception type for communicating controlled error situations from EntityAskHandler functions. These exceptions will be thrown at the remote callsite using the original type and they won't cause the handling actor to be terminated.
- SDK: Make SessionToken available in PlayerModel during a session. Include the token in core connection analytics events.
- SDK: Added MetaGuid type, which is a time-ordered globally unique identifier type.
- SDK: Added support for serializing GameConfigLibrary ID aliases in the GameConfig archive.
- SDK: GameConfig ID aliases can be entered in Google Sheets using a special "/Aliases" column that lists alternative IDs for a row.
- SDK: New BackgroundTaskActor allows running various one-shot jobs on the server.
- Dashboard: CS audit log events now show IP address and country of origin, and can be searched by IP or country.
- Dashboard: Added MetaClipboardCopy component to make copying text to the clipboard easier and more consistent.
- Dashboard: Added MetaCountryCode component to simplify display of country flags and names.
- Dashboard: Added conversion % of activations vs consumes to the activable details page (for example: events and offers).
- Dashboard: When viewing a Player whose Actor crashes (eg, due to broken migration code), a useful error message is shown.
- Server: Added optional maximum session length limit option `Session:MaximumSessionLength`.

### Changed

- SDK: Upgrade to .NET 5.0.9 (SDK 5.0.400).
- SDK: Upgrade dependencies, mainly EFCore 5.0.9, Pomelo 5.0.2, and Akka 1.4.25.
- SDK: Renamed old MainGameConfig to SharedGameConfig to emphasize that it's the part of GameConfigs that is shared between the client and the server.
- SDK: GameConfigBuilder scaffolding has been significantly upgraded, to add support for ServerGameConfigs, partial builds, building on the server, extract patch data for A/B tests, and to make integration to Unity projects simpler.
- SDK: Errors during the handling of EntityAsks are now returned back to the caller as EntityAskError exceptions, including the error type, message, and stack trace of what went wrong.
- SDK: Serializing MetaDuration as JSON now uses the format 'd.hh:mm:ss.FFFFFFF' to make it more easily consumable in the dashboard and analytics event pipelines.
- SDK: Overwriting entity event log segments no longer causes a (benign) error message.
- SDK: Tweaked player deletion sweep to make it less resource hungry.
- SDK: Player deletion sweep now happens at a configurable time of the day so that it is more predictable.
- SDK: Checksum mismatch handling no longer rolls back model state in time. This avoids failing potential sanity or consistency checks when rolled back state catches up with the present again.
- SDK: The JSON/S3 analytics event sink can now be configured with Runtime Options.
- SDK: Moved BlobStorageOptions to CloudCore, so it can be used by other systems in CloudCore (eg, analytics event sinks).
- SDK: Replaced the boolean `ServerCommitIdMustMatch` with an enum `ClientServerCommitIdCheckRule`.
- SDK: As part of the new AdminApi entrypoints for dealing with GameConfigs, the existing publish entrypoint paths have changed.
- SDK: The GoogleSheetFetcher utility has been moved to the Metaplay SDK Core to pave way for running GameConfig builds tasks on server.
- SDK: On ModelJournal desync where the current Model is no longer deterministically reachable from the latest checkpoint (non-deterministic actions), the current version is no longer reconstructed from action log. Instead, latest snapshot is synchronized to the current version, when possible.
- Dashboard: Project lockfile updated to Node 16.x and NPM 7.x.
- Dashboard: Docker builds now use Node 16.x (node:16-alpine) base image.
- Dashboard: Cypress test runner updated to 8.2.
- Dashboard: Now reports more detailed information about API errors to the user.
- Dashboard: Activable detail pages now include uptake and total uptake.
- Dashboard: Added confirmation modals when deleting Broadcasts and Notifications.
- Dashboard: Show humanized usernames in audit logs for auth_not_enabled and no_id.
- Dashboard: The previously fire'n'forget `MetaplayBaseController.TellEntityAsync()` now uses EntityAsk pattern to wait for a success response or an error that may have occurred during the handling of the message.
- Dashboard: Show any errors that happened while executing the various HTTP requests into the server.
- Dashboard: New, mobile friendly notifications for maintenance mode and game config updates.
- Reference project: Bots now use randomized names to make them appear more realistic.
- Reference project: New EditorWindow-based GameConfig builds UI that allows specifying partial builds and overriding spreadsheet.
- BotClient: Fix CdnBaseUrl to not expect `/GameConfig/` suffix, to match Unity client behavior.

### Fixed

- SDK: Improve AdminApiActor stability when routing EntityAsks. EntityAsks are no longer blocking and exceptions are routed to the caller instead of crashing AdminApiActor itself.
- SDK: Fix json analytics sink failing if GZip compression was not used.
- SDK: Fix MetaDuration.ParseExactFromJson() invalid input handling.
- SDK: Fix DB latency metrics including the time in the throttle queue. This could cause DB latencies appear very high when throttled background tasks were running.
- SDK: SegmentSizeEstimator now makes its queries sequentially, to smooth out the database accesses over time, avoiding the database latency spikes that happened before.
- Dashboard: Fix 'Open Grafana' link on front page to be enabled.
- Dashboard: Fixed not being able to remove broadcast and notification localization languages
- Reference project: Fix time handling innaccuracy in producer ticking.

## Release 13 (2021-06-30)

### Added

- SDK: New PlayerActorBase methods ShouldUpdatePlayerSearch() and GetPlayerSearchString() that can be overridden to provide custom player search strings.
- SDK: Added `MetaDatabase.InsertOrUpdateAsync()` for inserting a new or updating an existing record in a database table.
- SDK: Added `GameConfigLibrary.RegisterAlias()` to allow config ID aliases. This can be used for renaming config entries so that data serialized with old configs still continue to deserialize properly.
- Dashboard: Frontend errors and warnings now spawn notification messages so they are easier to notice without having the browser console open!
- Dashboard: New GameTypeDisplay generic component for rendering user-derived types based on integration.
- Dashboard: Segment size and audience size estimates now show how old the estimates are with a tooltip.
- Dashboard: The detail page of an activable displays how many players have activated it at least once.
- Dashboard: Game specific SASS files for defining your own variables and styles.
- Dashboard: Added icons next to players who have purchased IAP to make paying players easier to identify.
- Dashboard: Notifications detail page now shows audit log entries.
- Dashboard: Devices & Social Auths card on Player Details page now shows the date that each auth method was attached.
- Dashboard: Added ability to remove individual auth methods from a player.

### Changed

- SDK: Transient errors in IAP validation are no longer treated as successful. Instead, the server periodically retries such validations. This is transparent to the client, and the `Status` of the purchase will remain `PendingValidation` until it has been validated.
- SDK: `[MetaOnDeserialized]` methods are now called in class hierarchy order, base class first. The attribute is now forbidden on non-final virtual methods.
- SDK: The `[MetaActivableGameConfigLibrary]` and `[MetaActivablePlayerSubModel]` attributes have been replaced by `[MetaActivableConfigData]` and `[MetaActivableSet]` attributes, which should now be put on the config and state classes rather than on members of MainGameConfig and PlayerModel.
- SDK: Player search now supports searching by player ID in case-insensitive fashion.
- SDK: Removed PlayerModelBase.IsNameSearchValid and added PlayerModelBase.SearchVersion for tracking if the player name search database entries are up to date.
- SDK: Classes deriving from `MetaActivableState` now need to implement a getter that returns the id of the activable. The id is used by the SDK for collecting statistics about activables.
- SDK: On client, when server connection is lost and session resumption is done, consider the connection healthy once again only after server is known to have received the messages so far.
- SDK: On server, when a game connection ends, log information about remaining incoming data.
- SDK: On client, repeated connection resumptions, even if successful, trigger backup port cycling behavior.
- SDK: On application suspend, MetaplaySDK may block the app for up to 100ms for any pending messages to be delivered to the socket.
- SDK: Moved the Server/Game.Server/global.json file to Server/ to enable it on all .NET Core projects.
- Dashboard: Added a link to the referred offer in the "additional conditions" list of the offer details page
- Dashboard: Added more consistent styling and links to referenced segments in the individual segment conditions card.
- Dashboard: Notification details page's start time text label now has a tooltip.
- Dashboard: Ongoing work to generalise various components into reusable forms with opinionated styling to keep the overall UI layout more consistent.
- Dashboard: DynamicPurchaseContent.vue and ResolvedPurchaseContent.vue removed, replaced by new integration API addPurchaseContentType().
- Dashboard: Added confirmation modal when deleting player mail.
- Dashboard: Player's total IAP spend is now calculated server-side instead in the browser.

### Fixed

- SDK: Added stricter checks in Google Play IAP validation to disallow replaying the same receipt with different transaction IDs.
- SDK: MetaActivableSet.TryGetVisibleStatus: Make player's current UTC offset not affect already-existing active and in-review activables. Fix in-review status to report the relevant (previous) schedule occasion, not the next.
- SDK: On client, fix DisconnectedFromServer not being emitted if connection was closed due to application being suspended for too long, or if manually closed.
- Dashboard: Fixed deleted guild background color rendering on wide screens.
- Dashboard: Fixed sort order in meta-list-cards
- Dashboard: Fixed manual login UI flashing on initial load when auth0 is not configured.
- Dashboard: Fixed issue when multiple meta-collapse items shared the same id.
- Dashboard: A meta-collapse component that has nothing in the collapse slot no longer shows the clickable "hand" icon when moused over.
- Dashboard: Fixed overflowing layout when viewing database items of a large database.
- Dashboard: Fixed overflowing layout caused by very long IAP transaction receipts.

## Release 12 (2021-06-04)

### Added

- SDK: Serializer now supports the 'char' and 'char?' types (serialized as unsigned 16-bit integer).
- SDK: Augmented player segmentation utilities to support segmenting based on more than just integer-typed player properties.
- SDK: Added methods of SIP (Session Initiation Protocol) as well-known prefixes to server's detection of well-known traffic.
- SDK: Added a health check request/response to the client protocol to be used by monitoring services.
- SDK: Added more debugging diagnostics to login messages to help investigate connectivity issues.
- SDK: Added MetaDuration.ToSimplifiedString, which excludes some of the least significant units.
- SDK: Added PlayerModel.GameOnSessionStarted method, for custom model code to run specifically when player logs in and starts a session.
- SDK: Added support for targeting notification campaigns based on player ID list and/or player segment, similarly to broadcasts.
- SDK: Facebook Login validation now support OpenID Connect tokens. This allows use of Facebook Limited Login.
- SDK: Added support for Facebook Deauthorization Callback url. If enabled, Facebook users deauthorizing the app on Facebook will get their Facebook account deassociated with their game account.
- SDK: Preliminary support for collecting analytics events from the server (including shared logic) for the purposes of pushing it to an external storage.
- SDK: Extended event logs to support guilds, and implemented basic SDK-side events.
- SDK: Moved event logs' event and segment retention count configuration to server-side RuntimeOptions. Previously they were in virtual properties of the event log class. This also removes the need for a game-defined subclass of PlayerEventLogBase.
- SDK: Various improvements to the utilities for activables, to further aid in defining game-specific events.
- SDK: Made the SDK more aware of game-specific activables. Declaring game-specific kinds of activables with simple metadata attributes enables the SDK to visualize them automatically in the dashboard, without requiring game-specific modifications to the SDK code.
- SDK: Added `[MetaOnMemberDeserializationFailure]` attribute for member-level deserialization failure handling.
- SDK: Added `MetaplaySDK.RunOnUnityThreadAsync` for deferring `Func<>`s to Unity thread.
- SDK: Added `MetaplaySDK.LocalizationManager` to manage OTA updates to localization files.
- SDK: Added support for Guild Invite Codes to the guilds framework.
- SDK: Added Guild information to the Unity Inspector of the MetaplaySDKBehavior component. Select the `MetaplaySDK` in the running editor to observe guild state.
- Reference project: Added two example event features, implemented as activables: "Happy Hour" and "Special Producer Event".
- Reference project: Extended the existing "Shop Offer" feature to leverage the extended SDK-side support for activables.
- Dashboard: Added underlines to dates and durations that have a tooltip to make them more obvious.
- Dashboard: New `meta-time` component that replaces the previous `time-ago` and `duration` components.
- Dashboard: Added count of how many players have received each broadcast message.
- Dashboard: Added list of broadcasts to player details page.
- Dashboard: New dashboard component system that supports lazy loading and convenient game-specific overrides.
- Dashboard: New game-specific integration interface in game_specific/integration.js, existing game-specific elements moved into the integration layer.
- Dashboard: Added location to latest login list on player details page.
- Dashboard: Useful out-of-the-box visualization of game-specific activables (such as events and offers). Both the general configuration and scheduling of activables can be viewed, as well as per-player state of each activable.

### Changed

- SDK: Moved all CloudCore and Game.Server sources to either Metaplay/ or Game/ folders, for Metaplay core sources and game-specific sources, respectively.
- SDK: Update server dependencies: Akka.NET v1.4.18, AWSSDK v3.7, FirebaseAdmin v2.1.0, JSON.NET v13.0.1, JWT v8.2.0, YamlDotNet v11.1.1, Parquet.Net v3.8.6, Google.Apis v1.15.0.
- SDK: Upgrade & pin .NET Core SDK image to 5.0.300 and runtime image to 5.0.6 to avoid accidental version drift.
- SDK: `ConfigParser.RegisterParseFunc` is now private. Config library reference id parsers are registered automatically. Custom config parsers must be now be registered in CustomConfigParsers.RegisterCustomParsers (called from LogicInit) with `ConfigParser.RegisterCustomParseFunc`.
- SDK: Serializer generator now avoids producing overloads of serializer-internal methods, decreasing the compilation time of the generated serializer.
- SDK: Refactored the database: improve core-userland separation, support priority levels for database operations with throttling for low-priority queries, separate schema migration and re-sharding code into its own class.
- SDK: Removed the `Global` singleton registry class, along with all usage of it.
- SDK: Use BatchGet in GoogleSheetFetcher to significantly speed up the fetching. Suffix methods with Async and add CancellationTokens to methods.
- SDK: The GoogleSheetFetcher is now only compiled for Editor builds.
- SDK: MetaRecurringCalendarSchedule now uses a MetaCalendarPeriod-based recurrence (such as 5d) instead of a fixed set of supported recurrences (such as Monthly). Only one non-zero unit is supported in the recurrence.
- SDK: InitialPlayerState no longer depends on game specific PlayerModel class.
- SDK: New attributes to mark EntityActor methods for handling incoming commands/messages: `[CommandHandler]`, `[MessageHandler]`, `[EntityAskHandler]`, `[EntitySynchronizeHandler]`, `[PubSubMessageHandler]`. These replace the old magic-named methods `HandleCommand()`, `HandleMessage()`, etc. The magic-named methods are no longer allowed (to avoid accidentally relying on it).
- SDK: Optimize memory allocations from StringId interning when value already exists.
- SDK: Replace the `GetAll` method in `GameConfigLibrary<TKey, TInfo>` (for `IGameConfigLibrary`) with an `EnumerateAll` method that doesn't construct a new collection, and a `GetInfoByKey` method for getting a single item.
- SDK: Added `PlayerModelBase.PlayerId` to make it predictably available.
- SDK: Replace the `IOnDeserialized` interface with a `[MetaOnDeserialized]` attribute for methods, which supports on-deserialization hooks also for nested objects instead of only the top-level deserialized object.
- SDK: MetaMembers of an object are now serialized in ascending tag id order, allowing to retain serialization checksum compatibility while changing member declaration order or moving members between a base class and a derived class.
- SDK: Renamed PlayerModel.GameOnStateRestored to GameOnRestoredFromPersistedState and updated comments to clarify that it's called whenever actor is woken up, not just at player login.
- SDK: Renamed PlayerModel.SessionStartTime to TimeAtFirstTick, since it's not the start time of the actual game session (if any).
- SDK: Changed BroadcastMessageParams to refer to abstract BroadcastMessageContents to support game-specific broadcast message contents. The old implementation that creates MetaInGameGenericPlayerMail mail elements is re-introduced as GenericBroadcastMessageContents and GlobalState migration code has been added to migrate old broadcast messages.
- SDK: Move broadcast target evaluation to work with more generic IPlayerFilter, so that logic can be shared with notifications.
- SDK: The PlayerEventLog feature is now generalized to multiple entities (ie, Guilds) and renamed to Analytics Events.
- SDK: When using `MetaSerializableFlags.ImplicitMembers`, a `[MetaImplicitMembersRange(start, end)]` is now required on base classes with data members. It is optional on the ultimate concrete class, but if omitted, `[MetaImplicitMembersDefaultRangeForMostDerivedClass(start, end)]` must be present on a base class or interface. The purpose of these range allocations is to allow adding members to base classes without changing the implicitly-tagged members on derived classes.
- SDK: The deserializer now checks that the correct WireDataType is encoded also for builtin types (such as WireDataType.VarInt for int, and WireDataType.String string). Previously, the WireDataType for basic types was not checked, and invalid input would cause the deserializer to attempt to parse garbage.
- SDK: Player deserialization no longer fails when a single event log entry payload fails to deserialize. The erroneous payload is instead replaced by a placeholder indicating the failure.
- SDK: PrettyPrint now hides properties that are explicitly implemented from interface, matching default C# behavior.
- SDK: PrettyPrint now hides properties that have `[IgnoreDataMember]` in any step of the ancestor chain.
- SDK: PrettyPrint and JSON serializer now support all types of `[Sensitive]` fields, not just strings.
- SDK: Admin API now requires that POST/PUT requests set their content-type headers to application/json.
- Reference project: Upgrade to Unity 2019.4.24f1 (LTS).
- Reference project: Use batch-fetching of Google Sheets when building MainGameConfig.
- Reference project: MainGameConfig and Localizations are now built from Unity menu (Idler/Build xyz).
- Reference project: MainGameConfig and Localizations publishing to server is also now triggered from Unity menu (Idler/Publish xyz).
- Reference project: MainGameConfigBuilder is now its own class, to more clearly separate the runtime and build-time separation.
- Reference project: Cleanup `PlayerModel`. Remove `PlayerAvatar`: move player profile info into `PlayerModel` and resources into `PlayerWalletModel`.
- Dashboard: Guild members list is now a meta-list-card with search and sorting utilities.
- Dashboard: Top incidents card is now a meta-list-card with filtering, search and sorting utilities.
- Dashboard: Overview entry removed from sidebar in favour of a header logo link.
- Dashboard: Added ability to launch player deletion job from scan jobs page.
- Dashboard: Support src/<game> integration folders in place of src/game_specific.
- Dashboard: Routes and sidebar navigation now configured together in `navigation.js`.
- Dashboard: Random Players and Guilds list are now hidden unless user is in development mode.
- Dashboard: In-game mail and broadcast content display and editing split into separate components to allow overriding.
- Dashboard: Improvements to how segments are shown in the Dashboard, including overhaul of the segment detail page and consistent segment lists in other pages, such as the player details page.

### Fixed

- Dashboard: Removed duplicate created date labels from the player overview card. Oops!
- Dashboard: Added missing permission tooltips to audit log and incident log related links.
- SDK: Audit log events for player mails now include the correct mail ID.
- SDK: Fixed bug in `ClusterConfig.GetNodeShardIds()` which caused some node loss to not properly trigger Entity subscription loss events with some cluster topologies.
- SDK: Fixed precision issues in MetaTime.FromDateTime and MetaTime.ToDateTime for large dates by using integers instead of doubles.
- SDK: Support Nullables generally in JsonConvert, instead of hard-coding support for Nullable<MetaTime>.
- SDK: Serializing MetaTimes to JSON now includes sub-second resolution (previously was rounded to seconds).

## Release 11 (2020-04-16)

### Added

- SDK: Serializer now supports Nullable versions of serializable enums.
- SDK: Database scan job system now support priorities for jobs, such that a high-priority job can cause a low-priority job to be paused. For example, notification campaign jobs have higher priority than scheduled player deletion jobs.
- SDK: Database scan jobs for triggering schema version migration for all players and guilds.
- SDK: Game client now detects and reports ongoing maintenance even if server has been shut down. The status is fetched from a hint file in CDN.
- SDK: Server support for validating Facebook Login social login claims.
- SDK: Broadcast messages can be targeted by player ID or player segmentation.
- SDK: IAdminApiDataCache service for controllers for cached data needs in admin API.
- SDK: Provide segment size estimates by periodic sampling from the database.
- SDK: New audit log event target types for broadcasts and notifications.
- SDK: New http entrypoint for bulk validating players by ID, used by message audience component in dashboard.
- SDK: NotificationCampaign options section for specifying throughput parameters of notification campaigns.
- SDK: Support added for API endpoints to be rooted under paths other than /api.
- Dashboard: Export Player admin action now has a download button and preview and copying is disabled on very large exports to prevent browser timeouts.
- Dashboard: Import Game Entities and Overwrite Player can now use a file as a source.
- Dashboard: Raw data component now has collapsible sections to hide large data blobs for better UX and performance on some browsers.
- Dashboard: New "Scan Jobs" page to view and manage jobs.
- Dashboard: New MetaListCard and MetaCollapse components that make it much easier to quickly build consistent looking UI elements with utilities like searching, filtering and sorting.
- Dashboard: New MessageAudienceForm component for targeting broadcasts (and eventually notifications).
- Dashboard: New LocalizedMessageContent component for providing text content to messages (mail, broadcast, notification).

### Changed

- SDK: Remove obsolete InMemoryLogger.
- SDK: Elements of MetaSerializable Enums are checked to be unique. For example `enum { A=1, B=1 }` is no longer allowed.
- SDK: Player event log refactored to remove game-specific rendering code from the Dashboard.
- SDK: Include the binary-serialized client/server states (decoded into plaintext) in client log if a Desync happens. Helps debugging in cases where state diff is empty for some reason.
- SDK: Command line arguments are now Case Sensitive.
- SDK: New command line short-hands for specifying log level, eg, `dotnet run -LogLevel=Information`, for enabling metrics (`-EnableMetrics=true`), setting remoting port (`-RemotingPort=6000`), and for setting sharding config (`-ShardingConfig="path/to/config.json"`).
- SDK: Forward slash syntax for command line arguments (`foo.exe /Arg`) is no longer supported, and is an error. Use -Short or --Section:LongForms instead.
- SDK: Dashless syntax for command line arguments (`foo.exe Foo:Bar=1`) is no longer supported, and is an error. Use -Short or --Section:LongForms instead.
- SDK: Specifying same argument in command line twice is now an error, regardless whether the value is the same or not. This applies to short-hands as well, i.e. `.. -LogLevel=Information  --Logging:Level=Information` is an error.
- SDK: Specifying unrecognized command line arguments is now an error.
- SDK: `MetaplaySDK.ScheduledMaintenanceMode` is now exposed via `MetaplaySDK.MaintenanceMode`. To handle all situations in which this value is updated, a helper `MetaplaySDK.MaintenanceModeChanged` event has been added.
- SDK: Silence some unneeded Debug level logging to reduce overall volume.
- SDK: Remove explicit use of GeoTrust certificate: Apple has now update their certificates, so this hack is no longer needed.
- SDK: Don't log errors from server's connection code when packet parsing error is caused by well-known kinds of traffic (specifically, HTTP requests).
- SDK: If AdminApi (Dashboard), or any other service actor, fails to start during server startup sequence, the server is terminated immediately instead of waiting for a 5 minute timeout.
- SDK: Undo hack in segmentation info returned by `SegmentationController.GetSegmentation()` and provide per-segment info in a single list of segments.
- SDK: Add segment size estimates to segmentation info.
- SDK: Server now force terminates on with a single Ctrl-C. Previously server required two Ctrl-C signals.
- SDK: Upgrade to .NET 5.0.5 in Dockerfiles: fixes the NuGet certificate problem.
- SDK: Add `DeploymentOptions.RequiredMetaplayVersion`, so infra can indicate a minimum required version that it is compatible with. Replaces the old `DeploymentOptions.SupportedRelease`.
- Metrics: `game_entity_ask_duration` measures time of `EntityAskAsync` calls and it now includes the delay of the Entity getting rescheduled for execution (in addition to the message delivery and processing durations). This can increase observed durations very slightly.
- Metrics: Fixed `metaplay_diagnostics_ping_timeouts_total` rarely counting succesful pings as expired.
- Dashboard: Player and guild searching by name are now much faster, as the searches are now performed using a suffix table that can efficiently utilize an index.
- Dashboard: Most list elements have been refactored to use the new MetaListCard component and its utilities.
- Dashboard: Various fixes and updates to broadcasts UI, removed WIP disclaimer.
- Dashboard: Show Metaplay release in Runtime Options / Deployment.

### Fixed

- SDK: Support deserialization of types with a non-public parameterless constructor also in Unity Editor.
- SDK: Understandable error messages for missing LogicInit.Initialize() or MetaSerialization.Initialize() when they are used.
- SDK: Fix SearchEntitiesByIdAsync() to use prefix search instead of substring search (improves performance significantly).
- SDK: Fix comparison methods of DynamicEnum<> to account for null arguments.
- SDK: Make serializer work also for private properties and fields of base classes.
- SDK: Fixed bots to be able to re-create their Player states in case they're lost from the database.
- SDK: Assign PlayerModel's and GuildModel's GameConfig, LogicVersion and Log properties in the actors right after loading the persisted model, so that they're available already in MigrateState.
- Dashboard: Fixed mobile headerbar overflow when sidebar is open.
- Dashboard: Card showing incidents from the last 24 hours no longer times out if there are too many incidents. The sample size is reduced automatically if card would sample too many incidents.
- Dashboard: Fixed page position not always starting from the top on most browsers.
- Dashboard: Fixed errors in the incident report client logs sometimes not being highlighted.
- Dashboard: Broadcast delete uses "edit" permission.

## Release 10 (2020-03-10)

### Added

- SDK: Support for dynamic-content IAPs, where the content of a purchase is decided at runtime. Can be used e.g. for dynamic shop offers.
- SDK: Support recording the resolved contents of an in-app purchase, so that they're remembered for customer support purposes even if IAP configs later change.
- SDK: Keep current session's device id as a transient member in PlayerModel.
- SDK: MetaplaySDKBehavior now shows connection status and tools to simulate bad connections in Unity Editor.
- SDK: Ensure that client-driven PlayerModel.CurrentTime stays within accepted bounds from server wall clock (configurable via PlayerOptions.ClientTimeMaxBehind and ClientTimeMaxAhead).
- SDK: Server support for validating Sign in with Apple and Google Sign-In social login claims.
- SDK: Support associating each Firebase Messaging token with a specific device, allowing to properly avoid extraneous notifications after detaching a device from an account.
- SDK: Notification campaigns now collect more Firebase error messages besides just `MessagingErrorCode` to better help diagnose configuration problems.
- SDK: New server RuntimeOptions system with Options.xxx.yaml, hot-loading, classes defined locally, and Runtime Options page on dashboard.
- SDK: Notification campaigns now clean up unregistered Firebase messaging tokens.
- SDK: Add EntitySynchronize, a new EntityAsk-like communication primitive that allows arbitrary number of messages to be exchanged in both directions.
- SDK: Player IP geolocation using MaxMind's GeoLite2 Country database. `PlayerModel.LastKnownLocation` indicates the country of the latest login. Disabled by default.
- SDK: EntityActor.ExecuteOnActorContextAsync method for enqueuing operations on actor context.
- SDK: Support new `HandlePubSubMessage(EntitySubscriber, Message)`, `HandlePubSubMessage(EntitySubscription, Message)` handlers in entity actors for handling subscription and subscriber messages. If new handlers are not defined, the normal `HandleMessage` and `OnUnknownMessage` handlers are used as previously.
- SDK: Support a binary storage format for GameConfigs.
- SDK: Support a binary storage format for localization configs, and use it by default instead of the legacy csv.
- SDK: SpreadsheetParser utility for parsing config sheets in an extensible manner.
- SDK: Types in MetaSchedule.cs for dealing with recurring calendar schedules, and a SpreadsheetParserPass for parsing them.
- SDK: Utilities for "activables", i.e. game logic items that involve time-based activation, expiration, and player segment targeting. Intended to be a component in liveops features such as special offers and events.
- SDK: Version compatibility checks for infrastructure and Helm chart versions.
- SDK: Add PlatformOptions.EnableAppleAuthentication and .EnableGoogleAuthentication flags that can be disabled to avoid contacting the relevant servers. Useful for deploying the server in areas where connections to Apple/Google servers can't be established (eg, China).
- SDK: Server now logs the client-sent game version (Application.version from Unity) for each connection, and stores it in the player's login history. The version from the player's first login is also stored in PlayerModel.Stats.
- SDK: Add metrics about database scan jobs.
- SDK: Players list API (/api/players) now reports player deserialization errors in the response, instead of filtering out the erroneous players.
- Dashboard: Cluster sharding configuration is now shown on the Environment tab.
- Dashboard: Player Incidents now have their own tab.
- Dashboard: Added a customizable game icon to the top left corner.
- Dashboard: Alternative visual style in production environments.
- Dashboard: Ability to assume different user roles for testing Dashboard permissions.
- Dashboard: Added audit logging for actions that are performed through the Dashboard.
- Dashboard: Player incidents can now be viewed by type.
- Reference project: Added connection health indicator (shown when connection is in unhealthy state).
- Reference project: "Special offers" example feature using the new utilities for dynamic-content IAPs, calendar schedules, player segmentation, and activables.

### Changed

- SDK: Upgrade to .NET 5.0.4.
- SDK: Upgrade to Akka.NET v1.4.16, which significantly improves networking performance for Akka.Remote and reduces idle CPU usage.
- SDK: Serializer now supports Nullable versions of primitives, for example `int?`.
- SDK: `CsvReader` now checks that each cell is fully consumed by its parser, and throws if it's not. This guards against buggy parsers and typos in configs. This is a potentially breaking change.
- SDK: The constructor `OrderedDictionary(IEnumerable<KeyValuePair<TKey, TValue>>)` now throws if the enumerable contains multiple key-value mappings with the same key, instead of keeping the last one.
- SDK: Game Config storage options are replaced by a general-purpose `BlobStorageOptions`, supporting also a separate private S3 bucket in addition to the public CDN S3 bucket.
- SDK: Clean up server application start sequence.
- SDK: Roles and permissions overhauled and now managed through Options.xxx.yaml.
- SDK: Server now initializes Serilog before anything else so it can always be used. It is re-initialized with proper configuration after it has been parsed.
- SDK: `TransientError.Unknown` has been removed and replaced with `TransientError.TlsError` that contains more information of the TLS handshake failure. Incorrect message order in the login sequence is now a `SessionError`.
- SDK: Moved MasterDatabaseVersion into DatabaseOptions.MasterVersion. Add NukeOnVersionMismatch (defaults to false) for extra safety to protect against accidental resets.
- SDK: Server now enforces size limits also for the uncompressed packets and incident reports received from the client, not just compressed.
- SDK: Increased the MaxStringSize and MaxByteArraySize deserialization limits to 64MB.
- SDK: Refactored functionality of GameConfigBuilder to have it less strongly tied to Unity.
- SDK: GameConfigLibrary now holds the items in an OrderedDictionary (instead of Dictionary) in the source configuration order.
- SDK: The UTC offset range in PlayerTimeZoneInfo was changed from -12h...14h to -18h...18h for consistency with Noda Time.
- SDK: PlayerModel.TimeZoneInfo now persistently stores the latest info, instead of only being available during a session.
- SDK: MetaActionResult is now a class, so it's possible to include richer information about some errors. Common errors are declared as `static readonly` members, to avoid allocation overhead.
- SDK: All server and botclient configuration has been migrated to the new RuntimeOptions system, including switch to Options.xx.yaml (from old Config.xx.json). The Options are all visible in dashboard under 'Runtime Options' tab for easy viewing, including the source where they are defined.
- SDK: EntityId.Kind no longer throws even on invalid or illegal values, but returns an EntityKind with .IsValid == false. EntityId.IsValid now also checks for the EntityKind.IsValid as well. This makes the message routing code more robust against malformed EntityIds.
- SDK: GameConfigBuilder has been refactored: content-specific steps are now part of MainGameConfig, and GameConfigBuilder is mainly a simple Unity stub.
- SDK: GameConfigArchives are now persisted in binary by default. Old csv/json can still be used where needed, but migrating to binary format is recommended.
- SDK: MainGameConfig is now persisted as single .mpa archive file by default. Old multi-file archives can still be used.
- SDK: PlayerModel's SDK-side parts are now separated into a PlayerModelBase class, which the game-specific PlayerModel inherits.
- Dashboard: Sidebar Vue file refactored with a new `SidebarNavGroupItem` component for better legibility and easier maintenance
- Dashboard: Display large numbers as abbreviated (eg, 1.24M) to make them more readable.
- Dashboard: Player event log displays event timestamps based on game logic time by default, instead of server's real time. This is consistent with the current behavior of the IAP history display.

### Fixed

- SDK: MetaTime.ToString() now properly pads numbers and uses dashes to separate date, eg: `2020-12-15 03:00:02.468 Z`.
- SDK: UnhandledExceptionError stack traces are now limited to 8kB.
- SDK: Fixed integer overflow in MetaDuration.FromSeconds(F64) when duration was longer than 25 days.
- SDK: Fixed issue where SDK reported Unhealthy network state even after the unrelying network returned to good health.
- SDK: Don't allow [MetaSerializableDerived] on abstract classes.
- SDK: Don't try to upload incident reports when in Offline Mode.
- SDK: Fix duplicate call to `EntityActor.Initialize()` for `PersistedEntityActor`s.
- SDK: Increase timeout in database re-sharding query to avoid problems with large data sets.
- SDK: Fix some potential race conditions in server initialization.
- SDK: Notification campaigns: Avoid sending duplicate notifications to devices that have switched to a different account using social authentication.
- SDK: Fix an exception being thrown when purging old player event segments (due to accessing Akka's Context static variable).
- SDK: Check for correct game magic in network diagnostic game server probe when generating a report.
- SDK: `EntityActor.ExecuteInBackground()` now executes the operation on the current Actor context (assuming it's valid).
- SDK: Fix case where PlayerId `None` could be persisted in database by `SocialAuthenticateResolveConflict` when no pending conflict exists.
- SDK: Improve server robustness against invalid EntityIds in entity messaging and authentication.
- SDK: Fix race bug in GameConfigLibrary's type registration. Relevant for when multiple BotClients are started in quick succession.
- SDK: Client now more accurately follows the configured session resumption timeout, instead of waiting for a transport connection attempt to fail on its own.
- SDK: Fixed player event log http API when events contain game config references.
- SDK: ShardyDatabase.InsertOrIgnoreAsync() no longer throws ItemAlreadyExistsException when the item is already in database (fixes errors being logged from persisting player incident duplicates).
- SDK: Fixed various cases of formatting and string comparison so that they don't depend on the current culture.
- SDK: PersistedInAppPurchase.TransactionId now supports ids up to length 512 instead of 160. This fixes duplicate purchase detection for transaction ids longer than 160.
- SDK: Players list API (/api/players) now catches all exception types coming from deserialization, not just MetaSerializationException.
- SDK: Fix case where Json-to-S3 Analytics sink would report an error and drop overflowing events if write buffer got more events than could fit into a single S3-written blob (by default 100k events).
- Reference project: For players with `PlayerModel.Language == null`, set it to default in `PlayerActor.RestoreFromPersisted()` to ensure players always have a language set.
- Reference project: Producers are now updated properly when player returns to game after being offline.
- Dashboard: Fixed issues with API polling where the polling was not getting cancelled. Added new class to encapsulate correct polling behaviour

## Release 9 (2020-12-17)

### Added

- SDK: New ApplicationRuntimeConfig variable ExitAfterSeconds, which can be used to run the BotClients (or the server) only for a specific period of time.
- SDK: New ApplicationRuntimeConfig flag ExitOnLogError which can be used to exit the process in case an error happens. Intended for testing only.
- SDK: Support using external AWS credentials for S3-based GameConfigStorage. Enables more secure IRSA-based authentication in the cloud.
- SDK: Support fetching Firebase credentials from AWS secrets manager by specifying the FirebaseCredentialsPath as 'aws-sm://path/to/secret'.
- SDK: Player re-deletion allows players to be re-deleted after a database rollback.
- SDK: New client-side MetaplayConnection error state, `InternalWatchdogDeadlineExceeded`, for case where connection was terminated due to connection thread becoming unresponsive.
- SDK: Added `MetaplaySDK.OnApplicationAboutToBePaused(reason, expectedDuration)` method for hinting SDK to keep session alive even if application gets paused. Useful when showing Ads or opening external applications.
- Dashboard: Show exact times in local time and UTC as tooltip for all 'x ago' timestamps.
- Dashboard: Lots of new UI to accommodate guilds.
- Dashboard: Major iteration on incident reports UX. Reports now have static links that can be easily shared!
- Dashboard: Raw data printouts in dev mode now neatly show `null` values for better legibility.
- Dashboard: Link to Grafana on Overview page (requires Grafana to be enabled for environment).
- Dashboard: Link to Grafana/Loki logs for each player login event (requires Grafana to be enabled for environment).
- Dashboard: Added the current environment's name to the sidebar.
- Dashboard: Added a 404 page not found route for cleaner bad URL handling.

### Changed

- SDK: Added some utilities for testing client-side IAP management.
- SDK: SocialAuthenticateResult now contains the whole conflicting PlayerModel (as MetaSerialized<IPlayerModelBase>) instead of game-specific individual members.
- SDK: Upgraded to .NET 5.0 and C# 9.0 on server.
- SDK: Upgrade Akka.NET to v1.4.11.
- SDK: Optimize Util.IsBase64Encoded() to use pre-compiled regex.
- SDK: Removed Util.GetTimestamp(). Use Util.GetUtcUnixTimeSeconds() instead.
- SDK: Skip installation of Cypress in Dockerfile.server to speed up server builds.
- SDK: MetaplaySDKBehavior script must be present in the scene at all times the SDK is used.
- SDK: MetaplayConnection.OnApplicationPause() no longer needs to be called. It is managed automatically.
- SDK: NetworkDiagnosticsManager is moved into MetaplaySDK and it's lifetime is managed automatically.
- SDK: Game IncidentTracker is moved into MetaplaySDK, and it is managed automatically.
- SDK: `Connection.Update()` has been replaced with `MetaplaySDK.Update()`.
- SDK: UpdateResult.TooManyTicks has been removed. Too long suspends are now reported via AppTooLongSuspended connection Error. The timeout control is moved from `PlayerClientContext.MaxSimulateTicks` to `MetaplayConnection.Config.MaxSessionRetainingPauseDuration` and `MaxSessionRetainingFrameDuration`, with optional hinting with `MetaplaySDK.OnApplicationAboutToBePaused`.
- SDK: Incident reports now contain log messages from all threads, not just main thread.
- SDK: Internal Newtonsoft.Json dependency for GameConfigCuilder is no longer included into device builds.
- SDK: Using Metaplay.Core.Json on Client requires specifying METAPLAY_ENABLE_JSON define symbol.
- Reference project: Improved IAPManager and related client-side IAP management utilities.
- Dashboard: Bundled a default avatar for when the authentication is off to avoid making an external web request.
- Dashboard: Environment page now has more incident reports than the overview page to keep the overview page clean.

### Fixed

- SDK: Fix some situations where GameConfigArchive version could change after restoring from S3 (or local emulation of S3).
- SDK: Perform GameConfig fetching on main thread in Unity, to ensure that it also works when reading from StreamingAssets/ on Android, which uses UnityWebRequest for reading from within a .jar file.
- SDK: Parse all integers (int and long) with CultureInfo.InvariantCulture to make sure they behave the same regardless of system culture settings.
- SDK: Parse fixed-point values from JSON with CultureInfo.InvariantCulture to make sure they behave the same regardless of system culture settings.
- SDK: Fix one extra login counted for each created player.
- SDK: Fix some situations where GameConfigArchive version could change after restoring from S3 (or local emulation of S3).
- SDK: Fix propagation of GlobalState sometimes failing between GlobalStateManager and GlobalStateProxy in multi-node configurations.
- SDK: Fix some missing calls to Dispose()s in outgoing HTTP requests.
- SDK: Serialization of `byte[]` now writes the correct tag `WireDataType.Bytes` instead of `WireDataType.ValueCollection`.
- Dashboard: Better Auth0 error message for the most typical case of "no connections enabled".
- Dashboard: Fixed hard to read player list tables on very narrow screens by hiding some of the fields.
- Dashboard: Fixed mobile layout for GDPR export action.
- Dashboard: Fixed mobile layout for account reconnecting action.
- Dashboard: Fixed mobile layout for player overview card.
- Dashboard: Fixed player details background stripe rendering on wide screens.
- Dashboard: Fixed misaligned UI elements in notification campaign details on small screens.
- Dashboard: Fixed poorly formatting MySQL database details in the environments page on small screens.
- Dashboard: Fixed bad use of screen estate in the notification campaign details page on mobile.
- Dashboard: Usability fixes to player incident reports: PlayerId is now shown, copy-pasting of logs produces correct whitespace.
- Dashboard: Global player incident list on Overview page is ordered by time when received on server, not when reported by client, to avoid issues from clock skew.

## Release 8 (2020-11-03)

### Added

- SDK: More metrics related to IAP validation.
- SDK: Utility for defining jobs that scan an entity database table and perform a given task with each entity.
- SDK: NetworkDiagnostics tool which can be used to probe game server, CDN, and known sites on the internet to assess state and quality of network.
- SDK: Push notification campaign feature which allows sending notifications to all players at a specific date and time.
- SDK: Added support for deleting players in properly GDPR-compliant way.
- SDK: Support for sharded databases (re-sharding is only supported in limited scenarios).
- SDK: Support for up-sharding database in integer multiples (assumes that the added database shards are copies of the earlier shards).
- SDK: Added support for deleting players
- SDK: Source IPs of the connecting clients are logged on server in a partially-redacted form.
- SDK: Added a "player event log" feature for keeping a per-player record of game-defined events for admin purposes.
- Dashboard: Added Cypress end-to-end test runner with some initial test as a stating point. The intent is to steadily iterate and add useful tests going forward.
- Dashboard: Added a database section to the landing page status card.
- Dashboard: Added a new environment page with database sharding and item count details.

### Changed

- SDK: Replaced the MessageTargetActor enum by a MessageRoutingRule abstract class, allowing for better extensibility and separation of SDK and game.
- SDK: Introduced an IMainGameConfig interface for separating the game-specific representation of parts of MainGameConfig that are referred to by SDK.
- SDK: Serializer now supports enum values larger than maximum int.
- SDK: Serializer now requires the `[MetaSerializable]` attribute on serialized enum types.
- SDK: Serializer now supports `Queue<T>`.
- SDK: On Linux, server socket accept queue length metrics are optionally collected with sock_diag(7) instead of parsing /proc/net/tcp. This improves performance if the number of sockets is high.
- SDK: Changed the database-related RuntimeConfigs to better support sharding.
- SDK: Remove MetaplayClient.InMemoryLogger -- will be superceded by the player incident tracking.
- SDK: Failing Actions are now logged on the server with a Warning level.
- SDK: Google Play social login token signatures are now validated on server without extra roundtrips to Google API server. Improves reliability and lowers latency.
- SDK: Apple Game Center social login token signatures are now validated on server with cached certificates. Improves reliability and lowers latency.
- SDK: On client, CredentialsStore now stores user credentials to a file in additions to the PlayerPref.
- SDK: PlayerActorBase::ExecuteServerAction now takes "order" parameter which controls when the action will be executed. When in doubt, use `AfterPendingOperations`.
- SDK: PersistedEntityActor::{InitializeNew,RestoreFromPersisted,MigrateState,PostLoad} now operate on explicit state object argument to avoid incomplete state leaking to entity.
- SDK: PlayerActorBase::{RestoreFromPersisted, InitializeNew, MigrateState} now operate on explicit PlayerModel as above. Added `SwitchToNewModelImmediately` method to replace fragile `RestoreFromModel();PostLoad();` combo. Added `OnSwitchedToModel` callback to replace `RestoreFromModel` in all (re)load paths.
- SDK: Upgrade to .NET 3.1.9
- SDK: Upgrade dependencies: EFCore 3.1.9, MySqlConnector 0.69.10, prometheus-net 4.0, Serilog 3.10.0

### Fixed

- SDK: Allow 1 hour for database migration commands to execute to avoid timeouts with large databases (more than about 500k players).
- SDK: Fix race condition in InMemoryLogger when logging from multiple threads.
- SDK: Fix updating of references to GameConfig data in PlayerActor.InitializeSession() when the MainGameConfig version has changed while PlayerActor is live, but player not logged in.
- SDK: Terminate the connection on the client if the application stays paused for a long time.
- SDK: Fix race condition in ServerConnection related session message acknowledgement.
- Reference project: Fixed a nullref exception on the desync error screen when a network report wasn't ordered.
- Dashboard: Fixed player banning toggle sometimes working in unexpected ways.

### Refactored

- SDK: Refactored IPlayerModelServerListener and IPlayerModelClientListener into base and game-specific parts. Implement IPlayerModelServerListenerBase in PlayerActorBase.
- SDK: Split PlayerMessagesInternal.cs into game-specific and Metaplay core parts.
- SDK: Move some utility data types from PlayerModel.cs into Metaplay.Core.
- SDK: Introduced MessageCodesCore registry for storing the various typeCodes of Metaplay Core messages.
- SDK: Introduced ActionCodesCore registry for storing the various typeCodes of Metaplay Core ModelActions.
- SDK: Support Metaplay.Core level PlayerActions, which only operate on IPlayerModelBase. Base classes for actions are now PlayerActionBase and PlayerServerActionBase (use these when serializing).
- SDK: Moved InAppPurchase.cs, SocialAuth.cs and PushNotification.cs contents into Metaplay.Core.
- SDK: Moved core messages from PlayerMessages.cs into Metaplay.Core.
- Reference project: Moved StateManager.cs from Code/Systems to its own folder (too game-specific).
- Reference project: Introduced ActionCodes registry for storing typeCodes of game-specific ModelActions.

## Release 7 (2020-09-25)

### Added

- SDK: MiniMD5 helper, which computes the first 32 bits of MD5 without allocating memory (for short string inputs). Can also be computed in MySQL.
- SDK: Support passing in CI build number and git commit id as build args to Dockerfile(s).
- SDK: Resumable game sessions (tolerance for short-duration connection losses).
- SDK: Added `MetaBlockedMembersAttribute`, which can be used to block retired MetaMember tag ids to be used in the future, to avoid accidental migration problems.
- SDK: Generic attachment mechanism for including items/resources/whatnot in admin messages.
- SDK: New `MetaReward` base class that can be used to easily attach rewards to thing like in-game mail, events, quests, etc.
- SDK: Added RandomPCG utility methods `ShuffleInPlace(T[])`, `ShuffleInPlace(List<T>)`, and extension method `IEnumerable<T> Shuffle<T>(this IEnumerable<T>, RandomPCG)`.
- SDK: New Entity DiagnosticTool, which monitors the health of the cluster connections.
- SDK: Server can now listen to multiple client ports, to allow circumventing some networking issues.
- SDK: Client can now fallback to alternative ports if the primary address and port are not reachable.
- SDK: Added Entity Archive feature to allow easy export and import of entities between deployments
- SDK: Added new `MetaplayConnection.Error`s: NoNetworkConnectivity, FailedToResumeSession, SessionForceTerminated, SessionError, SessionLostInBackground.
- Reference project: Added a new `MetaInGameGenericPlayerMail` class with sensible default members (title, body, attachments) to make adopting in-game mails easier.
- Reference project: Botclient can now simulate app being put on background for a moment.
- Dashboard: Refactored the in-game mail sending UI to be data driven and to support arbitrary attachments, like resources and items.
- Dashboard: Added the ability to delete player mails.
- Dashboard: Added the ability to request refunds for Google Play purchases.

### Changed

- SDK: Rewrite of clustering logic for a more robust and scalable implementation.
- SDK: Separate PlayerId ranges for bots and players to avoid potentially confusing the two.
- SDK: Database data accesses are now implemented using Dapper for improved performance (schema operations still use EFCore).
- SDK: Use `ConfigureAwait(false)` in many more places to improve scheduling of Tasks.
- SDK: Authentication flow is now simpler and more robust, and bot-specific paths are clearly separate.
- SDK: Player PubSub has been rewritten and is now more robust, has more features and is more performant.
- SDK: Use new MiniMD5 when sharding items in database. Enables efficient up-sharding in the future.
- SDK: Refactored in-game mails to better separate user land and core SDK code. Mails now derive from `MetaInGameMail`.
- SDK: `MetaMessageAttribute` now more strictly requires specifying its direction and target actor parameters.
- SDK: Graceful shutdown endpoint is now at :8888/gracefulShutdown (only intended to be used by the Kubernetes operator).
- SDK: Renamed HealthProbeHttpServer to MetaplaySystemHttpServer and related runtime configs are now `EnableSystemHttpServer` and `SystemHttpPort`.
- SDK: Support reporting OnApplicationPause to MetaplayConnection, and report a specific error after a long pause.
- SDK: Upgrade Akka.NET to v1.4.10.
- SDK: Upgraded server runtime base image to v3.1.8. Manually include GeoTrust Global CA cert to ensure GameCenter authentication still works.
- SDK: Server now ignores unknown EntityKinds in cluster shard configuration. Useful when deploying new types of entities: can add to Helm values file first, then deploy the server change.
- SDK: `ServerRuntimeConfig.ClientPort` has been replaced with `ServerRuntimeConfig.ClientPorts` -- multiple listen ports can now be defined instead of one.
- SDK: Rewrote Export Player and Overwrite Player feature to use new Entity Archive feature.
- SDK: `EntityActor.AllowShutdown` is removed in favor of `EntityActor.ShutdownPolicy` which allows for more precise automatic entity shutdown.
- SDK: Refactored PlayerActor into a game-agnostic PlayerActorBase base type and a game-specific PlayerActor derived type.
- SDK: Introduced an IPlayerModel interface for separating the game-specific representation of parts of PlayerModel that are referred to by SDK.
- Dashboard: Refactored in-game mail API and client components to understand attachments. Game specific code was separated to individual, reusable components in anticipation of future use cases.
- Dashboard: Improved workflow of reconnecting new players to their old accounts.
- Dashboard: Added nicer error reporting for Auth0 connection issues in local development mode.
- Dashboard: Improved UI for Export Player and Overwrite Player.
- Dashboard: Added access to new Import Game Entities feature.
- Dashboard: Restructured project files for easier game specific forking and merging.
- Reference project: Auto-reconnect after a long pause.
- Reference project: Better error message when starting the game in Unity with dangling Assets/Metaplay/Metaplay.Generated.dll from previous build.
- AdminApi: Improved and more consistent handling and response for errors.

### Fixed

- Dashboard: Fixed header username being hard to read on narrow aspect ratios.
- SDK: Base client-side game update ticks on wall clock time more robustly instead of relying on Unity's deltaTime.
- SDK: Fix CsvReader to be able to parse base class properties with private setters.
- SDK: Fix time progress loss issue by adding `PlayerModel.FastForwardTime()`, which is called by PlayerActor when actor is live, but client doesn't progress the game clock.

## Release 6.1

### Changed

- SDK: Moved some localization-related types to Metaplay.Core.Localization.
- SDK: Split SDK-related parts of InAppProductInfo into Metaplay.Core.InAppPurchase.InAppProductInfoBase.

## Release 6 (2020-08-27)

### Added

- Dashboard: Added copying and overwriting of player states.
- SDK: Allow reducing client checksumming frequency. Improves performance but loses ability to identify the root cause of a checksum mismatch error.
- SDK: Support for backward-compatible game updates via supporting multiple LogicVersions. This is a breaking change to the wire protocol.
- SDK: PlayerActionDevelopmentOnlyAttribute to use for Actions that should only work in development builds.
- SDK: Support serialization of Nullable<T> where T is a struct.
- SDK: Added PlayerTimeZoneInfo and MessageLoginRequest.TimeZoneInfo to get information about client's time zone.
- Dashboard: Added Auth0 integration and a user page to show the current auth0 identity.
- Dashboard: Added user permission requirements to all actions.
- Dashboard: Developer mode toggle now persist in browser's local storage.
- Dashboard: Added current commit id to the sidebar in development mode.
- HTTP API server now optionally performs authentication and authorization.
- Reference project: Listeners for IAP claiming and validation failures.

### Changed

- Dashboard: Logout and ⚙️ buttons now look more like buttons.
- Dashboard: Updated dependencies.
- Dashboard: Replaced /info with /hello and /gamedata endpoints to facilitate dashboard lazy config loading.
- SDK: Allow reducing client checksumming frequency. Improves performance but loses ability to identify the root cause of a checksum mismatch error.
- SDK: Action flush processing is rate limited. Too frequent flushes are pooled and processed in batches.
- SDK: Listen socket accept queue sizes are increased significantly.
- SDK: Extend metrics collection to cover CLR Thread Pool healthiness, network IO statistics, and Game Entity and TCP socket lifecycle metrics.
- SDK: Rename Util.ComputeMD5() to Util.ComputeMiniMD5() (it only returns the first 32 bits of the hash).
- SDK: Support disallowing bots from connecting to production deployment.

### Fixed

- Dashboard: Fixed in-game mail form validation for null values.
- SDK: Authentication hash comparison could have returned true for non-equal values.
- SDK: Fixed deserialization of fields of value types in Unity Editor with .NET 2.0.
- SDK: PlayerModel.CurrentTime no longer drifts over time.
- SDK: Support scientific notation for MaxBotId in BotClient (generated by Helm for large values).

## Release 5 (2020-7-17)

### Added

- Use GCP service account -based authentication when publishing GameConfigs to server
- Reference project: Added `OnPlayerBannedStatusChanged()` to `StateManager`
- Reference project: Added in-app purchase shop (with a fake IAP store for now) and validation flow
- SDK: Added `ExcludeFromGdprExport` attribute to `PlayerModel`
- Dashboard: Added player search
- Dashboard: Added a generic UI component to warn about disruptive actions
- Dashboard: Added a new GDPR data export feature to player details
- Dashboard: Added a new account reconnect feature to player details
- Dashboard: Added name changing to player details
- Dashboard: Added failed and pending puchases to player details
- Dashboard: Added social authentications to player details
- SDK: Serializer now supports sbyte, short and ushort
- SDK: Interfaces can now be serialized in certain simple cases

### Changed

- Dashboard: Better IAP purchase history card that scales to large amounts of transactions and shows the purchase IDs
- Dashboard: Various ID fields changed to a monospace font for better legibility
- Dashboard: Player banning UI now looks and feels more like everything else
- Dashboard: Added route ID's to headerbar for a more obvious differentiation between many tabs
- Dashboard: Layout tweaks to player details to make the content flow better at various aspect rations
- Reference project: `OnPlayerExecuteServerAction()` moved from `StateManager` to `MetaplayClient` for less user land boilerplate
- Forcing .NET Core runtime image to version 3.1.3 to avoid issues (in versions 3.1.4 and 3.1.5) with certificate roots which breaks GameCenter authentication
- BotClient: Introduce Config.local.json (used by default) and only leave Prometheus metrics disabled when running locally
- SDK: Serializer: when deserializing to a shorter-than-int integer type, if the magnitude of the value is too large, throw instead of silently truncating
- SDK: Basic support for querying database read-replicas
- SDK, dashboard: Added optional down-time estimate for maintenance mode
- SDK: Serializer: when deserializing to a shorter-than-int integer type, if the magnitude of the value is too large, throw instead of silently truncating

### Fixed

- Generated serializer now works in Unity builds using Mono (previously only worked with IL2CPP builds)
- SDK: Fixes to in-app purchase persistence, validation retries, and treatment of transient validation error as success for the player's benefit
- SDK: Fixed in-process collected memory metrics (exposed via Prometheus to Grafana)
- Dashboard: Fixed very specific window sizes causing ugly line breaks in admin action buttons
- Dashboard: Fixed player reset action
- Reference project: Fixed broken aspect ratios in debug menu layout
- OfflineServer: Don't react to a IPlayerModelServerListener event until the corresponding action is acked

### Refactors

- SDK: Removed the old reflection-based TaggedWireSerializer (class is still there for the utility functions used elsewhere)

## Release 4 (2020-06-10)

### Added

- SDK: Server-side support for writing JSON log files (experimental feature, uses blocking disk writes)
- SDK: Support for compression of large messages (compression disabled by default for now, to avoid breaking backward-compatibility)
- SDK, dashboard & reference project: Maintenance mode
- SDK: Added ability to reconnect players to auth from an older player account through the Dashboard
- Dashboard: Added social auths to player device list

### Changed

- SDK: Full protocol hash mismatch is no longer a terminal error
- SDK: Refactor server-side to use two-layer configs: `Config/Config.base.json` (for common configs) and `Config/Config.{env}.json` (for env-specific configs)
- SDK: Action and tick checksum computation is 10x faster on dotnet core (server).
- Reference project: Switch back to .NET Standard 2.0 compatibility mode, as serializer no longer requires .NET 4.x
- Dashboard: System page refactored to sub-components for legibility and easier merges in the future

### Fixed

- SDK: Serializer is now compatible with Unity .NET 2.0 compatibility in Editor, too (uses reflection-based member accesses when running in Editor)
- SDK: Database-persisted members of type byte[] now default to SQL type 'longblob' to support sizes larger than 64kB (requires migration with manual exclusion of Sqlite)
- SDK: Store Unity Editor-time generated serializer .dll into MetaplayTemp/, so it doesn't get removed whenever Unity is restarted

## Release 3 (2020-MM-DD)

### Added

- SDK: Serializer v2: generates C# code which is compiled using Roslyn compiler (compiled .dll is included in IL2CPP builds)
- SDK: Added `ClientHelloMessage.Timestamp` to identify connections uniquely & to identify delayed connections
- SDK: Send `SocialAuthenticateForceReconnect` message to client when should reconnect due to attaching social account to a pre-existing player state
- SDK: Added `ConnectionState.Connected.LatestReceivedMessageTimestamp` to allow for custom healthchecks
- SDK: Add ClientCompatibilitySettings which can be used to accept/refuse clients based on their LogicVersion
- SDK: Support redirecting clients to another server based on LogicVersion (useful for testing production builds or handling app review before deploying new server)
- SDK: Warn if trying to send a message in a non-connected state
- SDK: Retry Config downloads on error for a configurable amount. After that, report `ConnectionState.TransientError.ConfigFetchFailed`
- Dashboard: Show raw database-persisted `PlayerModel` (in base64) at bottom of player details view
- Dashboard: Allow changing ClientCompatibilitySettings

### Changed

- SDK: Upgrade to Akka v1.4.4 (includes some memory allocation optimizations)
- SDK: Upgrade to MySqlConnection 0.63.0
- SDK: Deprecate & remove support for compact serialization format in favor of tagged format
- SDK: Switch to using .NET Server GC
- SDK: Include private `PlayerModel` members when serializing state to dashboard
- SDK: Detect and warn if executing an action (or tick) from within another action (or tick)
- SDK: Logging of `byte[]` now uses base64 (3x more compact), ends with '.' or '...' to mark the end of data and whether bytes were dropped
- SDK: PrettyPrinting is significantly more compact for Exceptions
- SDK: Replaced KeepAlive messages with lower-level ping-pong messages. This improves Healthiness assesment when no messages are sent.
- SDK: Store all authentication methods in PlayerModel
- SDK: Support unlinking of social auth profiles from player
- SDK: Add `flushEnqueuedMessages` flag to MetaplayConnection::Close to control whether to flush or to drop enqueued before closing.
- SDK: Added ModelJournal for a clean and efficient tick/action execution on a timeline.
- SDK: Check member serialization flags when deserializing: enables adding Transient attribute after persisting a value in database
- SDK: Disable server-side IAP retry when resuming PlayerActor to avoid race conditions: client retries should suffice
- SDK: ConnectionStates.WireFormatError is now a Terminal error.
- Reference Project: Upgrade to Unity 2019.3.6f1
- Reference Project: Better in-game error reporting especially for checksum errors
- Reference Project: `OnPlayerChecksumMismatch` handling moved from `MetaplayClient` to `StateManager`
- Reference Project: Responsive layout to in-game debug menu
- Reference Project: Send PlayerModel Desyncs reports to server for logging
- Reference Project: Changed all IGameConfigData class members to properties with private setter to avoid accidental modification
- Reference Project: Use ModelJournal for executing tick and actions.

### Fixed

- SDK: Fixed handling of social authenticate conflict resolve when choosing to keep current player profile (i.e., re-attach social auth)
- SDK: Fixed too aggressive timestamp checking in social authentication
- Added and changed source code files to contain a consistent licensing header
- Dashboard: Fixed graphs on overview page

## Release 2 (2020-03-30)

### Added

- SDK: Utility methods to RandomPCG: `Choice(IList<T>)`, `GetWeightedIndex(IEnumerable<int>)`, `GetWeightedIndex(IEnumerable<F32>)`
- SDK: Added `OrderedDictionary` and `OrderedSet`. They provide consistent iteration order without the cost of `SortedDictionary`/`SortedSet`.
- SDK: Check that MetaMessages don’t transitively include data that is not allowed (raw Actions, IGameConfigData references)
- SDK: Include diagnostics tools in docker container: dotnet-counters, dotnet-trace, dotnet-dump, dotnet-gcdump
- SDK: Added support for structured logging and optional json output format
- SDK: New connection management and health checks. Optional automatic reconnects.
- Reference project: Added a loading screen
- Reference project: Added a debug menu (tap the Metaplay logo)
- Reference project: Added a dedicated error popup with fields for some useful debug info
- Reference project: Added an app start scene for a cleaner example on initialization
- Reference project: Implemented connection error handling (reconnecting on error, connection error popup)

### Changed

- SDK: Access Google Sheets using service account credentials
- SDK: Upgrade to Akka 1.4.2, MySqlConnector 0.62.0, prometheus-net 3.5.0
- SDK: Better implementation of StringId by interning and reference comparisons, remove obsolete ShortStringId
- SDK: Moved connection management to Metaplay namespace
- SDK: Refactored connection management for easier usage and more robust error handling
- SDK: Optimize memory allocations from PrettyPrint
- SDK: Better IOReader error handling: throw IODecodingError if reading past buffer
- SDK: Optimize memory allocations from IOReader
- Reference project: Replace all uses of ShortStringId with StringId
- Reference project: New UI style
- Reference project: New StateManager class with support for discrete app start and loading states
- Reference project: New MetaplayClient class that will develop into a clean interface for controlling Metaplay going forward

### Fixed

- Reference project: Fixed admin mails not giving out gold

## Release 1 (2020-03-06)

We've started tracking releases!
