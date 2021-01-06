using fissure.Ast;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using static fissure.Ast.BinaryExpression.BOperator;
using static fissure.Ast.UnaryExpression.UOperator;

namespace fissure
{
    class Executor
    {
        Dictionary<string, Type> types;

        public Executor()
        { 
            types = new()
            {
                { "Integer", IntegerType.Instance },
                { "String", StringType.Instance },
                { "Float", FloatType.Instance },
                { "Bool", BoolType.Instance },
                { "Char", CharType.Instance },
                { "System", new UserType()
                { 
                    Fields = new()
                    {
                        { "StdOut", new UserType()
                        { 
                            Handlers = new()
                            {
                                {
                                    "Print",
                                    new BuiltinHandler("Print", (receiver, message) =>
                                    {
                                        ObjectValue str = (ObjectValue)ExecuteMessage((ObjectValue)message.Arguments["value"], new MessageValue("ToString"));
                                        Console.Write(str.InternalAs<string>());
                                        return NilType.New();
                                    })
                                },
                                {
                                    "PrintLn",
                                    new BuiltinHandler("PrintLn", (receiver, message) =>
                                    {
                                        ObjectValue str = (ObjectValue)ExecuteMessage((ObjectValue)message.Arguments["value"], new MessageValue("ToString"));
                                        Console.WriteLine(str.InternalAs<string>());
                                        return NilType.New();
                                    })
                                }
                            }
                        } }
                    }
                } }
            };
        }

        public Value ExecProgram(ProgramNode programNode)
        {
            foreach (var structDecl in programNode.StructDeclarations)
            {
                types.Add(structDecl.Name, new UserType());
            }
            foreach (var structDecl in programNode.StructDeclarations)
            {
                foreach (var field in structDecl.MemberDeclarations)
                {
                    types[structDecl.Name].Fields.Add(field.Name, EvalTypeExpression(field.Type));
                }
                foreach (var handler in structDecl.MessageHandlerDeclarations)
                {
                    types[structDecl.Name].Handlers.Add(handler.Name, new UserHandler(handler.Name, handler));
                }
            }

            var program = (ObjectValue)types["Program"].DefaultValue();
            var message = new MessageValue("Main");
            message.Arguments["system"] = types["System"].DefaultValue();
            return ExecuteMessage(program, message);
        }

        public Type EvalTypeExpression(TypeExpression typeExpression)
        {
            switch (typeExpression)
            {
                case IdentifierTypeExpression itx:
                    return types[itx.Name];
                case ListTypeExpression ltx:
                    return new ListType(EvalTypeExpression(ltx.InnerType));
                default:
                    throw new NotImplementedException();
            }
        }

        public Value ExecuteMessage(ObjectValue receiver, MessageValue message)
        {
            var handler = receiver.Type.Handlers.First(h => h.Key == message.Name).Value;
            switch (handler)
            {
                case UserHandler uh:
                    VariableContext variables = new ReceiverContext(receiver).Push();
                    foreach (var param in uh.MessageHandler.Parameters)
                    {
                        var paramType = param.Type switch
                        {
                            not null => EvalTypeExpression(param.Type),
                            null => null
                        };

                        try
                        {
                            var value = message.Arguments.First(a => a.Key == param.Name).Value ?? throw new Exception("Should never happen");
                            switch (value)
                            {
                                case ErrorValue e:
                                    return e;
                                case ObjectValue v:
                                    if (paramType is not null)
                                    {
                                        if (v.Type.Equivalent(paramType))
                                        {
                                            variables.New(param.Name, v);
                                        }
                                        else
                                        {
                                            return new ErrorValue($"Handler {handler.Name} expected argument {param.Name} to be of type {paramType}, but the received value was of type {v.Type}");
                                        }
                                    }
                                    else
                                    {
                                        variables.New(param.Name, v);
                                    }
                                    break;
                            }
                        }
                        catch (InvalidOperationException)
                        {
                            if (paramType is not null)
                            {
                                variables.New(param.Name, paramType.DefaultValue());
                            }
                            else
                            {
                                variables.New(param.Name, NilType.Instance.DefaultValue());
                            }
                        }
                    }
                    return EvalExpression(variables.Push(), uh.MessageHandler.Body);
                case BuiltinHandler bh:
                    return bh.Func(receiver, message);
                default:
                    throw new NotImplementedException();
            }

        }

        Value EvalExpression(VariableContext variables, Expression expression)
        {
            switch (expression)
            {
                case BlockExpression block:
                    if (block.Statements.Count == 0) return NilType.Instance.DefaultValue();
                    variables = variables.Push();
                    for (int i = 0; i < block.Statements.Count - 1; ++i)
                    {
                        if (EvalStatement(variables, block.Statements[0]) is ErrorValue e)
                        {
                            return e;
                        }
                    }
                    var ret = EvalStatement(variables, block.Statements[^1]);
                    return ret;
                case IdentifierExpression ident:
                    return variables[ident.Name];
                case StringLiteralExpression sl:
                    return StringType.New(sl.Value);
                case IntLiteralExpression il:
                    return IntegerType.New(il.Value);
                case FloatLiteralExpression fl:
                    return FloatType.New(fl.Value);
                case BoolLiteralExpression bl:
                    return BoolType.New(bl.Value);
                case CharLiteralExpression cl:
                    return CharType.New(cl.Value);
                case TupleConstructionExpression tce:
                    List<Value> values = new();
                    List<Type> types = new();
                    foreach (var e in tce.Expressions)
                    {
                        switch (EvalExpression(variables, e))
                        {
                            case ErrorValue ev:
                                return ev;
                            case ObjectValue ov:
                                values.Add(ov);
                                types.Add(ov.Type);
                                break;
                        }
                    }
                    return new ObjectValue(new TupleType(types), values);
                case ListConstructionExpression lce:
                    values = new();
                    Type? type = null;
                    if (lce.Expressions.Count == 0) throw new Exception("Zero-sized array literals not yet supported");
                    foreach (var e in lce.Expressions)
                    {
                        switch (EvalExpression(variables, e))
                        {
                            case ErrorValue ev:
                                return ev;
                            case ObjectValue ov:
                                if (type is null)
                                {
                                    type = ov.Type;
                                }
                                else if (type.Equivalent(ov.Type))
                                {
                                    values.Add(ov);
                                }
                                else
                                {
                                    return new ErrorValue("Array elements must be of a single type");
                                }
                                break;
                        }
                    }
                    return new ObjectValue(new ListType(type ?? throw new Exception()), values);
                case BinaryExpression be:
                    ObjectValue left = new ObjectValue(NilType.Instance);
                    switch (EvalExpression(variables, be.Left))
                    {
                        case ErrorValue e:
                            return e;
                        case ObjectValue o:
                            left = o;
                            break;
                    }
                    ObjectValue right = new ObjectValue(NilType.Instance);
                    switch (EvalExpression(variables, be.Right))
                    {
                        case ErrorValue e:
                            return e;
                        case ObjectValue o:
                            right = o;
                            break;
                    }
                    {
                        switch ((left.Type, be.Operator, right.Type))
                        {

                            case (IntegerType, Add, IntegerType):
                                return IntegerType.New(left.InternalAs<BigInteger>() + right.InternalAs<BigInteger>());
                            case (IntegerType, Add, StringType):
                                return StringType.New(left.InternalAs<BigInteger>() + right.InternalAs<string>());
                            case (IntegerType, Add, FloatType):
                                return FloatType.New((double)left.InternalAs<BigInteger>() + right.InternalAs<double>());
                            case (StringType, Add, IntegerType):
                                return StringType.New(left.InternalAs<string>() + right.InternalAs<double>());
                            case (StringType, Add, StringType):
                                return StringType.New(left.InternalAs<string>() + right.InternalAs<string>());
                            case (StringType, Add, FloatType):
                                return StringType.New(left.InternalAs<string>() + right.InternalAs<double>());
                            case (StringType, Add, BoolType):
                                return StringType.New(left.InternalAs<string>() + right.InternalAs<bool>());
                            case (StringType, Add, CharType):
                                return StringType.New(left.InternalAs<string>() + char.ConvertFromUtf32((int)right.InternalAs<uint>()));
                            case (StringType, Add, NilType):
                                return StringType.New(left.InternalAs<string>() + "nil");
                            case (FloatType, Add, IntegerType):
                                return FloatType.New(left.InternalAs<double>() + (double)right.InternalAs<BigInteger>());
                            case (FloatType, Add, StringType):
                                return StringType.New(left.InternalAs<double>() + right.InternalAs<string>());
                            case (FloatType, Add, FloatType):
                                return FloatType.New(left.InternalAs<double>() + right.InternalAs<double>());
                            case (BoolType, Add, StringType):
                                return StringType.New(left.InternalAs<bool>() + right.InternalAs<string>());
                            case (CharType, Add, StringType):
                                return StringType.New(char.ConvertFromUtf32((int)left.InternalAs<uint>()) + right.InternalAs<string>());
                            case (NilType, Add, StringType):
                                return StringType.New("nil" + right.InternalAs<string>());

                            case (IntegerType, Subtract, IntegerType):
                                return IntegerType.New(left.InternalAs<BigInteger>() - right.InternalAs<BigInteger>());
                            case (IntegerType, Subtract, FloatType):
                                return FloatType.New((double)left.InternalAs<BigInteger>() - right.InternalAs<double>());
                            case (FloatType, Subtract, IntegerType):
                                return FloatType.New(left.InternalAs<double>() - (double)right.InternalAs<BigInteger>());
                            case (FloatType, Subtract, FloatType):
                                return FloatType.New(left.InternalAs<double>() - right.InternalAs<double>());

                            case (IntegerType, Divide, IntegerType):
                                return FloatType.New((double)left.InternalAs<BigInteger>() / (double)right.InternalAs<BigInteger>());
                            case (IntegerType, Divide, FloatType):
                                return FloatType.New((double)left.InternalAs<BigInteger>() / right.InternalAs<double>());
                            case (FloatType, Divide, IntegerType):
                                return FloatType.New(left.InternalAs<double>() / (double)right.InternalAs<BigInteger>());
                            case (FloatType, Divide, FloatType):
                                return FloatType.New(left.InternalAs<double>() / right.InternalAs<double>());

                            case (IntegerType, IntegerDivide, IntegerType):
                                return IntegerType.New(left.InternalAs<BigInteger>() / right.InternalAs<BigInteger>());

                            case (IntegerType, Multiply, IntegerType):
                                return IntegerType.New(left.InternalAs<BigInteger>() * right.InternalAs<BigInteger>());
                            case (IntegerType, Multiply, FloatType):
                                return FloatType.New((double)left.InternalAs<BigInteger>() * right.InternalAs<double>());
                            case (StringType, Multiply, IntegerType):
                                var s = "";
                                for (int i = 0; i < right.InternalAs<BigInteger>(); ++i)
                                {
                                    s += left.InternalAs<string>();
                                }
                                return StringType.New(s);
                            case (FloatType, Multiply, IntegerType):
                                return FloatType.New(left.InternalAs<double>() * (double)right.InternalAs<BigInteger>());
                            case (FloatType, Multiply, FloatType):
                                return FloatType.New(left.InternalAs<double>() * right.InternalAs<double>());

                            case (IntegerType, GreaterThan, IntegerType):
                                return BoolType.New(left.InternalAs<BigInteger>() > right.InternalAs<BigInteger>());
                            case (IntegerType, GreaterThan, FloatType):
                                return BoolType.New((double)left.InternalAs<BigInteger>() > right.InternalAs<double>());
                            case (FloatType, GreaterThan, IntegerType):
                                return BoolType.New(left.InternalAs<double>() > (double)right.InternalAs<BigInteger>());
                            case (FloatType, GreaterThan, FloatType):
                                return BoolType.New(left.InternalAs<double>() > right.InternalAs<double>());
                            case (CharType, GreaterThan, CharType):
                                return BoolType.New(left.InternalAs<uint>() > right.InternalAs<uint>());

                            case (IntegerType, GreaterThanOrEqual, IntegerType):
                                return BoolType.New(left.InternalAs<BigInteger>() >= right.InternalAs<BigInteger>());
                            case (IntegerType, GreaterThanOrEqual, FloatType):
                                return BoolType.New((double)left.InternalAs<BigInteger>() >= right.InternalAs<double>());
                            case (FloatType, GreaterThanOrEqual, IntegerType):
                                return BoolType.New(left.InternalAs<double>() >= (double)right.InternalAs<BigInteger>());
                            case (FloatType, GreaterThanOrEqual, FloatType):
                                return BoolType.New(left.InternalAs<double>() >= right.InternalAs<double>());
                            case (CharType, GreaterThanOrEqual, CharType):
                                return BoolType.New(left.InternalAs<uint>() >= right.InternalAs<uint>());

                            case (IntegerType, LesserThan, IntegerType):
                                return BoolType.New(left.InternalAs<BigInteger>() < right.InternalAs<BigInteger>());
                            case (IntegerType, LesserThan, FloatType):
                                return BoolType.New((double)left.InternalAs<BigInteger>() < right.InternalAs<double>());
                            case (FloatType, LesserThan, IntegerType):
                                return BoolType.New(left.InternalAs<double>() < (double)right.InternalAs<BigInteger>());
                            case (FloatType, LesserThan, FloatType):
                                return BoolType.New(left.InternalAs<double>() < right.InternalAs<double>());
                            case (CharType, LesserThan, CharType):
                                return BoolType.New(left.InternalAs<uint>() < right.InternalAs<uint>());

                            case (IntegerType, LesserThanOrEqual, IntegerType):
                                return BoolType.New(left.InternalAs<BigInteger>() <= right.InternalAs<BigInteger>());
                            case (IntegerType, LesserThanOrEqual, FloatType):
                                return BoolType.New((double)left.InternalAs<BigInteger>() <= right.InternalAs<double>());
                            case (FloatType, LesserThanOrEqual, IntegerType):
                                return BoolType.New(left.InternalAs<double>() <= (double)right.InternalAs<BigInteger>());
                            case (FloatType, LesserThanOrEqual, FloatType):
                                return BoolType.New(left.InternalAs<double>() <= right.InternalAs<double>());
                            case (CharType, LesserThanOrEqual, CharType):
                                return BoolType.New(left.InternalAs<uint>() <= right.InternalAs<uint>());

                            case (_, Equal, _):
                                return BoolType.New(ObjectValue.IsEqual(left, right));
                            case (_, NotEqual, _):
                                return BoolType.New(!ObjectValue.IsEqual(left, right));

                            case (BoolType, And, BoolType):
                                return BoolType.New(left.InternalAs<bool>() && right.InternalAs<bool>());
                            case (BoolType, Or, BoolType):
                                return BoolType.New(left.InternalAs<bool>() || right.InternalAs<bool>());
                            case (BoolType, Xor, BoolType):
                                return BoolType.New((left.InternalAs<bool>() && !right.InternalAs<bool>()) || (left.InternalAs<bool>() && !right.InternalAs<bool>()));
                            default:
                                return new ErrorValue($"Invalid operator {left.Type} {be.Operator.GetString()} {right.Type}");
                        }
                    }
                case UnaryExpression ue:
                    ObjectValue val = new ObjectValue(NilType.Instance);
                    switch (EvalExpression(variables, ue.Expression))
                    { 
                        case ErrorValue e:
                            return e;
                        case ObjectValue v:
                            val = v;
                            break;
                    }
                    switch (ue.Operator, val.Type)
                    {
                        case (Negate, IntegerType):
                            return IntegerType.New(-val.InternalAs<BigInteger>());
                        case (Negate, FloatType):
                            return FloatType.New(-val.InternalAs<double>());
                        case (Not, BoolType):
                            return BoolType.New(!val.InternalAs<bool>());
                        default:
                            return new ErrorValue($"Invalid operator {ue.Operator.GetString()}{val.Type}");
                    }
                case PropertyAccessExpression pae:
                    val = new ObjectValue(NilType.Instance);
                    switch (EvalExpression(variables, pae.Root))
                    {
                        case ErrorValue e:
                            return e;
                        case ObjectValue o:
                            val = o;
                            break;
                        default:
                            throw new NotImplementedException();
                    }
                    if (val.Fields.ContainsKey(pae.Name))
                    {
                        return val.Fields[pae.Name];
                    }
                    else
                    {
                        return new ErrorValue($"Type {val.Type} has no field {pae.Name}");
                    }
                case ObjectCreationExpression oce:
                    return EvalTypeExpression(oce.Type).DefaultValue();
                case MessageCreationExpression mce:
                    var msg = new MessageValue(mce.Header);
                    foreach (var arg in mce.Arguments)
                    {
                        switch (EvalExpression(variables, arg.Value))
                        {
                            case ErrorValue e:
                                return e;
                            case ObjectValue o:
                                msg.Arguments.Add(arg.Key, o);
                                break;
                            default:
                                throw new NotImplementedException();
                        }
                    }
                    return msg;
                case MessageApplicationExpression mae:
                    ObjectValue receiver = new ObjectValue(NilType.Instance);
                    switch (EvalExpression(variables, mae.Receiver))
                    {
                        case ErrorValue e:
                            return e;
                        case ObjectValue o:
                            receiver = o;
                            break;
                        default:
                            throw new NotImplementedException();
                    }
                    msg = new("");
                    switch (EvalExpression(variables, mae.Message))
                    {
                        case ErrorValue e:
                            return e;
                        case MessageValue m:
                            msg = m;
                            break;
                        default:
                            throw new NotImplementedException();
                    }
                    return ExecuteMessage(receiver, msg);
                case IfExpression ie:
                    var cond = new ObjectValue(NilType.Instance);
                    switch (EvalExpression(variables, ie.Condition))
                    {
                        case ErrorValue e:
                            return e;
                        case ObjectValue o:
                            cond = o;
                            break;
                        default:
                            throw new NotImplementedException();
                    }
                    if (cond.Type is not BoolType) return new ErrorValue($"Condition of if expression must be Bool, but it was {cond.Type}");
                    if (cond.InternalAs<bool>())
                    {
                        return EvalExpression(variables, ie.Body);
                    }
                    else
                    {
                        foreach (var elif in ie.ElseIfExpressions)
                        {
                            cond = new ObjectValue(NilType.Instance);
                            switch (EvalExpression(variables, elif.Condition))
                            {
                                case ErrorValue e:
                                    return e;
                                case ObjectValue o:
                                    cond = o;
                                    break;
                                default:
                                    throw new NotImplementedException();
                            }
                            if (cond.Type is not BoolType) return new ErrorValue($"Condition of if expression must be Bool, but it was {cond.Type}");
                            if (cond.InternalAs<bool>())
                            { 
                                return EvalExpression(variables, elif.Body);
                            }
                        }

                        if (ie.ElseExpression is not null)
                        {
                            return EvalExpression(variables, ie.ElseExpression);
                        }

                        return NilType.New();
                    }
                case ElseIfExpression:
                    throw new Exception($"{nameof(EvalExpression)} should never be called with an {nameof(ElseIfExpression)} directly");
                default:
                    throw new NotImplementedException();
            }
        }

        Value EvalStatement(VariableContext variables, Statement statement)
        {
            switch (statement)
            {
                case LetStatement ls:
                    ObjectValue val = new ObjectValue(NilType.Instance);
                    switch (EvalExpression(variables, ls.Expression))
                    {
                        case ErrorValue e:
                            return e;
                        case ObjectValue o:
                            val = o;
                            break;
                        default:
                            throw new NotImplementedException();
                    }
                    return SetAtPattern(variables, ls.Pattern, val);
                case AssignStatement @as:
                    val = new ObjectValue(NilType.Instance);
                    switch (EvalExpression(variables, @as.Expression))
                    {
                        case ErrorValue e:
                            return e;
                        case ObjectValue o:
                            val = o;
                            break;
                        default:
                            throw new NotImplementedException();
                    }
                    return SetAtPath(variables, @as.Path, val);
                case ForStatement fs:
                    throw new NotImplementedException();
                case WhileStatement ws:
                    while (true)
                    {
                        var cond = new ObjectValue(NilType.Instance);
                        switch (EvalExpression(variables, ws.Condition))
                        {
                            case ErrorValue e:
                                return e;
                            case ObjectValue o:
                                cond = o;
                                break;
                            default:
                                throw new NotImplementedException();
                        }
                        if (cond.Type is not BoolType) return new ErrorValue($"Condition of while expression must be Bool, but it was {cond.Type}");
                        if (cond.InternalAs<bool>())
                        {
                            if (EvalExpression(variables, ws.Body) is ErrorValue e) return e;
                        }
                        else
                        {
                            return NilType.New();
                        }
                    }
                case ExpressionStatement es:
                    return EvalExpression(variables, es.Expression);
                default:
                    throw new NotImplementedException();
            }
        }

        Value SetAtPath(VariableContext variables, Path path, ObjectValue value)
        {
            List<string> parts = new();

            void Do(Path path)
            {
                switch (path)
                {
                    case IdentifierPath ip:
                        parts.Add(ip.Name);
                        return;
                    case PropertyPath pp:
                        Do(pp.Path);
                        parts.Add(pp.Name);
                        return;
                }
            }

            Do(path);

            var current = variables[parts[0]] as ObjectValue ?? throw new Exception();
            for (int i = 1; i < parts.Count - 1; ++i)
            {
                if (current.Fields.ContainsKey(parts[i]))
                {
                    current = (ObjectValue)current.Fields.First(f => f.Key == parts[i]).Value;
                }
                else
                {
                    return new ErrorValue($"Type {current.Type} does not contain a field {parts[i]}");
                }
            }
            var last = parts.Last();
            if (current.Fields.ContainsKey(last))
            {
                current.Fields[last] = value;
            }
            else
            {
                return new ErrorValue($"Type {current.Type} does not contain a field {last}");
            }
            return NilType.New();
        }

        Value SetAtPattern(VariableContext variables, Pattern pattern, ObjectValue value)
        {
            switch (pattern)
            {
                case IdentifierPattern ip:
                    variables[ip.Name] = value;
                    break;
                case ListPattern lp:
                    if (value.Type is not ListType) return new ErrorValue($"Value of type {value.Type} cannot be matched to a list pattern");
                    bool hasVariadic = false;
                    foreach (var subPattern in lp.InnerPatterns)
                    {
                        if (subPattern is VariadicPattern)
                        {
                            if (hasVariadic)
                            {
                                return new ErrorValue("Only one variadic pattern allowed in a list pattern");
                            }
                            hasVariadic = true;
                        }
                    }
                    if (hasVariadic)
                    {
                        throw new NotImplementedException();
                    }
                    else
                    {
                        if (lp.InnerPatterns.Count != value.InternalAs<List<ObjectValue>>().Count)
                        {
                            return new ErrorValue($"Length of list does not equal length of list pattern");
                        }
                        for (int i = 0; i < lp.InnerPatterns.Count; ++i)
                        {
                            SetAtPattern(variables, lp.InnerPatterns[i], value.InternalAs<List<ObjectValue>>()[i]);
                        }
                    }
                    break;
                case TuplePattern tp:
                    if (value.Type is not TupleType) return new ErrorValue($"Value of type {value.Type} cannot be matched to a tuple pattern");
                    hasVariadic = false;
                    foreach (var subPattern in tp.InnerPatterns)
                    {
                        if (subPattern is VariadicPattern)
                        {
                            if (hasVariadic)
                            {
                                return new ErrorValue("Only one variadic pattern allowed in a tuple pattern");
                            }
                            hasVariadic = true;
                        }
                    }
                    if (hasVariadic)
                    {
                        throw new NotImplementedException();
                    }
                    else
                    {
                        if (tp.InnerPatterns.Count != value.InternalAs<List<ObjectValue>>().Count)
                        {
                            return new ErrorValue($"Length of tuple does not equal length of tuple pattern");
                        }
                        for (int i = 0; i < tp.InnerPatterns.Count; ++i)
                        {
                            SetAtPattern(variables, tp.InnerPatterns[i], value.InternalAs<List<ObjectValue>>()[i]);
                        }
                    }
                    break;
                case IgnorePattern:
                    break;
                default:
                    throw new NotImplementedException();
            }
            return NilType.New();
        }
    }

    abstract class Type
    {
        public Dictionary<string, Type> Fields { get; init; } = new();
        public Dictionary<string, Handler> Handlers { get; init; } = new();

        public abstract Value DefaultValue();

        public abstract bool Equivalent(Type other);
    }

    class IntegerType : Type
    {
        public static IntegerType Instance { get; } = new IntegerType();

        private IntegerType()
        {
            Handlers = new()
            {
                { "ToString", new BuiltinHandler("ToString", (receiver, message) => new ObjectValue(StringType.Instance, $"{(BigInteger)(receiver.InternalValue ?? throw new Exception())}")) }
            };
        }

        public static Value New(BigInteger value) => new ObjectValue(Instance, value);

        public override Value DefaultValue() => new ObjectValue(this, 0);

        public override bool Equivalent(Type other) => other is IntegerType;
    }

    class StringType : Type
    {
        public static StringType Instance { get; } = new StringType();

        private StringType()
        {
            Handlers = new()
            {
                { "ToString", new BuiltinHandler("ToString", (receiver, message) => new ObjectValue(Instance, receiver.InternalValue)) }
            };
        }

        public static Value New(string value) => new ObjectValue(Instance, value);

        public override Value DefaultValue() => new ObjectValue(this, "");

        public override bool Equivalent(Type other) => other is StringType;
    }

    class FloatType : Type
    {
        public static FloatType Instance { get; } = new FloatType();

        private FloatType()
        {
            Handlers = new()
            {
                { "ToString", new BuiltinHandler("ToString", (receiver, message) => new ObjectValue(StringType.Instance, $"{(double)(receiver.InternalValue ?? throw new Exception())}")) }
            };
        }

        public static Value New(double value) => new ObjectValue(Instance, value);

        public override Value DefaultValue() => new ObjectValue(this, 0.0);

        public override bool Equivalent(Type other) => other is FloatType;
    }

    class BoolType : Type
    {
        public static BoolType Instance { get; } = new BoolType();

        private BoolType()
        {
            Handlers = new()
            {
                { "ToString", new BuiltinHandler("ToString", (receiver, message) => new ObjectValue(StringType.Instance, $"{(bool)(receiver.InternalValue ?? throw new Exception())}")) }
            };
        }

        public static Value New(bool value) => new ObjectValue(Instance, value);

        public override Value DefaultValue() => new ObjectValue(this, false);

        public override bool Equivalent(Type other) => other is BoolType;
    }

    class CharType : Type
    {
        public static CharType Instance { get; } = new CharType();

        private CharType()
        {
            Handlers = new()
            {
                { "ToString", new BuiltinHandler("ToString", (receiver, message) => new ObjectValue(StringType.Instance, $"{char.ConvertFromUtf32((int)(uint)(receiver.InternalValue ?? throw new Exception()))}")) }
            };
        }

        public static Value New(uint value) => new ObjectValue(Instance, value);

        public override Value DefaultValue() => new ObjectValue(this, 0);

        public override bool Equivalent(Type other) => other is CharType;
    }

    class ListType : Type
    {
        public Type InnerType;

        public ListType(Type innerType)
        {
            InnerType = innerType;
        }

        public override Value DefaultValue() => new ObjectValue(this, new List<ObjectValue>());

        public override bool Equivalent(Type other) => other is ListType l && l.InnerType.Equivalent(InnerType);
    }

    class TupleType : Type
    {
        public List<Type> InnerTypes;

        public TupleType(List<Type> innerTypes)
        {
            InnerTypes = innerTypes;
        }

        public override Value DefaultValue() => new ObjectValue(this, new List<ObjectValue>());

        public override bool Equivalent(Type other)
        {
            if (other is not TupleType) return false;
            if (((TupleType)other).InnerTypes.Count != InnerTypes.Count) return false;
            for (int i = 0; i < InnerTypes.Count; ++i)
            {
                if (!InnerTypes[i].Equivalent(((TupleType)other).InnerTypes[i])) return false;
            }
            return true;
        }
    }

    class NilType : Type
    {
        public static NilType Instance { get; } = new NilType();

        private NilType() { }

        public static Value New() => new ObjectValue(Instance);

        public override Value DefaultValue() => new ObjectValue(this);

        public override bool Equivalent(Type other) => other is NilType;
    }    

    class UserType : Type
    {
        public UserType()
        {

        }

        public override Value DefaultValue()
        {
            var fields = new Dictionary<string, Value>();
            foreach (var field in Fields)
            {
                fields.Add(field.Key, field.Value.DefaultValue());
            }
            return new ObjectValue(this, fields);
        }

        public override bool Equivalent(Type other) => other == this;
    }

    abstract class Handler
    {
        public string Name;

        public Handler(string name)
        {
            Name = name;
        }
    }

    class UserHandler : Handler
    {
        public MessageHandlerDeclaration MessageHandler;

        public UserHandler(string name, MessageHandlerDeclaration handler) : base(name)
        {
            MessageHandler = handler;
        }
    }

    class BuiltinHandler : Handler
    {
        public Func<ObjectValue, MessageValue, Value> Func;

        public BuiltinHandler(string name, Func<ObjectValue, MessageValue, Value> func) : base(name)
        {
            Func = func;
        }
    }

    abstract class Value
    { 
        
    }

    class ObjectValue : Value
    {
        public Type Type;

        public Dictionary<string, Value> Fields = new();

        public object? InternalValue = null;

        public T InternalAs<T>() => (T)(InternalValue ?? throw new Exception());

        public ObjectValue(Type type)
        {
            Type = type;
        }

        public ObjectValue(Type type, Dictionary<string, Value> fields) : this(type)
        {
            Fields = fields;
        }

        public ObjectValue(Type type, object? internalValue) : this(type)
        {
            InternalValue = internalValue;
        }

        public static bool IsEqual(ObjectValue left, ObjectValue right)
        {
            switch (left.Type, right.Type)
            {
                case (IntegerType, IntegerType):
                    return left.InternalAs<BigInteger>() == right.InternalAs<BigInteger>();
                case (IntegerType, FloatType):
                    return (double)left.InternalAs<BigInteger>() == right.InternalAs<double>();
                case (StringType, StringType):
                    return left.InternalAs<string>() == right.InternalAs<string>();
                case (FloatType, IntegerType):
                    return left.InternalAs<double>() == (double)right.InternalAs<BigInteger>();
                case (FloatType, FloatType):
                    return left.InternalAs<double>() == right.InternalAs<double>();
                case (BoolType, BoolType):
                    return left.InternalAs<bool>() == right.InternalAs<bool>();
                case (CharType, CharType):
                    return left.InternalAs<uint>() == right.InternalAs<uint>();
                case (ListType, ListType):
                    var l = left.InternalAs<List<ObjectValue>>();
                    var r = right.InternalAs<List<ObjectValue>>();
                    if (l.Count != r.Count) return false;
                    for (int i = 0; i < l.Count; ++i)
                    {
                        if (!IsEqual(l[i], r[i])) return false;
                    }
                    return true;
                case (TupleType, TupleType):
                    l = left.InternalAs<List<ObjectValue>>();
                    r = right.InternalAs<List<ObjectValue>>();
                    if (l.Count != r.Count) return false;
                    for (int i = 0; i < l.Count; ++i)
                    {
                        if (!IsEqual(l[i], r[i])) return false;
                    }
                    return true;
                case (NilType, NilType):
                    return true;
                default:
                    return false;
            }
        }
    }

    class MessageValue : Value
    {
        public string Name;

        public Dictionary<string, Value> Arguments = new();

        public MessageValue(string name)
        {
            Name = name;
        }
    }

    class ErrorValue : Value
    {
        public string Error;

        public ErrorValue(string error) => Error = error;
    }

    abstract class Context
    {
        protected Context? parent = null;

        public VariableContext Push()
        {
            var vc = new VariableContext();
            vc.parent = this;
            return vc;
        }

        public Context Pop() => parent ?? throw new Exception();

        public abstract Value this[string name] { get; set; }
    }

    class VariableContext : Context
    {
        Dictionary<string, Value> variables = new();

        public VariableContext() { }

        public override Value this[string name]
        {
            get
            {
                if (variables.ContainsKey(name))
                {
                    return variables[name];
                }
                return parent?[name] ?? new ErrorValue($"Undefined variable {name}");
            }
            set
            {
                if (variables.ContainsKey(name))
                {
                    variables[name] = value;
                }
                else if (parent is not null)
                {
                    parent[name] = value;
                }
                else
                {
                    throw new Exception();
                }
            }
        }

        public void New(string name, Value value)
        {
            variables[name] = value;
        }
    }

    class ReceiverContext : Context
    {
        new ObjectValue parent;

        public ReceiverContext(ObjectValue parent)
        {
            this.parent = parent;
        }

        public override Value this[string name]
        {
            get
            {
                if (parent.Fields.ContainsKey(name))
                {
                    return parent.Fields[name];
                }
                return new ErrorValue($"Undefined variable {name}");
            }
            set
            {
                if (parent.Fields.ContainsKey(name))
                {
                    parent.Fields[name] = value;
                }
                else
                {
                    throw new Exception();
                }
            }
        }
    }
}
