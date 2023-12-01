// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace Metaplay.Core.Forms
{
    public class FormValidationResult
    {
        public string Path   { get; }
        public string Reason { get; }

        public FormValidationResult(string path, string reason)
        {
            Path   = path;
            Reason = reason;
        }

        public override string ToString()
        {
            return $"ValidationFailure at {nameof(Path)}: {Path}, {nameof(Reason)}: {Reason}";
        }
    }

    public class FormValidationContext
    {
        public List<FormValidationResult> Results     { get; } = new List<FormValidationResult>();
        Stack<string>                     CurrentPath { get; } = new Stack<string>();

        public void Fail(string failureReason)
        {
            Results.Add(new FormValidationResult(GatherPath(), failureReason));
        }

        public void Fail(string failureReason, string fieldName)
        {
            Results.Add(new FormValidationResult(GatherPath(fieldName), failureReason));
        }

        public void PushPath(string fieldName)
        {
            CurrentPath.Push(fieldName);
        }

        public void PopPath()
        {
            CurrentPath.Pop();
        }

        public void ClearPath()
        {
            CurrentPath.Clear();
        }

        string GatherPath()
        {
            if (CurrentPath.Count == 0)
                return "";

            StringBuilder sb = new StringBuilder();
            foreach (string s in CurrentPath.Reverse())
            {
                sb.Append(s);
                sb.Append('/');
            }

            return sb.ToString(0, sb.Length - 1);
        }

        string GatherPath(string field)
        {
            if (CurrentPath.Count == 0)
                return field;

            StringBuilder sb = new StringBuilder();
            foreach (string s in CurrentPath.Reverse())
            {
                sb.Append(s);
                sb.Append('/');
            }

            sb.Append(field);

            return sb.ToString();
        }
    }

    public interface IMetaFormValidator
    {
        void Validate(object fieldOrForm, FormValidationContext ctx);
    }

    public interface IMetaFormValidator<in T> : IMetaFormValidator
    {
        void Validate(T fieldOrForm, FormValidationContext ctx);
    }

    public abstract class MetaFormValidator<T> : IMetaFormValidator<T>
    {
        public abstract void Validate(T fieldOrForm, FormValidationContext ctx);

        public void Validate(object fieldOrForm, FormValidationContext ctx) =>
            Validate((T)fieldOrForm, ctx);
    }

    [AttributeUsage(System.AttributeTargets.Class |
        System.AttributeTargets.Struct |
        System.AttributeTargets.Interface, AllowMultiple = true, Inherited = true)]
    public class MetaFormClassValidatorAttribute : Attribute
    {
        public MetaFormClassValidatorAttribute(Type customValidatorType)
        {
            CustomValidatorType = customValidatorType;
        }

        public Type CustomValidatorType { get; }
    }

    public interface IMetaFormFieldValidatorAttribute
    {
        Type CustomValidatorType { get; }
    }

}
