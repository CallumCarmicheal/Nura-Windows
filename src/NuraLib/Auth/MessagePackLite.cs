using System.Buffers.Binary;
using System.Text;

namespace NuraLib.Auth;

internal static class MessagePackLite {
    public static byte[] SerializeMap(IReadOnlyDictionary<string, object?> map) {
        var writer = new Writer();
        writer.WriteMap(map);
        return writer.ToArray();
    }

    public static object? Deserialize(ReadOnlySpan<byte> data) {
        var reader = new Reader(data);
        var value = reader.ReadValue();
        if (!reader.IsAtEnd) {
            throw new InvalidOperationException("unexpected trailing bytes in MessagePack payload");
        }

        return value;
    }

    private sealed class Writer {
        private readonly List<byte> _buffer = [];

        public void WriteMap(IReadOnlyDictionary<string, object?> map) {
            WriteMapHeader(map.Count);
            foreach (var entry in map) {
                WriteString(entry.Key);
                WriteValue(entry.Value);
            }
        }

        public byte[] ToArray() => [.. _buffer];

        private void WriteValue(object? value) {
            switch (value) {
            case null:
                _buffer.Add(0xc0);
                return;
            case string text:
                WriteString(text);
                return;
            case bool flag:
                _buffer.Add(flag ? (byte)0xc3 : (byte)0xc2);
                return;
            case byte number:
                WriteUnsigned(number);
                return;
            case short number:
                WriteSigned(number);
                return;
            case int number:
                WriteSigned(number);
                return;
            case long number:
                WriteSigned(number);
                return;
            case ushort number:
                WriteUnsigned(number);
                return;
            case uint number:
                WriteUnsigned(number);
                return;
            case ulong number:
                WriteUnsigned(number);
                return;
            case IReadOnlyDictionary<string, object?> nestedMap:
                WriteMap(nestedMap);
                return;
            case IDictionary<string, object?> dictionary:
                WriteMap(new Dictionary<string, object?>(dictionary));
                return;
            case IEnumerable<object?> sequence:
                WriteArray(sequence.ToArray());
                return;
            case byte[] bytes:
                WriteBinary(bytes);
                return;
            default:
                throw new InvalidOperationException($"unsupported MessagePack type: {value.GetType().FullName}");
            }
        }

        private void WriteArray(IReadOnlyList<object?> values) {
            if (values.Count <= 15) {
                _buffer.Add((byte)(0x90 | values.Count));
            } else if (values.Count <= ushort.MaxValue) {
                _buffer.Add(0xdc);
                WriteUInt16((ushort)values.Count);
            } else {
                _buffer.Add(0xdd);
                WriteUInt32((uint)values.Count);
            }

            foreach (var value in values) {
                WriteValue(value);
            }
        }

        private void WriteMapHeader(int count) {
            if (count <= 15) {
                _buffer.Add((byte)(0x80 | count));
            } else if (count <= ushort.MaxValue) {
                _buffer.Add(0xde);
                WriteUInt16((ushort)count);
            } else {
                _buffer.Add(0xdf);
                WriteUInt32((uint)count);
            }
        }

        private void WriteString(string value) {
            var bytes = Encoding.UTF8.GetBytes(value);
            if (bytes.Length <= 31) {
                _buffer.Add((byte)(0xa0 | bytes.Length));
            } else if (bytes.Length <= byte.MaxValue) {
                _buffer.Add(0xd9);
                _buffer.Add((byte)bytes.Length);
            } else if (bytes.Length <= ushort.MaxValue) {
                _buffer.Add(0xda);
                WriteUInt16((ushort)bytes.Length);
            } else {
                _buffer.Add(0xdb);
                WriteUInt32((uint)bytes.Length);
            }

            _buffer.AddRange(bytes);
        }

        private void WriteBinary(byte[] bytes) {
            if (bytes.Length <= byte.MaxValue) {
                _buffer.Add(0xc4);
                _buffer.Add((byte)bytes.Length);
            } else if (bytes.Length <= ushort.MaxValue) {
                _buffer.Add(0xc5);
                WriteUInt16((ushort)bytes.Length);
            } else {
                _buffer.Add(0xc6);
                WriteUInt32((uint)bytes.Length);
            }

            _buffer.AddRange(bytes);
        }

        private void WriteUnsigned(ulong value) {
            if (value <= 0x7f) {
                _buffer.Add((byte)value);
            } else if (value <= byte.MaxValue) {
                _buffer.Add(0xcc);
                _buffer.Add((byte)value);
            } else if (value <= ushort.MaxValue) {
                _buffer.Add(0xcd);
                WriteUInt16((ushort)value);
            } else if (value <= uint.MaxValue) {
                _buffer.Add(0xce);
                WriteUInt32((uint)value);
            } else {
                _buffer.Add(0xcf);
                WriteUInt64(value);
            }
        }

        private void WriteSigned(long value) {
            if (value >= 0) {
                WriteUnsigned((ulong)value);
                return;
            }

            if (value >= -32) {
                _buffer.Add(unchecked((byte)value));
            } else if (value >= sbyte.MinValue) {
                _buffer.Add(0xd0);
                _buffer.Add(unchecked((byte)(sbyte)value));
            } else if (value >= short.MinValue) {
                _buffer.Add(0xd1);
                WriteUInt16(unchecked((ushort)(short)value));
            } else if (value >= int.MinValue) {
                _buffer.Add(0xd2);
                WriteUInt32(unchecked((uint)(int)value));
            } else {
                _buffer.Add(0xd3);
                WriteUInt64(unchecked((ulong)value));
            }
        }

        private void WriteUInt16(ushort value) {
            Span<byte> scratch = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(scratch, value);
            _buffer.AddRange(scratch.ToArray());
        }

        private void WriteUInt32(uint value) {
            Span<byte> scratch = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(scratch, value);
            _buffer.AddRange(scratch.ToArray());
        }

        private void WriteUInt64(ulong value) {
            Span<byte> scratch = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64BigEndian(scratch, value);
            _buffer.AddRange(scratch.ToArray());
        }
    }

    private ref struct Reader {
        private readonly ReadOnlySpan<byte> _data;
        private int _offset;

        public Reader(ReadOnlySpan<byte> data) {
            _data = data;
            _offset = 0;
        }

        public bool IsAtEnd => _offset >= _data.Length;

        public object? ReadValue() {
            var prefix = ReadByte();

            if (prefix <= 0x7f) {
                return (long)prefix;
            }

            if ((prefix & 0xe0) == 0xa0) {
                return ReadString(prefix & 0x1f);
            }

            if ((prefix & 0xf0) == 0x80) {
                return ReadMap(prefix & 0x0f);
            }

            if ((prefix & 0xf0) == 0x90) {
                return ReadArray(prefix & 0x0f);
            }

            if (prefix >= 0xe0) {
                return (long)(sbyte)prefix;
            }

            return prefix switch {
                0xc0 => null,
                0xc2 => false,
                0xc3 => true,
                0xc4 => ReadBinary(ReadByte()),
                0xc5 => ReadBinary(ReadUInt16()),
                0xc6 => ReadBinary(checked((int)ReadUInt32())),
                0xca => ReadFloat32(),
                0xcb => ReadFloat64(),
                0xcc => (long)ReadByte(),
                0xcd => (long)ReadUInt16(),
                0xce => (long)ReadUInt32(),
                0xcf => ReadUInt64Checked(),
                0xd0 => (long)(sbyte)ReadByte(),
                0xd1 => (long)(short)ReadUInt16(),
                0xd2 => (long)(int)ReadUInt32(),
                0xd3 => (long)ReadUInt64(),
                0xd9 => ReadString(ReadByte()),
                0xda => ReadString(ReadUInt16()),
                0xdb => ReadString(checked((int)ReadUInt32())),
                0xdc => ReadArray(ReadUInt16()),
                0xdd => ReadArray(checked((int)ReadUInt32())),
                0xde => ReadMap(ReadUInt16()),
                0xdf => ReadMap(checked((int)ReadUInt32())),
                _ => throw new InvalidOperationException($"unsupported MessagePack prefix 0x{prefix:x2}")
            };
        }

        private Dictionary<string, object?> ReadMap(int count) {
            var map = new Dictionary<string, object?>(count, StringComparer.Ordinal);
            for (var i = 0; i < count; i++) {
                var keyValue = ReadValue();
                if (keyValue is not string key) {
                    throw new InvalidOperationException("MessagePack map key was not a string");
                }

                map[key] = ReadValue();
            }

            return map;
        }

        private List<object?> ReadArray(int count) {
            var list = new List<object?>(count);
            for (var i = 0; i < count; i++) {
                list.Add(ReadValue());
            }

            return list;
        }

        private string ReadString(int length) {
            EnsureAvailable(length);
            var value = Encoding.UTF8.GetString(_data[_offset..(_offset + length)]);
            _offset += length;
            return value;
        }

        private byte[] ReadBinary(int length) {
            EnsureAvailable(length);
            var value = _data[_offset..(_offset + length)].ToArray();
            _offset += length;
            return value;
        }

        private float ReadFloat32() {
            var scratch = ReadBinary(4);
            if (BitConverter.IsLittleEndian) {
                Array.Reverse(scratch);
            }

            return BitConverter.ToSingle(scratch);
        }

        private double ReadFloat64() {
            var scratch = ReadBinary(8);
            if (BitConverter.IsLittleEndian) {
                Array.Reverse(scratch);
            }

            return BitConverter.ToDouble(scratch);
        }

        private ulong ReadUInt64Checked() {
            var value = ReadUInt64();
            if (value > long.MaxValue) {
                throw new InvalidOperationException("uint64 value exceeds supported range");
            }

            return value;
        }

        private ulong ReadUInt64() {
            EnsureAvailable(8);
            var value = BinaryPrimitives.ReadUInt64BigEndian(_data[_offset..(_offset + 8)]);
            _offset += 8;
            return value;
        }

        private uint ReadUInt32() {
            EnsureAvailable(4);
            var value = BinaryPrimitives.ReadUInt32BigEndian(_data[_offset..(_offset + 4)]);
            _offset += 4;
            return value;
        }

        private ushort ReadUInt16() {
            EnsureAvailable(2);
            var value = BinaryPrimitives.ReadUInt16BigEndian(_data[_offset..(_offset + 2)]);
            _offset += 2;
            return value;
        }

        private byte ReadByte() {
            EnsureAvailable(1);
            return _data[_offset++];
        }

        private void EnsureAvailable(int count) {
            if (_offset + count > _data.Length) {
                throw new InvalidOperationException("unexpected end of MessagePack payload");
            }
        }
    }
}
