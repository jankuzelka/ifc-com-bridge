using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace IfcComBridge.Tests.Infrastructure
{
    /// <summary>
    /// Exports the type library of an assembly in memory, as TlbExp and RegAsm /tlb do, and reads it back
    /// through ITypeLib/ITypeInfo. Nothing is saved and nothing is registered.
    /// </summary>
    public static class TypeLibraryReader
    {
        public static ComTypes.ITypeLib Export(Assembly assembly, out IReadOnlyList<string> exporterEvents)
        {
            var sink = new ExporterSink();
            // The file name only becomes the library's path if the library is saved, which it never is.
            string unsavedPath = Path.Combine(Path.GetTempPath(), "IfcComBridge.Tests", Guid.NewGuid().ToString("N") + ".tlb");
            object library = new TypeLibConverter().ConvertAssemblyToTypeLib(assembly, unsavedPath, TypeLibExporterFlags.None, sink);
            exporterEvents = sink.Events;
            return (ComTypes.ITypeLib)library;
        }

        public static ComTypes.TYPELIBATTR LibraryAttributes(ComTypes.ITypeLib library)
        {
            library.GetLibAttr(out IntPtr ptr);
            try { return Marshal.PtrToStructure<ComTypes.TYPELIBATTR>(ptr); }
            finally { library.ReleaseTLibAttr(ptr); }
        }

        public static ComTypes.TYPEATTR Attributes(ComTypes.ITypeInfo type)
        {
            type.GetTypeAttr(out IntPtr ptr);
            try { return Marshal.PtrToStructure<ComTypes.TYPEATTR>(ptr); }
            finally { type.ReleaseTypeAttr(ptr); }
        }

        public static string Name(ComTypes.ITypeInfo type)
        {
            type.GetDocumentation(-1, out string name, out _, out _, out _);
            return name;
        }

        public static IEnumerable<ComTypes.ITypeInfo> Types(ComTypes.ITypeLib library)
        {
            int count = library.GetTypeInfoCount();
            for (int i = 0; i < count; i++)
            {
                library.GetTypeInfo(i, out ComTypes.ITypeInfo type);
                yield return type;
            }
        }

        /// <summary>The implemented interfaces of a coclass or the base of an interface, with their IMPLTYPEFLAGS.</summary>
        public static IEnumerable<(string Name, ComTypes.IMPLTYPEFLAGS Flags)> ImplementedTypes(ComTypes.ITypeInfo type)
        {
            for (int i = 0; i < Attributes(type).cImplTypes; i++)
            {
                type.GetRefTypeOfImplType(i, out int href);
                type.GetRefTypeInfo(href, out ComTypes.ITypeInfo implemented);
                type.GetImplTypeFlags(i, out ComTypes.IMPLTYPEFLAGS flags);
                yield return (Name(implemented), flags);
            }
        }

        /// <summary>For a dual dispinterface: the vtable interface behind it (GetRefTypeOfImplType(-1)).</summary>
        public static ComTypes.ITypeInfo VtableInterfaceOfDual(ComTypes.ITypeInfo dispinterface)
        {
            dispinterface.GetRefTypeOfImplType(-1, out int href);
            dispinterface.GetRefTypeInfo(href, out ComTypes.ITypeInfo vtable);
            return vtable;
        }

        /// <summary>
        /// One line per function declared by the type itself: DispId, vtable offset, name, parameters and
        /// return type as automation types, e.g. "7 @112 SaveIfc([in] VT_BSTR ifcFile, [in,opt=0] VT_I4 modelIndex) : VT_HRESULT".
        /// </summary>
        public static IEnumerable<string> Functions(ComTypes.ITypeInfo type)
        {
            for (int i = 0; i < Attributes(type).cFuncs; i++)
            {
                type.GetFuncDesc(i, out IntPtr ptr);
                try
                {
                    var func = Marshal.PtrToStructure<ComTypes.FUNCDESC>(ptr);
                    var names = new string[func.cParams + 1];
                    type.GetNames(func.memid, names, names.Length, out int nameCount);
                    int elementSize = Marshal.SizeOf<ComTypes.ELEMDESC>();
                    var parameters = Enumerable.Range(0, func.cParams).Select(p =>
                    {
                        var element = Marshal.PtrToStructure<ComTypes.ELEMDESC>(func.lprgelemdescParam + p * elementSize);
                        string name = p + 1 < nameCount ? " " + names[p + 1] : string.Empty;
                        return $"[{ParameterFlags(element)}] {TypeName(element.tdesc)}{name}";
                    });
                    yield return $"{func.memid} @{func.oVft} {names[0]}({string.Join(", ", parameters)}) : {TypeName(func.elemdescFunc.tdesc)}";
                }
                finally
                {
                    type.ReleaseFuncDesc(ptr);
                }
            }
        }

        private static string ParameterFlags(ComTypes.ELEMDESC element)
        {
            var flags = element.desc.paramdesc.wParamFlags;
            var parts = new List<string>();
            if (flags.HasFlag(ComTypes.PARAMFLAG.PARAMFLAG_FIN)) parts.Add("in");
            if (flags.HasFlag(ComTypes.PARAMFLAG.PARAMFLAG_FOUT)) parts.Add("out");
            if (flags.HasFlag(ComTypes.PARAMFLAG.PARAMFLAG_FRETVAL)) parts.Add("retval");
            if (flags.HasFlag(ComTypes.PARAMFLAG.PARAMFLAG_FOPT))
            {
                string value = string.Empty;
                if (flags.HasFlag(ComTypes.PARAMFLAG.PARAMFLAG_FHASDEFAULT))
                {
                    // PARAMDESCEX: ULONG cBytes, then the default VARIANT (8-byte aligned on x64).
                    IntPtr variant = element.desc.paramdesc.lpVarValue + IntPtr.Size;
                    value = "=" + Marshal.GetObjectForNativeVariant(variant);
                }
                parts.Add("opt" + value);
            }
            return string.Join(",", parts);
        }

        private static string TypeName(ComTypes.TYPEDESC type)
        {
            var vt = (VarEnum)type.vt;
            if (vt == VarEnum.VT_PTR)
                return "VT_PTR:" + TypeName(Marshal.PtrToStructure<ComTypes.TYPEDESC>(type.lpValue));
            return vt.ToString();
        }

        private sealed class ExporterSink : ITypeLibExporterNotifySink
        {
            public List<string> Events { get; } = new List<string>();

            public void ReportEvent(ExporterEventKind eventKind, int eventCode, string eventMsg) =>
                Events.Add($"{eventKind} {eventCode}: {eventMsg}");

            public object ResolveRef(Assembly assembly)
            {
                // Only mscorlib can be referenced (IDisposable); use the type library the .NET Framework ships.
                if (assembly == typeof(object).Assembly)
                {
                    LoadTypeLibEx(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "mscorlib.tlb"), RegKind.None, out ComTypes.ITypeLib mscorlib);
                    return mscorlib;
                }
                throw new InvalidOperationException("Unexpected type library reference: " + assembly.FullName);
            }
        }

        private enum RegKind { None = 2 }

        [DllImport("oleaut32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void LoadTypeLibEx(string file, RegKind regKind, out ComTypes.ITypeLib library);
    }
}
