using NbtEditor;
using System.IO.Compression;
using System.Numerics;

namespace WorldEditor
{
    public class PlayerReader : IObjectReader<LevelArgs, Player>
    {
        public Player Read(LevelArgs input)
        {
            Player player = new Player();
            CompoundTag? playerCompound = input.Data.TryGetChild("Player", out Tag playerTag) ? playerTag as CompoundTag : null;
            playerCompound ??= ReadPlayerFile(input);
            if (playerCompound is not null) 
            {
                ReadPlayerPositions(playerCompound, out Vector3 pos, out Vector3 spawn);
                
                return new Player()
                {
                    Position = pos,
                    Spawn = spawn,
                    Dimension = ReadPlayerDimension(playerCompound),
                    GameType = ReadPlayerGameType(playerCompound)
                };
            }

            if (input.Data.TryGetChild("spawn", out Tag spawnTag) && spawnTag is CompoundTag spawnCompound) 
            {
                if (spawnCompound.TryGetChild("pos", out Tag posTag) && posTag is ArrayTag posArray
                    && posArray.InternalArary.Length == 3) 
                {
                    int[] posCastArray = (int[]) posArray.InternalArary;

                    Vector3 pos = new Vector3(posCastArray[0], posCastArray[1], posCastArray[2]);
                    return new Player()
                    {
                        Position = pos,
                        Dimension = Dimension.Overworld,
                        GameType = GameType.Creative
                    };
                }
            }

            return player;
        }

        // A 26.x level.dat no longer holds the player: it is players/data/<uuid>.dat, the uuid level.dat names as
        // singleplayer_uuid (four ints, read as hex)
        private static CompoundTag? ReadPlayerFile(LevelArgs input)
        {
            if (input.Level.Directory is null) return null;
            if (!input.Data.TryGetChild("singleplayer_uuid", out Tag uuidTag) || uuidTag is not ArrayTag uuidArray) return null;
            if (uuidArray.InternalArary is not int[] { Length: 4 } parts) return null;

            string hex = string.Concat(parts.Select(part => ((uint)part).ToString("x8")));
            string uuid = $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
            string file = Path.Combine(input.Level.Directory, "players", "data", uuid + ".dat");
            if (!File.Exists(file)) return null;

            try
            {
                using FileStream stream = File.OpenRead(file);
                using GZipStream gzip = new(stream, CompressionMode.Decompress);
                using MemoryStream bytes = new();
                gzip.CopyTo(bytes);
                return Tag.FromBytes(bytes.ToArray(), 0) as CompoundTag;
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static void ReadPlayerPositions(CompoundTag player, out Vector3 pos, out Vector3 spawn)
        {
            if (player.TryGetChild("Pos", out Tag posTag) && posTag is ListTag posArray)
            {
                pos = new Vector3((float)(double)posArray[0], (float)(double)posArray[1], (float)(double)posArray[2]);
            }
            else
            {
                pos = new Vector3();
            }

            int x = 0, y = 0, z = 0;
            if (player.TryGetChild("SpawnX", out Tag xTag) && xTag is ValueTag<int> xValueTag) 
            {
                x = xValueTag;
            }
            if (player.TryGetChild("SpawnY", out Tag yTag) && yTag is ValueTag<int> yValueTag)
            {
                y = yValueTag;
            }
            if (player.TryGetChild("SpawnZ", out Tag zTag) && zTag is ValueTag<int> zValueTag)
            {
                z = zValueTag;
            }

            // 26.x keeps a bed or anchor spawn as respawn: { pos: [x, y, z], dimension }
            if (!player.TryGetChild("SpawnX", out _) && player.TryGetChild(out Tag respawnPos, "respawn", "pos")
                && respawnPos is ArrayTag { InternalArary: int[] { Length: 3 } respawn })
            {
                (x, y, z) = (respawn[0], respawn[1], respawn[2]);
            }

            spawn = new Vector3(x, y, z);
        }
        private static Dimension ReadPlayerDimension(CompoundTag player) 
        {
            Tag? dimensionTag = player["Dimension"];
            if (dimensionTag is null) return Dimension.Overworld;

            if (dimensionTag.Id == TagId.Int32) 
            {
                return (int)dimensionTag switch
                {
                    0 => Dimension.Overworld,
                    1 => Dimension.Nether,
                    2 => Dimension.TheEnd,
                    _ => Dimension.Overworld
                };
            }

            if (dimensionTag.Id == TagId.String) 
            {
                string dimension = dimensionTag;
                return dimension switch
                {
                    "minecraft:overworld" => Dimension.Overworld,
                    "minecraft:the_nether" or "minecraft:nether" => Dimension.Nether,
                    "minecraft:the_end" or "minecraft:end" => Dimension.TheEnd,
                    _ => Dimension.Overworld
                };
            }

            return Dimension.Overworld;
        }
        private static GameType ReadPlayerGameType(CompoundTag player) 
        {
            Tag? gameTypeTag = player["playerGameType"];
            if (gameTypeTag is null || gameTypeTag.Id != TagId.Int32) return GameType.Survival;

            return (GameType)(int)gameTypeTag;
        }
    }
}
