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

        private void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            string username = txtUsername.Text.Trim();
            string password = txtPassword.Password;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                MessageBox.Show("Пожалуйста, введите имя пользователя и пароль", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            progressBar.Visibility = Visibility.Visible;

            try
            {
                using (var db = new DatabaseManager())
                {
                    // Используем Task.Run для выполнения операции в фоновом потоке
                    User user = await Task.Run(() => db.LoginUser(username, password));

                    if (user != null)
                    {
                        // Если токены не установлены, генерируем их
                        if (string.IsNullOrEmpty(user.AccessToken) || string.IsNullOrEmpty(user.ClientToken))
                        {
                            user.AccessToken = SecurityManager.GenerateToken();
                            user.ClientToken = SecurityManager.GenerateToken();
                            await Task.Run(() => db.UpdateUserTokens(user.Id, user.AccessToken, user.ClientToken));
                        }

                        // Закрываем окно авторизации
                        this.DialogResult = true;
                        this.Close();
                    }
                    else
                    {
                        MessageBox.Show("Неверное имя пользователя или пароль", "Ошибка входа",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при входе: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                progressBar.Visibility = Visibility.Collapsed;
            }
        }

        private void TxtUsername_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                txtPassword.Focus();
                e.Handled = true;
            }
        }

        private void TxtPassword_PreviewKeyDown(object sender, KeyEventArgs e)
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

        private void BtnRegister_Click(object sender, RoutedEventArgs e)
        {
            var registrationWindow = new RegistrationWindow();
            registrationWindow.Owner = this;
            registrationWindow.ShowDialog();
        }

        private void BtnPrivacyPolicy_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Политика конфиденциальности вашего сервера...", "Политика конфиденциальности",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnDiagnostics_Click(object sender, RoutedEventArgs e)
        {
            var diagnosticsWindow = new DiagnosticsWindow();
            diagnosticsWindow.Owner = this;
            diagnosticsWindow.ShowDialog();
        }
    }
}