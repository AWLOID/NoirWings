namespace IronBrew2.Obfuscator.VM_Generation
{
	public static class VMStrings
	{
		public static string VMP1 = @"
local BitXOR=bit and bit.bxor or function(a,b)local p,c=1,0 while a>0 and b>0 do local ra,rb=a%2,b%2 if ra~=rb then c=c+p end a,b,p=(a-ra)/2,(b-rb)/2,p*2 end if a<b then a=b end while a>0 do local ra=a%2 if ra>0 then c=c+p end a,p=(a-ra)/2,p*2 end return c end
local function PayloadChecksum(Value)local A,B=1,0 for Index=1,#Value do A=(A+Byte(Value,Index,Index))%65521 B=(B+A)%65521 end return B*65536+A end
if PayloadChecksum(ByteString)~=PAYLOAD_CHECKSUM then error('NoirWings VM payload integrity check failed')end
local XorState=XOR_KEY
local function DecodeByte(Value)local Mask=Floor(XorState/16777216)local Decoded=BitXOR(Value,Mask)XorState=(XorState*XOR_MULTIPLIER+XOR_INCREMENT)%4294967296 return Decoded end
local function gBit(Bit,Start,End)if End then local Res=(Bit/2^(Start-1))%2^((End-1)-(Start-1)+1);return Res-Res%1;else local Plc=2^(Start-1);return(Bit%(Plc+Plc)>=Plc)and 1 or 0;end;end;
local Pos=1;
local function gBits32()local W,X,Y,Z=Byte(ByteString,Pos,Pos+3);W=DecodeByte(W)X=DecodeByte(X)Y=DecodeByte(Y)Z=DecodeByte(Z)Pos=Pos+4;return(Z*16777216)+(Y*65536)+(X*256)+W;end;
local function gBits8()local F=DecodeByte(Byte(ByteString,Pos,Pos));Pos=Pos+1;return F;end;
local function gBits16()local W,X=Byte(ByteString,Pos,Pos+1);W=DecodeByte(W)X=DecodeByte(X)Pos=Pos+2;return(X*256)+W;end;
local function gFloat()local Left=gBits32();local Right=gBits32();local IsNormal=1;local Mantissa=(gBit(Right,1,20)*(2^32))+Left;local Exponent=gBit(Right,21,31);local Sign=((-1)^gBit(Right,32));if(Exponent==0)then if(Mantissa==0)then return Sign*0;else Exponent=1;IsNormal=0;end;elseif(Exponent==2047)then return(Mantissa==0)and(Sign*(1/0))or(Sign*(0/0));end;return LDExp(Sign,Exponent-1023)*(IsNormal+(Mantissa/(2^52)));end;
local gSizet=gBits32;local function gString(Len)local Str;if(not Len)then Len=gSizet();if(Len==0)then return'';end;end;Str=Sub(ByteString,Pos,Pos+Len-1);local FStr={}for Idx=1,#Str do FStr[Idx]=Char(DecodeByte(Byte(Str,Idx,Idx)))end Pos=Pos+Len;return Concat(FStr);end;
local gInt=gBits32;local function _R(...)return{...},Select('#',...)end
local function Deserialize()local Instrs={};local Functions={};local Lines={};local Chunk={Instrs,Functions,nil,Lines};local ConstCount=gBits32()local Consts={}
for Idx=1,ConstCount do local Type=gBits8();local Cons;if(Type==CONST_BOOL)then Cons=(gBits8()~=0);elseif(Type==CONST_FLOAT)then Cons=gFloat();elseif(Type==CONST_STRING)then Cons=gString();end;Consts[Idx]=Cons;end;
";

		// Handler table dispatch version of VMP2
		public static string VMP2_HANDLER = @"
local function Wrap(Chunk, Upvalues, Env)
	local Instr  = Chunk[1];
	local Proto  = Chunk[2];
	local Params = Chunk[3];

	return function(...)
		local Instr  = Instr;
		local Proto  = Proto;
		local Params = Params;

		local _R = _R
		local InstrPoint = 1;
		local Top = -1;

		local Vararg = {};
		local Args	= {...};

		local PCount = Select('#', ...) - 1;

		local Lupvals	= {};
		local Stk		= {};

		for Idx = 0, PCount do
			if (Idx >= Params) then
				Vararg[Idx - Params] = Args[Idx + 1];
			else
				Stk[Idx] = Args[Idx + 1];
			end;
		end;

		local Varargsz = PCount - Params + 1

		local Inst;
		local Handlers = {};
";

		public static string VMP3_HANDLER = @"
		while true do
			Inst = Instr[InstrPoint];
			Handlers[Inst[OP_ENUM]](Inst);
			InstrPoint = InstrPoint + 1;
		end;
    end;
end;
return Wrap(Deserialize(), {}, GetFEnv())();
";

		// Original binary-tree dispatch (kept for fallback / PreserveLineInfo)
		public static string VMP2 = @"
local function Wrap(Chunk,Upvalues,Env)local Instr=Chunk[1];local Proto=Chunk[2];local Params=Chunk[3];return function(...)local Instr=Instr;local Proto=Proto;local Params=Params;local _R=_R local InstrPoint=1;local Top=-1;local Vararg={};local Args={...};local PCount=Select('#',...)-1;local Lupvals={};local Stk={};for Idx=0,PCount do if(Idx>=Params)then Vararg[Idx-Params]=Args[Idx+1];else Stk[Idx]=Args[Idx+1];end;end;local Varargsz=PCount-Params+1 local Inst;local Enum;while true do Inst=Instr[InstrPoint];Enum=Inst[OP_ENUM];";

		public static string VMP3 = @"
InstrPoint=InstrPoint+1;end;end;end;return Wrap(Deserialize(),{},GetFEnv())();
";
		public static string VMP2_LI = @"
local PCall=pcall local function Wrap(Chunk,Upvalues,Env)local Instr=Chunk[1];local Proto=Chunk[2];local Params=Chunk[3];return function(...)local InstrPoint=1;local Top=-1;local Args={...};local PCount=Select('#',...)-1;local function Loop()local Instr=Instr;local Const=Const;local Proto=Proto;local Params=Params;local _R=_R local Vararg={};local Lupvals={};local Stk={};for Idx=0,PCount do if(Idx>=Params)then Vararg[Idx-Params]=Args[Idx+1];else Stk[Idx]=Args[Idx+1];end;end;local Varargsz=PCount-Params+1 local Inst;local Enum;while true do Inst=Instr[InstrPoint];Enum=Inst[OP_ENUM];";

		public static string VMP3_LI = @"
InstrPoint=InstrPoint+1;end;end;A,B=_R(PCall(Loop))if not A[1]then local line=Chunk[7][InstrPoint]or'?'error('ERROR IN IRONBREW SCRIPT [LINE '..line..']:'..A[2])else return Unpack(A,2,B)end;end;end;return Wrap(Deserialize(),{},GetFEnv())();
";
	}
}
