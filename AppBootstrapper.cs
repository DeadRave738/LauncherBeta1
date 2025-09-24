using System;
using System.Windows;

namespace MinecraftLauncher
{
    public static class AppBootstrapper
    {
        [STAThread]
        public static void Main()
        {
            // Инициализация Material Design Themes
            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
    }
}