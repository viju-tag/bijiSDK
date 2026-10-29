using System;
using System.IO;
using System.Threading.Tasks;

namespace BijiSDK.OfflineChecks
{
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            TestSupport.Root = args.Length > 0 ? Path.GetFullPath(args[0]) : FindRoot();
            int failed = await OfflineSuite.RunAsync(Console.WriteLine);
            failed += await HttpChecks.RunAllAsync(Console.WriteLine);
            failed += await PagesChecks.RunAllAsync(Console.WriteLine);
            return failed == 0 ? 0 : 1;
        }

        private static string FindRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "contracts")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "clients")))
                    return directory.FullName;
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("Pass the bijiSDK project root as the first argument.");
        }
    }
}
