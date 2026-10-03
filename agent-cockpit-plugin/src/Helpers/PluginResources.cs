namespace Loupedeck.AgentCockpitPlugin
{
    using System;
    using System.IO;
    using System.Reflection;

    internal static class PluginResources
    {
        private static Assembly? _assembly;

        public static void Init(Assembly assembly)
        {
            assembly.CheckNullArgument(nameof(assembly));
            _assembly = assembly;
        }

        public static String FindFile(String fileName) => _assembly!.FindFileOrThrow(fileName);

        public static BitmapImage ReadImage(String resourceName) =>
            _assembly!.ReadImage(FindFile(resourceName));

        public static Stream GetStream(String resourceName) =>
            _assembly!.GetStream(FindFile(resourceName));
    }
}
