using System;
using System.Windows;
using MinecraftLauncher.Core;
using MinecraftLauncher.Models;
using MinecraftLauncher.Views;

namespace MinecraftLauncher.Views
{
    public partial class RegistrationWindow : Window
    {
        bool success = new DatabaseManager().RegisterUser(txtUsername.Text, txtPassword.Password);

        public RegistrationWindow()
        {
            InitializeComponent();
        }

        private void btnViewLicense_Click(object sender, RoutedEventArgs e)
        {
            var licenseWindow = new LicenseAgreementWindow();
            licenseWindow.Owner = this;
            bool? result = licenseWindow.ShowDialog();

            if (result == true && licenseWindow.IsAgreed)
            {
                MessageBox.Show("Теперь вы можете завершить регистрацию", "Успешно",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void btnRegister_Click(object sender, RoutedEventArgs e)
        {
            string username = txtUsername.Text.Trim();
            string email = txtEmail.Text.Trim();
            string password = txtPassword.Password;
            string confirmPassword = txtConfirmPassword.Password;
            string minecraftUsername = txtMinecraftUsername.Text.Trim();

            // Проверка данных
            if (string.IsNullOrEmpty(username))
            {
                MessageBox.Show("Введите имя пользователя", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (password.Length < 6)
            {
                MessageBox.Show("Пароль должен содержать минимум 6 символов", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (password != confirmPassword)
            {
                MessageBox.Show("Пароли не совпадают", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Проверка лицензионного соглашения
            var licenseWindow = new LicenseAgreementWindow();
            licenseWindow.Owner = this;
            bool? result = licenseWindow.ShowDialog();

            if (result != true || !licenseWindow.IsAgreed)
            {
                MessageBox.Show("Вы должны принять лицензионное соглашение для регистрации", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Создание пользователя
            var user = new User
            {
                Username = username,
                Email = string.IsNullOrEmpty(email) ? null : email,
                IsAgreedToLicense = true,
                MinecraftUsername = string.IsNullOrEmpty(minecraftUsername) ? username : minecraftUsername
            };
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}