"""Independent implementation of the IFC GloballyUniqueId encoding.

It generates the expected values used by IfcGuidTests. Kept as a SEPARATE implementation on
purpose: that is what makes those vectors evidence rather than a restatement of the C#.

    python3 tools/ifc_guid_reference.py 12345678-1234-5678-1234-567812345678
"""
import sys, uuid

ALPHABET = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$"


def to_ifc_guid(g: uuid.UUID) -> str:
    b = g.bytes_le  # matches .NET Guid.ToByteArray()
    groups = [
        b[3],
        b[2] * 65536 + b[1] * 256 + b[0],
        b[5] * 65536 + b[4] * 256 + b[7],
        b[6] * 65536 + b[8] * 256 + b[9],
        b[10] * 65536 + b[11] * 256 + b[12],
        b[13] * 65536 + b[14] * 256 + b[15],
    ]
    out = []
    for i, value in enumerate(groups):
        length = 2 if i == 0 else 4
        chunk = [""] * length
        for j in range(length):
            chunk[length - j - 1] = ALPHABET[value % 64]
            value //= 64
        out.append("".join(chunk))
    return "".join(out)


if __name__ == "__main__":
    for arg in sys.argv[1:] or ["12345678-1234-5678-1234-567812345678"]:
        print(f"{arg}  ->  {to_ifc_guid(uuid.UUID(arg))}")
