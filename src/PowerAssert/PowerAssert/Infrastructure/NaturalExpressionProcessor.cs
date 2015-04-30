﻿namespace PowerAssert.Infrastructure
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using PowerAssert.Infrastructure.Nodes;

    internal class NaturalExpressionParser
    {
        public static Node Parse(Expression e)
        {
            if (e.NodeType == ExpressionType.ArrayIndex)
            {
                return ArrayIndex((BinaryExpression)e);
            }
            if (e is BinaryExpression)
            {
                return Binary((BinaryExpression)e, Util.NaturalOperators[e.NodeType]);
            }
            if (e is MemberExpression)
            {
                return Member((MemberExpression)e);
            }
            if (e is ConstantExpression)
            {
                return Constant((ConstantExpression)e);
            }
            if (e is MethodCallExpression)
            {
                return MethodCall((MethodCallExpression)e);
            }
            if (e is ConditionalExpression)
            {
                return Conditional((ConditionalExpression)e);
            }
            if (e is NewArrayExpression)
            {
                return NewArray((NewArrayExpression)e);
            }
            if (e is UnaryExpression)
            {
                return Unary((UnaryExpression)e);
            }
            if (e.NodeType == ExpressionType.Lambda)
            {
                return Expression(e);
            }
            if (e is TypeBinaryExpression)
            {
                return TypeBinary((TypeBinaryExpression)e);
            }
            if(e is NewExpression)
            {
                return NewObject((NewExpression) e);
            }

            return Expression(e);
        }

        private static Node NewObject(NewExpression e)
        {
            string value = GetValue(e, false);

            return new ConstantNode
            {
                Text = value
            };
        }

        static Node TypeBinary(TypeBinaryExpression e)
        {
            switch (e.NodeType)
            {
                case ExpressionType.TypeIs:
                    return new BinaryNode()
                    {
                        Left = Parse(e.Expression),
                        Operator = "is a(n)",
                        Right = new ConstantNode() { Text = NameOfType(e.TypeOperand) },
                        Value = GetValue(e, false)
                    };
                default:
                    throw new NotImplementedException(string.Format(CultureInfo.CurrentCulture,
                                                                    "Can't handle TypeBinaryExpression of type {0}",
                                                                    e.NodeType));
            }
        }

        static Node Expression(Expression e)
        {
            return new ConstantNode() { Text = e.ToString() };
        }

        static Node Unary(UnaryExpression e)
        {
            switch (e.NodeType)
            {
                case ExpressionType.Convert:
                    return Parse(e.Operand);
                case ExpressionType.Not:
                    return new UnaryNode() { Prefix = "!", Operand = Parse(e.Operand), PrefixValue = GetValue(e, false) };
                case ExpressionType.Negate:
                case ExpressionType.NegateChecked:
                    return new UnaryNode() { Prefix = "-", Operand = Parse(e.Operand), PrefixValue = GetValue(e, false) };


            }
            throw new ArgumentOutOfRangeException("e", string.Format("Can't handle UnaryExpression expression of class {0} and type {1}", e.GetType().Name, e.NodeType));

        }

        static Node NewArray(NewArrayExpression e)
        {
            switch (e.NodeType)
            {
                case ExpressionType.NewArrayInit:
                    Type t = e.Type.GetElementType();
                    return new NewArrayNode
                    {
                        Items = e.Expressions.Select(Parse).ToList(),
                        Type = NameOfType(t)
                    };
                case ExpressionType.NewArrayBounds:
                //todo:
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static readonly Dictionary<Type, string> Aliases = new Dictionary<Type, string>()
            {
                { typeof(byte), "byte" },
                { typeof(sbyte), "sbyte" },
                { typeof(short), "short" },
                { typeof(ushort), "ushort" },
                { typeof(int), "int" },
                { typeof(uint), "uint" },
                { typeof(long), "long" },
                { typeof(ulong), "ulong" },
                { typeof(float), "float" },
                { typeof(double), "double" },
                { typeof(decimal), "decimal" },
                { typeof(object), "object" },
                { typeof(string), "string" },
            };


        static string NameOfType(Type t)
        {
            return Aliases.ContainsKey(t) ? Aliases[t] : t.Name;
        }

        static Node ArrayIndex(BinaryExpression e)
        {
            return new ArrayIndexNode() { Array = Parse(e.Left), Index = Parse(e.Right), Value = GetValue(e, false) };
        }

        static Node Conditional(ConditionalExpression e)
        {
            return new ConditionalNode
            {
                Condition = Parse(e.Test),
                FalseValue = Parse(e.IfFalse),
                TrueValue = Parse(e.IfTrue)
            };
        }

        static Node MethodCall(MethodCallExpression e)
        {
            var parameters = e.Arguments.Select(Parse);
            if (e.Method.GetCustomAttributes(typeof(ExtensionAttribute), true).Any())
            {
                return new MethodCallNode
                {
                    Container = parameters.First(),
                    MemberName = e.Method.Name,
                    MemberValue = GetValue(e, true),
                    Parameters = parameters.Skip(1).ToList(),
                };
            }
            if (e.Type == typeof (Delegate))
            {
                return parameters.Last();
            }
            return new MethodCallNode
            {
                Container = e.Object == null ? new ConstantNode() { Text = e.Method.DeclaringType.Name } : Parse(e.Object),
                MemberName = e.Method.Name,
                MemberValue = GetValue(e, true),
                Parameters = parameters.ToList(),
            };
        }

        
        static Node Constant(ConstantExpression e)
        {
            string value = GetValue(e, false);

            return new ConstantNode
            {
                Text = value
            };
        }

        static Node Member(MemberExpression e)
        {
            if (IsDisplayClass(e.Expression) || e.Expression == null)
            {
                return new ConstantNode
                {
                    Value = GetValue(e, true),
                    Text = e.Member.Name
                };
            }
            return new MemberAccessNode
            {
                Container = Parse(e.Expression),
                MemberValue = GetValue(e, true),
                MemberName = e.Member.Name
            };
        }


        static bool IsDisplayClass(Expression expression)
        {
            if (expression is ConstantExpression)
            {
                return expression.Type.Name.StartsWith("<");
            }
            return false;
        }

        static Node Binary(BinaryExpression e, string text)
        {
            return new BinaryNode
            {
                Operator = text,
                Value = GetValue(e, false),
                Left = Parse(e.Left),
                Right = Parse(e.Right),
            };
        }

        static string GetValue(Expression e, bool possessive)
        {
            object value;
            try
            {
                value = DynamicInvoke(e);
            }
            catch (Exception exception)
            {
                return FormatException(exception);
            }
            var formatted = possessive ? FormatObjectPossessive(value) : FormatObject(value);
            return formatted + GetHints(e, value);
        }

        static string FormatException(Exception exception)
        {
            var i = exception.GetBaseException();
            return string.Format("{0}: {1}", i.GetType().Name, i.Message);
        }

        static string GetHints(Expression e, object value)
        {
            if (value is bool && !(bool)value && e is BinaryExpression && e.NodeType == ExpressionType.Equal)
            {
                var be = (BinaryExpression)e;
                object left;
                object right;
                try
                {
                    left = DynamicInvoke(be.Left);
                    right = DynamicInvoke(be.Right);
                }
                catch (TargetInvocationException exception)
                {
                    return FormatException(exception);
                }
                if (Equals(left, right))
                {
                    return ", but would have been True with .Equals()";
                }
                if (left is string && right is string)
                {
                    if (((string)left).Equals((string)right, StringComparison.InvariantCultureIgnoreCase))
                    {
                        return ", but would have been True if case insensitive";
                    }
                    return "";
                }
                if (left is IEnumerable && right is IEnumerable)
                {
                    if (((IEnumerable)left).Cast<object>().SequenceEqual(((IEnumerable)right).Cast<object>()))
                    {
                        return ", but would have been True with .SequenceEqual()";
                    }
                }
            }
            return "";
        }

        static object DynamicInvoke(Expression e)
        {
            return System.Linq.Expressions.Expression.Lambda(e).Compile().DynamicInvoke();
        }

        static string FormatObject(object value)
        {
            if (value == null)
            {
                return "null";
            }
            var type = value.GetType();
            if (value is Type)
            {
                if (typeof(MulticastDelegate).IsAssignableFrom(type))
                {
                    return String.Empty;
                }
                return type.Name;
            }
            if (value is MemberInfo)
            {
                var info = (MemberInfo) value;

                return info.Name;
            }
            if (value is string)
            {
                return "\"" + value + "\"";
            }
            if (value is char)
            {
                return "'" + value + "'";
            }
            if (value is IEnumerable)
            {
                var enumerable = (IEnumerable)value;
                var values = enumerable.Cast<object>().Select(FormatObject);
                //in case the enumerable is really long, let's cut off the end arbitrarily?
                const int Limit = 100;
                values = values.Take(Limit);
                if (values.Count() == Limit)
                {
                    values = values.Concat(new[] { "... (MORE THAN " + Limit + " ITEMS FOUND)" });
                }
                return "{" + string.Join(", ", values.ToArray()) + "}";
            }
            //if (value is Exception) return "the "value.GetType().Name;
            
            if (type.IsValueType)
            {
                return value.ToString();
            }
            if (typeof (MulticastDelegate).IsAssignableFrom(type))
            {
                return String.Empty;
            }
           
            return ("The" + type.Name + "'s").Replace('.', ' ');
        }

        static string FormatObjectPossessive(object value)
        {
            if (value == null)
            {
                return "null";
            }
            var type = value.GetType();
            if (value is Type)
            {
                if (typeof(MulticastDelegate).IsAssignableFrom(type))
                {
                    return String.Empty;
                }
                return type.Name;
            }
            if (value is MemberInfo)
            {
                var info = (MemberInfo)value;

                return info.Name;
            }
            if (value is string)
            {
                return "\"" + value + "\"";
            }
            if (value is char)
            {
                return "'" + value + "'";
            }
            if (value is IEnumerable)
            {
                var enumerable = (IEnumerable)value;
                var values = enumerable.Cast<object>().Select(FormatObject);
                //in case the enumerable is really long, let's cut off the end arbitrarily?
                const int Limit = 100;
                values = values.Take(Limit);
                if (values.Count() == Limit)
                {
                    values = values.Concat(new[] { "... (MORE THAN " + Limit + " ITEMS FOUND)" });
                }
                return "{" + string.Join(", ", values.ToArray()) + "}";
            }
            //if (value is Exception) return "the "value.GetType().Name;
            if (type.IsValueType)
            {
                return value.ToString();
            }
            if (typeof(MulticastDelegate).IsAssignableFrom(type))
            {
                return String.Empty;
            }
            return ("The" + value.GetType().Name + "'s").Replace('.', ' ');
        }
    }

}
