using System;
using System.Windows;
using MinecraftLauncher;

namespace MinecraftLauncher
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Регистрируем обработчик разрешения сборок ДО любого использования Material Design
            AssemblyResolveHandler.RegisterHandler();

            base.OnStartup(e);

            // Устанавливаем обработчик глобальных исключений
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                LogException(args.ExceptionObject as Exception);
            };

            // Устанавливаем обработчик исключений в UI потоке
            DispatcherUnhandledException += (s, args) =>
            {
                LogException(args.Exception);
                args.Handled = true;
            };
        }

        private void LogException(Exception ex)
        {
            if (ex == null) return;

            string logPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MinecraftLauncher", "error.log");

            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath));
                string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n\n";
                System.IO.File.AppendAllText(logPath, logEntry);
            }
            catch
            {
                // Если не можем записать в лог, ничего не делаем
            }
        }
    }
}