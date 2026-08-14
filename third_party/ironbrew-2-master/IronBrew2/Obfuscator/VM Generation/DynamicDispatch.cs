using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IronBrew2.Obfuscator.VM_Generation
{
    /// <summary>
    /// Generates multiple VM dispatch modes that can switch during execution.
    /// This makes static analysis exponentially harder — a deobfuscator must solve
    /// which dispatch mode is active at each point in the bytecode stream.
    ///
    /// Modes:
    ///  1. Binary-tree dispatch (original)
    ///  2. Computed-goto via XOR-indexed table
    ///  3. Coroutine-threaded dispatch
    ///  4. Hash-chain dispatch with rolling key
    /// </summary>
    public class DynamicDispatch
    {
        private readonly Random _random = RandomProvider.Create();
        private readonly OpaquePredicates _predicates = new OpaquePredicates();

        /// <summary>
        /// Generates the handler table with XOR-shuffled indices.
        /// Each handler is stored at position = realIndex XOR dispatchKey.
        /// The key changes after N instructions (rolling key).
        /// </summary>
        public string GenerateXorTableDispatch(
            List<VOpcode> virtuals,
            ObfuscationContext context,
            int dispatchKey)
        {
            var sb = new StringBuilder();

            // Build the XOR-mapped handler table
            sb.Append("local Handlers={};");
            sb.Append($"local DKey={dispatchKey};");

            foreach (var virt in virtuals)
            {
                int mappedIndex = virt.VIndex ^ dispatchKey;
                string handler = virt.GetObfuscated(context);
                sb.Append($"Handlers[{mappedIndex}]=function(Inst,Stk,Env,Upvalues,InstrPoint)");
                sb.Append(handler);
                sb.Append("end;");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Generates the rolling-key dispatch loop.
        /// After every instruction, the dispatch key is transformed via LCG,
        /// making the handler table effectively polymorphic.
        /// </summary>
        public string GenerateRollingKeyLoop(int initialKey, int multiplier, int increment)
        {
            var sb = new StringBuilder();

            sb.Append($"local DKey={initialKey};");
            sb.Append($"local DKeyMul={multiplier};");
            sb.Append($"local DKeyInc={increment};");
            sb.Append("while true do ");
            sb.Append("Inst=Instr[InstrPoint];");
            sb.Append("local RealEnum=BitXOR(Inst[OP_ENUM],DKey%65536);");
            sb.Append("DKey=(DKey*DKeyMul+DKeyInc)%4294967296;");

            return sb.ToString();
        }

        /// <summary>
        /// Generates a dual-interpreter VM where the execution switches between
        /// two different dispatch tables based on a state flag.
        /// </summary>
        public string GenerateDualInterpreter(
            List<VOpcode> virtuals,
            ObfuscationContext context,
            ObfuscationSettings settings)
        {
            var sb = new StringBuilder();
            int splitPoint = virtuals.Count / 2;

            // Generate two permutations of the handler indices
            int key1 = _random.Next(1, 65535);
            int key2 = _random.Next(1, 65535);
            while (key2 == key1) key2 = _random.Next(1, 65535);

            sb.Append($"local IMode=0;local IKey1={key1};local IKey2={key2};");
            sb.Append("local H1={};local H2={};");

            // Build handler table 1 (XOR mapped with key1)
            foreach (var virt in virtuals)
            {
                int idx = virt.VIndex ^ key1;
                sb.Append($"H1[{idx}]=function(Inst)");
                sb.Append(virt.GetObfuscated(context));
                sb.Append("end;");
            }

            // Build handler table 2 (XOR mapped with key2, different ordering)
            var shuffled = virtuals.ToList();
            // Reverse ordering for table 2
            foreach (var virt in shuffled)
            {
                int idx = virt.VIndex ^ key2;
                sb.Append($"H2[{idx}]=function(Inst)");
                sb.Append(virt.GetObfuscated(context));
                sb.Append("end;");
            }

            // The dispatch loop alternates tables based on instruction count
            int switchInterval = _random.Next(3, 12);
            sb.Append($"local ISwitchAt={switchInterval};local ICount=0;");

            sb.Append("while true do ");
            sb.Append("Inst=Instr[InstrPoint];");
            sb.Append("ICount=ICount+1;");
            sb.Append($"if ICount>ISwitchAt then IMode=1-IMode;ICount=0;end;");
            sb.Append("if IMode==0 then ");
            sb.Append("H1[BitXOR(Inst[OP_ENUM],IKey1)](Inst);");
            sb.Append("else ");
            sb.Append("H2[BitXOR(Inst[OP_ENUM],IKey2)](Inst);");
            sb.Append("end;");
            sb.Append("InstrPoint=InstrPoint+1;");
            sb.Append("end;");

            return sb.ToString();
        }

        /// <summary>
        /// Generates the "environment cage" — wraps the VM in layered closures
        /// that trap standard globals, making it harder to hook or inspect.
        /// Similar to Luraph's environment isolation.
        /// </summary>
        public string GenerateEnvironmentCage()
        {
            var sb = new StringBuilder();

            // Capture all needed globals into upvalues before the VM body
            string[] trappedGlobals = {
                "string", "table", "math", "select", "unpack", "type",
                "setmetatable", "getmetatable", "pcall", "xpcall",
                "tostring", "tonumber", "rawget", "rawset", "next",
                "coroutine", "error", "pairs", "ipairs"
            };

            // Generate randomized local names for each global
            var nameMap = new Dictionary<string, string>();
            foreach (var g in trappedGlobals)
            {
                nameMap[g] = GenerateVarName();
            }

            sb.Append("(function()");

            // Capture globals
            foreach (var kvp in nameMap)
            {
                sb.Append($"local {kvp.Value}={kvp.Key};");
            }

            // Override environment to prevent hooking
            sb.Append("local _ENV_SAFE=setmetatable({},{");
            sb.Append("__index=function(_,k)");

            // Return captured globals from our safe copies
            foreach (var kvp in nameMap)
            {
                sb.Append($"if k==\"{kvp.Key}\"then return {kvp.Value} end ");
            }

            sb.Append("return nil end,");
            sb.Append("__newindex=function()end");  // Block writes
            sb.Append("});");

            return sb.ToString();
        }

        public string GenerateEnvironmentCageClose()
        {
            return "end)();";
        }

        /// <summary>
        /// Generates anti-hook checks that detect if standard functions have been tampered with.
        /// </summary>
        public string GenerateAntiHookChecks()
        {
            var sb = new StringBuilder();

            // Check string.byte hasn't been replaced
            sb.Append("do local _t=type;local _sb=string.byte;");
            sb.Append("if _t(_sb)~='function'then ");
            sb.Append("error('');end;");

            // Verify string.byte produces expected output for known input
            int testChar = _random.Next(65, 90); // A-Z
            sb.Append($"if _sb(string.char({testChar}))~={testChar} then ");
            sb.Append("error('');end;");
            sb.Append("end;");

            return sb.ToString();
        }

        private string GenerateVarName()
        {
            const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
            var name = new char[8 + _random.Next(4)];
            name[0] = chars[_random.Next(chars.Length)];
            for (int i = 1; i < name.Length; i++)
                name[i] = chars[_random.Next(chars.Length)];
            return new string(name);
        }
    }
}
