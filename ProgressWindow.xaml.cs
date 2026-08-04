using System;
using System.Windows;
using System.Windows.Threading;

namespace RevitToSupabasePlugin
{
    public partial class ProgressWindow : Window
    {
        public bool IsInterrupted { get; private set; } = false;

        public ProgressWindow()
        {
            InitializeComponent();
            Closing += ProgressWindow_Closing;
        }

        public void SetMaximum(int max)
        {
            progressBar.Maximum = max > 0 ? max : 1;
            progressBar.Value = 0;
            txtProgress.Text = $"0 / {max} Elementos Procesados";
            DoEvents();
        }

        public void UpdateProgress(int current, int total, string customMessage = "")
        {
            progressBar.Value = Math.Min(current, progressBar.Maximum);
            if (!string.IsNullOrEmpty(customMessage))
            {
                txtProgress.Text = customMessage;
            }
            else
            {
                txtProgress.Text = $"{current} / {total} Elementos Procesados";
            }
            DoEvents();
        }

        private void BtnInterrupt_Click(object sender, RoutedEventArgs e)
        {
            IsInterrupted = true;
            txtProgress.Text = "Interrumpiendo proceso...";
            btnInterrupt.IsEnabled = false;
            DoEvents();
        }

        private void ProgressWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            IsInterrupted = true;
        }

        private void DoEvents()
        {
            Dispatcher.Invoke(DispatcherPriority.Background, new Action(delegate { }));
        }
    }
}
