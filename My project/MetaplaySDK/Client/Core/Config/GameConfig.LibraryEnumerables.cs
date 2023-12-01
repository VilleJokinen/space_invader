// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Collections;
using System.Collections.Generic;

namespace Metaplay.Core.Config
{
    public sealed partial class GameConfigLibrary<TKey, TInfo>
    {
        public struct LibraryEnumerator : IEnumerator<KeyValuePair<TKey, TInfo>>
        {
            GameConfigLibrary<TKey, TInfo> _library;

            OrderedDictionary<TKey, GameConfigLibraryPatchedItemEntry<TInfo>>.Enumerator _deduplicationStorageEnumerator;
            OrderedSet<TKey>.Enumerator _patchAppendedKeysEnumerator;
            bool _enumeratingPatchAppendedItems;

            OrderedDictionary<TKey, TInfo>.Enumerator _soloStorageEnumerator;

            public LibraryEnumerator(GameConfigLibrary<TKey, TInfo> library)
            {
                _library = library;

                switch (library._storageMode)
                {
                    case GameConfigRuntimeStorageMode.Deduplicating:
                        _deduplicationStorageEnumerator = library._deduplicationStorage.PatchedItemEntries.GetEnumerator();
                        _patchAppendedKeysEnumerator = _library._patchAppendedKeys.GetEnumerator();
                        _enumeratingPatchAppendedItems = false;

                        _soloStorageEnumerator = default;

                        break;

                    case GameConfigRuntimeStorageMode.Solo:
                        _deduplicationStorageEnumerator = default;
                        _patchAppendedKeysEnumerator = default;
                        _enumeratingPatchAppendedItems = default;

                        _soloStorageEnumerator = _library._soloStorageItems.GetEnumerator();

                        break;

                    default:
                        throw new MetaAssertException("unreachable");
                }
            }

            object IEnumerator.Current => Current;

            public KeyValuePair<TKey, TInfo> Current => new KeyValuePair<TKey, TInfo>(CurrentKey, CurrentInfo);

            public TKey CurrentKey
            {
                get
                {
                    switch (_library._storageMode)
                    {
                        case GameConfigRuntimeStorageMode.Deduplicating:
                            if (!_enumeratingPatchAppendedItems)
                                return _deduplicationStorageEnumerator.Current.Key;
                            else
                                return _patchAppendedKeysEnumerator.Current;

                        case GameConfigRuntimeStorageMode.Solo:
                            return _soloStorageEnumerator.Current.Key;

                        default:
                            throw new MetaAssertException("unreachable");
                    }
                }
            }

            public TInfo CurrentInfo
            {
                get
                {
                    switch (_library._storageMode)
                    {
                        case GameConfigRuntimeStorageMode.Deduplicating:
                            if (!_enumeratingPatchAppendedItems)
                            {
                                (TKey key, GameConfigLibraryPatchedItemEntry<TInfo> entry) = _deduplicationStorageEnumerator.Current;
                                if (_library._exclusivelyOwnedItems.TryGetValue(key, out TInfo exclusivelyOwnedItem))
                                    return exclusivelyOwnedItem;
                                TInfo item = entry.TryGetItem(_library._activePatches);
                                if (item != null)
                                    return item;
                                else
                                    throw new MetaAssertException("TryGetItem returned null even though ContainsItem previously returned true");
                            }
                            else
                            {
                                TKey key = _patchAppendedKeysEnumerator.Current;
                                return _library[key];
                            }

                        case GameConfigRuntimeStorageMode.Solo:
                            return _soloStorageEnumerator.Current.Value;

                        default:
                            throw new MetaAssertException("unreachable");
                    }
                }
            }

            public bool MoveNext()
            {
                switch (_library._storageMode)
                {
                    case GameConfigRuntimeStorageMode.Deduplicating:
                        // We first enumerate only the items that aren't patch-appended, and then
                        // the patch-appended items that exist in this specialization.
                        // We do this to get a consistent order for appended items:
                        // the order of appended items can be different in different patches, but
                        // _library._deduplicationStorage.PatchedItemEntries only represents some one order.
                        // The order of _library._patchAppendedKeys depends on the patches in this specialization,
                        // but not other patches.

                        if (!_enumeratingPatchAppendedItems)
                        {
                            while (true)
                            {
                                if (!_deduplicationStorageEnumerator.MoveNext())
                                    break;

                                GameConfigLibraryPatchedItemEntry<TInfo> entry = _deduplicationStorageEnumerator.Current.Value;

                                // Skip patch-appended items in this loop, i.e. only include items
                                // which also exist in the baseline. Note that this also guarantees
                                // that the item exists in this specialization, and so we do not need
                                // to check it separately here.
                                if (entry.BaselineMaybe != null)
                                    return true;
                            }

                            _enumeratingPatchAppendedItems = true;
                        }

                        if (_patchAppendedKeysEnumerator.MoveNext())
                            return true;

                        return false;

                    case GameConfigRuntimeStorageMode.Solo:
                        return _soloStorageEnumerator.MoveNext();

                    default:
                        throw new MetaAssertException("unreachable");
                }
            }

            public void Reset()
            {
                switch (_library._storageMode)
                {
                    case GameConfigRuntimeStorageMode.Deduplicating:
                        _deduplicationStorageEnumerator.Reset();
                        _patchAppendedKeysEnumerator.Reset();
                        _enumeratingPatchAppendedItems = false;
                        break;

                    case GameConfigRuntimeStorageMode.Solo:
                        _soloStorageEnumerator.Reset();
                        break;

                    default:
                        throw new MetaAssertException("unreachable");
                }
            }

            void IDisposable.Dispose()
            {
            }
        }

        public struct KeysEnumerable : IEnumerable<TKey>
        {
            GameConfigLibrary<TKey, TInfo> _library;

            public KeysEnumerable(GameConfigLibrary<TKey, TInfo> library)
            {
                _library = library;
            }

            public Enumerator GetEnumerator() => new Enumerator(_library);
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            IEnumerator<TKey> IEnumerable<TKey>.GetEnumerator() => GetEnumerator();

            public struct Enumerator : IEnumerator<TKey>
            {
                LibraryEnumerator _impl;

                public Enumerator(GameConfigLibrary<TKey, TInfo> library)
                {
                    _impl = new LibraryEnumerator(library);
                }

                object IEnumerator.Current => Current;
                public TKey Current => _impl.CurrentKey;
                public bool MoveNext() => _impl.MoveNext();
                public void Reset() => _impl.Reset();
                void IDisposable.Dispose() { }
            }
        }

        public struct ValuesEnumerable : IEnumerable<TInfo>
        {
            GameConfigLibrary<TKey, TInfo> _library;

            public ValuesEnumerable(GameConfigLibrary<TKey, TInfo> library)
            {
                _library = library;
            }

            public Enumerator GetEnumerator() => new Enumerator(_library);
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            IEnumerator<TInfo> IEnumerable<TInfo>.GetEnumerator() => GetEnumerator();

            public struct Enumerator : IEnumerator<TInfo>
            {
                LibraryEnumerator _impl;

                public Enumerator(GameConfigLibrary<TKey, TInfo> library)
                {
                    _impl = new LibraryEnumerator(library);
                }

                object IEnumerator.Current => Current;
                public TInfo Current => _impl.CurrentInfo;
                public bool MoveNext() => _impl.MoveNext();
                public void Reset() => _impl.Reset();
                void IDisposable.Dispose() { }
            }
        }
    }
}
