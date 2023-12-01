// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Localization;
using System;

namespace Metaplay.Core.Forms
{
    /// <summary>
    /// On a Dashboard form, the value for this field must be given.
    /// </summary>
    public class MetaValidateRequiredAttribute : MetaFormFieldValidatorBaseAttribute
    {
        public class Validator : IMetaFormValidator
        {
            readonly MetaValidateRequiredAttribute _attribute;

            public Validator(MetaValidateRequiredAttribute attribute)
            {
                _attribute = attribute;
            }

            public void Validate(object fieldOrForm, FormValidationContext ctx)
            {
                if (fieldOrForm == null)
                {
                    ctx.Fail(_attribute.ErrorMessage);
                    return;
                }

                if (fieldOrForm is string fieldString)
                {
                    if (string.IsNullOrEmpty(fieldString))
                        ctx.Fail(_attribute.ErrorMessage);
                }
                else if (fieldOrForm is LocalizedString localizedString)
                {
                    if (localizedString.Localizations == null)
                        ctx.Fail(_attribute.ErrorMessage);
                    else
                    {
                        foreach (OrderedDictionary<LanguageId, string>.KeyValue keyValue in localizedString.Localizations)
                        {
                            if (string.IsNullOrEmpty(keyValue.Value))
                                ctx.Fail(_attribute.ErrorMessage, $"{nameof(LocalizedString.Localizations)}/{keyValue.Key}");
                        }
                    }
                }
                else if (fieldOrForm is MetaTime metaTime)
                {
                    if(metaTime == default)
                        ctx.Fail(_attribute.ErrorMessage);
                }
            }
        }

        string ErrorMessage { get; }

        public MetaValidateRequiredAttribute(string errorMessage)
        {
            ErrorMessage = errorMessage;
        }

        public MetaValidateRequiredAttribute()
        {
            ErrorMessage = "This field is required.";
        }

        public override string ValidationRuleName => "notEmpty";

        public override object ValidationRuleProps => new
        {
            message = ErrorMessage,
        };

        public override Type CustomValidatorType => typeof(Validator);
    }

    /// <summary>
    /// On a Dashboard form, the value for this field must be given such that it was in the future when it is given.
    /// </summary>
    public class MetaValidateInFutureAttribute : MetaFormFieldValidatorBaseAttribute
    {
        public class Validator : IMetaFormValidator
        {
            readonly MetaValidateInFutureAttribute _attribute;

            public Validator(MetaValidateInFutureAttribute attribute)
            {
                _attribute = attribute;
            }

            public void Validate(object fieldOrForm, FormValidationContext ctx)
            {
                if (fieldOrForm is MetaTime metaTime)
                {
                    if (metaTime < MetaTime.Now)
                        ctx.Fail(_attribute.ErrorMessage);
                }
            }
        }

        string ErrorMessage { get; }

        public MetaValidateInFutureAttribute(string errorMessage)
        {
            ErrorMessage = errorMessage;
        }

        public MetaValidateInFutureAttribute()
        {
            ErrorMessage = "Field must be a time from the future!";
        }

        public override string ValidationRuleName  => "inFuture";
        public override object ValidationRuleProps => new {message = ErrorMessage};
        public override Type   CustomValidatorType => typeof(Validator);
    }

    /// <summary>
    /// On a Dashboard form, the value for this field must be within the defined range.
    /// </summary>
    public class MetaValidateInRangeAttribute : MetaFormFieldValidatorBaseAttribute
    {
        public class Validator : IMetaFormValidator
        {
            readonly MetaValidateInRangeAttribute _attribute;

            public Validator(MetaValidateInRangeAttribute attribute)
            {
                _attribute = attribute;
            }

            public void Validate(object fieldOrForm, FormValidationContext ctx)
            {
                try
                {
                    int integer = (int)fieldOrForm;
                    if (integer < _attribute.Min || integer > _attribute.Max)
                    {
                        ctx.Fail(_attribute.ErrorMessage);
                    }
                }
                catch (InvalidCastException) { }
            }
        }

        string     ErrorMessage { get; }
        public int Min          { get; }
        public int Max          { get; }

        public MetaValidateInRangeAttribute(int min, int max)
        {
            Min          = min;
            Max          = max;
            ErrorMessage = FormattableString.Invariant($"value must be in range of {min} and {max}");
        }

        public MetaValidateInRangeAttribute(int min, int max, string message)
        {
            Min          = min;
            Max          = max;
            ErrorMessage = message;
        }

        public override string ValidationRuleName => "inRange";

        public override object ValidationRuleProps => new
        {
            min     = Min,
            max     = Max,
            message = ErrorMessage,
        };

        public override Type CustomValidatorType => typeof(Validator);
    }
}
