using System.Windows;

namespace MediaMetadataEditor.Views
{
    public partial class PromptWindow : Window
    {
        public string? Result { get; private set; }
        public PromptWindow(string message, string title = "Entrada", string defaultValue = "")
        {
            InitializeComponent();
            Title = title;
            LblMessage.Text = message;
            TxtInput.Text = defaultValue;
            Loaded += (s, e) => { TxtInput.Focus(); TxtInput.SelectAll(); };
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            Result = TxtInput.Text ?? "";
            DialogResult = true;
            Close();
        }
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Result = null;
            DialogResult = false;
            Close();
        }
    }
}
