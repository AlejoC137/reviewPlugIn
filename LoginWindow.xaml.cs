using System;
using System.Windows;
using System.Windows.Input;

namespace RevitToSupabasePlugin
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
            txtEmail.Text = string.IsNullOrEmpty(AuthenticationService.UserEmail) ? "alejandro@review.com" : AuthenticationService.UserEmail;
            this.Loaded += (s, e) =>
            {
                if (string.IsNullOrEmpty(txtEmail.Text))
                {
                    txtEmail.Focus();
                }
                else
                {
                    txtPassword.Focus();
                }
            };
        }

        private async void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            string email = txtEmail.Text.Trim();
            string password = txtPassword.Password;

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                txtStatus.Text = "Por favor ingresa tu correo y contraseña.";
                txtStatus.Foreground = System.Windows.Media.Brushes.DarkRed;
                txtStatus.Visibility = Visibility.Visible;
                return;
            }

            btnLogin.IsEnabled = false;
            btnCancel.IsEnabled = false;
            progLoading.Visibility = Visibility.Visible;
            txtStatus.Visibility = Visibility.Collapsed;

            try
            {
                var (ok, msg) = await AuthenticationService.LoginAsync(email, password);
                if (ok)
                {
                    DialogResult = true;
                    Close();
                }
                else
                {
                    txtStatus.Text = msg;
                    txtStatus.Foreground = System.Windows.Media.Brushes.DarkRed;
                    txtStatus.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"Error: {ex.Message}";
                txtStatus.Foreground = System.Windows.Media.Brushes.DarkRed;
                txtStatus.Visibility = Visibility.Visible;
            }
            finally
            {
                btnLogin.IsEnabled = true;
                btnCancel.IsEnabled = true;
                progLoading.Visibility = Visibility.Collapsed;
            }
        }

        private void TxtPassword_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnLogin_Click(sender, e);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
