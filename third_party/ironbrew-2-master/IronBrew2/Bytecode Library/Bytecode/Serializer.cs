using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IronBrew2.Bytecode_Library.IR;
using IronBrew2.Obfuscator;
using IronBrew2.Obfuscator.Opcodes;

namespace IronBrew2.Bytecode_Library.Bytecode
{
	public class Serializer
	{
		private ObfuscationContext _context;
		private ObfuscationSettings _settings;
		private Encoding _fuckingLua = Encoding.Latin1;

		public Serializer(ObfuscationContext context, ObfuscationSettings settings)
		{
			_context = context;
			_settings = settings;
		}

		public byte[] SerializeLChunk(Chunk chunk, bool factorXor = true)
		{
			List<byte> bytes = new List<byte>();
			long xorState = _context.PrimaryXorKey;

			byte EncodeByte(byte value)
			{
				if (!factorXor)
					return value;

				byte mask = (byte)(xorState / 16777216L);
				byte encoded = (byte)(value ^ mask);
				xorState = ((xorState * _context.XorMultiplier) + _context.XorIncrement) % 4294967296L;
				return encoded;
			}

			void WriteByte(byte b)
			{
				bytes.Add(EncodeByte(b));
			}

			void Write(byte[] b, bool checkEndian = true)
			{
				if (!BitConverter.IsLittleEndian && checkEndian)
					b = b.Reverse().ToArray();

				foreach (byte value in b)
					bytes.Add(EncodeByte(value));
			}

			void WriteInt32(int i) =>
				Write(BitConverter.GetBytes(i));

			void WriteInt16(short i) =>
				Write(BitConverter.GetBytes(i));

			void WriteNumber(double d) =>
				Write(BitConverter.GetBytes(d));

			void WriteString(string s)
			{
				byte[] sBytes = _fuckingLua.GetBytes(s);

				WriteInt32(sBytes.Length);
				Write(sBytes, false);
			}

			void WriteBool(bool b) =>
				Write(BitConverter.GetBytes(b));

			void SerializeInstruction(Instruction inst)
			{
				if (inst.InstructionType == InstructionType.Data)
				{
					WriteByte(1);
					return;
				}
				inst.UpdateRegisters();

				var cData = inst.CustomData;
				int opCode = (int)inst.OpCode;

				if (cData != null)
				{
					var virtualOpcode = cData.Opcode;

					opCode = cData.WrittenOpcode?.VIndex ?? virtualOpcode.VIndex;
					virtualOpcode?.Mutate(inst);
				}

				int a = inst.A;
				int b = inst.B;
				int c = inst.C;

				int t = (int)inst.InstructionType;
				int m = (int)inst.ConstantMask;
				WriteByte((byte)((t << 1) | (m << 3)));
				WriteInt16((short)opCode);
				WriteInt16((short)a);

				switch (inst.InstructionType)
				{
					case InstructionType.AsBx:
						b += 1 << 16;
						WriteInt32(b);
						break;
					case InstructionType.AsBxC:
						b += 1 << 16;
						WriteInt32(b);
						WriteInt16((short)c);
						break;
					case InstructionType.ABC:
						WriteInt16((short)b);
						WriteInt16((short)c);
						break;
					case InstructionType.ABx:
						WriteInt32(b);
						break;
				}

			}

			chunk.UpdateMappings();

			WriteInt32(chunk.Constants.Count);
			foreach (Constant c in chunk.Constants)
			{
				WriteByte((byte)_context.ConstantMapping[(int)c.Type]);
				switch (c.Type)
				{
					case ConstantType.Boolean:
						WriteBool(c.Data);
						break;
					case ConstantType.Number:
						WriteNumber(c.Data);
						break;
					case ConstantType.String:
						WriteString(c.Data);
						break;
				}
			}

			for (int i = 0; i < (int) ChunkStep.StepCount; i++)
			{
				switch (_context.ChunkSteps[i])
				{
					case ChunkStep.ParameterCount:
						WriteByte(chunk.ParameterCount);
						break;
					case ChunkStep.Instructions:
						WriteInt32(chunk.Instructions.Count);

						foreach (Instruction ins in chunk.Instructions)
							SerializeInstruction(ins);
						break;
					case ChunkStep.Functions:
						WriteInt32(chunk.Functions.Count);
						foreach (Chunk c in chunk.Functions)
							Write(SerializeLChunk(c, false));

						break;
					case ChunkStep.LineInfo when _settings.PreserveLineInfo:
						WriteInt32(chunk.Instructions.Count);
						foreach (var instr in chunk.Instructions)
							WriteInt32(instr.Line);
						break;
				}
			}

			return bytes.ToArray();
		}
	}
}
