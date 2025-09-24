using System;
using System.IO;
using System.Windows;

namespace MinecraftLauncher.Views
{
    public partial class LicenseAgreementWindow : Window
    {
        public bool IsAgreed { get; private set; } = false;

        public LicenseAgreementWindow()
        {
            InitializeComponent();
            LoadLicenseText();

            // Обработчик изменения состояния чекбокса
            chkAgree.Checked += (s, e) => btnAccept.IsEnabled = true;
            chkAgree.Unchecked += (s, e) => btnAccept.IsEnabled = false;
        }

        private void LoadLicenseText()
        {
            try
            {
                string licensePath = "Resources/License/license.txt";
                if (File.Exists(licensePath))
                {
                    txtLicense.Text = File.ReadAllText(licensePath);
                }
                else
                {
                    txtLicense.Text = "Текст лицензионного соглашения не найден.\n\n" +
                        "1. Вы соглашаетесь соблюдать правила сервера.\n" +
                        "2. Администрация оставляет за собой право изменять правила.\n" +
                        "3. Запрещено использование читов и модификаций, дающих преимущество.\n" +
                        "4. Администрация не несет ответственности за утерянные предметы.\n\n" +
                        "Пожалуйста, свяжитесь с администрацией, если этот текст отображается некорректно.";
                }
            }
            catch
            {
                txtLicense.Text = "Не удалось загрузить текст лицензионного соглашения.";
            }
        }

        private void btnAccept_Click(object sender, RoutedEventArgs e)
        {
            IsAgreed = true;
            this.DialogResult = true;
            this.Close();
        }

        private void chkAgree_Checked(object sender, RoutedEventArgs e)
        {

        }
    }
}