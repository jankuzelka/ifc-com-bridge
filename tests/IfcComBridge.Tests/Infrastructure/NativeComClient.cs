using System;
using System.Runtime.InteropServices;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace IfcComBridge.Tests.Infrastructure
{
    /// <summary>
    /// Calls a managed object through its COM callable wrapper the way a native client does: raw interface
    /// pointers, vtable slots, BSTR/VARIANT arguments and HRESULTs. Late-bound calls go through
    /// IDispatch::GetIDsOfNames/Invoke (what PHP or VBScript do); early-bound calls go through vtable slots
    /// of an interface obtained with QueryInterface. No registration is involved: the object is created
    /// in-process, only activation (CoCreateInstance) is not covered.
    /// </summary>
    public sealed class NativeComClient : IDisposable
    {
        public const int S_OK = 0;
        public const int E_NOINTERFACE = unchecked((int)0x80004002);
        public const int DISP_E_UNKNOWNNAME = unchecked((int)0x80020006);
        public const int DISP_E_EXCEPTION = unchecked((int)0x80020009);
        public const int COR_E_ARGUMENTOUTOFRANGE = unchecked((int)0x80131502);

        private const short DISPATCH_METHOD = 1;
        private const int VariantSize = 24; // x64
        private static readonly Guid IID_NULL = Guid.Empty;

        private IntPtr _unknown;

        public NativeComClient(object managed)
        {
            if (IntPtr.Size != 8)
                throw new PlatformNotSupportedException("x64 only");
            _unknown = Marshal.GetIUnknownForObject(managed);
        }

        public int QueryInterface(Guid iid, out IntPtr pointer) => Marshal.QueryInterface(_unknown, ref iid, out pointer);

        // ---- late-bound: IDispatch (vtable slots 5 and 6) ----

        public int GetIDsOfNames(string name, out int dispId)
        {
            IntPtr dispatch = Interface(typeof(IDispatchMarker).GUID);
            try
            {
                var ids = new int[1];
                Guid iidNull = IID_NULL;
                int hr = Slot<GetIDsOfNamesFn>(dispatch, 5)(dispatch, ref iidNull, new[] { name }, 1, 0, ids);
                dispId = ids[0];
                return hr;
            }
            finally
            {
                Marshal.Release(dispatch);
            }
        }

        /// <summary>IDispatch::Invoke(DISPATCH_METHOD) with positional arguments, as scripting clients call.</summary>
        public int Invoke(int dispId, object[] args, out object result, out string errorDescription)
        {
            IntPtr dispatch = Interface(typeof(IDispatchMarker).GUID);
            IntPtr variants = Marshal.AllocCoTaskMem(VariantSize * Math.Max(1, args.Length));
            IntPtr resultVariant = Marshal.AllocCoTaskMem(VariantSize);
            IntPtr excepInfo = Marshal.AllocCoTaskMem(Marshal.SizeOf<ComTypes.EXCEPINFO>());
            try
            {
                // DISPPARAMS holds the arguments in reverse order.
                for (int i = 0; i < args.Length; i++)
                    Marshal.GetNativeVariantForObject(args[args.Length - 1 - i], variants + i * VariantSize);
                VariantInit(resultVariant);
                Zero(excepInfo, Marshal.SizeOf<ComTypes.EXCEPINFO>());
                var parameters = new ComTypes.DISPPARAMS { rgvarg = variants, cArgs = args.Length };
                Guid iidNull = IID_NULL;

                int hr = Slot<InvokeFn>(dispatch, 6)(dispatch, dispId, ref iidNull, 0, DISPATCH_METHOD, ref parameters, resultVariant, excepInfo, IntPtr.Zero);

                result = hr == S_OK ? Marshal.GetObjectForNativeVariant(resultVariant) : null;
                errorDescription = null;
                if (hr == DISP_E_EXCEPTION)
                {
                    errorDescription = Marshal.PtrToStructure<ComTypes.EXCEPINFO>(excepInfo).bstrDescription;
                    Marshal.DestroyStructure<ComTypes.EXCEPINFO>(excepInfo);
                }
                return hr;
            }
            finally
            {
                for (int i = 0; i < args.Length; i++)
                    VariantClear(variants + i * VariantSize);
                VariantClear(resultVariant);
                Marshal.FreeCoTaskMem(variants);
                Marshal.FreeCoTaskMem(resultVariant);
                Marshal.FreeCoTaskMem(excepInfo);
                Marshal.Release(dispatch);
            }
        }

        // ---- early-bound: vtable slots of a queried interface ----

        public static TDelegate Slot<TDelegate>(IntPtr pointer, int slot) where TDelegate : Delegate
        {
            IntPtr vtable = Marshal.ReadIntPtr(pointer);
            return Marshal.GetDelegateForFunctionPointer<TDelegate>(Marshal.ReadIntPtr(vtable, slot * IntPtr.Size));
        }

        public void Dispose()
        {
            if (_unknown != IntPtr.Zero)
            {
                Marshal.Release(_unknown);
                _unknown = IntPtr.Zero;
            }
        }

        private IntPtr Interface(Guid iid)
        {
            int hr = QueryInterface(iid, out IntPtr pointer);
            Marshal.ThrowExceptionForHR(hr);
            return pointer;
        }

        private static void Zero(IntPtr memory, int size)
        {
            for (int i = 0; i < size; i++)
                Marshal.WriteByte(memory, i, 0);
        }

        [ComImport, Guid("00020400-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDispatchMarker { }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetIDsOfNamesFn(IntPtr self, ref Guid riid,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] names, int count, int lcid, [Out] int[] dispIds);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int InvokeFn(IntPtr self, int dispId, ref Guid riid, int lcid, short flags,
            ref ComTypes.DISPPARAMS parameters, IntPtr result, IntPtr excepInfo, IntPtr argError);

        [DllImport("oleaut32.dll")]
        private static extern void VariantInit(IntPtr variant);

        [DllImport("oleaut32.dll")]
        private static extern int VariantClear(IntPtr variant);
    }
}
