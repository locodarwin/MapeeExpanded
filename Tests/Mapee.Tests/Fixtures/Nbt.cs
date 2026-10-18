using System.Buffers.Binary;
using System.Text;

namespace Mapee.Tests.Fixtures
{
    // Writes NBT, little-endian as Bedrock stores it or big-endian as Java does, enough of it for test worlds.
    // Values: byte, short, int, long, string, long[], List<object> (all one type), Dictionary<string, object>
    public static class Nbt
    {
        public static byte[] Compound(Dictionary<string, object> values, bool littleEndian = true)
        {
            using MemoryStream stream = new();
            Writer writer = new(stream, littleEndian);
            stream.WriteByte(10);
            writer.String(string.Empty);
            writer.Payload(values);
            return stream.ToArray();
        }

        public static Dictionary<string, object> Block(string name, Dictionary<string, object>? states = null)
        {
            return new() { ["name"] = name, ["states"] = states ?? [], ["version"] = 18100737 };
        }
        public static Dictionary<string, object> LegacyBlock(string name, short value)
        {
            return new() { ["name"] = name, ["val"] = value };
        }

        private static byte TagId(object value) => value switch
        {
            byte => 1,
            short => 2,
            int => 3,
            long => 4,
            string => 8,
            List<object> => 9,
            Dictionary<string, object> => 10,
            long[] => 12,
            _ => throw new ArgumentException($"Unsupported NBT value {value.GetType()}")
        };

        private sealed class Writer(Stream stream, bool littleEndian)
        {
            public void Payload(object value)
            {
                switch (value)
                {
                    case byte b:
                        stream.WriteByte(b);
                        break;
                    case short s:
                        Short(s);
                        break;
                    case int i:
                        Int(i);
                        break;
                    case long l:
                        Long(l);
                        break;
                    case string text:
                        String(text);
                        break;
                    case long[] longs:
                        Int(longs.Length);
                        foreach (long l in longs) Long(l);
                        break;
                    case List<object> list:
                        stream.WriteByte(list.Count == 0 ? (byte)0 : TagId(list[0]));
                        Int(list.Count);
                        foreach (object item in list) Payload(item);
                        break;
                    case Dictionary<string, object> compound:
                        foreach (var child in compound)
                        {
                            stream.WriteByte(TagId(child.Value));
                            String(child.Key);
                            Payload(child.Value);
                        }
                        stream.WriteByte(0);
                        break;
                    default:
                        throw new ArgumentException($"Unsupported NBT value {value.GetType()}");
                }
            }

            public void String(string text)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                Span<byte> length = stackalloc byte[2];
                if (littleEndian) BinaryPrimitives.WriteUInt16LittleEndian(length, (ushort)bytes.Length);
                else BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)bytes.Length);
                stream.Write(length);
                stream.Write(bytes);
            }

            private void Short(short value)
            {
                Span<byte> buffer = stackalloc byte[2];
                if (littleEndian) BinaryPrimitives.WriteInt16LittleEndian(buffer, value);
                else BinaryPrimitives.WriteInt16BigEndian(buffer, value);
                stream.Write(buffer);
            }
            private void Int(int value)
            {
                Span<byte> buffer = stackalloc byte[4];
                if (littleEndian) BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
                else BinaryPrimitives.WriteInt32BigEndian(buffer, value);
                stream.Write(buffer);
            }
            private void Long(long value)
            {
                Span<byte> buffer = stackalloc byte[8];
                if (littleEndian) BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
                else BinaryPrimitives.WriteInt64BigEndian(buffer, value);
                stream.Write(buffer);
            }
        }
    }
}
