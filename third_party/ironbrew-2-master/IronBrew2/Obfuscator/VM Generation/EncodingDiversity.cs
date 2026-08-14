using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IronBrew2.Obfuscator.VM_Generation
{
    /// <summary>
    /// Adds instruction encoding diversity to the VM bytecode.
    /// Each chunk can use a different encoding scheme for its instructions,
    /// forcing deobfuscators to solve per-chunk which scheme is active.
    ///
    /// Techniques:
    /// 1. Per-chunk XOR key rotation (already partially done, enhanced here)
    /// 2. Mixed-radix constant encoding (numbers encoded in varying bases)
    /// 3. Polymorphic string encoding (multiple decrypt routines per script)
    /// 4. Instruction field scrambling with per-chunk permutation tables
    /// </summary>
    public class EncodingDiversity
    {
        private readonly Random _random = RandomProvider.Create();

        /// <summary>
        /// Generates multiple string decryption routines with different algorithms.
        /// The VM picks one per constant pool entry based on an index selector,
        /// making bulk string recovery require solving which decoder was used.
        /// </summary>
        public string GeneratePolymorphicStringDecrypt(int variants)
        {
            var sb = new StringBuilder();

            sb.Append("local StrDec={};");

            for (int i = 0; i < variants; i++)
            {
                int key = _random.Next(1, 255);
                int shift = _random.Next(1, 7);
                int xorKey2 = _random.Next(1, 255);

                switch (i % 4)
                {
                    case 0:
                        // XOR with rotating key
                        sb.Append($"StrDec[{i}]=function(s)local o={{}};local k={key};");
                        sb.Append("for i=1,#s do local b=Byte(s,i,i);");
                        sb.Append($"o[i]=Char(BitXOR(b,k));k=(k*{_random.Next(3,17)}+{_random.Next(1,255)})%256;end;");
                        sb.Append("return Concat(o);end;");
                        break;

                    case 1:
                        // Caesar cipher with variable shift
                        sb.Append($"StrDec[{i}]=function(s)local o={{}};");
                        sb.Append($"for i=1,#s do local b=Byte(s,i,i);o[i]=Char((b-{shift}+256)%256);end;");
                        sb.Append("return Concat(o);end;");
                        break;

                    case 2:
                        // Double XOR with position-dependent key
                        sb.Append($"StrDec[{i}]=function(s)local o={{}};");
                        sb.Append($"for i=1,#s do local b=Byte(s,i,i);o[i]=Char(BitXOR(BitXOR(b,{key}),(i*{xorKey2})%256));end;");
                        sb.Append("return Concat(o);end;");
                        break;

                    case 3:
                        // Nibble swap + XOR
                        sb.Append($"StrDec[{i}]=function(s)local o={{}};");
                        sb.Append("for i=1,#s do local b=Byte(s,i,i);");
                        sb.Append($"b=BitXOR(b,{key});");
                        sb.Append("local hi=Floor(b/16);local lo=b%16;");
                        sb.Append("o[i]=Char(lo*16+hi);end;");
                        sb.Append("return Concat(o);end;");
                        break;
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Generates a number encoding/decoding pair.
        /// Numbers in the constant pool are transformed through a bijective function
        /// and decoded at runtime. Different functions per chunk.
        /// </summary>
        public (string decoderDef, Func<double, double> encoder) GenerateNumberCodec()
        {
            int strategy = _random.Next(4);
            int key1 = _random.Next(1, 65535);
            int key2 = _random.Next(1, 65535);
            double addend = _random.Next(-9999, 9999);
            int multiplier = _random.Next(2, 7) * 2 + 1; // odd multiplier for invertibility

            switch (strategy)
            {
                case 0:
                    // XOR encoding (only works for integers, pass-through for floats)
                    return (
                        $"local function NumDec(n)if n~=Floor(n)then return n end;return BitXOR(Floor(n),{key1});end;",
                        n => n != Math.Floor(n) ? n : (int)n ^ key1
                    );

                case 1:
                    // Additive shift
                    return (
                        $"local function NumDec(n)return n-({addend});end;",
                        n => n + addend
                    );

                case 2:
                    // Multiplicative (odd multiplier mod 2^32 has inverse)
                    return (
                        $"local function NumDec(n)if n~=Floor(n)then return n end;return(n-{key2})/{multiplier};end;",
                        n => n != Math.Floor(n) ? n : n * multiplier + key2
                    );

                default:
                    // Combined: XOR then shift
                    return (
                        $"local function NumDec(n)if n~=Floor(n)then return n end;return BitXOR(Floor(n)-{(int)addend},{key1});end;",
                        n => n != Math.Floor(n) ? n : ((int)n ^ key1) + addend
                    );
            }
        }

        /// <summary>
        /// Generates instruction field reordering.
        /// Instead of always encoding as [ENUM, A, B, C], each chunk can use
        /// a permutation like [B, ENUM, C, A]. The deserializer is told the permutation
        /// via the constant pool header.
        /// </summary>
        public (int[] permutation, string decoderSnippet) GenerateFieldPermutation()
        {
            // Original order: 0=ENUM, 1=A, 2=B, 3=C
            int[] perm = { 0, 1, 2, 3 };
            // Fisher-Yates shuffle
            for (int i = perm.Length - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                (perm[i], perm[j]) = (perm[j], perm[i]);
            }

            // Generate the inverse permutation for decoding
            int[] inv = new int[4];
            for (int i = 0; i < 4; i++)
                inv[perm[i]] = i;

            // The decoder reads fields in permuted order and reconstructs the original
            var sb = new StringBuilder();
            sb.Append("local function ReorderInst(raw)return{");
            sb.Append($"raw[{inv[0] + 1}],raw[{inv[1] + 1}],raw[{inv[2] + 1}],raw[{inv[3] + 1}]");
            sb.Append("};end;");

            return (perm, sb.ToString());
        }

        /// <summary>
        /// Generates a "handler key derivation" system where the opcode index
        /// used for dispatch is derived from both the instruction's enum field
        /// and the previous instruction's enum, creating data-flow dependency.
        /// This makes it impossible to resolve handlers without full emulation.
        /// </summary>
        public string GenerateChainedDispatchKey()
        {
            int initialPrev = _random.Next(0, 255);
            int mixConstant = _random.Next(1, 65535);

            var sb = new StringBuilder();
            sb.Append($"local PrevEnum={initialPrev};");
            sb.Append("local function DeriveEnum(raw)");
            sb.Append($"local derived=BitXOR(raw,PrevEnum*{mixConstant}%65536);");
            sb.Append("PrevEnum=raw;return derived;end;");

            return sb.ToString();
        }
    }
}
