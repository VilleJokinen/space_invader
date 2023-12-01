// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

// Enable to use the pre-R25 fixed-point parsing methods (for backward compatibility)
//#define METAPLAY_USE_LEGACY_FIXED_POINT_PARSING

using Metaplay.Core.Activables;
using Metaplay.Core.Config;
using Metaplay.Core.Math;
using Metaplay.Core.Player;
using Metaplay.Core.Schedule;
using Metaplay.Core.Web3;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using static System.FormattableString;

namespace Metaplay.Core
{
    using ParseFunc = Func<ConfigLexer, object>;

    // ConfigParser

    public static class ConfigParser
    {
        static Dictionary<Type, ParseFunc> s_parseFuncs = new Dictionary<Type, ParseFunc>();

        public static bool TryParse(Type type, ConfigLexer lexer, out object result)
        {
            if (s_parseFuncs.TryGetValue(type, out ParseFunc parseFunc))
            {
                result = parseFunc(lexer);
                return true;
            }

            return TryParseBuiltin(type, lexer, out result);
        }

        public static T ParseExact<T>(string input)
        {
            ConfigLexer lexer = new ConfigLexer(input);
            T result = Parse<T>(lexer);
            if (!lexer.IsAtEnd)
                throw new ParseError($"Input was not fully consumed while parsing type {typeof(T).ToGenericTypeString()} '{input}'");
            return result;
        }

        public static T Parse<T>(ConfigLexer lexer)
        {
            if (!TryParse(typeof(T), lexer, out object result))
                throw new ParseError($"ConfigParser: Type {typeof(T).ToGenericTypeString()} does not have a registered parse method");

            return (T)result;
        }

        static void RegisterParseFunc(Type type, ParseFunc parseFunc)
        {
            if (s_parseFuncs.ContainsKey(type))
                throw new InvalidOperationException($"Trying to register multiple parse functions for type {type.ToGenericTypeString()}");

            s_parseFuncs.Add(type, parseFunc);
        }

        static void RegisterParseFunc<T>(ParseFunc parseFunc)
        {
            RegisterParseFunc(typeof(T), parseFunc);
        }

        [Obsolete("IGameConfigDataResolver no longer allowed in parse function")]
        public static void RegisterCustomParseFunc<T>(Func<ConfigLexer, IGameConfigDataResolver, object> parseFunc)
        {
            throw new NotImplementedException("IGameConfigDataResolver no longer allowed in parse function");
        }

        public static void RegisterCustomParseFunc<T>(ParseFunc parseFunc)
        {
            // Register for the given type.
            RegisterParseFunc(typeof(T), parseFunc);

            // Register also up the base type sequence, as long as they have UseCustomParserFromDerivedAttribute.
            for (Type ancestor = typeof(T).BaseType;
                ancestor.GetCustomAttribute<UseCustomParserFromDerivedAttribute>(inherit: false) != null;
                ancestor = ancestor.BaseType)
            {
                RegisterParseFunc(ancestor, parseFunc);
            }
        }

        public static bool HasRegisteredParseFunc(Type type)
        {
            return s_parseFuncs.ContainsKey(type);
        }

        public static void RegisterBasicTypeParsers()
        {
            RegisterParseFunc<string>((lexer) => lexer.ParseIdentifierOrString());
            RegisterParseFunc<bool>((lexer) => lexer.ParseBooleanLiteral());
            RegisterParseFunc<char>((lexer) => lexer.ParseCharLiteral());
            RegisterParseFunc<int>((lexer) => lexer.ParseIntegerLiteral());
            RegisterParseFunc<long>((lexer) => lexer.ParseLongLiteral());
            RegisterParseFunc<float>((lexer) => lexer.ParseFloatLiteral());
            RegisterParseFunc<double>((lexer) => lexer.ParseDoubleLiteral());
            RegisterParseFunc<F32>((lexer) => ParseF32(lexer));
            RegisterParseFunc<F32Vec2>((lexer) => ParseF32Vec2(lexer));
            RegisterParseFunc<F32Vec3>((lexer) => ParseF32Vec3(lexer));
            RegisterParseFunc<F64>((lexer) => ParseF64(lexer));
            RegisterParseFunc<F64Vec2>((lexer) => ParseF64Vec2(lexer));
            RegisterParseFunc<F64Vec3>((lexer) => ParseF64Vec3(lexer));
            RegisterParseFunc<IntVector2>((lexer) => ParseIntVector2(lexer));
            RegisterParseFunc<MetaTime>((lexer) => ParseMetaTime(lexer));
            RegisterParseFunc<MetaDuration>((lexer) => ParseMetaDuration(lexer));
            RegisterParseFunc<NftId>((lexer) => ParseNftId(lexer));

            RegisterParseFunc<MetaCalendarDate>(lexer => MetaCalendarDate.ConfigParse(lexer));
            RegisterParseFunc<MetaCalendarTime>(lexer => MetaCalendarTime.ConfigParse(lexer));
            RegisterParseFunc<MetaCalendarPeriod>(lexer => MetaCalendarPeriod.ConfigParse(lexer));
            RegisterParseFunc<MetaScheduleTimeMode>(lexer => ParseEnumCaseInsensitive<MetaScheduleTimeMode>(lexer));

            // \todo [nuutti] Remove from here? #activables
            RegisterParseFunc<MetaActivableLifetimeSpec>(MetaActivableLifetimeSpec.Parse);
            RegisterParseFunc<MetaActivableCooldownSpec>(MetaActivableCooldownSpec.Parse);
        }

        static bool TryParseBuiltin(Type type, ConfigLexer lexer, out object result)
        {
            if (type.ImplementsInterface<IStringId>())
            {
                // Empty input handling can be relevant when there's a list of StringIds that is specified
                // in multiple columns in a spreadsheet. SpreadsheetParser.cs (ValueCollectionParserPass)
                // parses (non-trailing) empty cells as empty strings.
                // \todo In any case this feels a bit out of place here in lexer-based parsing. Feels like it should
                //       be handled on the higher level which deals with cells, not on lexer level. And maybe there
                //       should be some proper syntax for null StringIds (and null Nullables) that could be used even
                //       in custom parse funcs where the StringId is just part of the parsed content.
                // \todo This might become unnecessary with the new config parsing in R25, if empty/omitted cells
                //       are no longer parsed as empty strings.
                if (lexer.IsAtEnd)
                {
                    result = null;
                    return true;
                }

                result = StringIdUtil.ConfigParse(type, lexer);
                return true;
            }
            else if (type.IsEnum)
            {
                string str = lexer.ParseIdentifier();
                try
                {
                    result = Enum.Parse(type, str);
                }
                catch (Exception ex)
                {
                    throw new ParseError($"Invalid enum value '{str}' of type {type.Name}", ex);
                }

                return true;
            }
            else if (type.IsGenericTypeOf(typeof(Nullable<>)))
            {
                if (lexer.IsAtEnd)
                {
                    result = null;
                    return true;
                }

                Type elemType = type.GetGenericArguments()[0];
                return TryParse(elemType, lexer, out result);
            }
            else
            {
                result = null;
                return false;
            }
        }

        public static void RegisterGameConfigs()
        {
            foreach (GameConfigRepository.GameConfigTypeInfo typeInfo in GameConfigRepository.Instance.AllGameConfigTypes)
            {
                foreach (MemberInfo memberInfo in GameConfigTypeUtil.EnumerateLibraryMembersOfGameConfig(typeInfo.GameConfigType))
                    RegisterGameConfigLibrary(memberInfo.GetDataMemberType());
            }
        }

        static void RegisterGameConfigLibrary(Type type)
        {
            if (!type.IsGameConfigLibrary())
                throw new ArgumentException($"{type} is not a game config library");

            Type[] arguments = type.GenericTypeArguments;
            RegisterGameConfigLibrary(arguments[0], arguments[1]);
        }

        public static void RegisterGameConfigLibrary(Type keyType, Type infoType)
        {
            // Register parser for MetaRef<> for info type (and info type's IGameConfigData<TKey>-implementing base types).
            // MetaRefs are parsed as the key type.
            // \todo [petri] delegate WireDataType, PrettyPrinting & config parser to generic keyType handler

            for (Type infoTypeAncestor = infoType; infoTypeAncestor.ImplementsInterface<IGameConfigData>(); infoTypeAncestor = infoTypeAncestor.BaseType)
            {
                if (!s_parseFuncs.ContainsKey(infoTypeAncestor))
                    RegisterGameConfigInfoReferenceTypeParsers(keyType, infoTypeAncestor);
            }
        }

        public static void RegisterCustomParsers()
        {
            foreach (ConfigParserProvider parserProvider in IntegrationRegistry.CreateAll<ConfigParserProvider>())
            {
                parserProvider.RegisterParsers();
            }
        }

        static void RegisterGameConfigInfoReferenceTypeParsers(Type keyType, Type infoType)
        {
            if (!infoType.ImplementsGenericInterface(typeof(IGameConfigData<>))
             || infoType.GetGenericInterfaceTypeArguments(typeof(IGameConfigData<>))[0] != keyType)
            {
                throw new ArgumentException($"Invalid key and/or info types given; {infoType.ToGenericTypeString()} does not implement IGameConfigData<{keyType.ToGenericTypeString()}>");
            }

            // Register plain IGameConfigData reference parser.
            // Note that plain IGameConfigData references within configs are no longer supported;
            // this exists just to provide a more helpful error message.
            RegisterParseFunc(infoType, (lexer) =>
            {
                if (lexer.IsAtEnd)
                    throw new InvalidOperationException($"Encountered a {infoType.ToGenericTypeString()} config data reference. Please change the reference type to a MetaRef<{infoType.Name}>. Plain IGameConfigData references are no longer supported within config itself.");

                if (!TryParse(keyType, lexer, out object key))
                    throw new InvalidOperationException($"Trying to parse {infoType.ToGenericTypeString()} but found no parser for its key type {keyType.ToGenericTypeString()}");

                throw new InvalidOperationException($"Encountered a {infoType.ToGenericTypeString()} config data reference (to '{key}'). Please change the reference type to a MetaRef<{infoType.Name}>. Plain IGameConfigData references are no longer supported within config itself.");
            });

            // Register MetaRef parser
            Type metaRefType = typeof(MetaRef<>).MakeGenericType(infoType);
            RegisterParseFunc(metaRefType, (lexer) =>
            {
                // \note Empty input always parses to null MetaRef, even when key type is not nullable.
                //       We don't try to parse an empty input as the key type, indeed because it might not be nullable.
                if (lexer.IsAtEnd)
                    return null;

                if (!TryParse(keyType, lexer, out object key))
                    throw new InvalidOperationException($"Trying to parse {infoType.ToGenericTypeString()} but found no parser for its key type {keyType.ToGenericTypeString()}");

                if (key != null)
                {
                    return MetaRefUtil.CreateFromKey(metaRefType: metaRefType, key);
                }
                else
                    return null;
            });
        }

        static F32 ParseF32(ConfigLexer lexer)
        {
            switch (lexer.CurrentToken.Type)
            {
                case ConfigLexer.TokenType.IntegerLiteral:
                    return F32.FromInt(lexer.ParseIntegerLiteral());

                case ConfigLexer.TokenType.FloatLiteral:
                    string str = lexer.GetTokenString(lexer.CurrentToken);
                    F32 value = F32.Parse(str);
                    lexer.Advance();
                    return value;

                default:
                    throw new ParseError($"");
            }
        }

        static F32Vec2 ParseF32Vec2(ConfigLexer lexer)
        {
            // \todo [petri] proper parser
            lexer.ParseToken(ConfigLexer.TokenType.LeftParenthesis);
            F32 x = ParseF32(lexer);
            lexer.ParseToken(ConfigLexer.TokenType.Comma);
            F32 y = ParseF32(lexer);
            lexer.ParseToken(ConfigLexer.TokenType.RightParenthesis);
            return new F32Vec2(x, y);
        }

        static F32Vec3 ParseF32Vec3(ConfigLexer lexer)
        {
            lexer.ParseToken(ConfigLexer.TokenType.LeftParenthesis);
            F32 x = ParseF32(lexer);
            lexer.ParseToken(ConfigLexer.TokenType.Comma);
            F32 y = ParseF32(lexer);
            lexer.ParseToken(ConfigLexer.TokenType.Comma);
            F32 z = ParseF32(lexer);
            lexer.ParseToken(ConfigLexer.TokenType.RightParenthesis);
            return new F32Vec3(x, y, z);
        }

        static F64 ParseF64(ConfigLexer lexer)
        {
            switch (lexer.CurrentToken.Type)
            {
                case ConfigLexer.TokenType.IntegerLiteral:
                    return F64.FromInt(lexer.ParseIntegerLiteral());

                case ConfigLexer.TokenType.FloatLiteral:
                    string str = lexer.GetTokenString(lexer.CurrentToken);
                    F64 value = F64.Parse(str);
                    lexer.Advance();
                    return value;

                default:
                    throw new ParseError($"Invalid lexer token type {lexer.CurrentToken.Type}, expecting integer or float literal");
            }
        }

        static F64Vec2 ParseF64Vec2(ConfigLexer lexer)
        {
            // \todo [petri] proper parser
            lexer.ParseToken(ConfigLexer.TokenType.LeftParenthesis);
            F64 x = ParseF64(lexer);
            lexer.ParseToken(ConfigLexer.TokenType.Comma);
            F64 y = ParseF64(lexer);
            lexer.ParseToken(ConfigLexer.TokenType.RightParenthesis);
            return new F64Vec2(x, y);
        }

        static F64Vec3 ParseF64Vec3(ConfigLexer lexer)
        {
            lexer.ParseToken(ConfigLexer.TokenType.LeftParenthesis);
            F64 x = ParseF64(lexer);
            lexer.ParseToken(ConfigLexer.TokenType.Comma);
            F64 y = ParseF64(lexer);
            lexer.ParseToken(ConfigLexer.TokenType.Comma);
            F64 z = ParseF64(lexer);
            lexer.ParseToken(ConfigLexer.TokenType.RightParenthesis);
            return new F64Vec3(x, y, z);
        }

        static IntVector2 ParseIntVector2(ConfigLexer lexer)
        {
            lexer.ParseToken(ConfigLexer.TokenType.LeftParenthesis);
            int x = lexer.ParseIntegerLiteral();
            lexer.ParseToken(ConfigLexer.TokenType.Comma);
            int y = lexer.ParseIntegerLiteral();
            lexer.ParseToken(ConfigLexer.TokenType.RightParenthesis);
            return new IntVector2(x, y);
        }

        static MetaTime ParseMetaTime(ConfigLexer lexer)
        {
            return MetaTime.ConfigParse(lexer);
        }

        static MetaDuration ParseMetaDuration(ConfigLexer lexer)
        {
            // First detect if the duration is in unit format (e.g. "5d 3h 12s")
            // or in simple plain-seconds format.
            // Unit format is assumed if the initial number is followed by an identifier.
            // \todo [nuutti] Remove the plain-seconds format if it's not needed?
            bool isUnitFormat;
            {
                ConfigLexer tmpLexer = new ConfigLexer(lexer);
                _ = ParseF64(tmpLexer);
                isUnitFormat = tmpLexer.CurrentToken.Type == ConfigLexer.TokenType.Identifier;
            }

            if (isUnitFormat)
                return ParseMetaDurationUnitFormat(lexer);
            else
                return ParseMetaDurationPlainSecondsFormat(lexer);
        }

        static MetaDuration ParseMetaDurationUnitFormat(ConfigLexer lexer)
        {
            // \todo [nuutti] Restrict ranges of values

            int? days           = null;
            int? hours          = null;
            int? minutes        = null;
            F64? seconds        = null;

            while (true)
            {
                ConfigLexer.Token token = lexer.CurrentToken;

                if (token.Type == ConfigLexer.TokenType.IntegerLiteral)
                {
                    int     value   = lexer.ParseIntegerLiteral();
                    string  unit    = lexer.ParseIdentifier();

                    switch (unit)
                    {
                        case "d": SetValueCheckNotAlready(ref days,     value,              "days");    break;
                        case "h": SetValueCheckNotAlready(ref hours,    value,              "hours");   break;
                        case "m": SetValueCheckNotAlready(ref minutes,  value,              "minutes"); break;
                        case "s": SetValueCheckNotAlready(ref seconds,  F64.FromInt(value), "seconds"); break;
                        default:
                            throw new ParseError($"Invalid duration unit '{unit}' (at value {value})");
                    }
                }
                else if (token.Type == ConfigLexer.TokenType.FloatLiteral)
                {
                    F64     value   = F64.Parse(lexer.GetTokenString(token));
                    lexer.Advance();
                    string  unit    = lexer.ParseIdentifier();

                    switch (unit)
                    {
                        case "s": SetValueCheckNotAlready(ref seconds, value, "seconds"); break;
                        default:
                            throw new ParseError($"Invalid duration unit '{unit}' for a non-integer value {value}. Non-integer values are only supported for the 's' unit.");
                    }
                }
                else
                    break;
            }

            if (!(days.HasValue || hours.HasValue || minutes.HasValue || seconds.HasValue))
                throw new ParseError("Must have at least one value and unit");

            long totalMilliseconds = 0;

            if (seconds.HasValue)
                totalMilliseconds += F64.RoundToInt(seconds.Value * 1000);

            if (minutes.HasValue)
                totalMilliseconds += minutes.Value * (60L * 1000L);

            if (hours.HasValue)
                totalMilliseconds += hours.Value * (60L * 60L * 1000L);

            if (days.HasValue)
                totalMilliseconds += days.Value * (24L * 60L * 60L * 1000L);

            return MetaDuration.FromMilliseconds(totalMilliseconds);
        }

        static void SetValueCheckNotAlready<T>(ref T? dst, T src, string name) where T : struct
        {
            if (dst.HasValue)
                throw new ParseError($"Value for {name} specified multiple times (previously {dst}, now {src})");
            dst = src;
        }

        // \todo [nuutti] Legacy? Remove?
        static MetaDuration ParseMetaDurationPlainSecondsFormat(ConfigLexer lexer)
        {
            // \todo [petri] assumes value in raw seconds
            // \todo [petri] range of parsing via F64 to milliseconds is limited to ~2 million seconds (~23 days)
            int milliseconds = F64.RoundToInt(ParseF64(lexer) * 1000);
            return MetaDuration.FromMilliseconds(milliseconds);
        }

        static readonly ConfigLexer.CustomTokenSpec s_nftIdConfigToken = new ConfigLexer.CustomTokenSpec(@"[0-9]+", "NftId decimal string");

        static NftId ParseNftId(ConfigLexer lexer)
        {
            string nftIdString = lexer.TryParseCustomToken(s_nftIdConfigToken)
                                 ?? throw new ParseError($"Failed to parse NftId. Expected decimal string. Input: {lexer.GetRemainingInputInfo()}");

            return NftId.ParseFromString(nftIdString);
        }

        public static bool TryParseCorePlayerPropertyId(ConfigLexer lexer, out PlayerPropertyId resultPropertyId)
        {
            // \note Be careful to not advance the lexer until it is known the player property id is of type we can parse (i.e. of a core type).

            if (lexer.CurrentToken.Type == ConfigLexer.TokenType.Identifier)
            {
                string type = lexer.GetTokenString(lexer.CurrentToken);

                switch (type)
                {
                    // "PlayerbaseSubset/NUM_SUBSETS"
                    // or "PlayerbaseSubset/NUM_SUBSETS/MODIFIER"
                    // e.g.  PlayerbaseSubset/10
                    //       PlayerbaseSubset/2
                    //       PlayerbaseSubset/10/1
                    case "PlayerbaseSubset":
                    {
                        lexer.Advance();
                        lexer.ParseToken(ConfigLexer.TokenType.ForwardSlash);
                        int     numSubsets  = lexer.ParseIntegerLiteral();
                        uint    modifier    = 0;
                        if (lexer.TryParseToken(ConfigLexer.TokenType.ForwardSlash))
                        {
                            int modifierInt = lexer.ParseIntegerLiteral();
                            if (modifierInt < 0)
                                throw new ParseError(Invariant($"In PlayerbaseSubset/{numSubsets}/{modifierInt} : modifier cannot be negative"));
                            modifier = (uint)modifierInt;
                        }
                        resultPropertyId = new PlayerPropertyPlayerbaseSubsetNumber(numSubsets: numSubsets, modifier: modifier);
                        return true;
                    }
                }
            }

            resultPropertyId = null;
            return false;
        }

        public static T ParseEnumCaseInsensitive<T>(ConfigLexer lexer)
            where T : Enum
        {
            string str = lexer.ParseIdentifier();
            return EnumUtil.ParseCaseInsensitive<T>(str);
        }
    }

    /// <summary>
    /// When a base class has this attribute, it uses the
    /// custom config parser (if any) defined for a subclass.
    /// </summary>
    public class UseCustomParserFromDerivedAttribute : Attribute
    {
    }

    public abstract class ConfigParserProvider : IMetaIntegration<ConfigParserProvider>
    {
        public abstract void RegisterParsers();
    }
}
