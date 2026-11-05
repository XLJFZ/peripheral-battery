"""Compile the checked-in WPF shader with Windows' bundled D3D compiler."""
from pathlib import Path
import ctypes as c
source = Path(__file__).resolve().parents[1] / 'native/src/BluetoothBatteryMonitor.App/Assets/Shaders/LiquidRefraction.hlsl'
text = source.read_bytes()
compile = c.WinDLL('d3dcompiler_47.dll').D3DCompile
compile.argtypes = [c.c_void_p,c.c_size_t,c.c_char_p,c.c_void_p,c.c_void_p,c.c_char_p,c.c_char_p,c.c_uint,c.c_uint,c.POINTER(c.c_void_p),c.POINTER(c.c_void_p)]
compile.restype = c.c_long
code, errors = c.c_void_p(), c.c_void_p()
def blob_data(blob):
    vtable = c.cast(blob,c.POINTER(c.POINTER(c.c_void_p))).contents
    ptr = c.WINFUNCTYPE(c.c_void_p,c.c_void_p)(vtable[3])(blob)
    size = c.WINFUNCTYPE(c.c_size_t,c.c_void_p)(vtable[4])(blob)
    return c.string_at(ptr,size)
hr = compile(text,len(text),b'LiquidRefraction.hlsl',None,None,b'main',b'ps_2_0',0x8000,0,c.byref(code),c.byref(errors))
if errors.value: print(blob_data(errors).decode(errors='replace'))
if hr < 0: raise SystemExit(f'D3DCompile failed: {hr:#x}')
source.with_suffix('.ps').write_bytes(blob_data(code))
print('Compiled',source.with_suffix('.ps'))
