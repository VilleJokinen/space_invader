// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Metaplay.Core
{
    public static class MethodInfoExtensions
    {
#if !NETCOREAPP
        public static T CreateDelegate<T>(this MethodInfo mi) where T : System.Delegate
        {
            return (T)mi.CreateDelegate(typeof(T));
        }
#endif

        /// <summary>
        /// Invokes the Method. Any exceptions thrown by the method are thrown as-is, without wrapping them into a TargetInvocationException.
        /// </summary>
        public static object InvokeWithoutWrappingError(this MethodInfo methodInfo, object obj, object[] parameters)
        {
            try
            {
                return methodInfo.Invoke(obj, parameters: parameters);
            }
            catch (TargetInvocationException tai)
            {
                ExceptionDispatchInfo.Capture(tai.InnerException).Throw();
                return null; // unreachable
            }
        }
    }
}
