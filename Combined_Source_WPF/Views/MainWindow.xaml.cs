using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Combined_Source_WPF.ViewModels;

namespace Combined_Source_WPF.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        // ==========================================
        // Window Control Buttons (Custom Title Bar)
        // ==========================================
        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
            {
                this.WindowState = WindowState.Normal;
                PathMaximize.Data = System.Windows.Media.Geometry.Parse("M 0 0 L 10 0 L 10 10 L 0 10 Z");
            }
            else
            {
                this.WindowState = WindowState.Maximized;
                PathMaximize.Data = System.Windows.Media.Geometry.Parse("M 2 0 L 10 0 L 10 8 L 8 8 L 8 10 L 0 10 L 0 2 L 2 2 Z M 2 2 L 8 2 L 8 8 L 2 8 Z");
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        // ==========================================
        // Window Closing - Clean up Resources
        // ==========================================
        protected override void OnClosing(CancelEventArgs e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.StopServer();
            }
            base.OnClosing(e);
        }

        // ==========================================
        // Drag and Drop support
        // ==========================================
        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && DataContext is MainViewModel viewModel)
                {
                    viewModel.HandleFileDrop(files);
                }
            }
        }

        // Log Auto Scroll
        private void TxtLog_TextChanged(object sender, TextChangedEventArgs e)
        {
            TxtLog.ScrollToEnd();
        }
    }
}