using System;
using System.Collections.Generic;
using System.Linq;
using IronBrew2.Bytecode_Library.IR;

namespace IronBrew2.Obfuscator.Opcodes
{
	public class OpMutated : VOpcode
	{
		public static Random rand = RandomProvider.Create();

		public VOpcode Mutated;
		public int[] Registers;

		public static string[] RegisterReplacements = {"OP__A", "OP__B", "OP__C"};

		public override bool IsInstruction(Instruction instruction) =>
			false;

		public bool CheckInstruction() =>
			rand.Next(1, 15) == 1;

		public override string GetObfuscated(ObfuscationContext context)
		{
			// For handler-table dispatch, each mutated opcode gets its own handler slot.
			// The mutation value comes from the fact that each OpMutated is a separate
			// VIndex entry in the handler table — the deobfuscator must trace which
			// underlying opcode each of potentially hundreds of handlers corresponds to.
			// Text-level register permutation of Inst[OP_X] markers is intentionally
			// NOT applied because many opcodes use complex patterns (Stk[A+1], loops from
			// A to B, etc.) that cannot be safely permuted by naive text replacement.
			// The Serializer DOES permute the A/B/C fields for simple ABC-type instructions,
			// which provides bytecode-level confusion even without code rewriting.
			return Mutated.GetObfuscated(context);
		}

		public override void Mutate(Instruction instruction)
		{
			Mutated.Mutate(instruction);
		}
	}
}
