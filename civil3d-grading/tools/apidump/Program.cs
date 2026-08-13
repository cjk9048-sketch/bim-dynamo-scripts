// API 메타데이터 덤프 — "쓰기 전에 확인한다"를 실제로 가능하게 하는 도구.
//
//   왜 필요한가. AutoCAD/Civil 관리형 DLL은 .NET 8 대상이라 Windows PowerShell(.NET Framework)의
//   ReflectionOnlyLoad로 못 읽고, 그냥 Load는 AutoCAD 프로세스 밖에서 의존성 때문에 터진다.
//   그래서 종전에는 DLL을 문자열로 훑거나(소속 클래스를 모른다) 코드를 써 보고 컴파일 오류로
//   더듬었다 — 실제로 GridStyle→ProfileViewStyle→GraphStyle 세 판을 헛짚었다.
//   MetadataReader는 파일을 '읽기만' 하므로 로드도 의존성도 없다.
//
// 쓰는 법:
//   dotnet run --project tools/apidump -- <dll경로|c3d|acad> member <이름조각>   ← 이 멤버가 어느 타입에 있나
//   dotnet run --project tools/apidump -- <...>                 type   <타입이름>   ← 그 타입의 멤버 전부
//   dotnet run --project tools/apidump -- <...>                 enum   <열거형이름> ← 열거형 값 전부
//
// 예:  dotnet run --project tools/apidump -- c3d member ClipGrid
//      dotnet run --project tools/apidump -- c3d type   GridStyle
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

const string AcadDir = @"C:\Program Files\Autodesk\AutoCAD 2026\";

if (args.Length < 3)
{
    Console.WriteLine("사용: apidump <dll|c3d|acad|acdb> <member|type|enum> <이름조각>");
    return 1;
}

string path = args[0] switch
{
    "c3d" => AcadDir + @"C3D\AeccDbMgd.dll",
    "acad" => AcadDir + "acmgd.dll",
    "acdb" => AcadDir + "acdbmgd.dll",
    "accore" => AcadDir + "accoremgd.dll",
    var p => p,
};
string mode = args[1].ToLowerInvariant();
string needle = args[2];

if (!File.Exists(path)) { Console.WriteLine("파일 없음: " + path); return 1; }

using var fs = File.OpenRead(path);
using var pe = new PEReader(fs);
if (!pe.HasMetadata) { Console.WriteLine("관리형 메타데이터가 없다: " + path); return 1; }
var md = pe.GetMetadataReader();

Console.WriteLine($"■ {Path.GetFileName(path)} · {mode} '{needle}'\n");
bool Hit(string s) => s.Contains(needle, StringComparison.OrdinalIgnoreCase);

// 타입 참조/정의 이름을 사람이 읽는 꼴로 — 시그니처 해독용.
var prov = new SigProvider(md);
int found = 0;

foreach (var th in md.TypeDefinitions)
{
    var td = md.GetTypeDefinition(th);
    string ns = md.GetString(td.Namespace), tn = md.GetString(td.Name);
    string full = string.IsNullOrEmpty(ns) ? tn : ns + "." + tn;
    var attrs = td.Attributes;
    bool pub = (attrs & TypeAttributes.VisibilityMask) is TypeAttributes.Public or TypeAttributes.NestedPublic;

    if (mode == "member")
    {
        foreach (var ph in td.GetProperties())
        {
            var pd = md.GetPropertyDefinition(ph);
            string pn = md.GetString(pd.Name);
            if (!Hit(pn)) continue;
            var sig = pd.DecodeSignature(prov, null);
            var acc = pd.GetAccessors();
            string rw = (acc.Getter.IsNil ? "" : "get") + (acc.Setter.IsNil ? "" : (acc.Getter.IsNil ? "set" : "/set"));
            Console.WriteLine($"{(pub ? "" : "(비공개) ")}{full}\n    {sig.ReturnType} {pn}  [{rw}]");
            found++;
        }
        foreach (var mh in td.GetMethods())
        {
            var mdf = md.GetMethodDefinition(mh);
            string mn = md.GetString(mdf.Name);
            if (!Hit(mn) || mn.StartsWith("get_") || mn.StartsWith("set_")) continue;
            var sig = mdf.DecodeSignature(prov, null);
            Console.WriteLine($"{(pub ? "" : "(비공개) ")}{full}\n    {sig.ReturnType} {mn}({string.Join(", ", sig.ParameterTypes)})");
            found++;
        }
        foreach (var fh in td.GetFields())
        {
            var fd = md.GetFieldDefinition(fh);
            string fn = md.GetString(fd.Name);
            if (!Hit(fn)) continue;
            Console.WriteLine($"{(pub ? "" : "(비공개) ")}{full}\n    {fd.DecodeSignature(prov, null)} {fn} (필드)");
            found++;
        }
    }
    else if (mode == "type" && Hit(tn))
    {
        Console.WriteLine($"── {full} {(pub ? "" : "(비공개)")}");
        foreach (var ph in td.GetProperties())
        {
            var pd = md.GetPropertyDefinition(ph);
            var sig = pd.DecodeSignature(prov, null);
            var acc = pd.GetAccessors();
            string rw = (acc.Getter.IsNil ? "" : "get") + (acc.Setter.IsNil ? "" : (acc.Getter.IsNil ? "set" : "/set"));
            Console.WriteLine($"    {sig.ReturnType,-52} {md.GetString(pd.Name)}  [{rw}]");
        }
        foreach (var mh in td.GetMethods())
        {
            var mdf = md.GetMethodDefinition(mh);
            string mn = md.GetString(mdf.Name);
            if (mn.StartsWith("get_") || mn.StartsWith("set_") || mn.StartsWith(".")) continue;
            if ((mdf.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public) continue;
            var sig = mdf.DecodeSignature(prov, null);
            Console.WriteLine($"    {sig.ReturnType,-52} {mn}({string.Join(", ", sig.ParameterTypes)})");
        }
        found++;
        Console.WriteLine();
    }
    else if (mode == "enum" && Hit(tn))
    {
        // 열거형인지: 필드에 value__ 가 있으면 맞다.
        var lits = new List<string>();
        bool isEnum = false;
        foreach (var fh in td.GetFields())
        {
            var fd = md.GetFieldDefinition(fh);
            string fn = md.GetString(fd.Name);
            if (fn == "value__") { isEnum = true; continue; }
            if ((fd.Attributes & FieldAttributes.Literal) == 0) continue;
            object v = null;
            try
            {
                var ch = fd.GetDefaultValue();
                if (!ch.IsNil)
                {
                    var c = md.GetConstant(ch);
                    var br = md.GetBlobReader(c.Value);
                    v = c.TypeCode switch
                    {
                        ConstantTypeCode.Int32 => br.ReadInt32(),
                        ConstantTypeCode.UInt32 => br.ReadUInt32(),
                        ConstantTypeCode.Int16 => br.ReadInt16(),
                        ConstantTypeCode.Byte => br.ReadByte(),
                        ConstantTypeCode.Int64 => br.ReadInt64(),
                        _ => null,
                    };
                }
            }
            catch { }
            lits.Add($"    {fn} = {v}");
        }
        if (!isEnum) continue;
        Console.WriteLine($"── {full}");
        foreach (var s in lits) Console.WriteLine(s);
        Console.WriteLine();
        found++;
    }
}

Console.WriteLine(found == 0 ? "찾은 것 없음." : $"\n총 {found}건.");
return 0;

/// <summary>시그니처의 타입을 이름 문자열로 — 우리는 읽기만 하므로 이름이면 충분하다.</summary>
sealed class SigProvider : ISignatureTypeProvider<string, object>
{
    private readonly MetadataReader _md;
    public SigProvider(MetadataReader md) => _md = md;

    public string GetPrimitiveType(PrimitiveTypeCode t) => t switch
    {
        PrimitiveTypeCode.Boolean => "bool",
        PrimitiveTypeCode.Double => "double",
        PrimitiveTypeCode.Int32 => "int",
        PrimitiveTypeCode.String => "string",
        PrimitiveTypeCode.Void => "void",
        PrimitiveTypeCode.Object => "object",
        _ => t.ToString(),
    };
    public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte _)
    { var t = r.GetTypeDefinition(h); return r.GetString(t.Name); }
    public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte _)
    { var t = r.GetTypeReference(h); return r.GetString(t.Name); }
    public string GetTypeFromSpecification(MetadataReader r, object g, TypeSpecificationHandle h, byte _) => "?";
    public string GetSZArrayType(string e) => e + "[]";
    public string GetArrayType(string e, ArrayShape s) => e + "[,]";
    public string GetByReferenceType(string e) => "ref " + e;
    public string GetPointerType(string e) => e + "*";
    public string GetGenericInstantiation(string g, System.Collections.Immutable.ImmutableArray<string> a)
        => g + "<" + string.Join(",", a) + ">";
    public string GetGenericMethodParameter(object g, int i) => "!!" + i;
    public string GetGenericTypeParameter(object g, int i) => "!" + i;
    public string GetModifiedType(string m, string u, bool req) => u;
    public string GetPinnedType(string e) => e;
    public string GetFunctionPointerType(MethodSignature<string> s) => "fnptr";
}
