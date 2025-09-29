using System.Windows;
using System.Windows.Input;

namespace PinTransferWPF
{
    public partial class ScriptNameDialog : Window
    {
        public string ScriptName { get; private set; }

        public ScriptNameDialog(string currentName = "")
        {
            InitializeComponent();
            txtScriptName.Text = currentName;
            txtScriptName.SelectAll();
            txtScriptName.Focus();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                this.DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtScriptName.Text))
            {
                MessageBox.Show("Please enter a script name.", "Invalid Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ScriptName = txtScriptName.Text.Trim();
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}