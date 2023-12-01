// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using Metaplay.Core.Forms;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.TypeCodes;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;

namespace Metaplay.Core.Web3
{
    [MetaSerializable]
    public struct NftId : IEquatable<NftId>, IComparable<NftId>
    {
        // \todo Implement UInt256 and use that here. Serialize in var-uint format so it's backwards compatible with ulong.
        [MetaMember(1)] public ulong Value;

        public static NftId Zero => new NftId(0);

        NftId(ulong value)
        {
            Value = value;
        }

        public static NftId ParseFromString(string str)
        {
            ulong value;
            try
            {
                value = ulong.Parse(str, NumberStyles.None, CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to parse {nameof(NftId)} from string '{str}': {ex.Message}", ex);
            }

            return new NftId(value);
        }

        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public override bool Equals(object obj) => obj is NftId other && Equals(other);
        public bool Equals(NftId other) => Value == other.Value;
        public override int GetHashCode() => Value.GetHashCode();
        public static bool operator ==(NftId left, NftId right) => left.Equals(right);
        public static bool operator !=(NftId left, NftId right) => !(left == right);

        public int CompareTo(NftId other) => Value.CompareTo(other.Value);

        public NftId Plus1()
        {
            return new NftId(Value + 1);
        }
    }

    [MetaSerializable]
    public struct NftKey : IEquatable<NftKey>, IComparable<NftKey>
    {
        [MetaMember(1)] public NftCollectionId CollectionId;
        [MetaMember(2)] public NftId TokenId;

        public NftKey(NftCollectionId collectionId, NftId tokenId)
        {
            CollectionId = collectionId;
            TokenId = tokenId;
        }

        public override string ToString() => $"{CollectionId}/{TokenId}";

        public override bool Equals(object obj) => obj is NftKey other && Equals(other);
        public bool Equals(NftKey other) => CollectionId == other.CollectionId
                                         && TokenId == other.TokenId;
        public override int GetHashCode() => Util.CombineHashCode(CollectionId.GetHashCode(), TokenId.GetHashCode());

        public static bool operator ==(NftKey left, NftKey right) => left.Equals(right);
        public static bool operator !=(NftKey left, NftKey right) => !(left == right);

        public int CompareTo(NftKey other)
        {
            if (CollectionId == null)
                return other.CollectionId == null ? 0 : -1;
            int c = CollectionId.CompareTo(other.CollectionId);
            if (c != 0)
                return c;
            return TokenId.CompareTo(TokenId);
        }
    }

    [MetaSerializable]
    [MetaReservedMembers(100, 200)]
    public abstract class MetaNft : ISchemaMigratable
    {
        [MetaMember(100), MetaFormNotEditable] public NftId TokenId;
        [MetaMember(101), MetaFormNotEditable] public EntityId OwnerEntity = EntityId.None;
        [MetaMember(102), MetaFormNotEditable] public NftOwnerAddress OwnerAddress = NftOwnerAddress.None;
        /// <summary>
        /// Whether the NFT has been minted (as far as the game knows).
        /// If false, the NFT exists in the game in a pre-minted state.
        /// Is changed to true when the game first gains knowledge that the
        /// token has been minted.
        /// </summary>
        [MetaMember(104), MetaFormNotEditable] public bool IsMinted = false;
        [MetaMember(103), MetaFormNotEditable] public ulong UpdateCounter = 0;

        public void SetBaseProperties(
            NftId tokenId,
            EntityId ownerEntity,
            NftOwnerAddress ownerAddress,
            bool isMinted,
            ulong updateCounter)
        {
            TokenId = tokenId;
            OwnerEntity = ownerEntity;
            OwnerAddress = ownerAddress;
            IsMinted = isMinted;
            UpdateCounter = updateCounter;
        }

        public void CopyBaseProperties(MetaNft other)
        {
            SetBaseProperties(
                other.TokenId,
                other.OwnerEntity,
                other.OwnerAddress,
                other.IsMinted,
                other.UpdateCounter);
        }

        [IgnoreDataMember] public NftMetadataImportContext MetadataImportContext { get; set; }

        public virtual void OnMetadataImported(NftMetadataImportContext context) { }

        [IgnoreDataMember] public NftSchemaMigrationContext SchemaMigrationContext { get; set; }
    }

    public class NftMetadataImportContext
    {
        public readonly ISharedGameConfig SharedGameConfig;

        public NftMetadataImportContext(ISharedGameConfig sharedGameConfig)
        {
            SharedGameConfig = sharedGameConfig;
        }
    }

    public class NftSchemaMigrationContext
    {
        public readonly ISharedGameConfig SharedGameConfig;

        public NftSchemaMigrationContext(ISharedGameConfig sharedGameConfig)
        {
            SharedGameConfig = sharedGameConfig;
        }
    }

    [MetaSerializable]
    public struct NftOwnerAddress
    {
        public static NftOwnerAddress None => default;

#if NETCOREAPP
        public static NftOwnerAddress CreateEthereum(Metaplay.Cloud.Web3.EthereumAddress address)
            => new NftOwnerAddress(AddressType.Ethereum, address.GetAddressString());
#endif

        [MetaSerializable]
        public enum AddressType
        {
            None = 0,
            Ethereum,
        }

        public AddressType Type => _type;
        public string AddressString => _str;

        [MetaMember(1)] AddressType _type;
        // \todo #nft Ideally we'd like to use a proper type like EthereumAddress here.
        //       However, there are some issues that need to be resolved first, in particular
        //       moving EthereumAddress to shared code, and to do that, shared code needs
        //       a dependency on BouncyCastle (or another implementation of keccak).
        //       So this string representation is a stopgap. When the issues have been resolved,
        //       we'll want to convert from this representation to that (in a backwards
        //       compatible manner, e.g. with a deserialization converter).
        [MetaMember(2)] string _str;

        NftOwnerAddress(AddressType type, string str)
        {
            _type = type;
            _str = str;
        }

        public override bool Equals(object obj) => obj is NftOwnerAddress other
                                                   && _type == other._type
                                                   && _str == other._str;
        public override int GetHashCode() => Util.CombineHashCode(_type.GetHashCode(), _str?.GetHashCode() ?? 0);
        public override string ToString()
        {
            if (_type == AddressType.None)
                return "None";
            else
                return $"{_type}/{_str}";
        }

        public static bool operator ==(NftOwnerAddress left, NftOwnerAddress right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(NftOwnerAddress left, NftOwnerAddress right) => !(left == right);
    }

    [MetaSerializable]
    public class NftCollectionId : StringId<NftCollectionId> { }

    [AttributeUsage(AttributeTargets.Class)]
    public class MetaNftAttribute : Attribute
    {
        public NftCollectionId CollectionId;

        public MetaNftAttribute(string collectionId)
        {
            if (collectionId == null)
                throw new ArgumentNullException(nameof(collectionId));

            CollectionId = NftCollectionId.FromString(collectionId);
        }
    }

    /// <summary>
    /// Add the given NFT to the player model's replica of owned NFTs.
    /// </summary>
    [ModelAction(ActionCodesCore.PlayerAddNft)]
    public class PlayerAddNft : PlayerSynchronizedServerActionCore<IPlayerModelBase>
    {
        public MetaNft Nft;

        PlayerAddNft() { }
        public PlayerAddNft(MetaNft nft)
        {
            Nft = nft;
        }

        public override MetaActionResult Execute(IPlayerModelBase player, bool commit)
        {
            NftKey key = NftTypeRegistry.Instance.GetNftKey(Nft);
            if (player.Nft.OwnedNfts.ContainsKey(key))
                return MetaActionResult.AlreadyHasNft;

            if (commit)
                player.Nft.OwnedNfts.Add(key, Nft);

            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// Update the state of the given NFT in the player model's replica of owned NFTs.
    /// </summary>
    [ModelAction(ActionCodesCore.PlayerUpdateNft)]
    public class PlayerUpdateNft : PlayerSynchronizedServerActionCore<IPlayerModelBase>
    {
        public MetaNft Nft;

        PlayerUpdateNft() { }
        public PlayerUpdateNft(MetaNft nft)
        {
            Nft = nft;
        }

        public override MetaActionResult Execute(IPlayerModelBase player, bool commit)
        {
            NftKey key = NftTypeRegistry.Instance.GetNftKey(Nft);
            if (!player.Nft.OwnedNfts.ContainsKey(key))
                return MetaActionResult.HasNoSuchNft;

            if (commit)
                player.Nft.OwnedNfts[key] = Nft;

            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// Remove the given NFT from the player model's replica of owned NFTs.
    /// </summary>
    [ModelAction(ActionCodesCore.PlayerRemoveNft)]
    public class PlayerRemoveNft : PlayerSynchronizedServerActionCore<IPlayerModelBase>
    {
        public NftKey Key;

        PlayerRemoveNft() { }
        public PlayerRemoveNft(NftKey key)
        {
            Key = key;
        }

        public override MetaActionResult Execute(IPlayerModelBase player, bool commit)
        {
            if (!player.Nft.OwnedNfts.ContainsKey(Key))
                return MetaActionResult.HasNoSuchNft;

            if (commit)
                player.Nft.OwnedNfts.Remove(Key);

            return MetaActionResult.Success;
        }
    }
}
