using System;
using System.Collections.Generic;
using System.Numerics;

namespace fissure
{
    [System.Serializable]
    class LexerException : Exception
    {
        public LexerException() { }
        public LexerException(Location location, string message) : base($"{location}: {message}") { }
        public LexerException(Location location, string message, Exception inner) : base($"{location}: {message}", inner) { }
        protected LexerException(
            System.Runtime.Serialization.SerializationInfo info,
            System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
    }
    
    class Lexer
    {
        public readonly string FileName;
        readonly string code;
        int index = 0;
        Location location;
        Location nextLocation;
        Location startLocation;

        public Lexer(string fileName, string code)
        {
            FileName = fileName;
            location = new GeneratedLocation(fileName);
            nextLocation = new ConcreteLocation(fileName, 1, 1, 0);
            startLocation = nextLocation with { };
            this.code = code;
        }

        public Token Next()
        {
            var c = NextChar();
            SetStart();
            switch (c)
            {
                case null:
                    return New(TokenType.EOF);
                case '\n':
                    return New(TokenType.EOL);
                case ':':
                    return Choice(New(TokenType.Colon), ':', New(TokenType.DoubleColon));
                case '(':
                    return New(TokenType.LParen);
                case ')':
                    return New(TokenType.RParen);
                case '[':
                    return New(TokenType.LBracket);
                case ']':
                    return New(TokenType.RBracket);
                case '{':
                    return New(TokenType.LBrace);
                case '}':
                    return New(TokenType.RBrace);
                case '=':
                    return Choice(New(TokenType.Assign), '=', New(TokenType.Equal));
                case '!':
                    return Choice(New(TokenType.Not), '=', New(TokenType.NotEqual));
                case '<':
                    return Choice(New(TokenType.LesserThan), '=', New(TokenType.LesserThanOrEqual));
                case '>':
                    return Choice(New(TokenType.GreaterThan), '=', New(TokenType.GreaterThanOrEqual));
                case '+':
                    return New(TokenType.Plus);
                case '-':
                    return New(TokenType.Minus);
                case '/':
                    return Choice(New(TokenType.Slash), '/', New(TokenType.DoubleSlash));
                case '*':
                    return New(TokenType.Star);
                case '.':
                    return Choice(New(TokenType.Period), '.', New(TokenType.DoublePeriod));
                case ',':
                    return New(TokenType.Comma);
                case '\'':
                    c = (NextChar()) switch
                    {
                        null => throw Error("Unclosed character literal"),
                        '\'' => throw Error("Empty character literal"),
                        '\\' => (NextChar()) switch
                        {
                            null => throw Error("Unclosed character literal"),
                            '\\' => '\\',
                            'n' => '\n',
                            't' => '\t',
                            'r' => '\r',
                            '\'' => '\'',
                            '"' => '"',
                            _ => throw Error("Unknown escape sequence"),
                        },
                        _ => code[index - 1],
                    };
                    if (NextChar() == '\'')
                    {
                        return New(c ?? throw new Exception("Unreachable"));
                    }
                    throw Error("Unclosed escape sequence, or character literal contains more than one character");
                case '"':
                    string buffer = "";
                    while (true)
                    {
                        switch (NextChar())
                        {
                            case null:
                                throw Error("Unclosed string literal");
                            case '"':
                                return New(TokenType.String, buffer);
                            case '\\':
                                buffer += (NextChar()) switch
                                {
                                    null => throw Error("Unclosed string literal"),
                                    '\\' => '\\',
                                    '\'' => '\'',
                                    '"' => '"',
                                    'n' => '\n',
                                    't' => '\t',
                                    'r' => '\r',
                                    _ => throw new LexerException(location, "Unknown escape sequence"),
                                };
                                break;
                            default:
                                buffer += code[index - 1];
                                break;
                        }
                    }
                case >= '0' and <= '9':
                    c = code[index - 1];
                    if (c == '0' && char.ToLower(PeekChar() ?? ' ') == 'x')
                    {
                        buffer = "";
                        bool cond = true;
                        while (cond)
                        {
                            switch (PeekChar())
                            {
                                case >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f':
                                    buffer += NextChar();
                                    break;
                                default:
                                    cond = false;
                                    break;
                            }
                        }
                        return New(BigInteger.Parse(buffer));
                    }
                    else if (c == '0' && char.ToLower(PeekChar() ?? ' ') == 'b')
                    {
                        buffer = "";
                        bool cond = true;
                        while (cond)
                        {
                            switch (PeekChar())
                            {
                                case '0' or '1':
                                    buffer += NextChar();
                                    break;
                                default:
                                    cond = false;
                                    break;
                            }
                        }
                        return New(BigInteger.Parse(buffer));
                    }
                    else
                    {
                        buffer = c?.ToString() ?? throw new Exception("Unreachable");
                        bool cond = true;
                        int state = 0;
                        while (cond)
                        {
                            switch (PeekChar())
                            {
                                case >= '0' and <= '9':
                                    buffer += NextChar();
                                    break;
                                case '.':
                                    if (state == 0)
                                    {
                                        NextChar();
                                        buffer += '.';
                                        state = 1;
                                    }
                                    else
                                    {
                                        cond = false;
                                    }
                                    break;
                                case 'e' or 'E':
                                    if (state < 2)
                                    {
                                        NextChar();
                                        buffer += 'e';
                                        state = 2;
                                    }
                                    else
                                    {
                                        cond = false;
                                    }
                                    break;
                                default:
                                    cond = false;
                                    break;
                            }
                        }
                        if (state > 0)
                        {
                            return New(double.Parse(buffer));
                        }
                        return New(BigInteger.Parse(buffer));
                    }
                default:
                    c = code[index - 1];
                    if (char.IsLetter(c ?? ' ') || c == '_')
                    {
                        buffer = code[index - 1].ToString();
                        while (true)
                        {
                            c = PeekChar();
                            if (char.IsLetter(c ?? ' ') || char.IsNumber(c ?? ' ') || c == '_')
                            {
                                NextChar();
                                buffer += c;
                            }
                            else
                            {
                                return buffer switch
                                {
                                    "struct" => New(TokenType.Struct),
                                    "let" => New(TokenType.Let),
                                    "do" => New(TokenType.Do),
                                    "if" => New(TokenType.If),
                                    "else" => New(TokenType.Else),
                                    "for" => New(TokenType.For),
                                    "in" => New(TokenType.In),
                                    "while" => New(TokenType.While),
                                    "true" => New(true),
                                    "false" => New(false),
                                    "new" => New(TokenType.New),
                                    "_" => New(TokenType.Ignore),
                                    _ => New(TokenType.Identifier, buffer)
                                };
                            }
                        }
                    }
                    if (char.IsWhiteSpace(c ?? '.'))
                    {
                        while (PeekChar() != '\n' && char.IsWhiteSpace(PeekChar() ?? '.'))
                        {
                            NextChar();
                        }
                        return Next();
                    }
                    throw Error($"Unexpected character ({code[index - 1]})");
            }
        }

        public List<Token> Lex()
        {
            List<Token> ret = new();
            while (true)
            {
                var tok = Next();
                if (tok.TokenType == TokenType.EOF)
                {
                    ret.Add(tok);
                    return ret;
                }
                ret.Add(tok);
            }
        }

        Token New(TokenType tokenType) => new(startLocation, tokenType);
        Token New(TokenType tokenType, string value) => new StringToken(startLocation, tokenType, value);
        Token New(BigInteger value) => new IntToken(startLocation, value);
        Token New(double value) => new FloatToken(startLocation, value);
        Token New(bool value) => new BoolToken(startLocation, value);
        Token New(uint value) => new CharToken(startLocation, value);

        LexerException Error(string message) => new(startLocation, message);

        void SetStart() => startLocation = location;

        Token Choice(Token a, char c, Token b)
        {
            if (PeekChar() == c)
            {
                NextChar();
                return b;
            }
            return a;
        }

        char? NextChar()
        {
            if (index >= code.Length)
            {
                return null;
            }
            location = nextLocation with { };
            switch (code[index])
            {
                case '\n':
                    ((ConcreteLocation)nextLocation).Row++;
                    ((ConcreteLocation)nextLocation).Column = 1;
                    ((ConcreteLocation)nextLocation).Index++;
                    break;
                default:
                    ((ConcreteLocation)nextLocation).Column++;
                    ((ConcreteLocation)nextLocation).Index++;
                    break;
            }
            var ret = code[index];
            index++;
            return ret;
        }

        char? PeekChar()
        {
            if (index >= code.Length)
            {
                return null;
            }

            return code[index];
        }
    }
}