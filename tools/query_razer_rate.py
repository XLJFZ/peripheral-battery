"""Read-only polling rate protocol check; sends no setting commands."""
import sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import time
import hid
from functools import reduce
from operator import xor
from windows_feature import FeatureDevice
d=next(d for d in hid.enumerate(0x1532,0xb7) if d['interface_number']==0)
f=FeatureDevice()
try:
    f.open_path(d['path'])
    for command in (0x85,0xc0):
        b=bytearray(91); b[2]=31; b[6]=1; b[8]=command
        b[89]=reduce(xor,b[3:89],0)
        f.send_feature_report(b);time.sleep(.06)
        r=f.get_feature_report(0,91)
        print(hex(command),r[:14].hex(),'checksum',reduce(xor,r[3:89],0)==r[89])
finally:
    f.close()
