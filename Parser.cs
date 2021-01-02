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
            public static ParseResult<T> Err(string error) => new Err_(error);
            public static ParseResult<T> Fatal(string error) => new Fatal_(error);

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

                public override ParseResult<U> Transmute<U>() => ParseResult<U>.Err(Error);

                public override ParseResult<T> MakeFatal() => Fatal(Error);
                public override ParseResult<T> MakeFatal(string error) => Fatal(error);

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
                }

                public override bool IsOk => false;

                public override bool IsErr => true;

                public override bool IsFatal => true;

                public override ParseResult<T> MakeFatal() => this;
                public override ParseResult<T> MakeFatal(string error) => this;

                public override ParseResult<U> Map<U>(Func<T, U> f) => Transmute<U>();

                public override ParseResult<U> Transmute<U>() => ParseResult<U>.Fatal(Error);

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

        Lexer lexer;
        List<Token> tokens = new();
        int index = 0;
        List<int> indentStack = new() { 0 };

        public Parser(Lexer lexer)
        {
            this.lexer = lexer;
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
                (a, ss) =>
                {
                    ProgramNode program = new(
                        ss.Count > 0 
                        ? ss[0].Location 
                        : (a.Count > 0 
                           ? a[0].Location 
                           : new Location { FileName = "???", Row = 1, Column = 1, Index = 0 })
                          );
                    program.StructDeclarations.AddRange(ss);
                    return Ok(program);
                }
            )
        );

        // struct_decl := STRUCT IDENTIFIER COLON EOL+ ((member_decl/handler_decl) EOL*)*
        ParseResult<StructDeclaration> ParseStructDeclaration() => Call(nameof(ParseStructDeclaration),
            () => ParseSequence(
                () => ParseToken(TokenType.Struct),
                () => ParseToken(TokenType.Identifier),
                () => ParseToken(TokenType.Colon),
                () => ParsePlus(() => ParseToken(TokenType.EOL)),
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
                ParseTypeExpression,
                () => ParseToken(TokenType.EOL),
                (a, b, c, d) => Ok(new MemberDeclaration(a.Location, ((StringToken)b).Value, c))
            )
        );

        // handler_decl := IDENTIFIER LPAREN (IDENTIFIER (DOUBLE_COLON type_expr)? (COMMA IDENTIFIER (DOUBLE_COLON type_expr)?)*)? RPAREN COLON EOL block[stmnt]
        ParseResult<MessageHandlerDeclaration> ParseMessageHandlerDeclaration() => Call(nameof(ParseMessageHandlerDeclaration),
            () => ParseSequence(
                () => ParseToken(TokenType.Identifier),
                () => ParseToken(TokenType.LParen),
                () => ParseOptional(() => ParseSequence(
                    () => ParseToken(TokenType.Identifier),
                    () => ParseOptional(() => ParseSequence(
                        () => ParseToken(TokenType.DoubleColon),
                        ParseTypeExpression,
                        (a, b) => Ok(b)
                    )),
                    () => ParseStar(() => ParseSequence(
                        () => ParseToken(TokenType.Comma),
                        () => ParseToken(TokenType.Identifier),
                        () => ParseOptional(() => ParseSequence(
                            () => ParseToken(TokenType.DoubleColon),
                            ParseTypeExpression,
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
                () => ParseToken(TokenType.RParen),
                () => ParseToken(TokenType.Colon),
                () => ParseToken(TokenType.EOL),
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
            () => ParseChoice(
                // Let
                () => ParseSequence(
                    () => ParseToken(TokenType.Let),
                    ParsePattern,
                    () => ParseToken(TokenType.Assign),
                    ParseExpression,
                    () => ParseToken(TokenType.EOL),
                    (a, b, c, d, e) => Ok((Statement)new LetStatement(a.Location, b, d))
                ),
                // Assign
                () => ParseSequence(
                    ParsePath,
                    () => ParseToken(TokenType.Assign),
                    ParseExpression,
                    () => ParseToken(TokenType.EOL),
                    (a, b, c, d) => Ok((Statement)new AssignStatement(a.Location, a, c))
                ),
                // For
                () => ParseSequence(
                    () => ParseToken(TokenType.For),
                    ParsePattern,
                    () => ParseToken(TokenType.In),
                    ParseExpression,
                    () => ParseToken(TokenType.Colon),
                    () => ParseToken(TokenType.EOL),
                    ParseStatementBlock,
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
                    ParseExpression,
                    () => ParseToken(TokenType.Colon),
                    () => ParseToken(TokenType.EOL),
                    ParseStatementBlock,
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

        ParseResult<Path> ParsePath() => throw new NotImplementedException();

        ParseResult<Expression> ParseExpression() => throw new NotImplementedException();

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
                        ParsePattern,
                        () => ParseStar(() => ParseSequence(
                            () => ParseToken(TokenType.Comma),
                            ParsePattern,
                            (a, b) => Ok(b)
                        )),
                        (a, b) =>
                        {
                            b.Insert(0, a);
                            return Ok(b);
                        }
                    )),
                    () => ParseToken(TokenType.RBracket),
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
                    ParsePattern,
                    () => ParseToken(TokenType.Comma),
                    () => ParseOptional(() => ParseSequence(
                        ParsePattern,
                        () => ParseStar(() => ParseSequence(
                            () => ParseToken(TokenType.Comma),
                            ParsePattern,
                            (a, b) => Ok(b)
                        )),
                        (a, b) =>
                        {
                            b.Insert(0, a);
                            return Ok(b);
                        }
                    )),
                    () => ParseToken(TokenType.RParen),
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
                    (a, b) => Ok((Pattern)new VariadicPattern(a.Location, b ?? new IgnorePattern(new GeneratedLocation())))
                )
            )
        );

        ParseResult<TypeExpression> ParseTypeExpression() => throw new NotImplementedException();

        ParseResult<List<T>> ParseStar<T>(Func<ParseResult<T>> func)
        {
            List<T> list = new();
            while (true)
            {
                var r = func();
                if (r.IsFatal) return r.Transmute<List<T>>();
                if (r.IsErr) break;
                list.Add(r.Unwrap());
            }
            return Ok(list);
        }

        ParseResult<List<T>> ParsePlus<T>(Func<ParseResult<T>> func)
        {
            var r = ParseStar(func);
            switch (r)
            {
                case ParseResult<List<T>>.Ok_(var value):
                    if (value.Count == 0) return Err<List<T>>("One or more was expected");
                    return Ok(value);
                default:
                    return r;
            }
        }

        ParseResult<T?> ParseOptional<T>(Func<ParseResult<T>> func) where T: class
        {
            var r = func();
            if (r.IsFatal) return r.Map(r => (T?)r);
            if (r.IsErr) return Ok<T?>(null);
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
            return Err<T>("No choice succeeded");
        }

        ParseResult<Token> ParseToken(TokenType tokenType)
        {
            if (Peek().TokenType == tokenType) return Ok(Next());
            return Err<Token>($"Expected token {tokenType}; found {Peek()}");
        }

#region ParseSequence
        ParseResult<R> ParseSequence<A, R>(Func<ParseResult<A>> fa, Func<A, ParseResult<R>> fr)
        {
            var ra = fa();
            if (ra.IsErr) return ra.Transmute<R>();

            return fr(ra.Unwrap());
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

            return fr(ra.Unwrap(), rb.Unwrap());
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

            return fr(ra.Unwrap(), rb.Unwrap(), rc.Unwrap());
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

            return fr(ra.Unwrap(), rb.Unwrap(), rc.Unwrap(), rd.Unwrap());
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

            return fr(ra.Unwrap(), rb.Unwrap(), rc.Unwrap(), rd.Unwrap(), re.Unwrap());
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

            return fr(ra.Unwrap(), rb.Unwrap(), rc.Unwrap(), rd.Unwrap(), re.Unwrap(), rf.Unwrap());
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

            return fr(ra.Unwrap(), rb.Unwrap(), rc.Unwrap(), rd.Unwrap(), re.Unwrap(), rf.Unwrap(), rg.Unwrap());
        }
#endregion

        ParseResult<List<T>> Block<T>(Func<ParseResult<T>> func) where T: Node
        {
            int i = 0;
            while (Peek(i).TokenType == TokenType.EOL)
            {
                i++;
            }
            if (Peek(i).TokenType == TokenType.EOF)
            {
                return Err<List<T>>("Expected indented block");
            }
            if (Peek(i).Location.Column <= indentStack.Last())
            {
                return Err<List<T>>("Expected indented block");
            }
            indentStack.Add(Peek(i).Location.Column);

            List<T> ret = new();

            while (true)
            {
                var start = index;
                var r = func();
                if (r.IsFatal) return r.Transmute<List<T>>();
                if (r.IsErr) break;
                var v = r.Unwrap();
                if (v.Location.Column != indentStack.Last())
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
                tok = lexer.Next();
                if (tok.TokenType == TokenType.EOF)
                {
                    return tok;
                }
                tokens.Add(tok);
                index++;
                return tok;
            }
            tok = tokens[index];
            index++;
            return tok;
        }
        Token Peek(int k)
        {
            while (index + k >= tokens.Count)
            {
                var tok = lexer.Next();
                if (tok.TokenType == TokenType.EOF)
                {
                    return tok;
                }
                tokens.Add(tok);
            }
            return tokens[index + k];
        }

        Token Peek() => Peek(0);

        ParseResult<T> Ok<T>(T value) => ParseResult<T>.Ok(value);
        ParseResult<T> Err<T>(string error) => ParseResult<T>.Err(error);
        ParseResult<T> Fatal<T>(string error) => ParseResult<T>.Fatal(error);
    }

    static class ExtensionMethods
    {
        public static Func<U> Upcast<T, U>(this Func<T> f) where T: U => () => f();
        public static Func<U> Downcast<T, U>(this Func<T> f) where U: T => () => (U)(f() ?? throw new Exception());
    }
}