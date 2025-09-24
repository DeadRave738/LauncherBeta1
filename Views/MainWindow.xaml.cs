using MinecraftLauncher.Core;
using MinecraftLauncher.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace SMLauncherCS
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }
        private void btnStartForge_Click(object sender, RoutedEventArgs e) // Теперь внутри класса
        {
            try
            {
                // Получаем текущего пользователя
                User currentUser = null;
                using (var db = new DatabaseManager())
                {
                    var users = db.GetUsers();
                    if (users.Count > 0)
                    {
                        currentUser = users[0];
                    }
                }

                if (currentUser == null)
                {
                    MessageBox.Show("Пожалуйста, войдите в аккаунт", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var clientManager = new MinecraftClientManager();
                clientManager.StartMinecraft(currentUser); // Запускаем только Forge версию
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Ошибка при запуске игры:\n\n{ex.Message}",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

}
