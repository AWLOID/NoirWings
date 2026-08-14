using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace IronBrew2.Obfuscator.Encryption
{
	public class Decryptor
	{
		public int[] Table;
		public int SLen = 0;
		
		public string Name;
		private readonly string _runtimeName;
		
		public string Encrypt(byte[] bytes)
		{
			List<byte> encrypted = new List<byte>();

			int L = Table.Length;
			
			for (var index = 0; index < bytes.Length; index++)
				encrypted.Add((byte) (bytes[index] ^ Table[index % L]));
			
			return $"((function(b)IB_INLINING_START(true);local function xor(b,c)IB_INLINING_START(true);local d,e=1,0;while b>0 and c>0 do local f,g=b%2,c%2;if f~=g then e=e+d end;b,c,d=(b-f)/2,(c-g)/2,d*2 end;if b<c then b=c end;while b>0 do local f=b%2;if f>0 then e=e+d end;b,d=(b-f)/2,d*2 end;return e end;local e={_runtimeName}[1];local h={_runtimeName}[2];local q={_runtimeName}[3];local o={{}};local f=\"{string.Join("", Table.Select(t => "\\" + t.ToString()))}\";for g=1,#b do local x=(g-1)%{Table.Length}+1;o[g]=h(xor(e(b,g),e(f,x)));end;return q(o);end)(\"{string.Join("", encrypted.Select(t => "\\" + t.ToString()))}\"))";
		}

		public Decryptor(string name, int maxLen, string runtimeName)
		{
			Random r = RandomProvider.Create();

			Name = name;
			_runtimeName = runtimeName;
			Table = Enumerable.Repeat(0, Math.Max(1, maxLen)).Select(i => r.Next(0, 256)).ToArray();
		}
	}
	
	public class ConstantEncryption
	{
		private string _src;
		private ObfuscationSettings _settings;
		private Encoding _fuckingLua = Encoding.Latin1;
		private string _runtimeName;
		private bool _runtimeRequired;

		private string GetRuntimeName()
		{
			if (_runtimeName != null)
				return _runtimeName;

			const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
			Random random = RandomProvider.Create();
			StringBuilder suffix = new StringBuilder(24);

			for (int i = 0; i < 24; i++)
				suffix.Append(alphabet[random.Next(alphabet.Length)]);

			string baseName = "__nwib_rt_" + suffix;
			string candidate = baseName;
			int collision = 0;

			while (_src.IndexOf(candidate, StringComparison.Ordinal) >= 0)
				candidate = baseName + "_" + (++collision).ToString("x");

			_runtimeName = candidate;
			return _runtimeName;
		}

		private string Encrypt(Decryptor decryptor, byte[] bytes)
		{
			_runtimeRequired = true;
			return decryptor.Encrypt(bytes);
		}

		public Decryptor GenerateGenericDecryptor(MatchCollection matches)
		{
			int len = 0;

			for (int i = 0; i < matches.Count; i++)
			{
				int l = matches[i].Length;
				if (l > len)
					len = l;
			}

			if (len > _settings.DecryptTableLen)
				len = _settings.DecryptTableLen;
			
			return new Decryptor("IRONBREW_STR_DEC_GENERIC", len, GetRuntimeName());
		}

		public static byte[] UnescapeLuaString(string str)
		{
			List<byte> bytes = new List<byte>();
			
			int i = 0;
			while (i < str.Length)
			{
				char cur = str[i++];
				if (cur == '\\')
				{
					char next = str[i++];

					switch (next)
					{
						case 'a':
							bytes.Add((byte) '\a');
							break;

						case 'b':
							bytes.Add((byte) '\b');
							break;

						case 'f':
							bytes.Add((byte) '\f');
							break;

						case 'n':
							bytes.Add((byte) '\n');
							break;

						case 'r':
							bytes.Add((byte) '\r');
							break;

						case 't':
							bytes.Add((byte) '\t');
							break;

						case 'v':
							bytes.Add((byte) '\v');
							break;

						default:
						{
							if (!char.IsDigit(next))
								bytes.Add((byte) next);
							else // \001, \55h, etc
							{
								string s = next.ToString(); 
								for (int j = 0; j < 2; j++, i++)
								{
									if (i == str.Length)
										break;

									char n = str[i];
									if (char.IsDigit(n))
										s = s + n;
									else
										break;
								}

								bytes.Add((byte) int.Parse(s));
							}

							break;
						}
					}
				}
				else
					bytes.Add((byte) cur);
			}

			return bytes.ToArray();
		}

		public string EncryptStrings()
		{
			const string encRegex = @"(['""])?(?(1)((?:[^\\]|\\.)*?)\1|\[(=*)\[(.*?)\]\3\])";
			
			if (_settings.EncryptStrings)
			{
				Regex r       = new Regex(encRegex, RegexOptions.Singleline | RegexOptions.Compiled);

				int indDiff = 0;
				var   matches = r.Matches(_src);
				
				Decryptor dec     = GenerateGenericDecryptor(matches);
			
				foreach (Match m in matches)
				{
					string before = _src.Substring(0, m.Index        + indDiff);
					string after  = _src.Substring(m.Index + indDiff + m.Length);

					string captured = m.Groups[2].Value + m.Groups[4].Value;
					
					string nStr = before + Encrypt(dec, m.Groups[2].Value != "" ? UnescapeLuaString(captured) : _fuckingLua.GetBytes(captured));
					nStr += after;
				
					indDiff += nStr.Length - _src.Length;
					_src    =  nStr;
				}
			}

			if (_settings.EncryptImportantStrings)
			{
				Regex r = new Regex(encRegex, RegexOptions.Singleline | RegexOptions.Compiled);
				var matches = r.Matches(_src);

				int indDiff = 0;
				int n = 0;

				List<string> sTerms = new List<string>() {"http", "function", "metatable", "local"};

				foreach (Match m in matches)
				{
					string captured = m.Groups[2].Value + m.Groups[4].Value;

					bool cont = false;

					foreach (string search in sTerms)
					{
						if (captured.ToLower().Contains(search.ToLower()))
							cont = true;
					}

					if (!cont)
						continue;

					Decryptor dec = new Decryptor("IRONBREW_STR_ENCRYPT_IMPORTANT" + n++, m.Length, GetRuntimeName());

					string before = _src.Substring(0, m.Index + indDiff);
					string after = _src.Substring(m.Index + indDiff + m.Length);

					string nStr = before + Encrypt(dec, m.Groups[2].Value != ""
						              ? UnescapeLuaString(captured)
						              : _fuckingLua.GetBytes(captured));

					nStr += after;

					indDiff += nStr.Length - _src.Length;
					_src = nStr;
				}
			}

			if (_runtimeRequired)
				_src = $"local {GetRuntimeName()}={{string.byte,string.char,table.concat}};" + _src;

			return _src;
		}

		public ConstantEncryption(ObfuscationSettings settings, string source)
		{
			_settings = settings;
			_src = source;
		}
	}
}
