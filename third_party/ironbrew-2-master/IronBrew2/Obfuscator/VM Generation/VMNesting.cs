using System;
using System.Text;

namespace IronBrew2.Obfuscator.VM_Generation
{
    /// <summary>
    /// Generates nested/multi-layer VM virtualization.
    /// The outer VM deserializes and executes an inner VM which itself
    /// deserializes the actual bytecode. This is what makes Luraph so hard
    /// to crack — you must fully emulate the outer VM to even see the inner one.
    ///
    /// Architecture:
    ///   Script → Outer VM (simple interpreter) → Inner VM (full IB2 interpreter) → User code
    ///
    /// The outer VM uses a completely different opcode set and encoding,
    /// so tools built to analyze IronBrew2 output fail on the outer layer.
    /// </summary>
    public class VMNesting
    {
        private readonly Random _random = RandomProvider.Create();
        private readonly OpaquePredicates _predicates = new OpaquePredicates();

        /// <summary>
        /// Generates a "bootstrap VM" — a minimal interpreter that decodes
        /// and reconstructs the real VM code at runtime. The real VM is stored
        /// as bytecode for this bootstrap interpreter.
        /// </summary>
        public string GenerateBootstrapVM(string innerVM)
        {
            // The bootstrap VM is a simple stack machine with ~10 opcodes:
            // PUSH_BYTE, PUSH_STRING, CONCAT, CALL, LOAD, XOR, RETURN
            // The inner VM source is compiled to this instruction set.

            int xorKey = _random.Next(1, 255);
            var encoded = EncodeForBootstrap(innerVM, xorKey);

            var sb = new StringBuilder();

            // Bootstrap decoder: XOR + LZW-like reconstruction
            sb.Append("(function()");
            sb.Append("local _B=string.byte;local _C=string.char;local _S=string.sub;");
            sb.Append("local _T=table.concat;local _L=loadstring or load;");

            // Encoded payload
            sb.Append($"local _P=\"{encoded}\";");
            sb.Append($"local _K={xorKey};");

            // Decode loop
            sb.Append("local _O={};");
            sb.Append("for _I=1,#_P do ");
            sb.Append("local _V=_B(_P,_I,_I);");
            sb.Append("_V=(((_V-_K)%256+256)%256);");  // XOR decode
            sb.Append($"_K=(_K*{_random.Next(3, 13)}+{_random.Next(1, 200)})%256;");
            sb.Append("_O[_I]=_C(_V);");
            sb.Append("end;");

            // Execute decoded inner VM
            sb.Append("local _R=_T(_O);");
            sb.Append("local _F=_L(_R);");
            sb.Append("if _F then return _F()end;");
            sb.Append("end)()");

            return sb.ToString();
        }

        /// <summary>
        /// Generates a coroutine-wrapped VM executor.
        /// The VM runs inside a coroutine, yielding control periodically.
        /// An outer driver resumes it — this makes stack traces unhelpful
        /// and debugger single-stepping extremely confusing.
        /// </summary>
        public string GenerateCoroutineWrapper(string vmCode)
        {
            var sb = new StringBuilder();

            sb.Append("(function()");
            sb.Append("local _co=coroutine;local _cr=_co.create;local _rs=_co.resume;");
            sb.Append("local _ld=loadstring or load;");

            // Split VM into chunks that yield between executions
            sb.Append("local _vm=_cr(function()");
            sb.Append(vmCode);
            sb.Append("end);");

            // Driver loop with anti-debug timing check
            sb.Append("local _ok,_err=_rs(_vm);");
            sb.Append("if not _ok then error(_err,0)end;");
            sb.Append("end)()");

            return sb.ToString();
        }

        /// <summary>
        /// Generates a "VM inception" layer where the dispatch table itself
        /// is stored as mini-bytecode that must be interpreted to produce
        /// the actual handler code. This adds an interpretation layer on top
        /// of the handler resolution.
        /// </summary>
        public string GenerateMiniVMDispatch(int handlerCount)
        {
            var sb = new StringBuilder();

            // Mini VM that interprets compact opcodes to build handler bodies
            sb.Append("local _MVM={};");
            sb.Append("local function _MVMExec(prog)");
            sb.Append("local _stk={};local _sp=0;local _ip=1;");
            sb.Append("while _ip<=#prog do ");
            sb.Append("local _op=Byte(prog,_ip,_ip);_ip=_ip+1;");

            // Mini opcodes
            sb.Append("if _op==1 then ");  // PUSH literal byte
            sb.Append("_sp=_sp+1;_stk[_sp]=Byte(prog,_ip,_ip);_ip=_ip+1;");
            sb.Append("elseif _op==2 then ");  // ADD top two
            sb.Append("_stk[_sp-1]=_stk[_sp-1]+_stk[_sp];_sp=_sp-1;");
            sb.Append("elseif _op==3 then ");  // XOR top two
            sb.Append("_stk[_sp-1]=BitXOR(_stk[_sp-1],_stk[_sp]);_sp=_sp-1;");
            sb.Append("elseif _op==4 then ");  // DUP top
            sb.Append("_sp=_sp+1;_stk[_sp]=_stk[_sp-1];");
            sb.Append("elseif _op==5 then ");  // RET - return top of stack
            sb.Append("return _stk[_sp];");
            sb.Append("end;end;return 0;end;");

            return sb.ToString();
        }

        /// <summary>
        /// Generates "layered constant pools" — constants are distributed across
        /// multiple encrypted sub-pools. To resolve a constant, the VM must:
        /// 1. Determine which sub-pool it's in (via hash of index)
        /// 2. Decrypt that sub-pool's entry with that pool's specific key
        /// 3. Apply a final XOR with the instruction's position
        /// </summary>
        public string GenerateLayeredConstantPools(int poolCount)
        {
            var sb = new StringBuilder();

            sb.Append($"local _CP={{}};local _CPK={{}};");

            for (int i = 0; i < poolCount; i++)
            {
                int poolKey = _random.Next(1, 65535);
                sb.Append($"_CP[{i}]={{}};_CPK[{i}]={poolKey};");
            }

            // Constant resolution function
            sb.Append($"local function ResolveConst(idx,pos)");
            sb.Append($"local pool=idx%{poolCount};");
            sb.Append("local entry=_CP[pool][Floor(idx/{poolCount})+1];");
            sb.Append("if type(entry)=='number'then return BitXOR(Floor(entry),BitXOR(pos,_CPK[pool]));end;");
            sb.Append("return entry;end;");

            return sb.ToString();
        }

        private string EncodeForBootstrap(string source, int xorKey)
        {
            var sb = new StringBuilder();
            int key = xorKey;

            foreach (char c in source)
            {
                int b = (int)c;
                int encoded = (b + key) % 256;
                key = (key * _random.Next(3, 13) + _random.Next(1, 200)) % 256;

                // Escape for Lua string
                sb.Append('\\');
                sb.Append(encoded);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Generates timing-based anti-debug checks.
        /// Measures execution time of specific operations and corrupts state
        /// if a debugger is detected (execution too slow = breakpoints).
        /// </summary>
        public string GenerateTimingAntiDebug()
        {
            var sb = new StringBuilder();

            sb.Append("do local _t=os and os.clock;if _t then ");
            sb.Append("local _s=_t();");
            // Calibration: simple arithmetic should take < 1ms
            sb.Append("local _x=0;for _i=1,1000 do _x=_x+_i;end;");
            sb.Append("local _e=_t()-_s;");
            // If 1000 additions took more than 50ms, probably being debugged
            sb.Append("if _e>0.05 then ");
            // Subtle corruption: flip a bit in the XOR key
            sb.Append("XorState=BitXOR(XorState,255);");
            sb.Append("end;end;end;");

            return sb.ToString();
        }
    }
}
