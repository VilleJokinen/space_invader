// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.ComponentModel;
using System.Globalization;

namespace Metaplay.Core
{
    /// <summary>
    /// Helper class for reducing boilerplate when defining a <see cref="TypeConverter"/>
    /// which converts to and from <see cref="string"/>.
    /// </summary>
    public abstract class StringTypeConverterHelper<T> : TypeConverter
    {
        protected abstract string ConvertValueToString(T obj);
        protected abstract T ConvertStringToValue(string str);

        public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType)
            => destinationType == typeof(string);

        public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
            => ConvertValueToString((T)value);

        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
            => sourceType == typeof(string);

        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
            => ConvertStringToValue((string)value);
    }
}
