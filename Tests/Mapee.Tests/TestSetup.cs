using System.Runtime.CompilerServices;

namespace Mapee.Tests
{
    internal static class TestSetup
    {
        // Resources/ is copied next to the test assembly; the libraries look for it in the working directory
        [ModuleInitializer]
        internal static void UseOutputFolder()
        {
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        }
    }
}
