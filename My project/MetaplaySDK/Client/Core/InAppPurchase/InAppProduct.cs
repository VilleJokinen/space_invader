// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using Metaplay.Core.Math;
using Metaplay.Core.Model;
using System.ComponentModel;

namespace Metaplay.Core.InAppPurchase
{
    /// <summary>
    /// Type-safe identifier for in-app product ids. Uses arbitrary-length StringId&lt;&gt;
    /// as internal implementation.
    /// </summary>
    [MetaSerializable]
    public class InAppProductId : StringId<InAppProductId> { }

    /// <summary>
    /// In-app purchase product types.
    /// </summary>
    [MetaSerializable]
    public enum InAppProductType
    {
        Consumable,     // Consumable product, claimed once and then transaction is cleared
        NonConsumable,  // Non-consumable product, can be claimed multiple times
        Subscription,   // Auto-renewing subscription
    }

    /// <summary>
    /// Base class describing a single in-app purchase product. Contains the Metaplay-required parts
    /// of the IAP. Each game should derive their own game-specific class from this class and specify
    /// any contents of the IAP in that class.
    /// </summary>
    [MetaSerializable]
    [MetaReservedMembers(100, 200)]
    public abstract class InAppProductInfoBase : IGameConfigData<InAppProductId>
    {
        [MetaMember(100)] public InAppProductId     ProductId           { get; private set; }   // Unique id of product for Metaplay. Doesn't need to match platform store product id.
        [MetaMember(101)] public string             Name                { get; private set; }   // Display name of product
        [MetaMember(102)] public InAppProductType   Type                { get; private set; }   // Type of the IAP product (eg, consumable, non-consumable)
        [MetaMember(103)] public F64                Price               { get; private set; }   // Reference price for product (in USD)
        [MetaMember(107)] public bool               HasDynamicContent   { get; private set; }   // Whether this IAP has dynamic content
        [MetaMember(104)] public string             DevelopmentId       { get; private set; }   // Development product id
        [MetaMember(105)] public string             GoogleId            { get; private set; }   // Google Play product id
        [MetaMember(106)] public string             AppleId             { get; private set; }   // Apple App Store product id

        public InAppProductInfoBase()
        {
        }

        public InAppProductInfoBase(InAppProductId productId, string name, InAppProductType type, F64 price, bool hasDynamicContent, string developmentId, string googleId, string appleId)
        {
            ProductId           = productId;
            Name                = name;
            Type                = type;
            Price               = price;
            HasDynamicContent   = hasDynamicContent;
            DevelopmentId       = developmentId;
            GoogleId            = googleId;
            AppleId             = appleId;
        }

        public InAppProductId ConfigKey => ProductId;
    }

    // \todo [antti] #helloworld: add support for non-abstract base classes in serializer
    [MetaSerializableDerived(100)]
    public class DefaultInAppProductInfo : InAppProductInfoBase { }


}
