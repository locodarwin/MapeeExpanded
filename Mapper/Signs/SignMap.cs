using NbtEditor;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using WorldEditor;

namespace Mapper
{
    // A sign in the world and what's written on it, front and back, a line each (Bedrock signs can hold more than
    // four). Hanging signs are told apart; wall and standing signs share one kind in the save
    public record Sign(Dimension Dimension, int X, int Y, int Z, bool Hanging, IReadOnlyList<string> Front, IReadOnlyList<string> Back)
    {
        public bool IsBlank => Front.All(string.IsNullOrWhiteSpace) && Back.All(string.IsNullOrWhiteSpace);

        // The lines run together with a space, as signs split their words across lines
        public string FrontText => SignText.Join(Front);
        public string BackText => SignText.Join(Back);

        // Both sides, the back after a slash
        public string Text => BackText.Length == 0 ? FrontText : FrontText.Length == 0 ? BackText : $"{FrontText} / {BackText}";

        // The first line with something on it, for a label
        public string Title => Front.Concat(Back).Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? string.Empty;

        public string Kind => Hanging ? "Hanging sign" : "Sign";

        public override string ToString() => $"{X}, {Y}, {Z}";
    }

    // One dimension's signs, as far as read, kept by the region they're in so an update reads again only the regions
    // the game has saved since
    public sealed class DimensionSigns
    {
        public Dimension Dimension { get; }

        // West to east, then north to south
        public IReadOnlyList<Sign> All { get; }

        internal IReadOnlyDictionary<Coords, List<Sign>> ByRegion { get; }

        // The regions read the last time, which say which shown signs can be known to be gone
        public IReadOnlyCollection<Coords> Read { get; }

        public DimensionSigns(Dimension dimension, Dictionary<Coords, List<Sign>> byRegion, IReadOnlyCollection<Coords> read)
        {
            Dimension = dimension;
            ByRegion = byRegion;
            Read = read;
            List<Sign> all = [.. byRegion.Values.SelectMany(s => s)];
            all.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.Y.CompareTo(b.Y));
            All = all;
        }

        public Sign? At(int x, int y, int z) =>
            ByRegion.TryGetValue(new Coords(x >> 9, z >> 9), out List<Sign>? signs) ? signs.FirstOrDefault(s => s.X == x && s.Y == y && s.Z == z) : null;
    }

    // Reading the signs of one dimension: Java's from each chunk's block entities, Bedrock's from each chunk's block
    // entity record
    public static class SignMap
    {
        public static List<Sign> Read(SceneInfo scene, CancellationToken cancellation = default) => [.. Update(null, scene, null, cancellation).All];

        public static DimensionSigns None(Dimension dimension) => new(dimension, [], []);

        // earlier, read again in the regions given; everything when there's nothing earlier or no regions are given
        public static DimensionSigns Update(DimensionSigns? earlier, SceneInfo scene, IReadOnlyCollection<Coords>? regions, CancellationToken cancellation = default)
        {
            HashSet<Coords> exist = [.. scene.Regions];
            bool whole = earlier is null || regions is null;
            List<Coords> toRead = whole ? [.. exist] : [.. regions!.Where(exist.Contains)];

            Dictionary<Coords, List<Sign>> byRegion = whole ? [] : earlier!.ByRegion.Where(r => exist.Contains(r.Key) && !regions!.Contains(r.Key)).ToDictionary();
            foreach (Coords region in toRead) byRegion.Remove(region);

            ConcurrentBag<(Coords Region, Sign Sign)> found = [];
            if (scene.RegionStore is BedrockRegionStore bedrock) Bedrock(scene.Dimension, bedrock, [.. toRead], found, cancellation);
            else Java(scene, toRead, found, cancellation);

            foreach (var (region, sign) in found)
            {
                if (!byRegion.TryGetValue(region, out List<Sign>? list)) byRegion[region] = list = [];
                list.Add(sign);
            }
            return new DimensionSigns(scene.Dimension, byRegion, toRead);
        }

        private static void Java(SceneInfo scene, List<Coords> regions, ConcurrentBag<(Coords, Sign)> found, CancellationToken cancellation)
        {
            ChunkEnumerator enumerator = new(2, 8, () => scene.RegionStore, new ChunkEnumeratorFromRegionFactory(_ => new SignReader(scene.Dimension, found))) { Light = false };
            enumerator.Enumerate([.. regions], new Cancellable(cancellation));
            cancellation.ThrowIfCancellationRequested();
        }

        // The signs among a Java chunk's block entities: block_entities from 1.18, Level.TileEntities before
        public static IEnumerable<Sign> InChunk(Dimension dimension, CompoundTag chunk)
        {
            CompoundTag root = chunk["Level"] is CompoundTag level ? level : chunk;
            if ((root["block_entities"] ?? root["TileEntities"]) is not ListTag entities) yield break;
            foreach (Tag tag in entities)
            {
                if (tag is CompoundTag entity && FromEntity(dimension, entity) is Sign sign) yield return sign;
            }
        }

        // A sign's block entity, either edition's: Java's minecraft:sign and minecraft:hanging_sign (Sign before 1.11),
        // Bedrock's Sign and HangingSign. Java keeps front_text and back_text from 1.20, four Text lines before;
        // Bedrock keeps FrontText and BackText from 1.19.80, one Text before
        public static Sign? FromEntity(Dimension dimension, CompoundTag entity)
        {
            if (entity["id"] is not StringTag id) return null;
            string name = id.InternalValue.StartsWith("minecraft:") ? id.InternalValue["minecraft:".Length..] : id.InternalValue;
            bool hanging;
            if (name is "sign" or "Sign") hanging = false;
            else if (name is "hanging_sign" or "HangingSign") hanging = true;
            else return null;

            if (Int(entity["x"]) is not int x || Int(entity["y"]) is not int y || Int(entity["z"]) is not int z) return null;

            List<string> front, back;
            if (entity["front_text"] is CompoundTag javaFront)
            {
                front = JavaLines(javaFront);
                back = entity["back_text"] is CompoundTag javaBack ? JavaLines(javaBack) : [];
            }
            else if (entity["FrontText"] is CompoundTag bedrockFront)
            {
                front = BedrockLines(bedrockFront);
                back = entity["BackText"] is CompoundTag bedrockBack ? BedrockLines(bedrockBack) : [];
            }
            else if (entity["Text1"] is not null)
            {
                front = [.. new[] { "Text1", "Text2", "Text3", "Text4" }.Select(k => entity[k] is Tag line ? SignText.Plain(line) : string.Empty)];
                back = [];
            }
            else
            {
                front = BedrockLines(entity);
                back = [];
            }
            return new Sign(dimension, x, y, z, hanging, Trim(front), Trim(back));
        }

        private static int? Int(Tag? tag) => tag is Int32Tag value ? value.InternalValue : null;

        private static List<string> JavaLines(CompoundTag side) =>
            side["messages"] is ListTag messages ? [.. messages.Select(SignText.Plain)] : [];

        private static List<string> BedrockLines(CompoundTag side) =>
            side["Text"] is StringTag text ? [.. SignText.StripCodes(text.InternalValue).Split('\n')] : [];

        // Blank lines at the end are left off
        private static List<string> Trim(List<string> lines)
        {
            for (int i = 0; i < lines.Count; i++) lines[i] = lines[i].Trim();
            while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
            return lines;
        }

        private static void Bedrock(Dimension dimension, BedrockRegionStore store, HashSet<Coords> regions, ConcurrentBag<(Coords, Sign)> found, CancellationToken cancellation)
        {
            store.Ready.Wait(cancellation);
            if (store.World is not BedrockWorld world || regions.Count == 0) return;

            ConcurrentBag<byte[]> keys = [];
            world.Database.ForEachKey(key => BedrockKey.TryParse(key, out BedrockKey parsed) && parsed.Tag == BedrockChunkTag.BlockEntity
                && parsed.Dimension == store.Dimension && regions.Contains(new Coords(parsed.X >> 5, parsed.Z >> 5)), key => keys.Add([.. key]));
            cancellation.ThrowIfCancellationRequested();

            foreach (byte[] key in keys)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!BedrockKey.TryParse(key, out BedrockKey parsed) || !world.Database.TryGet(key, out byte[]? value) || value is null) continue;
                Coords region = new(parsed.X >> 5, parsed.Z >> 5);
                foreach (Sign sign in InRecord(dimension, value)) found.Add((region, sign));
            }
        }

        // A Bedrock chunk's block entity record: one little-endian compound after another
        public static List<Sign> InRecord(Dimension dimension, byte[] value)
        {
            List<Sign> output = [];
            FastBufferProvider buffer = new(value);
            UnsafeNbtReader reader = new(buffer, ArrayPoolAllocation.CreateDefault()) { IsFormatLittleEndian = true };
            try
            {
                while (buffer.Index < value.Length)
                {
                    if (Tag.FromNbtReader(reader) is CompoundTag entity && FromEntity(dimension, entity) is Sign sign) output.Add(sign);
                }
            }
            catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException or InvalidDataException or NotSupportedException)
            {
                // A damaged record keeps what was read before the damage
            }
            return output;
        }

        // Reads each Java chunk's signs and nothing else, with the region of the chunk
        private sealed class SignReader(Dimension dimension, ConcurrentBag<(Coords, Sign)> found) : IObjectReader<ChunkParamater, IChunk?>
        {
            public IChunk? Read(ChunkParamater input)
            {
                CompoundTag root = input.Level["Level"] is CompoundTag level ? level : input.Level;
                if (root["xPos"] is not Int32Tag x || root["zPos"] is not Int32Tag z) return null;
                Coords region = new(x.InternalValue >> 5, z.InternalValue >> 5);
                foreach (Sign sign in InChunk(dimension, input.Level)) found.Add((region, sign));
                return null;
            }
        }

        private sealed class Cancellable(CancellationToken cancellation) : IEnumerationBody
        {
            public void BeginCycle() => cancellation.ThrowIfCancellationRequested();
            public void EndCycle() { }
            public void BeginReadingRegion(int index, Coords region) { }
            public void EndReadingRegion(int index, Coords region) { }
            public void EndReadingChunk(int regionIndex, int chunkIndex, IChunk chunk) { }
        }
    }

    // The words on a sign, without the formatting the game saves around them
    public static class SignText
    {
        public static string Join(IEnumerable<string> lines) => string.Join(" ", lines.Select(l => l.Trim()).Where(l => l.Length > 0));

        // Whether a sign says every word, on either side, in any case
        public static bool Says(Sign sign, IReadOnlyCollection<string> words)
        {
            if (words.Count == 0) return true;
            string text = sign.Text;
            return words.All(w => text.Contains(w, StringComparison.CurrentCultureIgnoreCase));
        }

        // A line as Java saves it: JSON text ({"text":"..."}, "...", or a list), or from 1.21.5 the same as NBT; plain
        // text in worlds from before 1.8
        public static string Plain(Tag tag)
        {
            StringBuilder output = new();
            switch (tag)
            {
                case StringTag text:
                    Json(text.InternalValue, output);
                    break;
                default:
                    Nbt(tag, output);
                    break;
            }
            return StripCodes(output.ToString());
        }

        private static void Json(string text, StringBuilder output)
        {
            string trimmed = text.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] is not ('{' or '[' or '"'))
            {
                output.Append(text);
                return;
            }
            try
            {
                using JsonDocument document = JsonDocument.Parse(text);
                Json(document.RootElement, output);
            }
            catch (JsonException)
            {
                output.Append(text);
            }
        }

        private static void Json(JsonElement element, StringBuilder output)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    output.Append(element.GetString());
                    break;
                case JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False:
                    output.Append(element.GetRawText());
                    break;
                case JsonValueKind.Array:
                    foreach (JsonElement part in element.EnumerateArray()) Json(part, output);
                    break;
                case JsonValueKind.Object:
                    if (element.TryGetProperty("text", out JsonElement text)) Json(text, output);
                    else if (element.TryGetProperty("translate", out JsonElement key) && key.ValueKind == JsonValueKind.String) output.Append(key.GetString());
                    if (element.TryGetProperty("extra", out JsonElement extra)) Json(extra, output);
                    break;
            }
        }

        private static void Nbt(Tag tag, StringBuilder output)
        {
            switch (tag)
            {
                case StringTag value:
                    output.Append(value.InternalValue);
                    break;
                case ListTag list:
                    foreach (Tag part in list) Nbt(part, output);
                    break;
                case CompoundTag compound:
                    if (compound["text"] is Tag text) Nbt(text, output);
                    else if (compound["translate"] is StringTag key) output.Append(key.InternalValue);
                    if (compound["extra"] is Tag extra) Nbt(extra, output);
                    break;
            }
        }

        // § and the character after it set a colour or style
        public static string StripCodes(string text)
        {
            if (!text.Contains('§')) return text;
            StringBuilder output = new(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '§')
                {
                    i++;
                    continue;
                }
                output.Append(text[i]);
            }
            return output.ToString();
        }
    }
}
