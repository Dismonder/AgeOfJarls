using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace System.Runtime.CompilerServices
{
    /// <summary>The C# compiler emits a module initializer for this attribute; .NET Framework runs it, but lacks the type.</summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ModuleInitializerAttribute : Attribute
    {
    }
}

namespace AgeOfJarls.Tests
{
    /// <summary>
    /// The game's assemblies are referenced from the install and never copied (see Directory.Build.props), so the
    /// tests load them from there when the mod's code first needs them. Only plain data types are used (ZPackage,
    /// Vector3, enums): nothing that needs the Unity engine running.
    /// </summary>
    internal static class GameAssemblies
    {
        private static string[] s_folders;

        [ModuleInitializer]
        internal static void Init()
        {
            string game = Environment.GetEnvironmentVariable("VALHEIM_DIR");
            if (string.IsNullOrEmpty(game))
            {
                game = @"C:\Program Files (x86)\Steam\steamapps\common\Valheim";
            }
            s_folders = new[]
            {
                Path.Combine(game, @"valheim_Data\Managed"),
                Path.Combine(game, @"BepInEx\core"),
                Path.Combine(game, @"BepInEx\plugins\Jotunn"),
            };
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }

        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            string file = new AssemblyName(args.Name).Name + ".dll";
            foreach (string folder in s_folders)
            {
                string path = Path.Combine(folder, file);
                if (File.Exists(path))
                {
                    return Assembly.LoadFrom(path);
                }
            }
            return null;
        }
    }
}
