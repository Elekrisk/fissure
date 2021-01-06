using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;

namespace fissure.Ast
{
    abstract class Node
    {
        public Location Location;

        protected Node(Location location)
        {
            Location = location;
        }
    }

    class ProgramNode : Node
    {
        public List<StructDeclaration> StructDeclarations { get; init; } = new();

        public ProgramNode(Location location) : base(location)
        {
            
        }

    }

    class StructDeclaration : Node
    {
        public string Name { get; init; }
        public List<String> Contracts { get; init; } = new();
        public List<MemberDeclaration> MemberDeclarations { get; init; } = new();
        public List<MessageHandlerDeclaration> MessageHandlerDeclarations { get; init; } = new();

        public StructDeclaration(Location location, string name) : base(location)
        {
            Name = name;
        }

    }

    class MemberDeclaration : Node
    {
        public string Name;
        public TypeExpression Type;

        public MemberDeclaration(Location location, string name, TypeExpression type) : base(location)
        {
            Name = name;
            Type = type;
        }

    }

    class MessageHandlerDeclaration : Node
    {
        public string Name;
        public List<string> TypeParameters { get; init; } = new();
        public List<Parameter> Parameters { get; init; } = new();
        public List<TypeConstraint> TypeConstraints { get; init; } = new();
        public Expression Body;

        public MessageHandlerDeclaration(Location location, string name, Expression body) : base(location)
        {
            Name = name;
            Body = body;
        }

    }

    class Parameter : Node
    {
        public string Name;
        public TypeExpression? Type;

        public Parameter(Location location, string name, TypeExpression? type) : base(location)
        {
            Name = name;
            Type = type;
        }

    }

    abstract class TypeExpression : Node
    {
        public TypeExpression(Location location) : base(location)
        {

        }

    }

    class IdentifierTypeExpression : TypeExpression
    {
        public string Name;
        public List<TypeExpression> TypeArguments { get; init; } = new();

        public IdentifierTypeExpression(Location location, string name) : base(location)
        {
            Name = name;
        }

    }

    class ListTypeExpression : TypeExpression
    {
        public TypeExpression InnerType;

        public ListTypeExpression(Location location, TypeExpression innerType) : base(location)
        {
            InnerType = innerType;
        }

    }

    class TupleTypeExpression : TypeExpression
    {
        public List<TypeExpression> InnerTypes { get; init; } = new();

        public bool IsVariadic => throw new NotImplementedException();

        public TupleTypeExpression(Location location) : base(location)
        {

        }

    }
    
    class VariadicTypeExpression : TypeExpression
    {
        public TypeExpression InnerType;

        public VariadicTypeExpression(Location location, TypeExpression innerType) : base(location)
        {
            InnerType = innerType;
        }

    }

    abstract class Pattern : Node
    {
        public Pattern(Location location) : base(location)
        {

        }
    }

    class IdentifierPattern : Pattern
    {
        public string Name;

        public IdentifierPattern(Location location, string name) : base(location)
        {
            Name = name;
        }

    }

    class ListPattern : Pattern
    {
        public List<Pattern> InnerPatterns { get; init; } = new();

        public ListPattern(Location location) : base(location)
        {

        }

    }

    class TuplePattern : Pattern
    {
        public List<Pattern> InnerPatterns { get; init; } = new();

        public TuplePattern(Location location) : base(location)
        {

        }

    }

    class VariadicPattern : Pattern
    {
        public Pattern InnerPattern;

        public VariadicPattern(Location location, Pattern innerPattern) : base(location)
        {
            InnerPattern = innerPattern;
        }

    }

    class IgnorePattern : Pattern
    {
        public IgnorePattern(Location location) : base(location)
        {

        }

    }

    abstract class TypeConstraint : Node
    {
        public TypeConstraint(Location location) : base(location)
        {

        }
    }

    abstract class Expression : Node
    {
        public Expression(Location location) : base(location)
        {

        }

    }

    class BlockExpression : Expression
    {
        public List<Statement> Statements { get; init; } = new();

        public BlockExpression(Location location) : base(location)
        {

        }

    }

    class IdentifierExpression : Expression
    {
        public string Name;

        public IdentifierExpression(Location location, string name) : base(location)
        {
            Name = name;
        }

    }

    class StringLiteralExpression : Expression
    {
        public string Value;

        public StringLiteralExpression(Location location, string value) : base(location)
        {
            Value = value;
        }

    }

    class IntLiteralExpression : Expression
    {
        public BigInteger Value;

        public IntLiteralExpression(Location location, BigInteger value) : base(location)
        {
            Value = value;
        }

    }

    class FloatLiteralExpression : Expression
    {
        public double Value;

        public FloatLiteralExpression(Location location, double value) : base(location)
        {
            Value = value;
        }

    }

    class BoolLiteralExpression : Expression
    {
        public bool Value;

        public BoolLiteralExpression(Location location, bool value) : base(location)
        {
            Value = value;
        }

    }

    class CharLiteralExpression : Expression
    {
        public uint Value;

        public CharLiteralExpression(Location location, uint value) : base(location)
        {
            Value = value;
        }

    }

    class TupleConstructionExpression : Expression
    {
        public List<Expression> Expressions { get; init; } = new();

        public TupleConstructionExpression(Location location, List<Expression> expressions) : base(location)
        {
            Expressions = expressions;
        }

    }

    class ListConstructionExpression : Expression
    {
        public List<Expression> Expressions { get; init; } = new();

        public ListConstructionExpression(Location location, List<Expression> expressions) : base(location)
        {
            Expressions = expressions;
        }

    }

    class BinaryExpression : Expression
    {
        public Expression Left;
        public BOperator Operator;
        public Expression Right;

        public BinaryExpression(Location location, Expression left, BOperator @operator, Expression right) : base(location)
        {
            Left = left;
            Operator = @operator;
            Right = right;
        }


        public enum BOperator {
            Add,
            Subtract,
            Divide,
            IntegerDivide,
            Multiply,
            GreaterThan,
            GreaterThanOrEqual,
            LesserThan,
            LesserThanOrEqual,
            Equal,
            NotEqual,
            And,
            Or,
            Xor
        }
    }

    static class ExtensionMethods
    {
        public static string GetString(this BinaryExpression.BOperator op) => op switch
        {
            BinaryExpression.BOperator.Add => "+",
            BinaryExpression.BOperator.Subtract => "-",
            BinaryExpression.BOperator.Divide => "/",
            BinaryExpression.BOperator.IntegerDivide => "//",
            BinaryExpression.BOperator.Multiply => "*",
            BinaryExpression.BOperator.GreaterThan => ">",
            BinaryExpression.BOperator.GreaterThanOrEqual => ">=",
            BinaryExpression.BOperator.LesserThan => "<",
            BinaryExpression.BOperator.LesserThanOrEqual => "<=",
            BinaryExpression.BOperator.Equal => "==",
            BinaryExpression.BOperator.NotEqual => "!=",
            BinaryExpression.BOperator.And => "and",
            BinaryExpression.BOperator.Or => "or",
            BinaryExpression.BOperator.Xor => "xor",
            _ => throw new NotImplementedException()
        };

        public static string GetString(this UnaryExpression.UOperator op) => op switch
        {
            UnaryExpression.UOperator.Negate => "-",
            UnaryExpression.UOperator.Not => "!",
            _ => throw new NotImplementedException()
        };
    }

    class UnaryExpression : Expression
    {
        public UOperator Operator;
        public Expression Expression;

        public UnaryExpression(Location location, UOperator @operator, Expression expression) : base(location)
        {
            Operator = @operator;
            Expression = expression;
        }


        public enum UOperator
        {
            Negate,
            Not
        }
    }

    class PropertyAccessExpression : Expression
    {
        public Expression Root;
        public string Name;

        public PropertyAccessExpression(Location location, Expression root, string name) : base(location)
        {
            Root = root;
            Name = name;
        }

    }

    class ObjectCreationExpression : Expression
    {
        public TypeExpression Type;

        public ObjectCreationExpression(Location location, TypeExpression type) : base(location)
        {
            Type = type;
        }

    }

    class MessageCreationExpression : Expression
    {
        public string Header;
        public List<Argument> Arguments { get; init; } = new();

        public MessageCreationExpression(Location location, string header) : base(location)
        {
            Header = header;
        }

    }

    class MessageApplicationExpression : Expression
    {
        public Expression Receiver;
        public Expression Message;

        public MessageApplicationExpression(Location location, Expression receiver, Expression message) : base(location)
        {
            Receiver = receiver;
            Message = message;
        }

    }

    class IfExpression : Expression
    {
        public Expression Condition;
        public Expression Body;
        public List<ElseIfExpression> ElseIfExpressions { get; init; } = new();
        public Expression? ElseExpression;

        public IfExpression(Location location, Expression condition, Expression body, Expression? elseExpression) : base(location)
        {
            Condition = condition;
            Body = body;
            ElseExpression = elseExpression;
        }

    }

    class ElseIfExpression : Expression
    {
        public Expression Condition;
        public Expression Body;

        public ElseIfExpression(Location location, Expression condition, Expression body) : base(location)
        {
            Condition = condition;
            Body = body;
        }

    }

    class Argument : Node
    {
        public string Key;
        public Expression Value;

        public Argument(Location location, string key, Expression value) : base(location)
        {
            Key = key;
            Value = value;
        }

    }

    abstract class Statement : Node
    {
        public Statement(Location location) : base(location)
        {

        }
    }

    class LetStatement : Statement
    {
        public Pattern Pattern;
        public Expression Expression;

        public LetStatement(Location location, Pattern pattern, Expression expression) : base(location)
        {
            Pattern = pattern;
            Expression = expression;
        }

    }

    class AssignStatement : Statement
    {
        public Path Path;
        public Expression Expression;

        public AssignStatement(Location location, Path path, Expression expression) : base(location)
        {
            Path = path;
            Expression = expression;
        }

    }

    abstract class Path : Node
    {
        public Path(Location location) : base(location)
        {

        }
    }

    class IdentifierPath : Path
    {
        public string Name;

        public IdentifierPath(Location location, string name) : base(location)
        {
            Name = name;
        }

    }

    class PropertyPath : Path
    {
        public Path Path;
        public string Name;

        public PropertyPath(Location location, Path path, string name) : base(location)
        {
            Path = path;
            Name = name;
        }

    }

    class ForStatement : Statement
    {
        public Pattern LoopPattern;
        public Expression Iterator;
        public Expression Body;

        public ForStatement(Location location, Pattern loopPattern, Expression iterator, Expression body) : base(location)
        {
            LoopPattern = loopPattern;
            Iterator = iterator;
            Body = body;
        }

    }

    class WhileStatement : Statement
    {
        public Expression Condition;
        public Expression Body;

        public WhileStatement(Location location, Expression condition, Expression body) : base(location)
        {
            Condition = condition;
            Body = body;
        }

    }

    class ExpressionStatement : Statement
    {
        public Expression Expression;

        public ExpressionStatement(Location location, Expression expression) : base(location)
        {
            Expression = expression;
        }

    }
}