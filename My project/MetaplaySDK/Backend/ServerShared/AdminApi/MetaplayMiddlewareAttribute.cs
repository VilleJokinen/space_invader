// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;

namespace Metaplay.Server.AdminApi
{
    /// <summary>
    /// Annotated middleware is automatically registed to AdminAPI.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class MetaplayMiddlewareAttribute : Attribute
    {
        public enum RegisterPhase
        {
            /// <summary>
            /// Middleware is added to the beginning of the ASP.NET Core middleware chain.
            /// </summary>
            Early,

            /// <summary>
            /// Middleware is added to the end of the ASP.NET Core middleware chain. The middleware
            /// is executed after Routing, CORS, Authentication and Authorization middlewares.
            /// </summary>
            Late,
        }

        public RegisterPhase Phase { get; }
        public MetaplayMiddlewareAttribute(RegisterPhase phase)
        {
            Phase = phase;
        }
    }
}
