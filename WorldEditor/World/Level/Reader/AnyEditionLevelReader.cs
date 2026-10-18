namespace WorldEditor
{
    // Reads a level.dat from either edition, telling them apart by Bedrock's db folder
    public class AnyEditionLevelReader : IObjectReader<string, Level?>
    {
        public IObjectReader<string, Level?> JavaReader { get; set; } = new LevelReader();
        public IObjectReader<string, Level?> BedrockReader { get; set; } = new BedrockLevelReader();

        public Level? Read(string file)
        {
            string? directory = Path.GetDirectoryName(file);
            if (directory is not null && BedrockLevelReader.IsBedrockWorld(directory))
            {
                return BedrockReader.Read(file);
            }

            return JavaReader.Read(file);
        }

        // Where the launchers keep worlds, Java first. On Windows, Bedrock moved its worlds to Roaming
        // in 1.21.120, with one folder per account plus a Shared one; older installs used the UWP package
        public static IList<string> DefaultWorldDirectories()
        {
            if (!OperatingSystem.IsWindows()) return LinuxWorldDirectories();

            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            List<string> output = [Path.Combine(roaming, ".minecraft", "saves")];

            string bedrockUsers = Path.Combine(roaming, "Minecraft Bedrock", "Users");
            if (Directory.Exists(bedrockUsers))
            {
                foreach (string user in Directory.GetDirectories(bedrockUsers))
                {
                    output.Add(Path.Combine(user, "games", "com.mojang", "minecraftWorlds"));
                }
            }

            output.Add(Path.Combine(local, "Packages", "Microsoft.MinecraftUWP_8wekyb3d8bbwe", "LocalState", "games", "com.mojang", "minecraftWorlds"));
            return output;
        }

        // The Java launcher's saves, and Bedrock worlds from mcpelauncher, the usual way to run Bedrock on Linux
        private static List<string> LinuxWorldDirectories()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string data = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            return
            [
                Path.Combine(home, ".minecraft", "saves"),
                Path.Combine(data, "mcpelauncher", "games", "com.mojang", "minecraftWorlds"),
            ];
        }
    }
}
