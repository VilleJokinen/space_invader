// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Serialization;
using System;

namespace Metaplay.Cloud.Persistence
{
    class AkkaCompactSerializer : Akka.Serialization.Serializer
    {
        public AkkaCompactSerializer(Akka.Actor.ExtendedActorSystem system) : base(system)
        {
        }

        public override bool IncludeManifest => true;

        public override int Identifier => 14480;

        public override byte[] ToBinary(object obj)
        {
            byte[] serialized = MetaSerialization.SerializeTagged(obj.GetType(), obj, MetaSerializationFlags.IncludeAll, logicVersion: null);

            // \todo [petri] fix clone check, add option for enabling
            //if (true)
            //{
            //    SharedGameConfig gameConfig = SharedGameConfig.Current;
            //    object clone = TaggedWireSerializer.Deserialize(serialized, obj.GetType(), gameConfig);
            //    byte[] cloneSerialized = TaggedWireSerializer.Serialize(obj.GetType(), clone);
            //    MetaDebug.Assert(Util.ArrayEqual(serialized, cloneSerialized), "Serialized result doesn't match its clone!");
            //}

            return serialized;
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            // \todo [petri] quite a hack to deserialize to null before serialization has been initialized
            if (MetaSerialization.IsInitialized)
                return MetaSerialization.DeserializeTagged(bytes, type, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);
            else
                return null;
        }
    }
}
