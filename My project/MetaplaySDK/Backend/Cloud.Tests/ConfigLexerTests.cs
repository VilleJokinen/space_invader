// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using NUnit.Framework;
using System;
using static Metaplay.Core.ConfigLexer;

namespace Cloud.Tests
{
    [TestFixture]
    public class ConfigLexerTests
    {
        [TestCase("", "\"\"")]
        [TestCase("foobar", "\"foobar\"")]
        [TestCase("\n", "\"\\n\"")]
        [TestCase("\"", "\"\\\"\"")]
        public void ParseStringLiteral(string expected, string input)
        {
            ConfigLexer lexer = new ConfigLexer(input);
            Assert.AreEqual(expected, lexer.ParseStringLiteral());
        }

        [TestCase('a', "'a'")]
        [TestCase('?', "'?'")]
        [TestCase('*', "'*'")]
        public void ParseCharLiteral(char expected, string input)
        {
            ConfigLexer lexer = new ConfigLexer(input);
            Assert.AreEqual(expected, lexer.ParseCharLiteral());
        }

        [TestCase(0.0f, "0")]
        [TestCase(1.0f, "1")]
        [TestCase(1.0f, "1.0")]
        [TestCase(100.0f, "100.0")]
        [TestCase(-0.25f, "-0.25")]
        public void ParseFloatLiteral(float expected, string input)
        {
            ConfigLexer lexer = new ConfigLexer(input);
            Assert.AreEqual(expected, lexer.ParseFloatLiteral());
        }

        [TestCase(0, "0")]
        [TestCase(1, "1")]
        [TestCase(1000000, "1000000")]
        [TestCase(-999999, "-999999")]
        public void ParseIntegerLiteral(int expected, string input)
        {
            ConfigLexer lexer = new ConfigLexer(input);
            Assert.AreEqual(expected, lexer.ParseIntegerLiteral());
        }

        [TestCase(true, "true")]
        [TestCase(true, "True")]
        [TestCase(true, "TRUE")]
        [TestCase(false, "false")]
        [TestCase(false, "False")]
        [TestCase(false, "FALSE")]
        public void ParseBoolLiteral(bool expected, string input)
        {
            ConfigLexer lexer = new ConfigLexer(input);
            Assert.AreEqual(expected, lexer.ParseBooleanLiteral());
        }

        [TestCase("abba", "abba")]
        [TestCase("_abba", "_abba")]
        [TestCase("_123", "_123")]
        public void ParseIdentifier(string expected, string input)
        {
            ConfigLexer lexer = new ConfigLexer(input);
            Assert.AreEqual(expected, lexer.ParseIdentifier());
        }

        [TestCase(TokenType.EqualEqual, @"==")]
        [TestCase(TokenType.NotEqual, @"!=")]
        [TestCase(TokenType.GreaterOrEqual, @">=")]
        [TestCase(TokenType.LessOrEqual, @"<=")]
        [TestCase(TokenType.GreaterThan, @">")]
        [TestCase(TokenType.LessThan, @"<")]
        [TestCase(TokenType.Comma, @",")]
        [TestCase(TokenType.Dot, @".")]
        [TestCase(TokenType.ForwardSlash, @"/")]
        [TestCase(TokenType.LeftParenthesis, @"(")]
        [TestCase(TokenType.RightParenthesis, @")")]
        [TestCase(TokenType.LeftBracket, @"[")]
        [TestCase(TokenType.RightBracket, @"]")]
        [TestCase(TokenType.LeftBrace, @"{")]
        [TestCase(TokenType.RightBrace, @"}")]
        public void ParseToken(TokenType expected, string input)
        {
            ConfigLexer lexer = new ConfigLexer(input);
            Assert.AreEqual(expected, lexer.CurrentToken.Type);
        }
    }
}
