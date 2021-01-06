using System;
using System.Numerics;

namespace fissure
{
    record Token
    {
        public Location Location;
        public TokenType TokenType;

        public Token(Location location, TokenType tokenType)
        {
            Location = location;
            TokenType = tokenType;
        }

        public override string ToString() => TokenType switch
        {
            TokenType.And => "And",
            TokenType.Assign => "Assign",
            TokenType.Bool => "Bool",
            TokenType.Char => "Char",
            TokenType.Struct => "Struct",
            TokenType.Colon => "Colon",
            TokenType.Comma => "Comma",
            TokenType.Do => "Do",
            TokenType.DoubleColon => "DoubleColon",
            TokenType.DoublePeriod => "DoublePeriod",
            TokenType.DoubleSlash => "DoubleSlash",
            TokenType.Else => "Else",
            TokenType.EOF => "EOF",
            TokenType.EOL => "EOL",
            TokenType.Equal => "Equal",
            TokenType.Float => "Float",
            TokenType.For => "For",
            TokenType.GreaterThan => "GreaterThan",
            TokenType.GreaterThanOrEqual => "GreaterThanOrEqual",
            TokenType.Identifier => "Identifier",
            TokenType.If => "If",
            TokenType.Ignore => "Ignore",
            TokenType.In => "In",
            TokenType.Int => "Int",
            TokenType.LBrace => "LBrace",
            TokenType.LBracket => "LBracket",
            TokenType.LesserThan => "LesserThan",
            TokenType.LesserThanOrEqual => "LesserThanOrEqual",
            TokenType.Let => "Let",
            TokenType.LParen => "LParen",
            TokenType.Minus => "Minus",
            TokenType.New => "New",
            TokenType.Not => "Not",
            TokenType.NotEqual => "NotEqual",
            TokenType.Or => "Or",
            TokenType.Period => "Period",
            TokenType.Plus => "Plus",
            TokenType.RBrace => "RBrace",
            TokenType.RBracket => "RBracket",
            TokenType.RParen => "RParen",
            TokenType.Slash => "Slash",
            TokenType.Star => "Star",
            TokenType.String => "String",
            TokenType.While => "While",
            TokenType.Xor => "Xor",
            _ => throw new NotImplementedException()
        };
    }

    record StringToken : Token
    {
        public string Value;

        public StringToken(Location location, TokenType tokenType, string value) : base(location, tokenType)
        {
            Value = value;
        }

        public override string ToString() => $"{base.ToString()}({Value})";
    }

    record IntToken : Token
    {
        public BigInteger Value;

        public IntToken(Location location, BigInteger value) : base(location, TokenType.Int)
        {
            Value = value;
        }

        public override string ToString() => $"{base.ToString()}({Value})";
    }

    record FloatToken : Token
    {
        public double Value;

        public FloatToken(Location location, double value) : base(location, TokenType.Float)
        {
            Value = value;
        }

        public override string ToString() => $"{base.ToString()}({Value})";
    }

    record BoolToken : Token
    {
        public bool Value;

        public BoolToken(Location location, bool value) : base(location, TokenType.Bool)
        {
            Value = value;
        }

        public override string ToString() => $"{base.ToString()}({Value})";
    }

    record CharToken : Token
    {
        public uint Value;

        public CharToken(Location location, uint value) : base(location, TokenType.Char)
        {
            Value = value;
        }

        public override string ToString() => $"{base.ToString()}({Value})";
    }

    enum TokenType
    {
        EOF,
        EOL,
        Colon,
        DoubleColon,
        LParen,
        RParen,
        LBracket,
        RBracket,
        LBrace,
        RBrace,
        Assign,
        Equal,
        NotEqual,
        GreaterThan,
        GreaterThanOrEqual,
        LesserThan,
        LesserThanOrEqual,
        Not,
        Plus,
        Minus,
        Slash,
        DoubleSlash,
        Star,
        Period,
        DoublePeriod,
        Comma,
        Ignore,

        Struct,
        Let,
        Do,
        If,
        Else,
        For,
        In,
        While,
        And,
        Or,
        Xor,
        New,

        Identifier,
        String,
        Int,
        Bool,
        Char,
        Float
    }
}