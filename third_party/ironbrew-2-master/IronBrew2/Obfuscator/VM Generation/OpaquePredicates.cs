using System;
using System.Collections.Generic;

namespace IronBrew2.Obfuscator.VM_Generation
{
    /// <summary>
    /// Generates opaque predicates - expressions that always evaluate to a known value
    /// but are difficult to prove true/false via static analysis.
    /// Luraph-level: uses algebraic identities, modular arithmetic, and hash chains.
    /// </summary>
    public class OpaquePredicates
    {
        private readonly Random _random = RandomProvider.Create();

        // Always-true predicates (harder to resolve statically than x*x+x % 2 == 0)
        public string AlwaysTrue(string varName = "InstrPoint")
        {
            return _generators[_random.Next(_generators.Length)](varName);
        }

        // Always-false predicates
        public string AlwaysFalse(string varName = "InstrPoint")
        {
            // Negate an always-true
            var pred = AlwaysTrue(varName);
            return $"not({pred})";
        }

        // Generates a value that equals the expected value but through complex computation
        public string OpaqueConstant(int value)
        {
            return _constantGenerators[_random.Next(_constantGenerators.Length)](value);
        }

        private readonly Func<string, string>[] _generators;
        private readonly Func<int, string>[] _constantGenerators;

        public OpaquePredicates()
        {
            _generators = new Func<string, string>[]
            {
                // x^2 + x is always even (x*(x+1) always has factor 2)
                v => $"(({v}*{v}+{v})%2==0)",

                // x^2 >= 0 for all real x
                v => $"({v}*{v}>=-1)",

                // (x | 1) is always odd
                v => {
                    // Implement bitwise OR via arithmetic for Lua 5.1
                    return $"(({v}-({v}%2)+1)%2==1)";
                },

                // Floor(x) <= x always
                v => $"(Floor({v}/{_random.Next(2,17)})*{_random.Next(2,17)}<={v}*{_random.Next(2,17)}+{_random.Next(1,100)})",

                // Fermat: a^2 + b^2 >= 2*a*b (AM-GM inequality)
                v => {
                    int a = _random.Next(1, 50);
                    int b = _random.Next(1, 50);
                    return $"({a}*{a}+{b}*{b}>={2*a*b})";
                },

                // |sin(x)| <= 1, approximated as modular identity
                v => {
                    int mod = _random.Next(3, 13);
                    // (x % mod) < mod is always true for positive mod
                    return $"(({v}%{mod})<{mod})";
                },

                // n*(n-1) is always even
                v => $"(({v}*({v}-1))%2==0)",

                // Triangular number identity: 2*T(n) = n*(n+1)
                v => {
                    int k = _random.Next(2, 8);
                    return $"(({v}*{v}+{v})==(({v}+1)*{v}))";
                },

                // Polynomial that's always non-negative: (x-a)^2 + b where b > 0
                v => {
                    int a = _random.Next(1, 100);
                    int b = _random.Next(1, 50);
                    return $"(({v}-{a})*({v}-{a})+{b}>0)";
                },

                // Bitwise identity: x XOR x = 0
                v => {
                    // In Lua 5.1 without bit ops, use modular: (x - x) == 0
                    int salt = _random.Next(100, 9999);
                    return $"(({v}+{salt}-{v}-{salt})==0)";
                },

                // 3*x^2 + 2 is always > 0
                v => {
                    int c1 = _random.Next(2, 7);
                    int c2 = _random.Next(1, 20);
                    return $"({c1}*({v}%{_random.Next(100,999)})*({v}%{_random.Next(100,999)})+{c2}>0)";
                },

                // (x % n + n) % n == x % n for positive n
                v => {
                    int n = _random.Next(7, 127);
                    return $"(({v}%{n}+{n})%{n}=={v}%{n})";
                },
            };

            _constantGenerators = new Func<int, string>[]
            {
                // value via polynomial evaluation
                val => {
                    int x = _random.Next(2, 10);
                    // Solve: a*x + b = value
                    int b = _random.Next(-50, 50);
                    // We want a*x + b = val, so a = (val - b) / x ... approximate with floor
                    // Instead, encode as: val + 0 through arithmetic
                    int mask = _random.Next(1000, 9999);
                    return $"(BitXOR({val ^ mask},{mask}))";
                },

                // value via modular inverse
                val => {
                    int add = _random.Next(100, 9999);
                    return $"({val + add}-{add})";
                },

                // value via multiplication/division
                val => {
                    int factor = _random.Next(2, 7);
                    return $"Floor({val * factor}/{factor})";
                },

                // value via XOR chain
                val => {
                    int k1 = _random.Next(1000, 99999);
                    int k2 = val ^ k1;
                    return $"(BitXOR({k1},{k2}))";
                },
            };
        }

        /// <summary>
        /// Generates a conditional block that will never execute (dead code injection).
        /// The predicate is opaque — always false but statically hard to prove.
        /// </summary>
        public string DeadBlock(string deadCode)
        {
            return $"if {AlwaysFalse()} then {deadCode} end;";
        }

        /// <summary>
        /// Generates a conditional block that will always execute.
        /// Wraps real code in an opaque-true branch.
        /// </summary>
        public string LiveBlock(string realCode, string deadCode)
        {
            if (_random.Next(2) == 0)
                return $"if {AlwaysTrue()} then {realCode} else {deadCode} end;";
            else
                return $"if {AlwaysFalse()} then {deadCode} else {realCode} end;";
        }

        /// <summary>
        /// Generates an opaque dispatcher index transform.
        /// Returns code that computes targetIndex from the real enum,
        /// making handler lookup indirect.
        /// </summary>
        public string OpaqueIndexTransform(string enumVar, int tableSize)
        {
            // XOR with a key then modulo table size
            int key = _random.Next(1, 65535);
            return $"(BitXOR({enumVar},{key})%{tableSize})";
        }
    }
}
