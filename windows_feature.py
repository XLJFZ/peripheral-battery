"""Windows feature-only HID access; no input stream or exclusive mouse handle."""
import ctypes
from ctypes import wintypes


class FeatureDevice:
    def __init__(self):
        self.handle = None
        self.kernel = ctypes.WinDLL('kernel32', use_last_error=True)
        self.hid = ctypes.WinDLL('hid', use_last_error=True)
        self.kernel.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD,
            wintypes.DWORD, ctypes.c_void_p, wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE]
        self.kernel.CreateFileW.restype = wintypes.HANDLE
        self.kernel.CloseHandle.argtypes = [wintypes.HANDLE]
        for name in ('HidD_SetFeature', 'HidD_GetFeature'):
            fn = getattr(self.hid, name)
            fn.argtypes = [wintypes.HANDLE, ctypes.c_void_p, wintypes.ULONG]
            fn.restype = wintypes.BOOLEAN

    def open_path(self, path):
        if isinstance(path, bytes):
            path = path.decode('utf-8')
        # Feature control transfers do not need GENERIC_READ/WRITE permission.
        handle = self.kernel.CreateFileW(path, 0, 3, None, 3, 0, None)
        if handle in (None, ctypes.c_void_p(-1).value):
            raise ctypes.WinError(ctypes.get_last_error())
        self.handle = handle

    def send_feature_report(self, data):
        buf = ctypes.create_string_buffer(bytes(data), len(data))
        if not self.hid.HidD_SetFeature(self.handle, buf, len(data)):
            raise ctypes.WinError(ctypes.get_last_error())
        return len(data)

    def get_feature_report(self, report_id, size):
        buf = ctypes.create_string_buffer(size)
        buf[0] = bytes([report_id])
        if not self.hid.HidD_GetFeature(self.handle, buf, size):
            raise ctypes.WinError(ctypes.get_last_error())
        return buf.raw

    def close(self):
        if self.handle is not None:
            self.kernel.CloseHandle(self.handle)
            self.handle = None
