// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using Metaplay.Core.IO;
using Metaplay.Core.Message;
using Metaplay.Core.Model;
using static System.FormattableString;

namespace Metaplay.Core.Serialization
{
    // MetaSerializationUtil

    public static class MetaSerializationUtil
    {
        public static int PeekMessageTypeCode(byte[] serialized, int offset = 0)
        {
            using (IOReader reader = new IOReader(serialized, offset, serialized.Length - offset))
            {
                // Skip prefix byte
                reader.ReadByte();

                // Read & return typeCode
                return reader.ReadVarInt();
            }
        }

        public static int PeekMessageTypeCode(MetaSerialized<MetaMessage> serialized)
        {
            return PeekMessageTypeCode(serialized.Bytes, offset: 0);
        }

        public static string PeekMessageName(byte[] serialized, int offset = 0)
        {
            // Read message typeCode and try to resolve its name (or return typeCode)
            int typeCode = PeekMessageTypeCode(serialized, offset);
            if (MetaMessageRepository.Instance.TryGetFromTypeCode(typeCode, out MetaMessageSpec msgSpec))
                return msgSpec.Name;
            else
                return Invariant($"MetaMessage.Unknown#{typeCode}");
        }

        public static string PeekMessageName(MetaSerialized<MetaMessage> serialized)
        {
            if (serialized.IsEmpty)
                return "<empty>";
            else
                return PeekMessageName(serialized.Bytes, offset: 0);
        }

        public static TModel CloneModel<TModel>(TModel model, IGameConfigDataResolver resolver) where TModel : IModel
        {
            int logicVersion = model.LogicVersion;
            byte[] serialized = MetaSerialization.SerializeTagged<TModel>(model, MetaSerializationFlags.IncludeAll, logicVersion);
            return MetaSerialization.DeserializeTagged<TModel>(serialized, MetaSerializationFlags.IncludeAll, resolver, logicVersion);
        }
    }
}
