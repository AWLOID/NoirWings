namespace IronBrew2.Obfuscator
{
	public class ObfuscationSettings
	{
		public bool EncryptStrings;
		public bool EncryptImportantStrings;
		public bool ControlFlow;
		public bool BytecodeCompress;
		public int DecryptTableLen;
		public bool PreserveLineInfo;
		public bool Mutate;
		public bool SuperOperators;
		public int MaxMiniSuperOperators;
		public int MaxMegaSuperOperators;
		public int MaxMutations;
		public bool HandlerTableDispatch;
		public int MaxJunkOpcodes;
		public bool RealMutations;

		// --- Luraph-tier features ---
		/// <summary>Use opaque predicates instead of trivial always-false guards for dead code.</summary>
		public bool OpaquePredicates;
		/// <summary>Number of opaque dead-code blocks injected into the dispatch loop.</summary>
		public int OpaqueDeadBlocks;
		/// <summary>Wrap the VM in an environment isolation cage (anti-hook).</summary>
		public bool EnvironmentCage;
		/// <summary>Inject runtime anti-hook integrity checks.</summary>
		public bool AntiHook;
		/// <summary>Use dual-interpreter dynamic dispatch (XOR-keyed handler tables).</summary>
		public bool DynamicDispatch;
		/// <summary>Use coroutine-threaded execution for the inner VM loop.</summary>
		public bool CoroutineDispatch;
		/// <summary>Number of phantom (unreachable) handler tables to inject.</summary>
		public int PhantomHandlerTables;
		/// <summary>Inject watermark verification that crashes on removal.</summary>
		public bool WatermarkIntegrity;

		public ObfuscationSettings()
		{
			EncryptStrings = false;
			EncryptImportantStrings = false;
			ControlFlow = true;
			BytecodeCompress = true;
			DecryptTableLen = 500;
			PreserveLineInfo = false;
			Mutate = true;
			SuperOperators = true;
			MaxMegaSuperOperators = 120;
			MaxMiniSuperOperators = 120;
			MaxMutations = 200;
			HandlerTableDispatch = true;
			MaxJunkOpcodes = 100;
			RealMutations = true;

			// Luraph-tier defaults
			OpaquePredicates = true;
			OpaqueDeadBlocks = 15;
			EnvironmentCage = true;
			AntiHook = true;
			DynamicDispatch = false;
			CoroutineDispatch = false;
			PhantomHandlerTables = 3;
			WatermarkIntegrity = true;
		}
	}
}