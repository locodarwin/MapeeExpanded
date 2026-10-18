using Mapee.Tests.Fixtures;
using System.Text;
using WorldEditor;

namespace Mapee.Tests.Bedrock
{
    public class LevelDbTests
    {
        private static byte[] Bytes(string text) => Encoding.ASCII.GetBytes(text);

        private static BedrockTestWorld Database(byte? tableCompression)
        {
            BedrockTestWorld world = new();
            for (int i = 0; i < 20; i++) world.Put(Bytes($"key{i:D2}"), Bytes($"value {i}"));
            return world
                .Put(Bytes("other"), Bytes("x"))
                .Update(Bytes("key05"), Bytes("newer"))
                .Delete(Bytes("key07"))
                .Save(tableCompression: tableCompression);
        }

        // Bedrock compresses table blocks with raw deflate; older worlds used zlib
        [Theory]
        [InlineData(null)]
        [InlineData((byte)0)]
        [InlineData((byte)2)]
        [InlineData((byte)4)]
        public void ReadsTablesAndNewerLogWrites(byte? tableCompression)
        {
            using BedrockTestWorld world = Database(tableCompression);
            using LevelDbReader db = new(Path.Combine(world.Directory, "db"));

            Assert.True(db.TryGet(Bytes("key13"), out byte[]? value));
            Assert.Equal("value 13", Encoding.ASCII.GetString(value!));

            IDictionary<byte[], byte[]> keys = db.GetByPrefix(Bytes("key"));
            Assert.Equal(19, keys.Count);
            Assert.Equal("newer", Encoding.ASCII.GetString(keys[Bytes("key05")]));
            Assert.False(keys.ContainsKey(Bytes("key07")));
            Assert.False(db.TryGet(Bytes("key07"), out _));

            List<string> visited = [];
            db.ForEachKey(key => key.StartsWith("key0"u8), key => visited.Add(Encoding.ASCII.GetString(key)));
            Assert.Equal(9, visited.Count);
            Assert.DoesNotContain("key07", visited);
        }

        [Fact]
        public void ParsesOnlyChunkShapedKeys()
        {
            Assert.True(BedrockKey.TryParse(BedrockTestWorld.Key(-3, 7, 0, BedrockChunkTag.SubChunkPrefix, -2), out BedrockKey key));
            Assert.Equal((-3, 7, 0, BedrockChunkTag.SubChunkPrefix, (sbyte)-2), (key.X, key.Z, key.Dimension, key.Tag, key.SubChunkY));

            Assert.True(BedrockKey.TryParse(BedrockTestWorld.Key(1, 2, 1, BedrockChunkTag.Data3D), out key));
            Assert.Equal(1, key.Dimension);

            // "~local_player" is 13 bytes, a chunk key's length in another dimension
            Assert.False(BedrockKey.TryParse(Bytes("~local_player"), out _));
            Assert.False(BedrockKey.TryParse(Bytes("BiomeData"), out _));
        }
    }
}
