// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Metaplay.Core.Forms
{
    public interface IMetaFormFieldDecorator
    {
        bool   IsMultiDecorator    { get; }
        string FieldDecoratorKey   { get; }
        object FieldDecoratorValue { get; }
    }

    [AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Field, AllowMultiple = false)]
    public abstract class MetaFormFieldDecoratorBaseAttribute : Attribute, IMetaFormFieldDecorator
    {
        public          bool   IsMultiDecorator    => false;
        public abstract string FieldDecoratorKey   { get; }
        public abstract object FieldDecoratorValue { get; }
    }

    [AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Field, AllowMultiple = true)]
    public abstract class MetaFormFieldMultiDecoratorBaseAttribute : Attribute, IMetaFormFieldDecorator
    {
        public          bool   IsMultiDecorator    => true;
        public abstract string FieldDecoratorKey   { get; }
        public abstract object FieldDecoratorValue { get; }
    }

    public class MetaFormFieldContextAttribute : MetaFormFieldMultiDecoratorBaseAttribute
    {
        public string Key;
        public object Value;

        public MetaFormFieldContextAttribute(string key, object value)
        {
            Key   = key;
            Value = value;
        }

        public override string FieldDecoratorKey => "context";
        public override object FieldDecoratorValue => new
        {
            key   = Key,
            value = Value,
        };
    }

    public abstract class MetaFormFieldTypeHintAttribute : MetaFormFieldDecoratorBaseAttribute
    {
        public override string FieldDecoratorKey => "fieldTypeHint";
        public override object FieldDecoratorValue => new
        {
            type  = FieldType,
            props = FieldTypeProps,
        };

        public abstract string FieldType      { get; }
        public abstract object FieldTypeProps { get; }
    }

    [AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Field, AllowMultiple = true)]
    public abstract class MetaFormFieldValidatorBaseAttribute : MetaFormFieldMultiDecoratorBaseAttribute, IMetaFormFieldValidatorAttribute
    {
        public override string FieldDecoratorKey => "validationRules";
        public override object FieldDecoratorValue => new
        {
            type  = ValidationRuleName,
            props = ValidationRuleProps,
        };

        public abstract string ValidationRuleName  { get; }
        public abstract object ValidationRuleProps { get; }
        public abstract Type   CustomValidatorType { get; }
    }

    public class MetaFormFieldCustomValidatorAttribute : MetaFormFieldValidatorBaseAttribute
    {
        Type _validatorType;
        public MetaFormFieldCustomValidatorAttribute(Type validatorType)
        {
            _validatorType = validatorType;
        }

        public override string ValidationRuleName => _validatorType.Name;
        public override object ValidationRuleProps => null;
        public override Type CustomValidatorType => _validatorType;
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class MetaFormDerivedMembersOnlyAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class)]
    public class MetaFormDeprecatedAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class)]
    public class MetaFormHiddenAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class MetaFormDontCaptureDefaultAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class)]
    public class MetaFormConfigLibraryItemReference : Attribute
    {
        public Type ConfigItemType { get; }

        public MetaFormConfigLibraryItemReference(Type configItemType)
        {
            ConfigItemType = configItemType;
        }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class MetaFormNotEditableAttribute : MetaFormFieldDecoratorBaseAttribute
    {
        public override string FieldDecoratorKey => "notEditable";

        public override object FieldDecoratorValue => true;
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class MetaFormLayoutOrderHintAttribute : MetaFormFieldDecoratorBaseAttribute
    {
        public int Order { get; }

        public MetaFormLayoutOrderHintAttribute(int order)
        {
            Order = order;
        }

        public override string FieldDecoratorKey   => "orderHint";
        public override object FieldDecoratorValue => Order;
    }
}
