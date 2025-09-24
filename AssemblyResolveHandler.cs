using System;
using System.IO;
using System.Reflection;
using System.Windows;

namespace MinecraftLauncher
{
    public static class AssemblyResolveHandler
    {
        public static void RegisterHandler()
        {
            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
        }

        private static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            // Пытаемся найти проблемные сборки
            string assemblyName = new AssemblyName(args.Name).Name;

            if (assemblyName == "MaterialDesignColors" || assemblyName == "MaterialDesignThemes.Wpf")
            {
                string assemblyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", $"{assemblyName}.dll");

                if (!File.Exists(assemblyPath))
                {
                    assemblyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"{assemblyName}.dll");
                }

                if (File.Exists(assemblyPath))
                {
                    return Assembly.LoadFrom(assemblyPath);
                }
            }

            return null;
        }
    }
}