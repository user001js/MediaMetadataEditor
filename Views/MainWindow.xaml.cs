using System.Threading.Tasks;
using System.Windows;
using MediaMetadataEditor.Core;

namespace MediaMetadataEditor.Views
{
    public partial class MainWindow : Window
    {
        public MainViewModel VM { get; }

        public MainWindow()
        {
            InitializeComponent();
            VM = new MainViewModel();
            DataContext = new { VM };

            _ = HideIntroAfterDelay();
        }

        private async Task HideIntroAfterDelay()
        {
            try
            {
                await Task.Delay(5000);
                Dispatcher.Invoke(() => IntroOverlay.Visibility = Visibility.Collapsed);
            }
            catch { }
        }
    }
}
