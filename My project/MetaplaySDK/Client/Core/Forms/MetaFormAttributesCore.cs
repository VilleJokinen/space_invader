// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Metaplay.Core.Forms
{
    public class MetaFormDisplayPropsAttribute : MetaFormFieldDecoratorBaseAttribute
    {
        public string DisplayName        { get; }
        public string DisplayHint        { get; set; } = null;
        public string DisplayPlaceholder { get; set; } = null;

        public MetaFormDisplayPropsAttribute(string displayName)
        {
            DisplayName = displayName;
        }

        public override string FieldDecoratorKey => "displayProps";
        public override object FieldDecoratorValue => new
        {
            displayName = DisplayName,
            displayHint = DisplayHint,
            placeholder = DisplayPlaceholder,
        };
    }

    public class MetaFormTextAreaAttribute : MetaFormFieldTypeHintAttribute
    {
        public int Rows    { get; set; } = 4;
        public int MaxRows { get; set; } = 8;

        public override string FieldType => "textArea";
        public override object FieldTypeProps => new
        {
            rows    = Rows,
            maxRows = MaxRows,
        };
    }

    public class MetaFormRangeAttribute : MetaFormFieldTypeHintAttribute
    {
        public double Min  { get; }
        public double Max  { get; }
        public double Step { get; }

        public MetaFormRangeAttribute(double min, double max, double step)
        {
            Min  = min;
            Max  = max;
            Step = step;
        }

        public override string FieldType => "range";
        public override object FieldTypeProps => new
        {
            min  = Min,
            max  = Max,
            step = Step,
        };
    }
}
