// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Metaplay.Server.AdminApi
{
    public class PermissionDefinition
    {
        public readonly string      Name;
        public readonly string      Description;
        public readonly bool        IsDashboardOnly;
        public readonly string[]    DefaultRoles;

        public PermissionDefinition(string name, string description, bool isDashboardOnly, string[] defaultRoles)
        {
            Name = name;
            Description = description;
            IsDashboardOnly = isDashboardOnly;
            DefaultRoles = defaultRoles;
        }
    }

    public class PermissionGroupDefinition
    {
        public readonly string                  Title;
        public readonly PermissionDefinition[]  Permissions;

        public PermissionGroupDefinition(string title, PermissionDefinition[] permissions)
        {
            Title = title;
            Permissions = permissions;
        }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class PermissionAttribute : Attribute
    {
        public readonly bool        IsDashboardOnly;
        public readonly string[]    DefaultRoles;

        public PermissionAttribute(params string[] defaultRoles)
        {
            DefaultRoles = defaultRoles;
            IsDashboardOnly = false;
        }

        public PermissionAttribute(bool isDashboardOnly, params string[] defaultRoles)
        {
            DefaultRoles = defaultRoles;
            IsDashboardOnly = isDashboardOnly;
        }
    }

    /// <summary>
    /// Mark a class as containing a named group of AdminApi permissions. Each permission needs to be of type 'public const string'
    /// and have the <see cref="PermissionAttribute"/> and <see cref="MetaDescriptionAttribute"/> attributes.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class AdminApiPermissionGroupAttribute : Attribute
    {
        public readonly string Title;

        public AdminApiPermissionGroupAttribute(string title) => Title = title;
    }

    /// <summary>
    /// Resolved permissions configuration that combines the built-in defaults with any user-specified values.
    /// Also supports printing out the resolved values as yaml-compatible text block for easy copy-pasting from
    /// the server output to the Options.base.yaml.
    /// </summary>
    public class ResolvedPermissionConfig
    {
        public readonly PermissionGroupDefinition[]         PermissionGroups;       // All permission group definitions defined in the code
        public readonly string[]                            ActiveRoles;            // List of active roles for the environment
        public readonly OrderedDictionary<string, string[]> RolesForPermission;     // List of roles for each permission
        public readonly Dictionary<string, List<string>>    RolePermissions;        // Resolved permissions for each role (with env-specific prefix applied to roles)

        readonly Dictionary<string, int>                _roleStringOffset = new Dictionary<string, int>();
        readonly int                                    _roleStringLength = 0;

        public ResolvedPermissionConfig(PermissionGroupDefinition[] allPermissionGroups, string[] activeRoles, Dictionary<string, List<string>> rolesForPermission, string rolePrefix)
        {
            PermissionGroups = allPermissionGroups;
            ActiveRoles = activeRoles;

            // Validations for permissions
            if (rolesForPermission != null)
            {
                string[] allPermissionNames = allPermissionGroups.SelectMany(group => group.Permissions).Select(perm => perm.Name).ToArray();
                string[] definedPermissions = rolesForPermission.Keys.ToArray();

                // Check that Permissions contains no unknown entries (which are not defined in code)
                string[] unknownPermissions = definedPermissions.Except(allPermissionNames).ToArray();
                if (unknownPermissions.Length > 0)
                    throw new InvalidOperationException($"Unknown permissions specified (these do not exist in code): {string.Join(", ", unknownPermissions)}");

                // Check that Permissions contains definitions for all known permissions
                string[] missingPermissions = allPermissionNames.Except(definedPermissions).ToArray();
                if (missingPermissions.Length > 0)
                    throw new InvalidOperationException($"Missing definitions for the following permissions (not found in the .yaml): {string.Join(", ", missingPermissions)}");

                // Check that roles in Permissions refer to valid entries in Roles
                foreach ((string permName, List<string> roles) in rolesForPermission)
                {
                    if (!roles.Contains(DefaultRole.GameAdmin))
                        throw new InvalidOperationException($"Permission '{permName}' is missing '{DefaultRole.GameAdmin}' (required role for all permissions)");

                    foreach (string role in roles)
                    {
                        if (!activeRoles.Contains(role))
                            throw new InvalidOperationException($"Permission '{permName}' is associated with unknown role '{role}'");
                    }
                }
            }

            // Order for RolesForPermission:
            // a) by permission group name (alphabetic, bit of a hack)
            // b) by permissions (in declaration order)
            // c) roles in the order they are in 'ActiveRoles'
            RolesForPermission = new OrderedDictionary<string, string[]>();
            foreach (PermissionGroupDefinition permGroup in allPermissionGroups.OrderBy(group => group.Title, StringComparer.Ordinal))
            {
                foreach (PermissionDefinition perm in permGroup.Permissions)
                {
                    // Resolve defined roles (if any) or the defaults
                    IEnumerable<string> rolesForPerm = (IEnumerable<string>)rolesForPermission?.GetValueOrDefault(perm.Name, null) ?? perm.DefaultRoles;

                    // Resolve final role list in the order defined in ActiveRoles
                    RolesForPermission.Add(perm.Name, activeRoles.Where(role => rolesForPerm.Contains(role)).ToArray());
                }
            }

            // Compute the list of permissions for each role (with auth-specific prefix for roles)
            RolePermissions = new Dictionary<string, List<string>>();
            foreach ((string permission, string[] roles) in RolesForPermission)
            {
                foreach (string role in roles)
                {
                    string roleWithPrefix = $"{rolePrefix}{role}";
                    if (RolePermissions.ContainsKey(roleWithPrefix))
                        RolePermissions[roleWithPrefix].Add(permission);
                    else
                        RolePermissions[roleWithPrefix] = new List<string>() { permission };
                }
            }

            // Compute string offets for each role name
            int offset = 0;
            foreach (string role in ActiveRoles)
            {
                _roleStringOffset[role] = offset;
                offset += role.Length + 2; // comma and space
            }
            _roleStringLength = offset - 2;
        }

        /// <summary>
        /// Convert a list of roles to a string such that each role is in the same character offset,
        /// to help with tabular formatting.
        /// </summary>
        /// <param name="roles"></param>
        /// <returns></returns>
        string GetAlignedRoles(string[] roles)
        {
            Span<char> buffer = stackalloc char[_roleStringLength];
            buffer.Fill(' ');

            if (roles.Length == 0)
                return "<no roles defined>".PadRight(_roleStringLength);

            for (int ndx = 0; ndx < roles.Length; ndx++)
            {
                string role = roles[ndx];
                int offset = _roleStringOffset[role];
                role.CopyTo(buffer.Slice(offset, role.Length));
                if (ndx != roles.Length - 1)
                    buffer[offset + role.Length] = ',';
            }

            return new string(buffer);
        }

        /// <summary>
        /// Convert to a string that is in a tabular format that can be copy-pasted directly into the Options.yaml.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            int longestPermName = RolesForPermission.Keys.MaxBy(permName => permName.Length).Length;

            StringBuilder sb = new StringBuilder();
            foreach (PermissionGroupDefinition group in PermissionGroups)
            {
                if (group.Permissions.Length > 0)
                {
                    sb.Append($"\n    # {group.Title}");
                    foreach (PermissionDefinition perm in group.Permissions)
                    {
                        string[] roles = RolesForPermission[perm.Name];
                        sb.Append($"\n    {(perm.Name + ":").PadRight(longestPermName + 1)} [ {GetAlignedRoles(roles)} ]");
                    }
                }
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Type of authentication to use for the API
    /// </summary>
    public enum AuthenticationType
    {
        Disabled, // Prevent the server from starting with this option. Used to force the user to make an explicit decision about which type of authentication they want.
        None,     // No authentication enabled - anyone can connect
        JWT,      // JWT authentication
    }

    /// <summary>
    /// Authentication options config for <see cref="AuthenticationType.None"/>
    /// </summary>
    public class AuthenticationTypeNoneConfiguration
    {
        public void Validate()
        {
        }
    }

    /// <summary>
    /// Authentication options config for <see cref="AuthenticationType.JWT"/>.
    /// </summary>
    public class AuthenticationTypeJwtConfiguration
    {
        public string Domain            { get; private set; } = ""; // JWT domain.
        public string Audience          { get; private set; } = ""; // JWT audience.
        public string RolePrefix        { get; private set; } = ""; // Prefixed to all role names.
        public string BearerTokenSource { get; private set; } = ""; // Name of the header that contains the bearer token.
        public string LogoutUri         { get; private set; } = ""; // Optional: If provided, the logout button on the Dashboard jumps to here when clicked.
        public string UserInfoUri       { get; private set; } = ""; // The URI that the Dash uses to get extended user information for the Users page.

        public void Validate()
        {
            if (string.IsNullOrEmpty(Domain) ||
                string.IsNullOrEmpty(Audience) ||
                string.IsNullOrEmpty(RolePrefix) ||
                string.IsNullOrEmpty(BearerTokenSource))
                throw new InvalidOperationException("Authentication is set to JWT, but at least one of Domain, Audience, RolePrefix or BearerTokenSource is empty - authentication would always fail.");
        }
    }

    // Specify all options related to the admin api
    [RuntimeOptions("AdminApi", isStatic: false, "Configuration options for authentication of the Admin API and LiveOps Dashboard users.")]
    public class AdminApiOptions : RuntimeOptionsBase
    {
        // Changing any of these four options during runtime will likely lead to broken API behaviour - they
        // should be considered as static
        [CommandLineAlias("-Auth")]
        [MetaDescription("Type of authentication and authorization used for the HTTP admin API (`None` or `JWT`).")]
        public AuthenticationType                      Type                 { get; private set; } = IsLocalEnvironment ? AuthenticationType.None : AuthenticationType.Disabled;
        [MetaDescription(("Configuration options for `None` type authentication and authorization."))]
        public AuthenticationTypeNoneConfiguration     NoneConfiguration    { get; private set; } = new AuthenticationTypeNoneConfiguration();
        [MetaDescription(("Configuration options for `JWT` type authentication and authorization."))]
        public AuthenticationTypeJwtConfiguration      JwtConfiguration     { get; private set; } = new AuthenticationTypeJwtConfiguration();

        [CommandLineAlias("-AdminApiListenHost")]
        [MetaDescription("Host/interface that the Admin API listens on. Setting 0.0.0.0 listens on all IPv4 interfaces, 'localhost' only allows local connections.")]
        public string                           ListenHost                  { get; private set; } = IsLocalEnvironment ? "localhost" : "0.0.0.0";
        [CommandLineAlias("-AdminApiListenPort")]
        [MetaDescription("Port that the Admin API listens on.")]
        public int                              ListenPort                  { get; private set; } = IsLocalEnvironment ? 5550 : 80;

        [MetaDescription("The file path to the LiveOps Dashboard build artifact.")]
        public string                           WebRootPath                 { get; private set; } = IsLocalEnvironment ? "../Dashboard/dist" : "wwwroot"; // Defaults to where built dashboard is copied to in docker builds.

        // These option are ok to change during runtime
        [MetaDescription("List of all available user roles.")]
        public string[]                         Roles                       { get; private set; }
        [PrettyPrint(PrettyPrintFlag.Hide)]
        [MetaDescription("List of all available permissions and the roles that include them.")]
        public Dictionary<string, List<string>> Permissions                 { get; private set; }

        // Resolved role/permission config
        [IgnoreDataMember]
        public ResolvedPermissionConfig         ResolvedPermissions         { get; private set; }

        // Request logging
        [CommandLineAlias("-LogAllRequests")]
        [MetaDescription("Should all requests be logged (or only ones exceeding the LongRequestThreshold)?")]
        public bool                             LogAllRequests              { get; private set; } = IsCloudEnvironment;
        [MetaDescription("Warn about requests taking more than X milliseconds.")]
        public int                              LongRequestThreshold        { get; private set; } = 1000;

        [MetaDescription("Computed list of permissions for each role (with the environment role prefix applied).")]
        [PrettyPrint(PrettyPrintFlag.Hide)]
        public Dictionary<string, List<string>> RolePermissions => ResolvedPermissions?.RolePermissions;

        public string GetAdminApiDomain()
        {
            switch (Type)
            {
                case AuthenticationType.None:   return null;
                case AuthenticationType.JWT:    return $"https://{JwtConfiguration.Domain}/";
                default:
                    throw new InvalidOperationException($"Unknown authentication type {Type}");
            }
        }

        public override Task OnLoadedAsync()
        {
            // Validate the appropriate config..
            switch (Type)
            {
                case AuthenticationType.Disabled:
                    throw new InvalidOperationException("Cannot start with authentication type set to Disabled. You must configure authentication correctly before the server will start.");

                case AuthenticationType.None:
                    NoneConfiguration.Validate();
                    break;

                case AuthenticationType.JWT:
                    JwtConfiguration.Validate();
                    break;

                default:
                    throw new InvalidOperationException("Invalid or incomplete auth type chosen.");
            }

            // If Roles not specified, default to the list of built-in ones
            if (Roles == null || Roles.Length == 0)
                Roles = DefaultRole.All;

            // Roles must always contain GameAdmin
            if (!Roles.Contains(DefaultRole.GameAdmin))
                throw new InvalidOperationException($"Authentication:Roles must contain '{DefaultRole.GameAdmin}'!");

            // Resolve PermissionConfig
            ResolvedPermissions = new ResolvedPermissionConfig(GetAllPermissions(), Roles, Permissions, GetRolePrefix());

            return Task.CompletedTask;
        }

        /// <summary>
        /// Return an authentication-method dependent prefix that should be prepended to all role names.
        /// Useful for specifying different roles for users in different environments, where the environment
        /// name is used as the role prefix.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public string GetRolePrefix()
        {
            switch (Type)
            {
                case AuthenticationType.None:   return "";
                case AuthenticationType.JWT:    return JwtConfiguration.RolePrefix;
                default:
                    throw new InvalidOperationException("Invalid or incomplete auth type chosen.");
            }
        }

        /// <summary>
        /// Get all declared permission groups from the code.
        /// </summary>
        /// <returns></returns>
        public static PermissionGroupDefinition[] GetAllPermissions()
        {
            List<PermissionGroupDefinition> permissionGroups = new List<PermissionGroupDefinition>();

            foreach (Type groupType in TypeScanner.GetClassesWithAttribute<AdminApiPermissionGroupAttribute>())
            {
                AdminApiPermissionGroupAttribute groupAttrib = groupType.GetCustomAttribute<AdminApiPermissionGroupAttribute>();
                List<PermissionDefinition> permissions = new List<PermissionDefinition>();

                foreach (FieldInfo fi in groupType.GetFields(BindingFlags.Static | BindingFlags.Public))
                {
                    if (fi.GetCustomAttribute<PermissionAttribute>() is PermissionAttribute attrib)
                    {
                        if (!fi.IsLiteral || fi.FieldType != typeof(string))
                            throw new InvalidOperationException($"AdminApi permission {groupType.ToGenericTypeString()}.{fi.Name} must be of type 'public const string'");

                        MetaDescriptionAttribute descriptionAttrib = fi.GetCustomAttribute<MetaDescriptionAttribute>();
                        if (descriptionAttrib == null)
                            throw new InvalidOperationException($"Permission {groupType.ToGenericTypeString()}.{fi.Name} is missing the [MetaDescription] attribute");

                        if (!fi.IsMetaFeatureEnabled())
                            continue;

                        string name = (string)fi.GetValue(null);
                        permissions.Add(new PermissionDefinition(name, descriptionAttrib.Description, attrib.IsDashboardOnly, attrib.DefaultRoles));
                    }
                }

                permissionGroups.Add(new PermissionGroupDefinition(groupAttrib.Title, permissions.ToArray()));
            }

            return permissionGroups.ToArray();
        }
    }
}
