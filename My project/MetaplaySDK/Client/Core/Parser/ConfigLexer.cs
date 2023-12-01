// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Metaplay.Core
{
    /// <summary>
    /// Represents an error during parsing of game config.
    /// </summary>
    public class ParseError : Exception
    {
        public ParseError() { }
        public ParseError(string message) : base(message) { }
        public ParseError(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Lexer for converting game configs from raw string into token (lexemes).
    /// </summary>
    public class ConfigLexer
    {
        public enum TokenType
        {
            Invalid,
            EndOfInput,

            IntegerLiteral,
            FloatLiteral,
            BooleanLiteral,
            StringLiteral,
            CharLiteral,
            Identifier,

            EqualEqual,
            NotEqual,
            GreaterOrEqual,
            LessOrEqual,
            GreaterThan,
            LessThan,

            SlashSlash,
            Comma,
            Dot,
            ForwardSlash,
            LeftParenthesis,
            RightParenthesis,
            LeftBracket,
            RightBracket,
            LeftBrace,
            RightBrace,
            Colon,
            Hash,
        }

        /// <summary>
        /// Single token parsed from input of lexer.
        /// </summary>
        public struct Token
        {
            public TokenType    Type;
            public int          StartOffset;
            public int          Length;

            public Token(TokenType type, int startOffset, int length)
            {
                Type = type;
                StartOffset = startOffset;
                Length = length;
            }
        }

        /// <summary>
        /// Regex-based specification for a single token in input.
        /// </summary>
        internal class TokenSpec
        {
            Regex       _regex;
            TokenType   _tokenType;

            public TokenSpec(string regex, TokenType type)
            {
                _regex = new Regex(@"\G" + regex);
                _tokenType = type;
            }

            public TokenSpec(Regex regex, TokenType type)
            {
                _regex     = regex;
                _tokenType = type;
            }

            public bool TryMatch(string input, int offset, out Token token)
            {
                Match m = _regex.Match(input, offset);
                if (m.Success)
                {
                    token = new Token(_tokenType, offset, m.Length);
                    return true;
                }
                else
                {
                    token = new Token();
                    return false;
                }
            }
        }

        public class CustomTokenSpec
        {
            Regex _regex;
            public string Name { get; private set; }

            public CustomTokenSpec(string regex, string name)
            {
                _regex = new Regex(@"\G" + regex);
                Name = name;
            }

            public bool TryMatch(string input, int offset, out int length)
            {
                Match m = _regex.Match(input, offset);
                if (m.Success)
                {
                    length = m.Length;
                    return true;
                }
                else
                {
                    length = 0;
                    return false;
                }
            }
        }

        static readonly TokenSpec[] s_tokenSpecs = new TokenSpec[]
        {
            new TokenSpec(new Regex(@"\G""(?:\\.|[^\\""])*""", RegexOptions.Compiled), TokenType.StringLiteral),
            new TokenSpec(new Regex(@"\G'(?:[^'])+'", RegexOptions.Compiled), TokenType.CharLiteral), // \todo [petri] doesn't support escaped single quote
            new TokenSpec(new Regex(@"\G[-+]?\d*\.\d+([eE][-+]?\d+)?", RegexOptions.Compiled), TokenType.FloatLiteral),
            new TokenSpec(new Regex(@"\G[-+]?\d+", RegexOptions.Compiled), TokenType.IntegerLiteral),
            new TokenSpec(new Regex(@"\Gtrue", RegexOptions.Compiled), TokenType.BooleanLiteral),
            new TokenSpec(new Regex(@"\GTrue", RegexOptions.Compiled), TokenType.BooleanLiteral),
            new TokenSpec(new Regex(@"\GTRUE", RegexOptions.Compiled), TokenType.BooleanLiteral),
            new TokenSpec(new Regex(@"\Gfalse", RegexOptions.Compiled), TokenType.BooleanLiteral),
            new TokenSpec(new Regex(@"\GFalse", RegexOptions.Compiled), TokenType.BooleanLiteral),
            new TokenSpec(new Regex(@"\GFALSE", RegexOptions.Compiled), TokenType.BooleanLiteral),
            new TokenSpec(new Regex(@"\G[a-zA-Z_][a-zA-Z0-9_]*", RegexOptions.Compiled), TokenType.Identifier),

            new TokenSpec(new Regex(@"\G==", RegexOptions.Compiled), TokenType.EqualEqual),
            new TokenSpec(new Regex(@"\G!=", RegexOptions.Compiled), TokenType.NotEqual),
            new TokenSpec(new Regex(@"\G>=", RegexOptions.Compiled), TokenType.GreaterOrEqual),
            new TokenSpec(new Regex(@"\G<=", RegexOptions.Compiled), TokenType.LessOrEqual),
            new TokenSpec(new Regex(@"\G>", RegexOptions.Compiled), TokenType.GreaterThan),
            new TokenSpec(new Regex(@"\G<", RegexOptions.Compiled), TokenType.LessThan),

            new TokenSpec(new Regex(@"\G//", RegexOptions.Compiled), TokenType.SlashSlash),
            new TokenSpec(new Regex(@"\G,", RegexOptions.Compiled), TokenType.Comma),
            new TokenSpec(new Regex(@"\G\.", RegexOptions.Compiled), TokenType.Dot),
            new TokenSpec(new Regex(@"\G/", RegexOptions.Compiled), TokenType.ForwardSlash),
            new TokenSpec(new Regex(@"\G\(", RegexOptions.Compiled), TokenType.LeftParenthesis),
            new TokenSpec(new Regex(@"\G\)", RegexOptions.Compiled), TokenType.RightParenthesis),
            new TokenSpec(new Regex(@"\G\[", RegexOptions.Compiled), TokenType.LeftBracket),
            new TokenSpec(new Regex(@"\G\]", RegexOptions.Compiled), TokenType.RightBracket),
            new TokenSpec(new Regex(@"\G{", RegexOptions.Compiled), TokenType.LeftBrace),
            new TokenSpec(new Regex(@"\G}", RegexOptions.Compiled), TokenType.RightBrace),
            new TokenSpec(new Regex(@"\G:", RegexOptions.Compiled), TokenType.Colon),
            new TokenSpec(new Regex(@"\G#", RegexOptions.Compiled), TokenType.Hash),
        };

        public readonly string  Input           = "";
        public int              Offset          { get; private set; } = 0;
        public Token            CurrentToken    { get; private set; } = new Token(TokenType.Invalid, 0, 0);

        public bool             IsAtEnd         => CurrentToken.Type == TokenType.EndOfInput;

        public ConfigLexer()
        {
            Advance();
        }

        public ConfigLexer(string input)
        {
            Input = input;
            Advance();
        }

        /// <summary>
        /// Clone another lexer, including its current lexing position.
        /// </summary>
        public ConfigLexer(ConfigLexer other)
        {
            Input           = other.Input;
            Offset          = other.Offset;
            CurrentToken    = other.CurrentToken;
        }

        public void Advance()
        {
            if (Offset == Input.Length)
                CurrentToken = new Token(TokenType.EndOfInput, Offset, Offset);
            else
                CurrentToken = ParseToken();
        }

        Token ParseToken()
        {
            while (Offset < Input.Length)
            {
                // Skip all whitespace
                char c = Input[Offset];
                if (IsWhitespace(c))
                {
                    Offset++;
                    continue;
                }

                // Find matching token from token specs
                foreach (TokenSpec spec in s_tokenSpecs)
                {
                    if (spec.TryMatch(Input, Offset, out Token matched))
                    {
                        Offset += matched.Length;
                        return matched;
                    }
                }

                // \todo [petri] better error message
                throw new ParseError($"Invalid input '{Input}' at {Offset}");
            }

            // End-of-input
            return new Token(TokenType.EndOfInput, Offset, Offset);
        }

        bool IsWhitespace(char c)
        {
            return (c == ' ' || c == '\t' || c == '\r' || c == '\n');
        }

        public void ExpectToken(TokenType expectedTokenType)
        {
            if (CurrentToken.Type != expectedTokenType)
                throw new ParseError($"Expecting token {expectedTokenType}, got token {CurrentToken.Type}");
        }

        public Token ParseToken(TokenType expectedTokenType)
        {
            // Make sure we have expected token typ
            ExpectToken(expectedTokenType);

            // Advance to next token
            Token token = CurrentToken;
            Advance();

            // Return previous token
            return token;
        }

        public string TryParseCustomToken(CustomTokenSpec spec)
        {
            if (IsAtEnd)
                return null;

            // \note Custom token parsing is a kludge.
            //
            //       ConfigLexer parses the upcoming token "eagerly" already in Advance(),
            //       before any public Parse* method is called. (In particular, the first
            //       token is parsed already when the ConfigLexer is constructed.) This
            //       means that by the time ParseCustomToken is called, the lexer has already
            //       parsed the input at that point as a non-custom token, and assigned it
            //       to CurrentToken. Then, ParseCustomToken simply ignores whatever non-custom
            //       token had been parsed, and re-parses the input from that point as
            //       the given custom token.
            //
            //       This means two things:
            //       - If the lexer advances to a position that contains input that does
            //         not represent any non-custom token, it'll throw a ParseError
            //         (in the private ParseToken()), even if we would've wanted to parse
            //         it as a custom token that does support said input. In other words,
            //         a custom token cannot start with a prefix that is not a valid
            //         non-custom token.
            //       - This implementation is hacky: we don't parse starting from Offset,
            //         but from CurrentToken.StartOffset.

            int startOffset = CurrentToken.StartOffset;
            if (spec.TryMatch(Input, startOffset, out int length))
            {
                string content = Input.Substring(startOffset, length);
                Offset = startOffset + length;
                Advance();
                return content;
            }
            else
                return null;
        }

        public string ParseCustomToken(CustomTokenSpec spec)
        {
            string result = TryParseCustomToken(spec);
            if (result == null)
                throw new ParseError($"Failed to parse custom token '{spec.Name}' at: {GetRemainingInputInfo()}");
            else
                return result;
        }

        public string GetTokenString(Token token)
        {
            return Input.Substring(token.StartOffset, token.Length);
        }

        public string ParseIdentifier()
        {
            Token token = ParseToken(TokenType.Identifier);
            return GetTokenString(token);
        }

        char ConvertQuotedChar(char c)
        {
            switch (c)
            {
                case 't': return '\t';
                case 'n': return '\n';
                case 'r': return '\r';
                case '\\': return '\\';
                case '"': return '"';
                default:
                    throw new ParseError($"Invalid quoted char '{c}'");
            }
        }

        public string ParseQuotedString(string quoted)
        {
            char[] result = new char[quoted.Length];

            int outNdx = 0;
            int endNdx = quoted.Length - 1;
            for (int inNdx = 1; inNdx < endNdx; inNdx++)
            {
                char c = quoted[inNdx];
                char o = c;

                if (c == '\\')
                {
                    inNdx++;
                    o = ConvertQuotedChar(quoted[inNdx]);
                }

                result[outNdx++] = o;
            }

            return new string(result, 0, outNdx);
        }

        public char ParseQuotedChar(string quoted)
        {
            // \todo [petri] only supports single character in quotes, add support for escaped chars, unicode, etc.
            if (quoted.Length != 3)
                throw new ParseError($"Invalid quoted char encountered: {quoted}");
            return quoted[1];
        }

        public string ParseStringLiteral()
        {
            Token token = ParseToken(TokenType.StringLiteral);
            string quoted = GetTokenString(token);
            return ParseQuotedString(quoted);
        }

        public string ParseIdentifierOrString()
        {
            switch (CurrentToken.Type)
            {
                case TokenType.Identifier:
                    return ParseIdentifier();

                case TokenType.StringLiteral:
                    return ParseStringLiteral();

                default:
                    throw new ParseError($"Expecting string or identifier, got {CurrentToken.Type}");
            }
        }

        public bool ParseBooleanLiteral()
        {
            Token token = ParseToken(TokenType.BooleanLiteral);
            return GetTokenString(token).Equals("true", StringComparison.InvariantCultureIgnoreCase);
        }

        public char ParseCharLiteral()
        {
            switch (CurrentToken.Type)
            {
                case TokenType.CharLiteral:
                    return ParseQuotedChar(GetTokenString(ParseToken(TokenType.CharLiteral)));

                case TokenType.Identifier:
                    Token token = ParseToken(TokenType.Identifier);
                    string str = GetTokenString(token);
                    if (str.Length != 1)
                        throw new ParseError($"Expecting a char (only single-character identifiers supported for now), got {token.Type}");
                    return str[0];

                default:
                    throw new ParseError($"Expecting char literal, got {CurrentToken.Type}");
            }
        }

        public int ParseIntegerLiteral()
        {
            Token token = ParseToken(TokenType.IntegerLiteral);
            return int.Parse(GetTokenString(token), CultureInfo.InvariantCulture);
        }

        public long ParseLongLiteral()
        {
            Token token = ParseToken(TokenType.IntegerLiteral);
            return long.Parse(GetTokenString(token), CultureInfo.InvariantCulture);
        }

        public float ParseFloatLiteral()
        {
            switch (CurrentToken.Type)
            {
                case TokenType.IntegerLiteral:
                    return (float)ParseIntegerLiteral();

                case TokenType.FloatLiteral:
                    string str = GetTokenString(ParseToken(TokenType.FloatLiteral));
                    return float.Parse(str, CultureInfo.InvariantCulture);

                default:
                    throw new ParseError($"Expecting float (or integer) literal, got {CurrentToken.Type}");
            }
        }

        public double ParseDoubleLiteral()
        {
            switch (CurrentToken.Type)
            {
                case TokenType.IntegerLiteral:
                    return (double)ParseIntegerLiteral();

                case TokenType.FloatLiteral:
                    string str = GetTokenString(ParseToken(TokenType.FloatLiteral));
                    return double.Parse(str, CultureInfo.InvariantCulture);

                default:
                    throw new ParseError($"Expecting float (or integer) literal, got {CurrentToken.Type}");
            }
        }

        public bool TryParseToken(TokenType tokenType)
        {
            if (CurrentToken.Type == tokenType)
            {
                Advance();
                return true;
            }
            else
                return false;
        }

        public string GetRemainingInputInfo()
        {
            if (IsAtEnd)
                return "<end of input>";
            else
            {
                const int maxInputLength = 50;
                int startOffset = CurrentToken.StartOffset;
                if (Input.Length - startOffset <= maxInputLength)
                    return Input.Substring(startOffset);
                else
                    return Input.Substring(startOffset, maxInputLength) + "...";
            }
        }
    }
}
