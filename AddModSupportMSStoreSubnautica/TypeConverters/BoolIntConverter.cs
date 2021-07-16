using System;
using System.ComponentModel;
using System.Globalization;

namespace AddModSupportMSStoreSubnautica.TypeConverters
{
    public class BoolIntConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
        {
            return sourceType == typeof(int) || base.CanConvertFrom(context, sourceType);
        }

        public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType)
        {
            return destinationType == typeof(int) || base.CanConvertTo(context, destinationType);
        }

        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            if (value is int i)
            {
                return i != 0;
            }
            return base.ConvertFrom(context, culture, value) ?? false;
        }
    }
}