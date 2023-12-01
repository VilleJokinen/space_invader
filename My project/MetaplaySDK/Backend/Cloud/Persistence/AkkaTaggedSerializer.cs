// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using System;

namespace Metaplay.Cloud.Persistence
{
    class AkkaTaggedSerializer : Akka.Serialization.Serializer
    {
        public AkkaTaggedSerializer(Akka.Actor.ExtendedActorSystem system) : base(system)
        {
        }

        public override bool IncludeManifest => true;

        public override int Identifier => 14481;

        public override byte[] ToBinary(object obj)
        {
            byte[] serialized = MetaSerialization.SerializeTagged(obj.GetType(), obj, MetaSerializationFlags.IncludeAll, logicVersion: null);

            // Ensure that Skip() works for all serialized types
            // \todo [petri] enable in debug builds?
            //TaggedWireSerializer.TestSkip(serialized);

            // Ensure that serialized value is structurally consistent
            //string str = TaggedWireSerializer.ToString(serialized);
            //DebugLog.Debug("Serialized for persisting: {0}", str);

            // \todo [petri] option flag for clone check
            //if (true)
            //{
            //    MainGameConfig gameConfig = MainGameConfig.Current;
            //    object clone = TaggedWireSerializer.Deserialize(serialized, obj.GetType(), gameConfig);
            //    byte[] cloneSerialized = TaggedWireSerializer.Serialize(obj.GetType(), clone);
            //    MetaDebug.Assert(Util.ArrayEqual(serialized, cloneSerialized), "Serialized result doesn't match its clone!");
            //}

            return serialized;
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            object result = MetaSerialization.DeserializeTagged(bytes, type, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);

            return result;
        }
    }
}
