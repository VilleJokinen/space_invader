// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.IO;
using Metaplay.Core.Serialization;
using NUnit.Framework;
using System;

namespace Cloud.Tests
{
    [TestFixture]
    public class TaggedWirePushParserTests
    {
        class ReachTheEndTestParser : TaggedWirePushParser
        {
            bool _reachedEnd = false;

            ReachTheEndTestParser()
            {
            }

            public static void Test(IOReader reader)
            {
                ReachTheEndTestParser parser = new ReachTheEndTestParser();
                parser.Parse(reader);
                if (!parser._reachedEnd)
                    throw new Exception("did not reach end");
            }

            protected override void OnEnd()
            {
                if (!Reader.IsFinished)
                    throw new Exception("input was not exhausted on End");
                _reachedEnd = true;
            }
        }

        [Test]
        public void TestReachTheEndTestParser()
        {
            TestObjectTypes input = new TestObjectTypes(init: true);
            byte[] serialized = MetaSerialization.SerializeTagged(input, MetaSerializationFlags.IncludeAll, logicVersion: null);
            using (IOReader reader = new IOReader(serialized))
            {
                ReachTheEndTestParser.Test(reader);
            }
        }

        void CheckTreeHasResolvedOffsets(TaggedSerializedInspector.ObjectInfo obj, int parentPayloadStart, int parentPayloadEnd)
        {
            if (obj.EnvelopeStartOffset > obj.EnvelopeEndOffset)
                throw new Exception("invalid envelope span");
            if (obj.PayloadStartOffset > obj.PayloadEndOffset)
                throw new Exception("invalid payload span");
            if (obj.EnvelopeStartOffset > obj.PayloadStartOffset)
                throw new Exception("payload start before envelope start");
            if (obj.PayloadEndOffset > obj.EnvelopeEndOffset)
                throw new Exception("payload end after envelope end");
            if (obj.EnvelopeStartOffset < parentPayloadStart)
                throw new Exception("invalid envelope span, start before parent start");
            if (obj.EnvelopeEndOffset > parentPayloadEnd)
                throw new Exception("invalid payload span, end after parent end");

            if (obj.Members != null)
            {
                foreach (TaggedSerializedInspector.ObjectInfo.MemberInfo member in obj.Members)
                    CheckTreeHasResolvedOffsets(member.ObjectInfo, obj.PayloadStartOffset, obj.PayloadEndOffset);
            }
            if (obj.ValueCollection != null)
            {
                foreach (TaggedSerializedInspector.ObjectInfo element in obj.ValueCollection)
                    CheckTreeHasResolvedOffsets(element, obj.PayloadStartOffset, obj.PayloadEndOffset);
            }
            if (obj.KeyValueCollection != null)
            {
                foreach (TaggedSerializedInspector.ObjectInfo key in obj.KeyValueCollection.Keys)
                    CheckTreeHasResolvedOffsets(key, obj.PayloadStartOffset, obj.PayloadEndOffset);
                foreach (TaggedSerializedInspector.ObjectInfo value in obj.KeyValueCollection.Values)
                    CheckTreeHasResolvedOffsets(value, obj.PayloadStartOffset, obj.PayloadEndOffset);
            }
        }

        [Test]
        public void TestTaggedSerializedInspectorWithoutReferenceType()
        {
            TestObjectTypes input = new TestObjectTypes(init: true);
            byte[] serialized = MetaSerialization.SerializeTagged(input, MetaSerializationFlags.IncludeAll, logicVersion: null);
            TaggedSerializedInspector.ObjectInfo objectTree;
            using (IOReader reader = new IOReader(serialized))
            {
                objectTree = TaggedSerializedInspector.Inspect(reader, null, checkReaderWasCompletelyConsumed: true);
            }
            CheckTreeHasResolvedOffsets(objectTree, 0, serialized.Length);
        }

        void CheckTreeHasResolvedTypes(TaggedSerializedInspector.ObjectInfo obj)
        {
            if (!obj.IsPrimitive && obj.SerializableType == null)
                throw new Exception("missing resolved type");
            if (obj.Members != null)
            {
                foreach (TaggedSerializedInspector.ObjectInfo.MemberInfo member in obj.Members)
                    CheckTreeHasResolvedTypes(member.ObjectInfo);
            }
            if (obj.ValueCollection != null)
            {
                foreach (TaggedSerializedInspector.ObjectInfo element in obj.ValueCollection)
                    CheckTreeHasResolvedTypes(element);
            }
            if (obj.KeyValueCollection != null)
            {
                foreach (TaggedSerializedInspector.ObjectInfo key in obj.KeyValueCollection.Keys)
                    CheckTreeHasResolvedTypes(key);
                foreach (TaggedSerializedInspector.ObjectInfo value in obj.KeyValueCollection.Values)
                    CheckTreeHasResolvedTypes(value);
            }
        }

        [Test]
        public void TestTaggedSerializedInspectorWithReferenceType()
        {
            TestObjectTypes input = new TestObjectTypes(init: true);
            byte[] serialized = MetaSerialization.SerializeTagged(input, MetaSerializationFlags.IncludeAll, logicVersion: null);
            TaggedSerializedInspector.ObjectInfo objectTree;
            using (IOReader reader = new IOReader(serialized))
            {
                objectTree = TaggedSerializedInspector.Inspect(reader, input.GetType(), checkReaderWasCompletelyConsumed: true);
            }

            // Check objectTree was completely resolved
            CheckTreeHasResolvedTypes(objectTree);
            CheckTreeHasResolvedOffsets(objectTree, 0, serialized.Length);
        }
    }
}
