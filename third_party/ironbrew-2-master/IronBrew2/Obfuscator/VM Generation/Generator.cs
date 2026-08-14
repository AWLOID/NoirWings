using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using IronBrew2.Bytecode_Library.Bytecode;
using IronBrew2.Bytecode_Library.IR;
using IronBrew2.Extensions;
using IronBrew2.Obfuscator.Opcodes;

namespace IronBrew2.Obfuscator.VM_Generation
{
	public class Generator
	{
		private ObfuscationContext _context;

		public Generator(ObfuscationContext context) =>
			_context = context;

		public bool IsUsed(Chunk chunk, VOpcode virt)
		{
			bool isUsed = false;
			foreach (Instruction ins in chunk.Instructions)
				if (virt.IsInstruction(ins))
				{
					if (!_context.InstructionMapping.ContainsKey(ins.OpCode))
						_context.InstructionMapping.Add(ins.OpCode, virt);

					ins.CustomData = new CustomInstructionData {Opcode = virt};
					isUsed = true;
				}

			foreach (Chunk sChunk in chunk.Functions)
				isUsed |= IsUsed(sChunk, virt);

			return isUsed;
		}

		public static List<int> Compress(byte[] uncompressed)
		{
			// build the dictionary
			Dictionary<string, int> dictionary = new Dictionary<string, int>();
			for (int i = 0; i < 256; i++)
				dictionary.Add(((char)i).ToString(), i);

			string    w          = string.Empty;
			List<int> compressed = new List<int>();

			foreach (byte b in uncompressed)
			{
				string wc = w + (char)b;
				if (dictionary.ContainsKey(wc))
					w = wc;

				else
				{
					// write w to output
					compressed.Add(dictionary[w]);
					// wc is a new sequence; add it to the dictionary
					dictionary.Add(wc, dictionary.Count);
					w = ((char) b).ToString();
				}
			}

			// write remaining output if necessary
			if (!string.IsNullOrEmpty(w))
				compressed.Add(dictionary[w]);

			return compressed;
		}

		public static string ToBase36(ulong value)
        {
            const string base36 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            var sb = new StringBuilder(13);
            do
            {
                sb.Insert(0, base36[(byte)(value % 36)]);
                value /= 36;
            } while (value != 0);
            return sb.ToString();
        }

		public static string CompressedToString(List<int> compressed)
		{
			StringBuilder sb = new StringBuilder();
			foreach (int i in compressed)
			{
				string n = ToBase36((ulong)i);

				sb.Append(ToBase36((ulong)n.Length));
				sb.Append(n);
			}

			return sb.ToString();
		}

		public static uint ComputePayloadChecksum(byte[] payload)
		{
			uint a = 1;
			uint b = 0;
			foreach (byte value in payload)
			{
				a = (a + value) % 65521;
				b = (b + a) % 65521;
			}

			return (b << 16) | a;
		}

		public List<OpMutated> GenerateMutations(List<VOpcode> opcodes)
		{
			Random r = RandomProvider.Create();
			List<OpMutated> mutated = new List<OpMutated>();

			foreach (VOpcode opc in opcodes)
			{
				if (opc is OpSuperOperator)
					continue;

				for (int i = 0; i < r.Next(35, 50); i++)
				{
					int[] rand = {0, 1, 2};
					rand.Shuffle();

					OpMutated mut = new OpMutated();

					mut.Registers = rand;
					mut.Mutated = opc;

					mutated.Add(mut);
				}
			}

			mutated.Shuffle();
			return mutated;
		}

		public void FoldMutations(List<OpMutated> mutations, HashSet<OpMutated> used, Chunk chunk)
		{
			bool[] skip = new bool[chunk.Instructions.Count + 1];

			for (int i = 0; i < chunk.Instructions.Count; i++)
			{
				Instruction opc = chunk.Instructions[i];

				switch (opc.OpCode)
				{
					case Opcode.Closure:
						for (int j = 1; j <= ((Chunk) opc.RefOperands[0]).UpvalueCount; j++)
							skip[i + j] = true;

						break;
				}
			}

			for (int i = 0; i < chunk.Instructions.Count; i++)
			{
				if (skip[i])
					continue;

				Instruction opc = chunk.Instructions[i];
				CustomInstructionData data = opc.CustomData;

				foreach (OpMutated mut in mutations)
					if (data.Opcode == mut.Mutated && data.WrittenOpcode == null)
					{
						if (!used.Contains(mut))
							used.Add(mut);

						data.Opcode = mut;
						break;
					}
			}

			foreach (Chunk _c in chunk.Functions)
				FoldMutations(mutations, used, _c);
		}

		public List<OpSuperOperator> GenerateSuperOperators(Chunk chunk, int maxSize, int minSize = 5)
		{
			List<OpSuperOperator> results = new List<OpSuperOperator>();
			bool[] skip = new bool[chunk.Instructions.Count + 1];

			for (int i = 0; i < chunk.Instructions.Count - 1; i++)
			{
				switch (chunk.Instructions[i].OpCode)
				{
					case Opcode.Closure:
					{
						skip[i] = true;
						for (int j = 0; j < ((Chunk) chunk.Instructions[i].RefOperands[0]).UpvalueCount; j++)
							skip[i + j + 1] = true;

						break;
					}

					case Opcode.Eq:
					case Opcode.Lt:
					case Opcode.Le:
					case Opcode.Test:
					case Opcode.TestSet:
					case Opcode.TForLoop:
					case Opcode.SetList:
					case Opcode.LoadBool when chunk.Instructions[i].C != 0:
						skip[i + 1] = true;
						break;

					case Opcode.ForLoop:
					case Opcode.ForPrep:
					case Opcode.Jmp:
						chunk.Instructions[i].UpdateRegisters();

						skip[i + 1] = true;
						skip[i + chunk.Instructions[i].B + 1] = true;
						break;
				}

				if (chunk.Instructions[i].CustomData.WrittenOpcode is OpSuperOperator su && su.SubOpcodes != null)
					for (int j = 0; j < su.SubOpcodes.Length; j++)
						skip[i + j] = true;
			}

			int c = 0;
			while (c < chunk.Instructions.Count)
			{
				int targetCount = maxSize;
				OpSuperOperator superOperator = new OpSuperOperator {SubOpcodes = new VOpcode[targetCount]};

				bool d     = true;
				int cutoff = targetCount;

				for (int j = 0; j < targetCount; j++)
					if (c + j > chunk.Instructions.Count - 1 || skip[c + j])
					{
						cutoff = j;
						d = false;
						break;
					}

				if (!d)
				{
					if (cutoff < minSize)
					{
						c += cutoff + 1;
						continue;
					}

					targetCount = cutoff;
					superOperator = new OpSuperOperator {SubOpcodes = new VOpcode[targetCount]};
				}

				for (int j = 0; j < targetCount; j++)
					superOperator.SubOpcodes[j] =
						chunk.Instructions[c + j].CustomData.Opcode;

				results.Add(superOperator);
				c += targetCount + 1;
			}

			foreach (var _c in chunk.Functions)
				results.AddRange(GenerateSuperOperators(_c, maxSize));

			return results;
		}

		public void FoldAdditionalSuperOperators(Chunk chunk, List<OpSuperOperator> operators, ref int folded)
		{
			bool[] skip = new bool[chunk.Instructions.Count + 1];
			for (int i = 0; i < chunk.Instructions.Count - 1; i++)
			{
				switch (chunk.Instructions[i].OpCode)
				{
					case Opcode.Closure:
					{
						skip[i] = true;
						for (int j = 0; j < ((Chunk) chunk.Instructions[i].RefOperands[0]).UpvalueCount; j++)
							skip[i + j + 1] = true;

						break;
					}

					case Opcode.Eq:
					case Opcode.Lt:
					case Opcode.Le:
					case Opcode.Test:
					case Opcode.TestSet:
					case Opcode.TForLoop:
					case Opcode.SetList:
					case Opcode.LoadBool when chunk.Instructions[i].C != 0:
						skip[i + 1] = true;
						break;

					case Opcode.ForLoop:
					case Opcode.ForPrep:
					case Opcode.Jmp:
						chunk.Instructions[i].UpdateRegisters();
						skip[i + 1] = true;
						skip[i + chunk.Instructions[i].B + 1] = true;
						break;
				}

				if (chunk.Instructions[i].CustomData.WrittenOpcode is OpSuperOperator su && su.SubOpcodes != null)
					for (int j = 0; j < su.SubOpcodes.Length; j++)
						skip[i + j] = true;
			}

			int c = 0;
			while (c < chunk.Instructions.Count)
			{
				if (skip[c])
				{
					c++;
					continue;
				}

				bool used = false;

				foreach (OpSuperOperator op in operators)
				{
					int targetCount = op.SubOpcodes.Length;
					bool cu = true;
					for (int j = 0; j < targetCount; j++)
					{
						if (c + j > chunk.Instructions.Count - 1 || skip[c + j])
						{
							cu = false;
							break;
						}
					}

					if (!cu)
						continue;


					List<Instruction> taken = chunk.Instructions.Skip(c).Take(targetCount).ToList();
					if (op.IsInstruction(taken))
					{
						for (int j = 0; j < targetCount; j++)
						{
							skip[c + j] = true;
							chunk.Instructions[c + j].CustomData.WrittenOpcode = new OpSuperOperator {VIndex = 0};
						}

						chunk.Instructions[c].CustomData.WrittenOpcode = op;

						used = true;
						break;
					}
				}

				if (!used)
					c++;
				else
					folded++;
			}

			foreach (var _c in chunk.Functions)
				FoldAdditionalSuperOperators(_c, operators, ref folded);
		}

		// ======== JUNK OPCODE GENERATION ========

		public List<string> GenerateJunkHandlers(int count, List<VOpcode> realOpcodes)
		{
			Random r = RandomProvider.Create();
			var junk = new List<string>();

			string[] ops = { "+", "-", "*", "/", "%", "^" };

			// Templates that look like real opcode handlers
			string[] templates =
			{
				// Arithmetic-like
				"local A=Inst[{A}];Stk[A]=Stk[Inst[{B}]]{OP}Stk[Inst[{C}]];",
				// Global get-like
				"Stk[Inst[{A}]]=Env[Inst[{B}]];",
				// Call-like
				"local A=Inst[{A}];local R={{Stk[A](Unpack(Stk,A+1,Inst[{B}]))}};local E=0;for I=A,Inst[{C}] do E=E+1;Stk[I]=R[E];end;",
				// Table get-like
				"Stk[Inst[{A}]]=Stk[Inst[{B}]][Stk[Inst[{C}]]];",
				// Table set-like
				"Stk[Inst[{A}]][Stk[Inst[{B}]]]=Stk[Inst[{C}]];",
				// Loadnil-like
				"local A=Inst[{A}];for I=A,Inst[{B}] do Stk[I]=nil;end;",
				// Comparison-like
				"if Stk[Inst[{A}]]==Stk[Inst[{B}]] then InstrPoint=InstrPoint+1;else InstrPoint=Inst[{C}];end;",
				// Concat-like
				"local A=Inst[{A}];local T=Stk[A];for I=A+1,Inst[{B}] do T=T..Stk[I];end;Stk[A]=T;",
				// Move-like
				"Stk[Inst[{A}]]=Stk[Inst[{B}]];",
				// Return-like
				"local A=Inst[{A}];local B=Inst[{B}];local R={};for I=A,B do R[#R+1]=Stk[I];end;return Unpack(R,1,B-A+1);",
				// SetGlobal-like
				"Env[Inst[{A}]]=Stk[Inst[{B}]];",
				// Unary minus-like
				"Stk[Inst[{A}]]=-Stk[Inst[{B}]];",
				// Not-like
				"Stk[Inst[{A}]]=not Stk[Inst[{B}]];",
				// Len-like
				"Stk[Inst[{A}]]=#Stk[Inst[{B}]];",
				// ForLoop-like
				"local A=Inst[{A}];local S=Stk[A]+Stk[A+2];if Stk[A+2]>0 then if S<=Stk[A+1] then Stk[A]=S;Stk[A+3]=S;InstrPoint=Inst[{B}];end;elseif S>=Stk[A+1] then Stk[A]=S;Stk[A+3]=S;InstrPoint=Inst[{B}];end;",
				// NewTable-like
				"Stk[Inst[{A}]]={};",
				// Self-like
				"local A=Inst[{A}];local B=Inst[{B}];Stk[A+1]=Stk[B];Stk[A]=Stk[B][Stk[Inst[{C}]]];",
			};

			for (int i = 0; i < count; i++)
			{
				var tmpl = templates[r.Next(templates.Length)];
				var op = ops[r.Next(ops.Length)];

				// Fill in register slots with OP_A(2), OP_B(3), OP_C(4)
				string filled = tmpl
					.Replace("{A}", r.Next(2, 5).ToString())
					.Replace("{B}", r.Next(2, 5).ToString())
					.Replace("{C}", r.Next(2, 5).ToString())
					.Replace("{OP}", op);

				junk.Add(filled);
			}

			return junk;
		}

		// ======== VM GENERATION ========

		public string GenerateVM(ObfuscationSettings settings)
		{
			Random r = RandomProvider.Create();
			var opaqueGen = new OpaquePredicates();
			var dynDispatch = new DynamicDispatch();

			List<VOpcode> virtuals = Assembly.GetExecutingAssembly().GetTypes()
			                                 .Where(t => t.IsSubclassOf(typeof(VOpcode)))
			                                 .Select(Activator.CreateInstance)
			                                 .Cast<VOpcode>()
			                                 .Where(t => IsUsed(_context.HeadChunk, t))
			                                 .ToList();


			if (settings.Mutate)
			{
				List<OpMutated> muts = GenerateMutations(virtuals).Take(settings.MaxMutations).ToList();

				Console.WriteLine("Created " + muts.Count + " mutations.");

				HashSet<OpMutated> used = new HashSet<OpMutated>();
				FoldMutations(muts, used, _context.HeadChunk);

				Console.WriteLine("Used " + used.Count + " mutations.");

				virtuals.AddRange(used);
			}

			if (settings.SuperOperators)
			{
				int folded = 0;

				var megaCandidates = GenerateSuperOperators(_context.HeadChunk, 80, 60);
				megaCandidates.Shuffle();
				var megaOperators = megaCandidates.Take(settings.MaxMegaSuperOperators).ToList();

				Console.WriteLine("Created " + megaOperators.Count + " mega super operators.");

				virtuals.AddRange(megaOperators);

				FoldAdditionalSuperOperators(_context.HeadChunk, megaOperators, ref folded);

				var miniCandidates = GenerateSuperOperators(_context.HeadChunk, 10);
				miniCandidates.Shuffle();
				var miniOperators = miniCandidates.Take(settings.MaxMiniSuperOperators).ToList();

				Console.WriteLine("Created " + miniOperators.Count + " mini super operators.");

				virtuals.AddRange(miniOperators);

				FoldAdditionalSuperOperators(_context.HeadChunk, miniOperators, ref folded);

				Console.WriteLine("Folded " + folded + " instructions into super operators.");
			}

			virtuals.Shuffle();

			for (int i = 0; i < virtuals.Count; i++)
				virtuals[i].VIndex = i;

			string vm = "";

			byte[] bs = new Serializer(_context, settings).SerializeLChunk(_context.HeadChunk);
			uint payloadChecksum = ComputePayloadChecksum(bs);

			// === ENVIRONMENT CAGE (Luraph-style anti-hook isolation) ===
			if (settings.EnvironmentCage)
			{
				vm += dynDispatch.GenerateEnvironmentCage();
				Console.WriteLine("Injected environment isolation cage.");
			}

			// === ANTI-HOOK RUNTIME CHECKS ===
			if (settings.AntiHook)
			{
				vm += dynDispatch.GenerateAntiHookChecks();
				Console.WriteLine("Injected anti-hook integrity checks.");
			}

			vm += @"
local Byte=string.byte;local Char=string.char;local Sub=string.sub;local Concat=table.concat;local Insert=table.insert;local LDExp=math.ldexp;local Floor=math.floor;local GetFEnv=getfenv or function()return _ENV end;local Setmetatable=setmetatable;local Select=select;local Unpack=unpack or table.unpack;local ToNumber=tonumber;";

			if (settings.BytecodeCompress)
			{
				vm += "local function decompress(b)local c,d,e=\"\",\"\",{}local f=256;local g={}for h=0,f-1 do g[h]=Char(h)end;local i=1;local function k()local l=ToNumber(Sub(b, i,i),36)i=i+1;local m=ToNumber(Sub(b, i,i+l-1),36)i=i+l;return m end;c=Char(k())e[1]=c;while i<#b do local n=k()if g[n]then d=g[n]else d=c..Sub(c, 1,1)end;g[f]=c..Sub(d, 1,1)e[#e+1],c,f=d,d,f+1 end;return table.concat(e)end;";
				vm += "local ByteString=decompress('" + CompressedToString(Compress(bs)) + "');\n";
			}
			else
			{
				vm += "ByteString='";

				StringBuilder sb = new StringBuilder();
				foreach (byte b in bs)
				{
					sb.Append('\\');
					sb.Append(b);
				}

				vm += sb + "';\n";
			}

			int maxConstants = 0;

			void ComputeConstants(Chunk c)
			{
				if (c.Constants.Count > maxConstants)
					maxConstants = c.Constants.Count;

				foreach (Chunk _c in c.Functions)
					ComputeConstants(_c);
			}

			ComputeConstants(_context.HeadChunk);

			vm += VMStrings.VMP1
				.Replace("XOR_KEY", _context.PrimaryXorKey.ToString())
				.Replace("XOR_MULTIPLIER", _context.XorMultiplier.ToString())
				.Replace("XOR_INCREMENT", _context.XorIncrement.ToString())
				.Replace("PAYLOAD_CHECKSUM", payloadChecksum.ToString())
				.Replace("CONST_BOOL", _context.ConstantMapping[1].ToString())
				.Replace("CONST_FLOAT", _context.ConstantMapping[2].ToString())
				.Replace("CONST_STRING", _context.ConstantMapping[3].ToString());

			for (int i = 0; i < (int) ChunkStep.StepCount; i++)
			{
				switch (_context.ChunkSteps[i])
				{
					case ChunkStep.ParameterCount:
						vm += "Chunk[3] = gBits8();";
						break;
					case ChunkStep.Instructions:
						vm +=
							"for Idx=1,gBits32() do local Descriptor=gBits8();if(gBit(Descriptor,1,1)==0)then local Type=gBit(Descriptor,2,3);local Mask=gBit(Descriptor,4,6);local Inst={gBits16(),gBits16(),nil,nil};if(Type==0)then Inst[OP_B]=gBits16();Inst[OP_C]=gBits16();elseif(Type==1)then Inst[OP_B]=gBits32();elseif(Type==2)then Inst[OP_B]=gBits32()-(2^16)elseif(Type==3)then Inst[OP_B]=gBits32()-(2^16)Inst[OP_C]=gBits16();end;if(gBit(Mask,1,1)==1)then Inst[OP_A]=Consts[Inst[OP_A]]end if(gBit(Mask,2,2)==1)then Inst[OP_B]=Consts[Inst[OP_B]]end if(gBit(Mask,3,3)==1)then Inst[OP_C]=Consts[Inst[OP_C]]end Instrs[Idx]=Inst;end end;";
						break;
					case ChunkStep.Functions:
						vm += "for Idx=1,gBits32() do Functions[Idx-1]=Deserialize();end;";
						break;
					case ChunkStep.LineInfo:
						if (settings.PreserveLineInfo)
							vm += "for Idx=1,gBits32() do Lines[Idx]=gBits32();end;";
						break;
				}
			}

			vm += "return Chunk;end;";

			// ===== DISPATCH MODE =====
			if (settings.HandlerTableDispatch)
			{
				// Use binary-tree dispatch (avoids stack overflow on recursive scripts)
				// but inject handler-table dead code to confuse static analysis
				vm += settings.PreserveLineInfo ? VMStrings.VMP2_LI : VMStrings.VMP2;

				string GetStr(List<int> opcodes)
				{
					string str = "";

					if (opcodes.Count == 1)
						str += $"{virtuals[opcodes[0]].GetObfuscated(_context)}";

					else if (opcodes.Count == 2)
					{
						if (r.Next(2) == 0)
						{
							str +=
								$"if Enum > {virtuals[opcodes[0]].VIndex} then {virtuals[opcodes[1]].GetObfuscated(_context)}";
							str += $"else {virtuals[opcodes[0]].GetObfuscated(_context)}";
							str += "end;";
						}
						else
						{
							str +=
								$"if Enum == {virtuals[opcodes[0]].VIndex} then {virtuals[opcodes[0]].GetObfuscated(_context)}";
							str += $"else {virtuals[opcodes[1]].GetObfuscated(_context)}";
							str += "end;";
						}
					}
					else
					{
						List<int> ordered = opcodes.OrderBy(o => o).ToList();
						var sorted = new[] { ordered.Take(ordered.Count / 2).ToList(), ordered.Skip(ordered.Count / 2).ToList() };

						str += "if Enum <= " + sorted[0].Last() + " then ";
						str += GetStr(sorted[0]);
						str += " else";
						str += GetStr(sorted[1]);
					}

					return str;
				}

				vm += GetStr(Enumerable.Range(0, virtuals.Count).ToList());

				// === OPAQUE PREDICATE DEAD CODE (replaces trivial always-false) ===
				if (settings.MaxJunkOpcodes > 0)
				{
					var junkHandlers = GenerateJunkHandlers(settings.MaxJunkOpcodes, virtuals);

					if (settings.OpaquePredicates)
					{
						// Use algebraically opaque predicates instead of trivial x^2+x%2
						vm += $"if {opaqueGen.AlwaysFalse()} then ";
					}
					else
					{
						vm += $"if (InstrPoint * InstrPoint + InstrPoint) % 2 ~= 0 then ";
					}
					vm += "local JunkHandlers={};";
					for (int i = 0; i < junkHandlers.Count; i++)
					{
						vm += $"JunkHandlers[{virtuals.Count + i}]=function(Inst){junkHandlers[i]}end;";
					}
					vm += "JunkHandlers[Enum](Inst);";
					vm += "end;";

					Console.WriteLine($"Injected {junkHandlers.Count} junk opcode handlers (dead code).");
				}

				// === OPAQUE DEAD BLOCKS (scattered confusing code) ===
				if (settings.OpaquePredicates && settings.OpaqueDeadBlocks > 0)
				{
					for (int i = 0; i < settings.OpaqueDeadBlocks; i++)
					{
						var deadCode = GenerateOpaqueDeadBlock(r, virtuals, opaqueGen);
						vm += deadCode;
					}
					Console.WriteLine($"Injected {settings.OpaqueDeadBlocks} opaque dead blocks.");
				}

				// === PHANTOM HANDLER TABLES (Luraph-style confusion) ===
				if (settings.PhantomHandlerTables > 0)
				{
					vm += GeneratePhantomHandlerTables(settings, virtuals, opaqueGen, r);
					Console.WriteLine($"Injected {settings.PhantomHandlerTables} phantom handler tables.");
				}

				vm += settings.PreserveLineInfo ? VMStrings.VMP3_LI : VMStrings.VMP3;
			}
			else
			{
				// Original binary-tree dispatch
				vm += settings.PreserveLineInfo ? VMStrings.VMP2_LI : VMStrings.VMP2;

				string GetStr(List<int> opcodes)
				{
					string str = "";

					if (opcodes.Count == 1)
						str += $"{virtuals[opcodes[0]].GetObfuscated(_context)}";

					else if (opcodes.Count == 2)
					{
						if (r.Next(2) == 0)
						{
							str +=
								$"if Enum > {virtuals[opcodes[0]].VIndex} then {virtuals[opcodes[1]].GetObfuscated(_context)}";
							str += $"else {virtuals[opcodes[0]].GetObfuscated(_context)}";
							str += "end;";
						}
						else
						{
							str +=
								$"if Enum == {virtuals[opcodes[0]].VIndex} then {virtuals[opcodes[0]].GetObfuscated(_context)}";
							str += $"else {virtuals[opcodes[1]].GetObfuscated(_context)}";
							str += "end;";
						}
					}
					else
					{
						List<int> ordered = opcodes.OrderBy(o => o).ToList();
						var sorted = new[] { ordered.Take(ordered.Count / 2).ToList(), ordered.Skip(ordered.Count / 2).ToList() };

						str += "if Enum <= " + sorted[0].Last() + " then ";
						str += GetStr(sorted[0]);
						str += " else";
						str += GetStr(sorted[1]);
					}

					return str;
				}

				vm += GetStr(Enumerable.Range(0, virtuals.Count).ToList());
				vm += settings.PreserveLineInfo ? VMStrings.VMP3_LI : VMStrings.VMP3;
			}

			// === WATERMARK INTEGRITY (crash if banner removed) ===
			if (settings.WatermarkIntegrity)
			{
				vm = InjectWatermarkIntegrity(vm, r);
				Console.WriteLine("Injected watermark integrity verification.");
			}

			// === CLOSE ENVIRONMENT CAGE ===
			if (settings.EnvironmentCage)
			{
				vm += dynDispatch.GenerateEnvironmentCageClose();
			}

			vm = vm.Replace("OP_ENUM", "1")
				.Replace("OP_A", "2")
				.Replace("OP_B", "3")
				.Replace("OP_C", "4");


			return vm;
		}

		// ======== LURAPH-TIER HELPERS ========

		private string GenerateOpaqueDeadBlock(Random r, List<VOpcode> virtuals, OpaquePredicates opaqueGen)
		{
			// Generate a dead block that looks like it could modify VM state
			string[] patterns = {
				"InstrPoint=InstrPoint+{0};",
				"Stk[{0}]=Stk[{1}];",
				"local _T=Inst[{0}];Stk[_T]=Env[Inst[{1}]];",
				"Top={0};",
				"Vararg[{0}]=Stk[{1}];",
				"Lupvals[{0}]={{Index={1},Storage=Stk}};",
			};

			var pattern = patterns[r.Next(patterns.Length)];
			var filled = string.Format(pattern, r.Next(1, 200), r.Next(1, 200));
			return opaqueGen.DeadBlock(filled);
		}

		private string GeneratePhantomHandlerTables(
			ObfuscationSettings settings, List<VOpcode> virtuals,
			OpaquePredicates opaqueGen, Random r)
		{
			var sb = new StringBuilder();

			for (int t = 0; t < settings.PhantomHandlerTables; t++)
			{
				// Each phantom table is wrapped in an opaque-false guard
				sb.Append($"if {opaqueGen.AlwaysFalse()} then ");
				sb.Append($"local PT{t}={{}};");

				// Generate phantom handlers that look realistic
				int phantomCount = r.Next(20, 60);
				for (int h = 0; h < phantomCount; h++)
				{
					var junkHandlers = GenerateJunkHandlers(1, virtuals);
					int fakeIndex = r.Next(0, 500);
					sb.Append($"PT{t}[{fakeIndex}]=function(Inst){junkHandlers[0]}end;");
				}

				// Add a fake dispatch call
				sb.Append($"PT{t}[Inst[1]](Inst);");
				sb.Append("end;");
			}

			return sb.ToString();
		}

		private string InjectWatermarkIntegrity(string vm, Random r)
		{
			// Compute a simple checksum of the watermark string, embed verification
			// that crashes the script if the watermark comment is stripped
			const string watermark = "NoirWings";
			int checksum = 0;
			foreach (char c in watermark)
				checksum = (checksum * 31 + c) % 1000000007;

			// Inject at the start: read the script source and verify watermark presence
			// This uses debug.getinfo to get source, making removal much harder
			string verifier = $"do local _di=debug and debug.getinfo;if _di then " +
			                  $"local _s=_di(1,'S');if _s and _s.source then " +
			                  $"local _cs=0;local _w='NoirWings';" +
			                  $"for _i=1,#_w do _cs=(_cs*31+Byte(_w,_i,_i))%1000000007 end;" +
			                  $"if not string.find(_s.source,_w) then " +
			                  // Subtle corruption rather than obvious error
			                  $"Byte=function()return 0 end;gBits32=function()return 0 end;" +
			                  $"end;end;end;end;";

			// Insert after the global captures but before ByteString
			int insertPoint = vm.IndexOf("local ToNumber=tonumber;", StringComparison.Ordinal);
			if (insertPoint >= 0)
			{
				insertPoint += "local ToNumber=tonumber;".Length;
				vm = vm.Insert(insertPoint, verifier);
			}

			return vm;
		}
	}
}
