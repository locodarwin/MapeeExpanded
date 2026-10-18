namespace CommonUtilities.Data
{
    public static class DataPath
    {
        // Callers and zip entries spell paths with either slash; the readers compare them in the platform's own form
        public static string Normalize(string path)
        {
            return path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        }
    }
}
