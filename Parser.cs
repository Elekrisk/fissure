using System;
using System.Collections.Generic;
using System.Linq;
using fissure.Ast;

#nullable enable

namespace fissure
{
    class Parser
    {
        public abstract class ParseResult<T>
        {
            public static ParseResult<T> Ok(T value) => new Ok_(value);
            public static ParseResult<T> Err(Location location, string error) => new Err_($"{location}: {error}");
            public static ParseResult<T> Fatal(Location location, string error) => new Fatal_($"{location}: {error}");

            public abstract T Unwrap();
            public abstract ParseResult<U> Transmute<U>();
            public abstract ParseResult<U> Downcast<U>() where U: class, T;
            public abstract ParseResult<U> CastUnchecked<U>();
            public abstract ParseResult<U> Map<U>(Func<T, U> f);
            public abstract ParseResult<T> MakeFatal();
            public abstract ParseResult<T> MakeFatal(string error);
            public abstract bool IsOk { get; }
            public abstract bool IsErr { get; }
            public abstract bool IsFatal { get; }

            public class Ok_ : ParseResult<T>
            {
                public T Value;

                public override bool IsOk => true;

                public override bool IsErr => false;

                public override bool IsFatal => false;

                public Ok_(T value)
                {
                    Value = value;
                }

                public override T Unwrap() => Value;
                public override ParseResult<U> Map<U>(Func<T, U> f) => ParseResult<U>.Ok(f(Value));

                public override ParseResult<U> Transmute<U>() => throw new Exception();

                public override ParseResult<T> MakeFatal() => this;
                public override ParseResult<T> MakeFatal(string error) => this;

                public void Deconstruct(out T value) => value = Value;

#nullable disable
                public override ParseResult<U> Downcast<U>() => ParseResult<U>.Ok((U)Value);

                public override ParseResult<U> CastUnchecked<U>() => ParseResult<U>.Ok((U)(object)Value);
#nullable enable
            }

            public class Err_ : ParseResult<T>
            {
                public string Error;

                public override bool IsOk => false;

                public override bool IsErr => true;

                public override bool IsFatal => false;

                public Err_(string error)
                {
                    Error = error;
                }

                public override T Unwrap() => throw new Exception($"Tried to unwrap an Error value: {Error}");
                public override ParseResult<U> Map<U>(Func<T, U> f) => Transmute<U>();

                public override ParseResult<U> Transmute<U>() => new ParseResult<U>.Err_(Error);

                public override ParseResult<T> MakeFatal() => new Fatal_(Error);
                public override ParseResult<T> MakeFatal(string error) => new Fatal_(error);

                public void Deconstruct(out string error) => error = Error;

                public override ParseResult<U> Downcast<U>() => Transmute<U>();

                public override ParseResult<U> CastUnchecked<U>() => Transmute<U>();
            }

            public class Fatal_ : ParseResult<T>
            {
                public string Error;

                public Fatal_(string error)
                {
                    Error = error;
                    throw new Exception(error);
                }

                public override bool IsOk => false;

                public override bool IsErr => true;

                public override bool IsFatal => true;

                public override ParseResult<T> MakeFatal() => this;
                public override ParseResult<T> MakeFatal(string error) => this;

                public override ParseResult<U> Map<U>(Func<T, U> f) => Transmute<U>();

                public override ParseResult<U> Transmute<U>() => new ParseResult<U>.Fatal_(Error);

                public override T Unwrap() => throw new Exception($"Tried to unwrap an Error value: {Error}");

                public void Deconstruct(out string error) => error = Error;

                public override ParseResult<U> Downcast<U>() => Transmute<U>();
                public override ParseResult<U> CastUnchecked<U>() => Transmute<U>();
            }
        }

        Dictionary<(string, int), (ParseResult<object>, int)> results = new();

        ParseResult<T> Call<T>(string name, Func<ParseResult<T>> func)
        {
            if (results.ContainsKey((name, index)))
            {
                var t = results[(name, index)];
                index = t.Item2;
                return t.Item1.Map(n => (T)n);
            }
            var start = index;
            var result = func();
#nullable disable
            results.Add((name, start), (result.Map(n => (object)n), index));
#nullable enable
            return result;
        }

        string fileName;
        List<Token> tokens;
        int index = 0;
        List<int> indentStack = new() { 0 };

        public Parser(string fileName, List<Token> tokens)
        {
            this.fileName = fileName;
            this.tokens = tokens;
        }


        // program := EOL* (struct_decl EOL*)*
        public ParseResult<ProgramNode> ParseProgram() => Call(nameof(ParseProgram),
            () => ParseSequence(
                () => ParseStar(() => ParseToken(TokenType.EOL)),
                () => ParseStar(() => ParseSequence(
                    ParseStructDeclaration,
                    () => ParseStar(() => ParseToken(TokenType.EOL)),
                    (a, b) => Ok(a)
                )),
                () => ParseToken(TokenType.EOF).MakeFatal(),
                (a, ss, c) =>
                {
                    ProgramNode program = new(
                        ss.Count > 0 
                        ? ss[0].Location 
                        : (a.Count > 0 
                           ? a[0].Location 
                           : new ConcreteLocation(fileName, 1, 1, 0)
                          ));
                    program.StructDeclarations.AddRange(ss);
                    return Ok(program);
                }
            )
        );

        // struct_decl := STRUCT IDENTIFIER COLON EOL+ ((member_decl/handler_decl) EOL*)*
        ParseResult<StructDeclaration> ParseStructDeclaration() => Call(nameof(ParseStructDeclaration),
            () => ParseSequence(
                () => ParseToken(TokenType.Struct),
                () => ParseToken(TokenType.Identifier).MakeFatal(),
                () => ParseToken(TokenType.Colon).MakeFatal(),
                () => ParsePlus(() => ParseToken(TokenType.EOL)).MakeFatal(),
                () => Block(() => ParseSequence(
                    () => ParseChoice(
                        () => ParseMemberDeclaration().CastUnchecked<Node>(),
                        () => ParseMessageHandlerDeclaration().CastUnchecked<Node>()
                    ),
                    () => ParseStar(() => ParseToken(TokenType.EOL)),
                    (a, b) => Ok(a)
                )),
                (a, b, c, d, e) =>
                {
                    StructDeclaration structDeclaration = new(a.Location, ((StringToken)b).Value);
                    foreach (var node in e)
                    {
                        switch (node)
                        {
                            case MemberDeclaration memberDeclaration:
                                structDeclaration.MemberDeclarations.Add(memberDeclaration);
                                break;
                            case MessageHandlerDeclaration messageHandlerDeclaration:
                                structDeclaration.MessageHandlerDeclarations.Add(messageHandlerDeclaration);
                                break;
                        }
                    }
                    return Ok(structDeclaration);
                }
            )
        );

        // member_decl := IDENTIFIER DOUBLE_COLON type_expr EOL
        ParseResult<MemberDeclaration> ParseMemberDeclaration() => Call(nameof(ParseMemberDeclaration),
            () => ParseSequence(
                () => ParseToken(TokenType.Identifier),
                () => ParseToken(TokenType.DoubleColon),
                () => ParseTypeExpression().MakeFatal(),
                () => ParseToken(TokenType.EOL).MakeFatal(),
                (a, b, c, d) => Ok(new MemberDeclaration(a.Location, ((StringToken)b).Value, c))
            )
        );

        // handler_decl := IDENTIFIER LPAREN (IDENTIFIER (DOUBLE_COLON type_expr)? (COMMA IDENTIFIER (DOUBLE_COLON type_expr)?)*)? RPAREN COLON EOL block[stmnt]
        ParseResult<MessageHandlerDeclaration> ParseMessageHandlerDeclaration() => Call(nameof(ParseMessageHandlerDeclaration),
            () => ParseSequence(
                () => ParseToken(TokenType.Identifier),
                () => ParseToken(TokenType.LParen).MakeFatal(),
                () => ParseOptional(() => ParseSequence(
                    () => ParseToken(TokenType.Identifier),
                    () => ParseOptional(() => ParseSequence(
                        () => ParseToken(TokenType.DoubleColon),
                        () => ParseTypeExpression().MakeFatal(),
                        (a, b) => Ok(b)
                    )),
                    () => ParseStar(() => ParseSequence(
                        () => ParseToken(TokenType.Comma),
                        () => ParseToken(TokenType.Identifier).MakeFatal(),
                        () => ParseOptional(() => ParseSequence(
                            () => ParseToken(TokenType.DoubleColon),
                            () => ParseTypeExpression().MakeFatal(),
                            (a, b) => Ok(b)
                        )),
                        (a, b, c) =>
                        {
                            var name = ((StringToken)b).Value;
                            var type = c;
                            return Ok(new Parameter(b.Location, name, type));
                        }
                    )),
                    (a, b, c) =>
                    {
                        var name = ((StringToken)a).Value;
                        var type = b;
                        var parameters = c;
                        parameters.Insert(0, new Parameter(a.Location, name, type));
                        return Ok(parameters);
                    }
                )),
                () => ParseToken(TokenType.RParen).MakeFatal(),
                () => ParseToken(TokenType.Colon).MakeFatal(),
                () => ParseToken(TokenType.EOL).MakeFatal(),
                () => Block(() => ParseSequence(
                    ParseStatement,
                    () => ParseStar(() => ParseToken(TokenType.EOL)),
                    (a, b) => Ok(a)
                )),
                (a, b, c, d, e, f, g) =>
                {
                    var name = ((StringToken)a).Value;
                    var parameters = c ?? new();
                    var body = new BlockExpression(e.Location);
                    body.Statements.AddRange(g);
                    var handler = new MessageHandlerDeclaration(a.Location, name, body);
                    handler.Parameters.AddRange(parameters);
                    return Ok(handler);
                }
            )
        );

        ParseResult<Statement> ParseStatement() => Call(nameof(ParseStatement),
            () =>  ParseChoice(
                // Let
                () => ParseSequence(
                    () => ParseToken(TokenType.Let),
                    () => ParsePattern().MakeFatal(),
                    () => ParseToken(TokenType.Assign).MakeFatal(),
                    () => ParseExpression().MakeFatal(),
                    () => ParseToken(TokenType.EOL).MakeFatal(),
                    (a, b, c, d, e) => Ok((Statement)new LetStatement(a.Location, b, d))
                ),
                // Assign
                () => ParseSequence(
                    ParsePath,
                    () => ParseToken(TokenType.Assign),
                    () => ParseExpression().MakeFatal(),
                    () => ParseToken(TokenType.EOL).MakeFatal(),
                    (a, b, c, d) => Ok((Statement)new AssignStatement(a.Location, a, c))
                ),
                // For
                () => ParseSequence(
                    () => ParseToken(TokenType.For),
                    () => ParsePattern().MakeFatal(),
                    () => ParseToken(TokenType.In).MakeFatal(),
                    () => ParseExpression().MakeFatal(),
                    () => ParseToken(TokenType.Colon).MakeFatal(),
                    () => ParseToken(TokenType.EOL).MakeFatal(),
                    () => ParseStatementBlock().MakeFatal(),
                    (a, b, c, d, e, f, g) =>
                    {
                        var body = new BlockExpression(e.Location);
                        body.Statements.AddRange(g);
                        return Ok((Statement)new ForStatement(a.Location, b, d, body));
                    }
                ),
                // While
                () => ParseSequence(
                    () => ParseToken(TokenType.While),
                    () => ParseExpression().MakeFatal(),
                    () => ParseToken(TokenType.Colon).MakeFatal(),
                    () => ParseToken(TokenType.EOL).MakeFatal(),
                    () => ParseStatementBlock().MakeFatal(),
                    (a, b, c, d, e) =>
                    {
                        var body = new BlockExpression(c.Location);
                        body.Statements.AddRange(e);
                        return Ok((Statement)new WhileStatement(a.Location, b, body));
                    }
                ),
                // Expression
                () => ParseSequence(
                    ParseExpression,
                    () => ParseToken(TokenType.EOL),
                    (a, b) => Ok((Statement)new ExpressionStatement(a.Location, a))
                )
            )
        );

        ParseResult<List<Statement>> ParseStatementBlock() => Call(nameof(ParseStatementBlock),
            () => Block(() => ParseSequence(
                ParseStatement,
                () => ParseStar(() => ParseToken(TokenType.EOL)),
                (a, b) => Ok(a)
            ))
        );

        ParseResult<Path> ParsePath() => Call(nameof(ParsePath),
            () => ParseSequence(
                () => ParseToken(TokenType.Identifier),
                () => ParseStar(() => ParseSequence(
                    () => ParseToken(TokenType.Period),
                    () => ParseToken(TokenType.Identifier).MakeFatal(),
                    (a, b) => Ok(b)
                )),
                (a, b) =>
                {
                    Path v = new IdentifierPath(a.Location, ((StringToken)a).Value);
                    while (b.Count > 0)
                    {
                        v = new PropertyPath(v.Location, v, ((StringToken)b[0]).Value);
                        b.RemoveAt(0);
                    }
                    return Ok(v);
                }
            )
        );

        /*
         * 
         * Operator precedence
         * 9 .
         * 8 - !
         * 7 * / //
         * 6 + -
         * 5 > < >= <=
         * 4 == !=
         * 3 and
         * 2 or
         * 1 xor
         * 0   
         * 
         */
        ParseResult<Expression> ParseExpression() => Call(nameof(ParseExpression),
            () => ParseChoice(
                // If
                () => ParseSequence(
                    () => ParseToken(TokenType.If),
                    () => ParseExpression().MakeFatal(),
                    () => ParseToken(TokenType.Colon).MakeFatal(),
                    () => ParseToken(TokenType.EOL).MakeFatal(),
                    () => ParseStatementBlock().MakeFatal(),
                    () => ParseStar(() => ParseSequence(
                        () => ParseToken(TokenType.Else),
                        () => ParseToken(TokenType.If),
                        () => ParseExpression().MakeFatal(),
                        () => ParseToken(TokenType.Colon).MakeFatal(),
                        () => ParseToken(TokenType.EOL).MakeFatal(),
                        () => ParseStatementBlock().MakeFatal(),
                        (a, b, c, d, e, f) =>
                        {
                            var body = new BlockExpression(d.Location);
                            body.Statements.AddRange(f);
                            return Ok(new ElseIfExpression(a.Location, c, body));
                        }
                    )),
                    () => ParseOptional(() => ParseSequence(
                        () => ParseToken(TokenType.Else),
                        () => ParseToken(TokenType.Colon).MakeFatal(),
                        () => ParseToken(TokenType.EOL).MakeFatal(),
                        () => ParseStatementBlock().MakeFatal(),
                        (a, b, c, d) =>
                        {
                            var body = new BlockExpression(b.Location);
                            body.Statements.AddRange(d);
                            return Ok(body);
                        }
                    )),
                    (a, b, c, d, e, f, g) =>
                    {
                        var cond = b;
                        var body = new BlockExpression(c.Location);
                        body.Statements.AddRange(e);
                        return Ok((Expression)new IfExpression(a.Location, cond, body, g));
                    }
                ),
                ParseExpression0
            )
        );

        ParseResult<Expression> ParseExpression0() => Call(nameof(ParseExpression0),
            () => ParseChoice(
                // MessageApplication
                () => ParseSequence(
                    () => ParseStar(ParseExpression1),
                    ee =>
                    {
                        if (ee.Count < 2)
                        {
                            return Err<Expression>(ee.Count > 0 ? ee[0].Location : Peek().Location, "Expected two or more expressions");
                        }
                        var e = ee[0];
                        ee.RemoveAt(0);
                        while (ee.Count > 0)
                        {
                            e = new MessageApplicationExpression(e.Location, e, ee[0]);
                            ee.RemoveAt(0);
                        }
                        return Ok(e);
                    }
                ),
                // Expression1
                ParseExpression1
            )
        );

        ParseResult<Expression> ParseExpression1() => Call(nameof(ParseExpression1),
            () => ParseChoice(
                // Xor
                () => ParseBinary(TokenType.Xor, ParseExpression2, BinaryExpression.BOperator.Xor),
                // Expression2
                ParseExpression2
            )
        );

        ParseResult<Expression> ParseExpression2() => Call(nameof(ParseExpression2),
            () => ParseChoice(
                // Or
                () => ParseBinary(TokenType.Or, ParseExpression3, BinaryExpression.BOperator.Or),
                // Expression3
                ParseExpression3
            )
        );

        ParseResult<Expression> ParseExpression3() => Call(nameof(ParseExpression3),
            () => ParseChoice(
                // And
                () => ParseBinary(TokenType.And, ParseExpression4, BinaryExpression.BOperator.And),
                // Expression4
                ParseExpression4
            )
        );

        ParseResult<Expression> ParseExpression4() => Call(nameof(ParseExpression4),
            () => ParseChoice(
                // Equal
                () => ParseSequence(
                    ParseExpression5,
                    () => ParseToken(TokenType.Equal),
                    () => ParseExpression5().MakeFatal(),
                    (a, b, c) => Ok((Expression)new BinaryExpression(a.Location, a, BinaryExpression.BOperator.Equal, c))
                ),
                // NotEqual
                () => ParseSequence(
                    ParseExpression5,
                    () => ParseToken(TokenType.NotEqual),
                    () => ParseExpression5().MakeFatal(),
                    (a, b, c) => Ok((Expression)new BinaryExpression(a.Location, a, BinaryExpression.BOperator.NotEqual, c))
                ),
                // Expression5
                ParseExpression5
            )
        );

        ParseResult<Expression> ParseExpression5() => Call(nameof(ParseExpression5),
            () => ParseChoice(
                // GreaterThan
                () => ParseSequence(
                    ParseExpression6,
                    () => ParseToken(TokenType.GreaterThan),
                    () => ParseExpression6().MakeFatal(),
                    (a, b, c) => Ok((Expression)new BinaryExpression(a.Location, a, BinaryExpression.BOperator.GreaterThan, c))
                ),
                // GreaterThanOrEqual
                () => ParseSequence(
                    ParseExpression6,
                    () => ParseToken(TokenType.GreaterThanOrEqual),
                    () => ParseExpression6().MakeFatal(),
                    (a, b, c) => Ok((Expression)new BinaryExpression(a.Location, a, BinaryExpression.BOperator.GreaterThanOrEqual, c))
                ),
                // LesserThan
                () => ParseSequence(
                    ParseExpression6,
                    () => ParseToken(TokenType.LesserThan),
                    () => ParseExpression6().MakeFatal(),
                    (a, b, c) => Ok((Expression)new BinaryExpression(a.Location, a, BinaryExpression.BOperator.LesserThan, c))
                ),
                // LesserThanOrEqual
                () => ParseSequence(
                    ParseExpression6,
                    () => ParseToken(TokenType.LesserThanOrEqual),
                    () => ParseExpression6().MakeFatal(),
                    (a, b, c) => Ok((Expression)new BinaryExpression(a.Location, a, BinaryExpression.BOperator.LesserThanOrEqual, c))
                ),
                ParseExpression6
            )
        );

        ParseResult<Expression> ParseExpression6() => Call(nameof(ParseExpression6),
            () => ParseChoice(
                // Add
                () => ParseBinary(TokenType.Plus, ParseExpression7, BinaryExpression.BOperator.Add),
                // Subtract
                () => ParseBinary(TokenType.Minus, ParseExpression7, BinaryExpression.BOperator.Subtract),
                ParseExpression7
            )
        );
        
        ParseResult<Expression> ParseExpression7() => Call(nameof(ParseExpression7),
            () => ParseChoice(
                // Multiply
                () => ParseBinary(TokenType.Star, ParseExpression8, BinaryExpression.BOperator.Multiply),
                // Divide
                () => ParseBinary(TokenType.Slash, ParseExpression8, BinaryExpression.BOperator.Divide),
                // IntegerDivide
                () => ParseBinary(TokenType.DoubleSlash, ParseExpression8, BinaryExpression.BOperator.IntegerDivide),
                // ParseExpression8
                ParseExpression8
            )
        );

        ParseResult<Expression> ParseExpression8() => Call(nameof(ParseExpression8),
            () => ParseChoice(
                // Negate
                () => ParseSequence(
                    () => ParseToken(TokenType.Minus),
                    () => ParseExpression9().MakeFatal(),
                    (a, b) => Ok((Expression)new UnaryExpression(a.Location, UnaryExpression.UOperator.Negate, b))
                ),
                // Not
                () => ParseSequence(
                    () => ParseToken(TokenType.Not),
                    () => ParseExpression9().MakeFatal(),
                    (a, b) => Ok((Expression)new UnaryExpression(a.Location, UnaryExpression.UOperator.Not, b))
                ),
                // ParseExpression9
                ParseExpression9
            )
        );

        ParseResult<Expression> ParseExpression9() => Call(nameof(ParseExpression9),
            () => ParseChoice(
                // PropertyAccess
                () => ParseSequence(
                    ParseExpressionAtom,
                    () => ParseToken(TokenType.Period),
                    () => ParseToken(TokenType.Identifier).MakeFatal(),
                    (a, b, c) => Ok((Expression)new PropertyAccessExpression(a.Location, a, ((StringToken)c).Value))
                ),
                // ExpressionAtom
                ParseExpressionAtom
            )
        );

        ParseResult<Expression> ParseExpressionAtom() => Call(nameof(ParseExpressionAtom),
            () => ParseChoice(
                // BlockExpression
                () => ParseSequence(
                    () => ParseToken(TokenType.Do),
                    () => ParseToken(TokenType.Colon).MakeFatal(),
                    () => ParseToken(TokenType.EOL).MakeFatal(),
                    () => ParseStatementBlock().MakeFatal(),
                    (a, b, c, d) =>
                    {
                        var body = new BlockExpression(b.Location);
                        body.Statements.AddRange(d);
                        return Ok((Expression)body);
                    }
                ),
                // ObjectCreationExpression
                () => ParseSequence(
                    () => ParseToken(TokenType.New),
                    () => ParseTypeExpression().MakeFatal(),
                    (a, b) => Ok((Expression)new ObjectCreationExpression(a.Location, b))
                ),
                // MessageCreationExpression
                () => ParseSequence(
                    () => ParseToken(TokenType.LBrace),
                    () => ParseToken(TokenType.Identifier).MakeFatal(),
                    () => ParseStar(() => ParseSequence(
                        () => ParseToken(TokenType.Identifier),
                        () => ParseToken(TokenType.Colon).MakeFatal(),
                        () => ParseExpression().MakeFatal(),
                        (a, b, c) => Ok(new Argument(a.Location, ((StringToken)a).Value, c))
                    )),
                    () => ParseToken(TokenType.RBrace).MakeFatal(),
                    (a, b, c, d) =>
                    {
                        var expr = new MessageCreationExpression(a.Location, ((StringToken)b).Value);
                        expr.Arguments.AddRange(c);
                        return Ok((Expression)expr);
                    }
                ),
                // TupleConstructionExpression
                () => ParseSequence(
                    () => ParseToken(TokenType.LParen),
                    () => ParseExpression().MakeFatal(),
                    () => ParseToken(TokenType.Comma),
                    () => ParseOptional(() => ParseSequence(
                        ParseExpression,
                        () => ParseStar(() => ParseSequence(
                            () => ParseToken(TokenType.Comma),
                            () => ParseExpression().MakeFatal(),
                            (a, b) => Ok(b)
                        )),
                        (a, b) =>
                        {
                            b.Insert(0, a);
                            return Ok(b);
                        }
                    )),
                    () => ParseToken(TokenType.RParen).MakeFatal(),
                    (a, b, c, d, e) => Ok((Expression)new TupleConstructionExpression(a.Location, d ?? new()))
                ),
                // ListConstructionExpression
                () => ParseSequence(
                    () => ParseToken(TokenType.LBracket),
                    () => ParseOptional(() => ParseSequence(
                        ParseExpression,
                        () => ParseStar(() => ParseSequence(
                            () => ParseToken(TokenType.Comma),
                            () => ParseExpression().MakeFatal(),
                            (a, b) => Ok(b)
                        )),
                        (a, b) =>
                        {
                            b.Insert(0, a);
                            return Ok(b);
                        }
                    )),
                    () => ParseToken(TokenType.RBracket).MakeFatal(),
                    (a, b, c) => Ok((Expression)new ListConstructionExpression(a.Location, b ?? new()))
                ),
                // IdentifierExpression
                () => ParseToken(TokenType.Identifier).Map(t => (Expression)new IdentifierExpression(t.Location, ((StringToken)t).Value)),
                // StringLiteralExpression
                () => ParseToken(TokenType.String).Map(t => (Expression)new StringLiteralExpression(t.Location, ((StringToken)t).Value)),
                // IntLiteralExpression
                () => ParseToken(TokenType.Int).Map(t => (Expression)new IntLiteralExpression(t.Location, ((IntToken)t).Value)),
                // FloatLiteralExpression
                () => ParseToken(TokenType.Float).Map(t => (Expression)new FloatLiteralExpression(t.Location, ((FloatToken)t).Value)),
                // BoolLiteralExpression
                () => ParseToken(TokenType.Bool).Map(t => (Expression)new BoolLiteralExpression(t.Location, ((BoolToken)t).Value)),
                // CharLiteralExpression
                () => ParseToken(TokenType.Char).Map(t => (Expression)new CharLiteralExpression(t.Location, ((CharToken)t).Value)),
                // (Expression)
                () => ParseSequence(
                    () => ParseToken(TokenType.LParen),
                    () => ParseExpression().MakeFatal(),
                    () => ParseToken(TokenType.RParen).MakeFatal(),
                    (a, b, c) => Ok(b)
                )
            )
        );

        ParseResult<Expression> ParseBinary(TokenType t, Func<ParseResult<Expression>> lower, BinaryExpression.BOperator op) => ParseSequence(
            lower,
            () => ParsePlus(() => ParseSequence(
                () => ParseToken(t),
                () => lower().MakeFatal(),
                (a, b) => Ok(b)
            )),
            (a, b) =>
            {
                while (b.Count > 0)
                {
                    a = new BinaryExpression(a.Location, a, op, b[0]);
                    b.RemoveAt(0);
                }
                return Ok(a);
            }
        );

        ParseResult<Pattern> ParsePattern() => Call(nameof(ParsePattern),
            () => ParseChoice(
                // Identifier
                () => ParseSequence(
                    () => ParseToken(TokenType.Identifier),
                    t => Ok((Pattern)new IdentifierPattern(t.Location, ((StringToken)t).Value))
                ),
                // List
                () => ParseSequence(
                    () => ParseToken(TokenType.LBracket),
                    () => ParseOptional(() => ParseSequence(
                        () => ParsePattern().MakeFatal(),
                        () => ParseStar(() => ParseSequence(
                            () => ParseToken(TokenType.Comma),
                            () => ParsePattern().MakeFatal(),
                            (a, b) => Ok(b)
                        )),
                        (a, b) =>
                        {
                            b.Insert(0, a);
                            return Ok(b);
                        }
                    )),
                    () => ParseToken(TokenType.RBracket).MakeFatal(),
                    (a, b, c) =>
                    {
                        ListPattern pattern = new(a.Location);
                        pattern.InnerPatterns.AddRange(b ?? new());
                        return Ok((Pattern)pattern);
                    }
                ),
                // Tuple
                () => ParseSequence(
                    () => ParseToken(TokenType.LParen),
                    () => ParsePattern().MakeFatal(),
                    () => ParseToken(TokenType.Comma),
                    () => ParseOptional(() => ParseSequence(
                        ParsePattern,
                        () => ParseStar(() => ParseSequence(
                            () => ParseToken(TokenType.Comma),
                            () => ParsePattern().MakeFatal(),
                            (a, b) => Ok(b)
                        )),
                        (a, b) =>
                        {
                            b.Insert(0, a);
                            return Ok(b);
                        }
                    )),
                    () => ParseToken(TokenType.RParen).MakeFatal(),
                    (a, b, c, d, e) =>
                    {
                        TuplePattern pattern = new(a.Location);
                        d?.Insert(0, b);
                        pattern.InnerPatterns.AddRange(d ?? new() { b });
                        return Ok((Pattern)pattern);
                    }
                ),
                // Variadic
                () => ParseSequence(
                    () => ParseToken(TokenType.DoublePeriod),
                    () => ParseOptional(ParsePattern),
                    (a, b) => Ok((Pattern)new VariadicPattern(a.Location, b ?? new IgnorePattern(new GeneratedLocation(fileName))))
                )
            )
        );

        ParseResult<TypeExpression> ParseTypeExpression() => throw new NotImplementedException();





        ParseResult<List<T>> ParseStar<T>(Func<ParseResult<T>> func)
        {
            List<T> list = new();
            while (true)
            {
                var start = index;
                var r = func();
                if (r.IsFatal)
                {
                    index = start;
                    return r.Transmute<List<T>>();
                }
                if (r.IsErr)
                {
                    index = start;
                    break;
                }
                list.Add(r.Unwrap());
            }
            return Ok(list);
        }

        ParseResult<List<T>> ParsePlus<T>(Func<ParseResult<T>> func)
        {
            var start = index;
            var r = ParseStar(func);
            switch (r)
            {
                case ParseResult<List<T>>.Ok_(var value):
                    if (value.Count == 0)
                    {
                        index = start;
                        return Err<List<T>>(Peek().Location, "One or more was expected");
                    }
                    return Ok(value);
                default:
                    return r;
            }
        }

        ParseResult<T?> ParseOptional<T>(Func<ParseResult<T>> func) where T: class
        {
            var start = index;
            var r = func();
            if (r.IsFatal)
            {
                index = start;
                return r.Map(r => (T?)r);
            }
            if (r.IsErr)
            {
                index = start;
                return Ok<T?>(null);
            }
            return r.Map(r => (T?)r);
        }

        ParseResult<T> ParseChoice<T>(params Func<ParseResult<T>>[] funcs)
        {
            var start = index;
            foreach (var func in funcs)
            {
                var r = func();
                if (r.IsFatal) return r;
                if (r.IsErr)
                {
                    index = start;
                    continue;
                }
                return r;
            }
            return Err<T>(Peek().Location, "No choice succeeded");
        }

        ParseResult<Token> ParseToken(TokenType tokenType)
        {
            if (Peek().TokenType == tokenType) return Ok(Next());
            return Err<Token>(Peek().Location, $"Expected token {tokenType}; found {Peek()}");
        }

#region ParseSequence
        ParseResult<R> ParseSequence<A, R>(Func<ParseResult<A>> fa, Func<A, ParseResult<R>> fr)
        {
            var start = index;
            var ra = fa();
            if (ra.IsErr) return ra.Transmute<R>();

            var rr = fr(ra.Unwrap()); if (rr.IsErr) { index = start; } return rr;
        }
        ParseResult<R> ParseSequence<A, B, R>(
            Func<ParseResult<A>> fa,
            Func<ParseResult<B>> fb,
            Func<A, B, ParseResult<R>> fr)
        {
            var start = index;
            var ra = fa();
            if (ra.IsErr) return ra.Transmute<R>();

            var rb = fb();
            if (rb.IsErr)
            {
                index = start;
                return rb.Transmute<R>();
            }

            var rr = fr(ra.Unwrap(), rb.Unwrap()); if (rr.IsErr) { index = start; } return rr;
        }
        ParseResult<R> ParseSequence<A, B, C, R>(
            Func<ParseResult<A>> fa,
            Func<ParseResult<B>> fb,
            Func<ParseResult<C>> fc,
            Func<A, B, C, ParseResult<R>> fr
        )
        {
            var start = index;
            var ra = fa();
            if (ra.IsErr) return ra.Transmute<R>();

            var rb = fb();
            if (rb.IsErr)
            {
                index = start;
                return rb.Transmute<R>();
            }

            var rc = fc();
            if (rc.IsErr)
            {
                index = start;
                return rc.Transmute<R>();
            }

            var rr = fr(ra.Unwrap(), rb.Unwrap(), rc.Unwrap()); if (rr.IsErr) { index = start; } return rr;
        }
        ParseResult<R> ParseSequence<A, B, C, D, R>(
            Func<ParseResult<A>> fa,
            Func<ParseResult<B>> fb,
            Func<ParseResult<C>> fc,
            Func<ParseResult<D>> fd,
            Func<A, B, C, D, ParseResult<R>> fr
        )
        {
            var start = index;
            var ra = fa();
            if (ra.IsErr) return ra.Transmute<R>();

            var rb = fb();
            if (rb.IsErr)
            {
                index = start;
                return rb.Transmute<R>();
            }

            var rc = fc();
            if (rc.IsErr)
            {
                index = start;
                return rc.Transmute<R>();
            }

            var rd = fd();
            if (rd.IsErr)
            {
                index = start;
                return rd.Transmute<R>();
            }

            var rr = fr(ra.Unwrap(), rb.Unwrap(), rc.Unwrap(), rd.Unwrap()); if (rr.IsErr) { index = start; } return rr;
        }
        ParseResult<R> ParseSequence<A, B, C, D, E, R>(
            Func<ParseResult<A>> fa,
            Func<ParseResult<B>> fb,
            Func<ParseResult<C>> fc,
            Func<ParseResult<D>> fd,
            Func<ParseResult<E>> fe,
            Func<A, B, C, D, E, ParseResult<R>> fr
        )
        {
            var start = index;
            var ra = fa();
            if (ra.IsErr) return ra.Transmute<R>();

            var rb = fb();
            if (rb.IsErr)
            {
                index = start;
                return rb.Transmute<R>();
            }

            var rc = fc();
            if (rc.IsErr)
            {
                index = start;
                return rc.Transmute<R>();
            }

            var rd = fd();
            if (rd.IsErr)
            {
                index = start;
                return rd.Transmute<R>();
            }

            var re = fe();
            if (re.IsErr)
            {
                index = start;
                return re.Transmute<R>();
            }

            var rr = fr(ra.Unwrap(), rb.Unwrap(), rc.Unwrap(), rd.Unwrap(), re.Unwrap()); if (rr.IsErr) { index = start; } return rr;
        }
        ParseResult<R> ParseSequence<A, B, C, D, E, F, R>(
            Func<ParseResult<A>> fa,
            Func<ParseResult<B>> fb,
            Func<ParseResult<C>> fc,
            Func<ParseResult<D>> fd,
            Func<ParseResult<E>> fe,
            Func<ParseResult<F>> ff,
            Func<A, B, C, D, E, F, ParseResult<R>> fr
        )
        {
            var start = index;
            var ra = fa();
            if (ra.IsErr) return ra.Transmute<R>();

            var rb = fb();
            if (rb.IsErr)
            {
                index = start;
                return rb.Transmute<R>();
            }

            var rc = fc();
            if (rc.IsErr)
            {
                index = start;
                return rc.Transmute<R>();
            }

            var rd = fd();
            if (rd.IsErr)
            {
                index = start;
                return rd.Transmute<R>();
            }

            var re = fe();
            if (re.IsErr)
            {
                index = start;
                return re.Transmute<R>();
            }

            var rf = ff();
            if (rf.IsErr)
            {
                index = start;
                return rf.Transmute<R>();
            }

            var rr = fr(ra.Unwrap(), rb.Unwrap(), rc.Unwrap(), rd.Unwrap(), re.Unwrap(), rf.Unwrap()); if (rr.IsErr) { index = start; } return rr;
        }
        ParseResult<R> ParseSequence<A, B, C, D, E, F, G, R>(
            Func<ParseResult<A>> fa,
            Func<ParseResult<B>> fb,
            Func<ParseResult<C>> fc,
            Func<ParseResult<D>> fd,
            Func<ParseResult<E>> fe,
            Func<ParseResult<F>> ff,
            Func<ParseResult<G>> fg,
            Func<A, B, C, D, E, F, G, ParseResult<R>> fr
        )
        {
            var start = index;
            var ra = fa();
            if (ra.IsErr) return ra.Transmute<R>();

            var rb = fb();
            if (rb.IsErr)
            {
                index = start;
                return rb.Transmute<R>();
            }

            var rc = fc();
            if (rc.IsErr)
            {
                index = start;
                return rc.Transmute<R>();
            }

            var rd = fd();
            if (rd.IsErr)
            {
                index = start;
                return rd.Transmute<R>();
            }

            var re = fe();
            if (re.IsErr)
            {
                index = start;
                return re.Transmute<R>();
            }

            var rf = ff();
            if (rf.IsErr)
            {
                index = start;
                return rf.Transmute<R>();
            }

            var rg = fg();
            if (rg.IsErr)
            {
                index = start;
                return rg.Transmute<R>();
            }

            var rr = fr(ra.Unwrap(), rb.Unwrap(), rc.Unwrap(), rd.Unwrap(), re.Unwrap(), rf.Unwrap(), rg.Unwrap()); if (rr.IsErr) { index = start; } return rr;
        }
#endregion

        ParseResult<List<T>> Block<T>(Func<ParseResult<T>> func) where T: Node
        {
            int i = 0;
            while (Peek(i).TokenType == TokenType.EOL)
            {
                i++;
            }
            var tok = Peek(i);
            var col = (tok.Location as ConcreteLocation ?? throw new Exception("Generated location is not valid here; find place where it is generated and resolve")).Column;
            if (tok.TokenType == TokenType.EOF)
            {
                return Err<List<T>>(Peek().Location, "Expected indented block");
            }
            if (col <= indentStack.Last())
            {
                return Err<List<T>>(Peek().Location, "Expected indented block");
            }
            indentStack.Add(col);

            List<T> ret = new();

            while (true)
            {
                var start = index;
                var r = func();
                if (r.IsFatal) return r.Transmute<List<T>>();
                if (r.IsErr) break;
                var v = r.Unwrap();
                if ((v.Location as ConcreteLocation ?? throw new Exception("Generated location is not valid here; find place where it is generated and resolve")).Column != indentStack.Last())
                {
                    index = start;
                    break;
                }
                ret.Add(r.Unwrap());
            }
            indentStack.RemoveAt(indentStack.Count - 1);
            return Ok(ret);
        }

        Token Next()
        {
            Token tok;
            if (index >= tokens.Count)
            {
                return new Token(new GeneratedLocation(fileName), TokenType.EOF);
            }
            tok = tokens[index];
            if (tok.TokenType != TokenType.EOF)
            {
                index++;
            }
            return tok;
        }
        Token Peek(int k)
        {
            if (index + k >= tokens.Count)
            {
                return new Token(new GeneratedLocation(fileName), TokenType.EOF);
            }
            return tokens[index + k];
        }

        Token Peek() => Peek(0);

        ParseResult<T> Ok<T>(T value) => ParseResult<T>.Ok(value);
        ParseResult<T> Err<T>(Location location, string error) => ParseResult<T>.Err(location, error);
        ParseResult<T> Fatal<T>(Location location, string error) => ParseResult<T>.Fatal(location, error);
    }

    static class ExtensionMethods
    {
        public static Func<U> Upcast<T, U>(this Func<T> f) where T: U => () => f();
        public static Func<U> Downcast<T, U>(this Func<T> f) where U: T => () => (U)(f() ?? throw new Exception());
    }
}