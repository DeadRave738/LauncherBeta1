using System;
using System.Threading.Tasks;
using System.Windows;
using MinecraftLauncher.Core;
using MinecraftLauncher.Models;
using MinecraftLauncher.Views;

namespace MinecraftLauncher.Views
{
    public partial class AuthWindow : Window
    {
        public AuthWindow()
        {
            InitializeComponent();
            Loaded += (s, e) => txtUsername.Focus();
        }

        private async void btnLogin_Click(object sender, RoutedEventArgs e)
        {
            await AttemptLogin();
        }

        private void txtUsername_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                txtPassword.Focus();
                e.Handled = true;
            }
        }

        private async void txtPassword_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                await AttemptLogin();
                e.Handled = true;
            }
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter && System.Windows.Input.Keyboard.FocusedElement != txtUsername && System.Windows.Input.Keyboard.FocusedElement != txtPassword)
            {
                txtUsername.Focus();
                e.Handled = true;
            }
        }

        private async Task AttemptLogin()
{
    string username = txtUsername.Text.Trim();
    string password = txtPassword.Password;
    
    if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
    {
        MessageBox.Show("Пожалуйста, введите имя пользователя и пароль", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
    }
    
    progressBar.Visibility = Visibility.Visible;
    btnLogin.IsEnabled = false;
    this.Title = "Minecraft Launcher - Проверка данных...";
}

        private void btnRegister_Click(object sender, RoutedEventArgs e)
        {
            var registrationWindow = new RegistrationWindow();
            registrationWindow.Owner = this;
            registrationWindow.ShowDialog();
        }

        private void btnPrivacyPolicy_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Политика конфиденциальности вашего сервера...", "Политика конфиденциальности",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private void btnDiagnostics_Click(object sender, RoutedEventArgs e)
        {
            var diagnosticsWindow = new DiagnosticsWindow();
            diagnosticsWindow.Owner = this;
            diagnosticsWindow.ShowDialog();
        }
    }
}