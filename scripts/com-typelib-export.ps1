<#
.SYNOPSIS
    Internal helper of the COM scripts (register-com.ps1, unregister-com.ps1, com-smoke-test.ps1):
    inspects the COM identity and type library of the library. Prints JSON.

.DESCRIPTION
    Must run in 64-bit Windows PowerShell 5.1 (.NET Framework); com-common.ps1 starts it that way.

    -Library <dll> -TypeLibraryPath <tlb>    (no registration)
        Declared identity by reflection, as RegAsm sees it (type library GUID, COM-visible types,
        registrable classes with CLSID and ProgID). Then exports the type library like RegAsm /tlb or
        TlbExp, saves it to -TypeLibraryPath (saving does not register it) and describes it.
        RegAsm /tlb and TlbExp cannot export this library from its two-file deployment: with
        ComVisible(false) on the assembly the exporter loads every type, and some private types need
        Costura-embedded dependencies that only resolve once the library's module initializer (Costura)
        has run. This helper runs it first.

    -Identity <dll>                           (no registration)
        Only the declared identity; no type library.

    -DescribeTypeLibrary <tlb>                (no registration)
        Describes an existing type library file.

    -RegisterTypeLibrary <tlb>                (writes HKCR\TypeLib and HKCR\Interface; needs elevation)
        Registers a type library file (LoadTypeLibEx with REGKIND_REGISTER), which is what RegAsm /tlb
        does after exporting.

    -UnregisterTypeLibrary <LIBID>            (removes the registration; needs elevation)
        UnRegisterTypeLib for version 1.0, LCID 0, SYS_WIN64.
#>
[CmdletBinding(DefaultParameterSetName = 'Export')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Export')] [string] $Library,
    [Parameter(Mandatory = $true, ParameterSetName = 'Export')] [string] $TypeLibraryPath,
    [Parameter(Mandatory = $true, ParameterSetName = 'Identity')] [string] $Identity,
    [Parameter(Mandatory = $true, ParameterSetName = 'Describe')] [string] $DescribeTypeLibrary,
    [Parameter(Mandatory = $true, ParameterSetName = 'Register')] [string] $RegisterTypeLibrary,
    [Parameter(Mandatory = $true, ParameterSetName = 'Unregister')] [string] $UnregisterTypeLibrary
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -eq 'Core' -or -not [Environment]::Is64BitProcess) {
    throw 'com-typelib-export.ps1 must run in 64-bit Windows PowerShell 5.1.'
}

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using ComTypes = System.Runtime.InteropServices.ComTypes;

public static class IfcComBridgeComInspection
{
    public static Dictionary<string, object> DeclaredIdentity(Assembly assembly)
    {
        var registration = new RegistrationServices();
        var comVisible = assembly.GetExportedTypes().Where(Marshal.IsTypeVisibleFromCom).OrderBy(t => t.FullName, StringComparer.Ordinal);
        var comVisibleAttribute = assembly.GetCustomAttributes(typeof(ComVisibleAttribute), false).Cast<ComVisibleAttribute>().FirstOrDefault();
        return new Dictionary<string, object>
        {
            { "AssemblyName", assembly.GetName().Name },
            { "AssemblyFullName", assembly.FullName },
            { "AssemblyVersion", assembly.GetName().Version.ToString() },
            { "AssemblyComVisible", comVisibleAttribute == null || comVisibleAttribute.Value },
            { "TypeLibraryId", Marshal.GetTypeLibGuidForAssembly(assembly).ToString() },
            { "ComVisibleTypes", comVisible.Select(t => new Dictionary<string, object>
                {
                    { "Name", t.FullName },
                    { "Kind", t.IsInterface ? "interface" : t.IsClass ? "class" : t.IsEnum ? "enum" : "struct" },
                    { "Guid", Marshal.GenerateGuidForType(t).ToString() },
                }).ToList() },
            { "RegistrableClasses", registration.GetRegistrableTypesInAssembly(assembly).Select(t => new Dictionary<string, object>
                {
                    { "Name", t.FullName },
                    { "Clsid", Marshal.GenerateGuidForType(t).ToString() },
                    { "ProgId", registration.GetProgIdForType(t) },
                }).ToList() },
        };
    }

    public static List<string> ExportTypeLibrary(Assembly assembly, string path)
    {
        var sink = new ExporterSink();
        object library = new TypeLibConverter().ConvertAssemblyToTypeLib(assembly, path, TypeLibExporterFlags.None, sink);
        ((ICreateTypeLib)library).SaveAllChanges();
        return sink.Events;
    }

    public static Dictionary<string, object> DescribeTypeLibrary(string path)
    {
        ComTypes.ITypeLib library;
        LoadTypeLibEx(path, 2 /* REGKIND_NONE */, out library);
        IntPtr ptr;
        library.GetLibAttr(out ptr);
        var attributes = (ComTypes.TYPELIBATTR)Marshal.PtrToStructure(ptr, typeof(ComTypes.TYPELIBATTR));
        library.ReleaseTLibAttr(ptr);
        string name, doc, helpFile; int helpContext;
        library.GetDocumentation(-1, out name, out doc, out helpContext, out helpFile);

        var types = new List<object>();
        for (int i = 0; i < library.GetTypeInfoCount(); i++)
        {
            ComTypes.ITypeInfo type;
            library.GetTypeInfo(i, out type);
            var described = DescribeType(type);
            if (Attributes(type).typekind == ComTypes.TYPEKIND.TKIND_DISPATCH && Attributes(type).wTypeFlags.HasFlag(ComTypes.TYPEFLAGS.TYPEFLAG_FDUAL))
            {
                int href;
                type.GetRefTypeOfImplType(-1, out href);
                ComTypes.ITypeInfo vtable;
                type.GetRefTypeInfo(href, out vtable);
                described["Vtable"] = DescribeType(vtable);
            }
            types.Add(described);
        }

        return new Dictionary<string, object>
        {
            { "Name", name },
            { "Guid", attributes.guid.ToString() },
            { "Version", attributes.wMajorVerNum + "." + attributes.wMinorVerNum },
            { "SysKind", attributes.syskind.ToString() },
            { "Types", types },
        };
    }

    private static Dictionary<string, object> DescribeType(ComTypes.ITypeInfo type)
    {
        ComTypes.TYPEATTR attributes = Attributes(type);
        var implemented = new List<object>();
        for (int i = 0; i < attributes.cImplTypes; i++)
        {
            int href;
            type.GetRefTypeOfImplType(i, out href);
            ComTypes.ITypeInfo other;
            type.GetRefTypeInfo(href, out other);
            ComTypes.IMPLTYPEFLAGS flags;
            type.GetImplTypeFlags(i, out flags);
            implemented.Add(new Dictionary<string, object> { { "Name", Name(other) }, { "Guid", Attributes(other).guid.ToString() }, { "Flags", flags.ToString() } });
        }

        return new Dictionary<string, object>
        {
            { "Name", Name(type) },
            { "Kind", attributes.typekind.ToString() },
            { "Guid", attributes.guid.ToString() },
            { "Flags", attributes.wTypeFlags.ToString() },
            { "Implements", implemented },
            { "Functions", Functions(type, attributes) },
        };
    }

    private static List<string> Functions(ComTypes.ITypeInfo type, ComTypes.TYPEATTR attributes)
    {
        var result = new List<string>();
        int elementSize = Marshal.SizeOf(typeof(ComTypes.ELEMDESC));
        for (int i = 0; i < attributes.cFuncs; i++)
        {
            IntPtr ptr;
            type.GetFuncDesc(i, out ptr);
            var func = (ComTypes.FUNCDESC)Marshal.PtrToStructure(ptr, typeof(ComTypes.FUNCDESC));
            var names = new string[func.cParams + 1];
            int count;
            type.GetNames(func.memid, names, names.Length, out count);
            var parameters = new List<string>();
            for (int p = 0; p < func.cParams; p++)
            {
                var element = (ComTypes.ELEMDESC)Marshal.PtrToStructure(func.lprgelemdescParam + p * elementSize, typeof(ComTypes.ELEMDESC));
                parameters.Add("[" + ParameterFlags(element) + "] " + TypeName(element.tdesc) + (p + 1 < count ? " " + names[p + 1] : string.Empty));
            }
            result.Add(func.memid + " @" + func.oVft + " " + names[0] + "(" + string.Join(", ", parameters) + ") : " + TypeName(func.elemdescFunc.tdesc));
            type.ReleaseFuncDesc(ptr);
        }
        return result;
    }

    private static string ParameterFlags(ComTypes.ELEMDESC element)
    {
        var flags = element.desc.paramdesc.wParamFlags;
        var parts = new List<string>();
        if (flags.HasFlag(ComTypes.PARAMFLAG.PARAMFLAG_FIN)) parts.Add("in");
        if (flags.HasFlag(ComTypes.PARAMFLAG.PARAMFLAG_FOUT)) parts.Add("out");
        if (flags.HasFlag(ComTypes.PARAMFLAG.PARAMFLAG_FRETVAL)) parts.Add("retval");
        if (flags.HasFlag(ComTypes.PARAMFLAG.PARAMFLAG_FOPT))
            parts.Add(flags.HasFlag(ComTypes.PARAMFLAG.PARAMFLAG_FHASDEFAULT)
                ? "opt=" + Marshal.GetObjectForNativeVariant(element.desc.paramdesc.lpVarValue + IntPtr.Size)
                : "opt");
        return string.Join(",", parts);
    }

    private static string TypeName(ComTypes.TYPEDESC type)
    {
        var vt = (VarEnum)type.vt;
        return vt == VarEnum.VT_PTR
            ? "VT_PTR:" + TypeName((ComTypes.TYPEDESC)Marshal.PtrToStructure(type.lpValue, typeof(ComTypes.TYPEDESC)))
            : vt.ToString();
    }

    private static ComTypes.TYPEATTR Attributes(ComTypes.ITypeInfo type)
    {
        IntPtr ptr;
        type.GetTypeAttr(out ptr);
        try { return (ComTypes.TYPEATTR)Marshal.PtrToStructure(ptr, typeof(ComTypes.TYPEATTR)); }
        finally { type.ReleaseTypeAttr(ptr); }
    }

    private static string Name(ComTypes.ITypeInfo type)
    {
        string name, doc, helpFile; int helpContext;
        type.GetDocumentation(-1, out name, out doc, out helpContext, out helpFile);
        return name;
    }

    private sealed class ExporterSink : ITypeLibExporterNotifySink
    {
        public readonly List<string> Events = new List<string>();

        public void ReportEvent(ExporterEventKind eventKind, int eventCode, string eventMsg)
        {
            Events.Add(eventKind + " " + eventCode + ": " + eventMsg);
        }

        public object ResolveRef(Assembly assembly)
        {
            if (assembly != typeof(object).Assembly)
                throw new InvalidOperationException("Unexpected type library reference: " + assembly.FullName);
            ComTypes.ITypeLib mscorlib;
            LoadTypeLibEx(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "mscorlib.tlb"), 2 /* REGKIND_NONE */, out mscorlib);
            return mscorlib;
        }
    }

    public static void RegisterTypeLibrary(string path)
    {
        ComTypes.ITypeLib library;
        LoadTypeLibEx(path, 1 /* REGKIND_REGISTER */, out library);
    }

    /// <summary>Returns the HRESULT; TYPE_E_REGISTRYACCESS (0x8002801C) means it was not registered.</summary>
    public static int UnregisterTypeLibrary(Guid libraryId)
    {
        return UnRegisterTypeLib(ref libraryId, 1, 0, 0, ComTypes.SYSKIND.SYS_WIN64);
    }

    // Only SaveAllChanges is called; the other members keep the vtable order.
    [ComImport, Guid("00020406-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICreateTypeLib
    {
        void CreateTypeInfo(); void SetName(); void SetVersion(); void SetGuid(); void SetDocString();
        void SetHelpFileName(); void SetHelpContext(); void SetLcid(); void SetLibFlags();
        void SaveAllChanges();
    }

    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void LoadTypeLibEx(string file, int regKind, out ComTypes.ITypeLib library);

    [DllImport("oleaut32.dll")]
    private static extern int UnRegisterTypeLib(ref Guid libraryId, short major, short minor, int lcid, ComTypes.SYSKIND sysKind);
}
'@

# JSON on stdout, pure ASCII (other characters as \uXXXX): console code pages cannot alter paths.
function ConvertTo-AsciiJson {
    param([Parameter(ValueFromPipeline = $true)] $InputObject)
    process {
        $json = ConvertTo-Json -InputObject $InputObject -Depth 10
        [regex]::Replace($json, '[^\x00-\x7F]', { param($m) '\u{0:x4}' -f [int][char]$m.Value })
    }
}

switch ($PSCmdlet.ParameterSetName) {
    'Identity' {
        $assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $Identity).Path)
        [System.Runtime.CompilerServices.RuntimeHelpers]::RunModuleConstructor($assembly.ManifestModule.ModuleHandle)
        [pscustomobject]@{ Declared = [IfcComBridgeComInspection]::DeclaredIdentity($assembly) } | ConvertTo-AsciiJson
    }
    'Export' {
        $assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $Library).Path)
        # Let Costura resolve the embedded dependencies that reflection and the exporter may need.
        # (Whether RegAsm itself, which does not run Costura, can read the library is checked separately
        # with RegAsm /regfile.)
        [System.Runtime.CompilerServices.RuntimeHelpers]::RunModuleConstructor($assembly.ManifestModule.ModuleHandle)
        $declared = [IfcComBridgeComInspection]::DeclaredIdentity($assembly)
        # The .NET type library exporter cannot save to a path with non-ASCII characters (TYPE_E_IOERROR;
        # TlbExp fails the same way). Export to a temporary ASCII path and copy the file.
        $TypeLibraryPath = [IO.Path]::GetFullPath($TypeLibraryPath)
        $exportPath = $TypeLibraryPath
        if ($TypeLibraryPath -match '[^\x00-\x7F]') {
            $exportPath = Join-Path ([IO.Path]::GetTempPath()) ('IfcComBridge-' + [Guid]::NewGuid().ToString('N') + '.tlb')
            if ($exportPath -match '[^\x00-\x7F]') {
                throw "The type library exporter cannot save to '$TypeLibraryPath' or to the TEMP folder: both paths contain non-ASCII characters. Set TEMP to a folder with an ASCII-only path."
            }
        }
        $events = [IfcComBridgeComInspection]::ExportTypeLibrary($assembly, $exportPath)
        if ($exportPath -ne $TypeLibraryPath) {
            [IO.File]::Copy($exportPath, $TypeLibraryPath, $true)
            [IO.File]::Delete($exportPath)
        }
        [pscustomobject]@{
            Declared     = $declared
            ExportEvents = @($events)
            TypeLibrary  = [IfcComBridgeComInspection]::DescribeTypeLibrary($TypeLibraryPath)
        } | ConvertTo-AsciiJson
    }
    'Describe' {
        [pscustomobject]@{ TypeLibrary = [IfcComBridgeComInspection]::DescribeTypeLibrary((Resolve-Path $DescribeTypeLibrary).Path) } | ConvertTo-AsciiJson
    }
    'Register' {
        [IfcComBridgeComInspection]::RegisterTypeLibrary((Resolve-Path $RegisterTypeLibrary).Path)
        [pscustomobject]@{ Registered = (Resolve-Path $RegisterTypeLibrary).Path } | ConvertTo-AsciiJson
    }
    'Unregister' {
        $hr = [IfcComBridgeComInspection]::UnregisterTypeLibrary([Guid]$UnregisterTypeLibrary)
        [pscustomobject]@{ Unregistered = $UnregisterTypeLibrary; HResult = ('0x{0:X8}' -f $hr) } | ConvertTo-AsciiJson
    }
}
