// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Forms;
using System.Collections.Generic;
using System.Linq;
using Metaplay.Core.Model;
using Metaplay.Core.Rewards;
using Newtonsoft.Json;
using System;
using Metaplay.Core.Localization;
using Metaplay.Core.Player;

namespace Metaplay.Core.InGameMail
{
    public class InGameMailRewardListValidator : MetaFormValidator<List<MetaPlayerRewardBase>>
    {
        public override void Validate(List<MetaPlayerRewardBase> field, FormValidationContext ctx)
        {
            if (field != null && field.Any(reward => reward == null))
                ctx.Fail("Null rewards are not allowed!");
        }
    }

    /// <summary>
    /// Base class for the contents of all in-game mails that can be sent to a player from outside sources.
    /// The mail can be anything that is receivable in the game and that requires
    /// some kind of acknowledging (or claiming) by the player.
    ///
    /// Examples include: sending messages (or gifts) from the admin dashboard,
    /// receiving gifts from other players, automatically generated quest rewards, etc.
    /// </summary>
    [MetaSerializable]
    [MetaReservedMembers(101, 200)]
    [MetaFormDerivedMembersOnly]
    public abstract class MetaInGameMail
    {
        [MetaMember(102)] public MetaGuid Id { get; set; } // Id of the mail contents.
        [MetaMember(101)] public MetaTime CreatedAt { get; set; } // Time when the mail contents were created.

        /// <summary>
        /// A brief description of the mail, used in Dashboard and in Audit logs to identify sent mails. Description does not
        /// need to describe the mail Rewards as the rewards are visualized separately.
        /// </summary>
        public abstract string Description { get; }
        public virtual IEnumerable<MetaPlayerRewardBase> ConsumableRewards => new List<MetaPlayerRewardBase>();
        // Mail must be consumed (by PlayerConsumeMail action) before it can be deleted by player (by PlayerDeleteMail).
        // If this is true and mail has not been consumed, PlayerDeleteMail will trigger an error. This default
        // implementation requires mail to be consumed if there are ConsumableRewards.
        public virtual bool MustBeConsumed => ConsumableRewards.Any();
        // \todo #mail-refactor: should this be static per mail type (attribute maybe)?
        public virtual bool LocalizeInClient => false;

        /// <summary>
        /// Creates a new mail with an unique Id. Since the Id is uniquely generated, this is not deterministic.
        /// </summary>
        protected MetaInGameMail() : this(MetaGuid.New()) { }

        /// <summary>
        /// Creates a new mail with the given unique Id. Mail creation date is deducted from the (time-based) GUID.
        /// </summary>
        protected MetaInGameMail(MetaGuid maildId)
        {
            Id = maildId;
            CreatedAt = MetaTime.FromDateTime(maildId.GetDateTime());
        }
    }

    /// <summary>
    /// Legacy implementation, previously known as MetaInGameGenericPlayerMail
    /// </summary>
    [MetaSerializableDerived(1)]
    [MetaReservedMembers(0, 101)]   // Reserve tagIds 0..100 for this class (so base doesn't accidentally use them)
    [MetaBlockedMembers(4, 5)]      // Prevent using conflicting ID's from previous version of this example project: NumGems, NumGold
    [MetaFormDeprecated]
    public class LegacyPlayerMail : MetaInGameMail
    {
        // Legacy integer identifier, retained only for audit log purposes
        [MetaMember(100), JsonIgnore] public int LegacyId { get; private set; }
        [MetaMember(1)] public string Title { get; private set; }
        [MetaMember(2)] public string Body { get; private set; }
        [MetaMember(6)] public List<MetaPlayerRewardBase> Attachments { get; private set; }

        public override string Description => FormattableString.Invariant($"{Title} (LegacyId: {LegacyId})");
        public override IEnumerable<MetaPlayerRewardBase> ConsumableRewards => Attachments ?? base.ConsumableRewards;

        private LegacyPlayerMail() { }
    }

    /// <summary>
    /// A generic mail with a title, a body, and optionally attachments. You could create a more game specific mail type if this generic version does not fit your game's needs.
    /// </summary>
    [MetaSerializableDerived(100)]
    [MetaReservedMembers(0, 100)]   // Reserve tagIds 0..99 for this class (so base doesn't accidentally use them)
    public sealed class SimplePlayerMail : MetaInGameMail
    {
        [MetaValidateRequired]
        [MetaMember(1)] public LocalizedString Title { get; private set; }

        [MetaFormTextArea]
        [MetaMember(2)] public LocalizedString Body { get; private set; }

        [MetaFormFieldContext("AttachmentRewardList", true)]
        [MetaFormFieldCustomValidator(typeof(InGameMailRewardListValidator))]
        [MetaMember(3)] public List<MetaPlayerRewardBase> Attachments { get; private set; }

        public override string Description => Title.Localize() ?? "no title";
        public override IEnumerable<MetaPlayerRewardBase> ConsumableRewards => Attachments ?? base.ConsumableRewards;

        public SimplePlayerMail() { }

        /// <summary>
        /// Create a new in-game mail.
        /// </summary>
        /// <param name="lang">Language of the contents of the mail.</param>
        /// <param name="title">Title of the mail.</param>
        /// <param name="body">Main message body of the mail.</param>
        /// <param name="attachments">A list of <see cref="MetaInGamePlayerMailAttachment"/>s to be consumed by the receiving player.</param>
        /// <param name="mailId">Creation ID of the mail. The creation date is deducted from this time-based GUID.</param>
        public SimplePlayerMail(LanguageId lang, string title, string body, List<MetaPlayerRewardBase> attachments, MetaGuid mailId) : base(mailId)
        {
            Title = new LocalizedString(new[] { (lang, title) });
            Body = new LocalizedString(new[] { (lang, body) });
            Attachments = attachments;
        }

        public static SimplePlayerMail FromLegacy(LegacyPlayerMail legacy, LanguageId lang)
        {
            return new SimplePlayerMail(lang, legacy.Title, legacy.Body, legacy.Attachments, MetaGuid.NewWithTime(legacy.CreatedAt.ToDateTime()));
        }
    }

    /// <summary>
    /// An instance of a mail item in the player's inbox. Contains the immutable mail contents and state metadata associated to the mail item.
    /// </summary>
    [MetaSerializable]
    [MetaReservedMembers(100, 200)]
    public abstract class PlayerMailItem
    {
        [MetaMember(100)] MetaInGameMail _contents;
        [MetaMember(101)] public MetaTime SentAt { get; private set; }
        [MetaMember(102)] public bool HasBeenConsumed { get; protected set; }
        [MetaMember(103)] public MetaTime ConsumedAt { get; protected set; }
        [MetaMember(104)] public bool IsRead { get; protected set; }
        [MetaMember(105)] public MetaTime ReadAt { get; protected set; }

        protected PlayerMailItem() { }
        protected PlayerMailItem(MetaInGameMail contents, MetaTime sentAt)
        {
            _contents = contents;
            SentAt = sentAt;
        }

        public MetaInGameMail Contents => _contents;
        public MetaGuid Id => _contents.Id;

        public virtual void ConsumeRewards(IPlayerModelBase player)
        {
            MetaRewardSourceProvider rewardSourceProvider = IntegrationRegistry.Get<MetaRewardSourceProvider>();
            foreach (MetaPlayerRewardBase consumable in Contents.ConsumableRewards)
                consumable.InvokeConsume(player, rewardSourceProvider.DeclareMailRewardSource(Contents));
            HasBeenConsumed = true;
            ConsumedAt = player.CurrentTime;
        }

        public virtual void ToggleIsRead(IPlayerModelBase player, bool isRead)
        {
            IsRead = isRead;
            if (IsRead && ReadAt == MetaTime.Epoch)
                ReadAt = player.CurrentTime;
        }

        // Mail item lifecycle events for derived classes
        public virtual void OnAddedToInbox(IPlayerModelBase player) { }
        public virtual void OnDeletedFromInbox(IPlayerModelBase player) { }
    }

    /// <summary>
    /// The default PlayerMailItem implementation, used when no customizations are needed.
    /// </summary>
    [MetaSerializableDerived(100)]
    public sealed class DefaultPlayerMailItem : PlayerMailItem
    {
        public DefaultPlayerMailItem() { }
        public DefaultPlayerMailItem(MetaInGameMail contents, MetaTime sentAt) : base(contents, sentAt) { }
    }

    public class InGameMailIntegration : IMetaIntegrationSingleton<InGameMailIntegration>
    {
        public virtual PlayerMailItem MakePlayerMailItem(MetaInGameMail contents, MetaTime sentAt)
        {
            return new DefaultPlayerMailItem(contents, sentAt);
        }
    }

}
