using System;
using System.Threading.Tasks;
using System.Windows;
using MinecraftLauncher.Core;

namespace MinecraftLauncher.Views
{
    public partial class DiagnosticsWindow : Window
    {
        public DiagnosticsWindow()
        {
            InitializeComponent();
        }

        private async void btnCheckInternet_Click(object sender, RoutedEventArgs e)
        {
            txtResults.Text = "Проверка подключения к интернету...\n";
            bool isConnected = await NetworkDiagnostics.CheckInternetConnectionAsync();

            if (isConnected)
            {
                txtResults.Text += "✓ Подключение к интернету доступно";
            }
            else
            {
                txtResults.Text += "✗ Подключение к интернету недоступно. Проверьте сетевые настройки.";
            }
        }

        private async void btnCheckServer_Click(object sender, RoutedEventArgs e)
        {
            txtResults.Text = "Проверка доступности сервера...\n";

            string gameFilesUrl = ConfigManager.GetAppSetting("GameFilesUrl") + ConfigManager.GetAppSetting("ManifestFile");
            string updateUrl = ConfigManager.GetAppSetting("UpdateCheckUrl");

            txtResults.Text += await NetworkDiagnostics.CheckUrlAvailabilityAsync(gameFilesUrl) + "\n\n";
            txtResults.Text += await NetworkDiagnostics.CheckUrlAvailabilityAsync(updateUrl);
        }

        private async void btnCheckAll_Click(object sender, RoutedEventArgs e)
        {
            txtResults.Text = "Запуск полной диагностики...\n\n";

            // Проверка интернета
            txtResults.Text += "1. Проверка подключения к интернету:\n";
            bool isConnected = await NetworkDiagnostics.CheckInternetConnectionAsync();
            txtResults.Text += isConnected ? "✓ Подключение к интернету доступно\n\n" : "✗ Подключение к интернету недоступно\n\n";

            if (!isConnected)
            {
                txtResults.Text += "Дальнейшая диагностика невозможна без интернета.";
                return;
            }

            // Проверка сервера
            txtResults.Text += "2. Проверка доступности сервера:\n";
            string gameFilesUrl = ConfigManager.GetAppSetting("GameFilesUrl") + ConfigManager.GetAppSetting("ManifestFile");
            string updateUrl = ConfigManager.GetAppSetting("UpdateCheckUrl");

            txtResults.Text += await NetworkDiagnostics.CheckUrlAvailabilityAsync(gameFilesUrl) + "\n";
            txtResults.Text += await NetworkDiagnostics.CheckUrlAvailabilityAsync(updateUrl) + "\n\n";

            // Информация о сети
            txtResults.Text += "3. Информация о сети:\n";
            txtResults.Text += NetworkDiagnostics.GetNetworkInfo();
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}