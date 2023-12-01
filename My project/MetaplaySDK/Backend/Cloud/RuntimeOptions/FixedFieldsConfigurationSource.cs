// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Microsoft.Extensions.Configuration;

namespace Metaplay.Cloud.RuntimeOptions
{
    class FixedFieldsConfigurationSource : IConfigurationSource
    {
        readonly OrderedDictionary<string, string> _fields;

        public FixedFieldsConfigurationSource(OrderedDictionary<string, string> fields)
        {
            _fields = fields;
        }

        IConfigurationProvider IConfigurationSource.Build(IConfigurationBuilder builder) => new FixedFieldsConfigurationProvider(_fields);
    }

    class FixedFieldsConfigurationProvider : ConfigurationProvider
    {
        readonly OrderedDictionary<string, string> _fields;

        public FixedFieldsConfigurationProvider(OrderedDictionary<string, string> fields)
        {
            _fields = fields;
        }

        public override void Load()
        {
            Data = new OrderedDictionary<string, string>(_fields);
        }
    }
}
