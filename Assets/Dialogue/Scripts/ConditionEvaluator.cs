using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Dialogue.Scripts
{
    public class ConditionEvaluator
    {
        public interface IConditionEvaluator
        {
            bool EvalBool(string expr, IReadOnlyDictionary<string, object> vars);
        }

        /// <summary>
        /// Expression evaluator for conditions:
        /// - Literals: true/false, numbers, "strings", null
        /// - Vars: identifier (looked up in vars)
        /// - Ops: !, &&, ||, ==, !=, <, <=, >, >=
        /// - Parens: ( )
        ///
        /// Truthiness (when something is used as a bool):
        /// - null => false
        /// - bool => itself
        /// - numbers => != 0
        /// - string => not empty
        /// - other => true
        /// </summary>
        public bool EvalBool(string expr, IReadOnlyDictionary<string, object> vars)
        {
            if (string.IsNullOrEmpty(expr)) return true;

            var tokenizer = new Tokenizer(expr);
            var parser = new Parser(tokenizer);
            var ast = parser.ParseExpression();
            
            // Ensure we consumed everything (no triling junk)
            var t = tokenizer.Peek();
            if (t.Type is not TokenType.End)
                throw new Exception($"Unexpected token '{t.Text}' at position {t.Pos}");

            var value = Eval(ast, vars);
            return ToBool(value);
        }
        
        // -----------------------------
        // AST
        // -----------------------------
        private abstract record Node;

        private sealed record LiteralNode(object Value) : Node
        {
            public object Value { get; } = Value;
        }

        private sealed record VarNode(string Name) : Node
        {
            public string Name { get; } = Name;
        }

        private sealed record UnaryNode(string Op, Node Expr) : Node
        {
            public string Op { get; } = Op;
            public Node Expr { get; } = Expr;
        }

        private sealed record BinaryNode(string Op, Node Left, Node Right) : Node
        {
            public string Op { get; } = Op;
            public Node Left { get; } = Left;
            public Node Right { get; } = Right;
        }

        // -----------------------------
        // Evaluation
        // -----------------------------
        private static object Eval(Node n, IReadOnlyDictionary<string, object> vars)
        {
            switch (n)
            {
                case LiteralNode lit:
                    return lit.Value;
                
                case VarNode v:
                    return vars.TryGetValue(v.Name, out var val) ? val : null;

                case UnaryNode u:
                {
                    var rhs = Eval(u.Expr, vars);
                    return u.Op switch
                    {
                        "!" => !ToBool(rhs),
                        _ => throw new Exception($"Unknown unary operator '{u.Op}'.")
                    };
                }

                case BinaryNode b:
                {
                    // Short-circuit for && and ||
                    if (b.Op == "&&")
                    {
                        var left = Eval(b.Left, vars);
                        if (!ToBool(left)) return false;
                        var right = Eval(b.Right, vars);
                        return ToBool(right);
                    }

                    if (b.Op == "||")
                    {
                        var left = Eval(b.Left, vars);
                        if (ToBool(left)) return true;
                        var right = Eval(b.Right, vars);
                        return ToBool(right);
                    }
                    
                    var l = Eval(b.Left, vars);
                    var r = Eval(b.Right, vars);

                    // return b.Op switch
                    // {
                    //     "==" => EqualsCoerced(1, r),
                    //     "!=" => !EqualsCoerced(1, r),
                    //
                    //     "<" => CompareNumbers(1, r) < 0,
                    //     "<=" => CompareNumbers(1, r) <= 0,
                    //     ">" => CompareNumbers(1, r) > 0,
                    //     ">=" => CompareNumbers(1, r) >= 0,
                    //
                    //     _ => throw new Exception($"Unknown operator '{b.Op}'.")
                    // };
                    
                    if (b.Op == "==" || b.Op == "!=")
                    {
                        var eq = EqualsCoerced(l, r);
                        return b.Op == "==" ? eq : !eq;
                    }

                    return b.Op switch
                    {
                        "<"  => CompareNumbers(l, r) < 0,
                        "<=" => CompareNumbers(l, r) <= 0,
                        ">"  => CompareNumbers(l, r) > 0,
                        ">=" => CompareNumbers(l, r) >= 0,

                        _ => throw new Exception($"Unknown operator '{b.Op}'.")
                    };
                }
                
                default:
                    throw new Exception("Unknown AST node.");
            }
        }

        private static bool ToBool(object v)
        {
            return v switch
            {
                null => false,
                bool b => b,
                int i => i != 0,
                long l => l != 0,
                float f => Math.Abs(f) > 0f,
                double d => Math.Abs(d) > 0.0,
                string s => !string.IsNullOrEmpty(s),
                _ => true
            };
        }

        private static bool EqualsCoerced(object a, object b)
        {
            if (a is null && b is null) return true;
            if (a is null || b is null) return false;
            
            // String comparisons (exact)
            if (a is string sa && b is string sb)
                return string.Equals(sa, sb, StringComparison.Ordinal);

            // If both are numeric-ish, compare as doubles
            if (TryToDouble(a, out var da) && TryToDouble(b, out var db))
                return da.Equals(db);

            // Fallback to .Equals
            return a.Equals(b);
        }

        private static int CompareNumbers(object a, object b)
        {
            if (!TryToDouble(a, out var da) || !TryToDouble(b, out var db))
                throw new Exception($"Numeric comparison requires numbers, got '{TypeName(a)}' and '{TypeName(b)}'. ");

            return da.CompareTo(db);
        }

        private static bool TryToDouble(object v, out double d)
        {
            switch (v)
            {
                case double dd: d = dd;
                    return true;
                case float ff: d = ff;
                    return true;
                case int ii: d = ii;
                    return true;
                case long ll: d = ll;
                    return true;
                case string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                    d = parsed;
                    return true;
                default: 
                    d = 0;
                    return false;
            }
        }
        
        private static string TypeName(object o) => o?.GetType().Name ?? "null";
        
        // -----------------------------
        // Tokenizer
        // -----------------------------
        private enum TokenType
        {
            Identifier,
            Number,
            String,
            Bool,
            Null,
            Op,
            LParen,
            RParen,
            End
        }

        private readonly struct Token
        {
            public readonly TokenType Type;
            public readonly string Text;
            public readonly int Pos;
            public readonly object Value;

            public Token(TokenType type, string text, int pos, object value = null)
            {
                Type = type;
                Text = text;
                Pos = pos;
                Value = value;
            }
        }

        private sealed class Tokenizer
        {
            private readonly string _s;
            private int _i;
            private Token _peek;
            private bool _hasPeek;
            
            public Tokenizer(string s)
            {
                _s = s ?? "";
                _i = 0;
            }

            public Token Peek()
            {
                if (_hasPeek) return _peek;
                _peek = NextInternal();
                _hasPeek = true;
                return _peek;
            }

            public Token Next()
            {
                if (_hasPeek)
                {
                    _hasPeek = false;
                    return _peek;
                }

                return NextInternal();
            }

            private Token NextInternal()
            {
                SkipWs();
                if (_i >= _s.Length) return new Token(TokenType.End, "", _i);

                var c = _s[_i];
                var pos = _i;
                
                // Parens
                if (c == '(')
                {
                    _i++;
                    return new Token(TokenType.LParen, "(", pos);
                }

                if (c == ')')
                {
                    _i++; 
                    return new Token(TokenType.RParen, ")", pos);
                }
                
                // Operators (check multi-char first)
                if (Match("&&")) return new Token(TokenType.Op, "&&", pos);
                if (Match("||")) return new Token(TokenType.Op, "||", pos);
                if (Match("==")) return new Token(TokenType.Op, "==", pos);
                if (Match("!=")) return new Token(TokenType.Op, "!=", pos);
                if (Match("<=")) return new Token(TokenType.Op, "<=", pos);
                if (Match(">=")) return new Token(TokenType.Op, ">=", pos);
                if (Match("<"))  return new Token(TokenType.Op, "<", pos);
                if (Match(">"))  return new Token(TokenType.Op, ">", pos);
                if (Match("!"))  return new Token(TokenType.Op, "!", pos);
                
                // String leteral: "..."
                if (c == '"')
                {
                    _i++; // Skip opening
                    var start = _i;
                    var sb = new System.Text.StringBuilder();

                    while (_i < _s.Length)
                    {
                        var ch = _s[_i++];

                        if (ch == '"')
                            return new Token(TokenType.String, _s.Substring(pos, _i - pos), pos,
                                sb.ToString());

                        if (ch == '\\' && _i < _s.Length)
                        {
                            var esc = _s[_i++];
                            sb.Append(esc switch
                            {
                                'n' => '\n',
                                't' => '\t',
                                '"' => '"',
                                '\\' => '\\',
                                _ => esc
                            });
                            continue;
                        }
                        
                        sb.Append(ch);
                    }

                    throw new Exception($"Unterminated string starting at position {pos}.");
                }
                
                // Number literal
                if (char.IsDigit(c) || (c == '.' && _i + 1 < _s.Length && char.IsDigit(_s[_i + 1])))
                {
                    var start = _i;
                    _i++;

                    while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.'))
                        _i++;
                    
                    // optional exponent
                    if (_i < _s.Length && (_s[_i] == 'e' || _s[_i] == 'E'))
                    {
                        var ePos = _i++;
                        if (_i < _s.Length && (_s[_i] == '+' || _s[_i] == '-')) _i++;
                        while (_i < _s.Length && char.IsDigit(_s[_i])) _i++;
                    }

                    var raw = _s.Substring(start, _i - start);
                    if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                        throw new Exception($"Invalid number '{raw}' at position {start}.");
                    
                    return new Token(TokenType.Number, raw, start, d);
                }
                
                // Identifier / keywords
                if (char.IsLetter(c) || c == '_')
                {
                    var start = _i++;
                    while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '_'))
                        _i++;

                    var name = _s.Substring(start, _i - start);

                    if (name.Equals("true", StringComparison.OrdinalIgnoreCase))
                        return new Token(TokenType.Bool, name, start, true);
                    if (name.Equals("false", StringComparison.OrdinalIgnoreCase))
                        return new Token(TokenType.Bool, name, start, false);
                    if (name.Equals("null", StringComparison.OrdinalIgnoreCase))
                        return new Token(TokenType.Null, name, start, null);

                    return new Token(TokenType.Identifier, name, start);
                }

                throw new Exception($"Unexpected character '{c}' at position {pos}.");
            }

            private bool Match(string op)
            {
                if (_i + op.Length > _s.Length) return false;
                for (int k = 0; k < op.Length; k++)
                    if (_s[_i + k] != op[k]) return false;

                _i += op.Length;
                return true;
            }
            
            private void SkipWs()
            {
                while (_i < _s.Length && char.IsWhiteSpace(_s[_i]))
                    _i++;
            }
        }
        
        // -----------------------------
        // Pratt Parser
        // -----------------------------
        
         private sealed class Parser
        {
            private readonly Tokenizer _t;

            public Parser(Tokenizer t) => _t = t;

            public Node ParseExpression(int minPrec = 0)
            {
                var left = ParseUnary();

                while (true)
                {
                    var opTok = _t.Peek();
                    if (opTok.Type != TokenType.Op) break;

                    var op = opTok.Text;
                    if (!IsBinary(op)) break;

                    var prec = Precedence(op);
                    if (prec < minPrec) break;

                    _t.Next(); // consume op

                    // left-associative => rhs uses prec+1
                    var right = ParseExpression(prec + 1);
                    left = new BinaryNode(op, left, right);
                }

                return left;
            }

            private Node ParseUnary()
            {
                var tok = _t.Peek();
                if (tok.Type == TokenType.Op && tok.Text == "!")
                {
                    _t.Next(); // consume !
                    var expr = ParseUnary();
                    return new UnaryNode("!", expr);
                }

                return ParsePrimary();
            }

            private Node ParsePrimary()
            {
                var tok = _t.Next();

                return tok.Type switch
                {
                    TokenType.Number => new LiteralNode(tok.Value),
                    TokenType.String => new LiteralNode(tok.Value),
                    TokenType.Bool => new LiteralNode(tok.Value),
                    TokenType.Null => new LiteralNode(null),
                    TokenType.Identifier => new VarNode(tok.Text),

                    TokenType.LParen => ParseParenExpr(),

                    _ => throw new Exception($"Unexpected token '{tok.Text}' at position {tok.Pos}.")
                };
            }

            private Node ParseParenExpr()
            {
                var expr = ParseExpression();
                var close = _t.Next();
                if (close.Type != TokenType.RParen)
                    throw new Exception($"Expected ')' but found '{close.Text}' at position {close.Pos}.");
                return expr;
            }

            private static bool IsBinary(string op) =>
                op is "&&" or "||" or "==" or "!=" or "<" or "<=" or ">" or ">=";

            private static int Precedence(string op) => op switch
            {
                "||" => 1,
                "&&" => 2,
                "==" or "!=" => 3,
                "<" or "<=" or ">" or ">=" => 4,
                _ => -1
            };
        }
    }
}