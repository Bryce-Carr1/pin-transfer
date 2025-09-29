using Integration;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PinTransferWPF
{
    /// <summary>
    /// Interaction logic for LabwareDefinitionsWindow.xaml
    /// </summary>
    public partial class LabwareDefinitionsWindow : Window
    {
        private readonly LabwareManager _labwareManager;

        public LabwareDefinitionsWindow(string connectionString)
        {
            InitializeComponent();
            _labwareManager = new LabwareManager(connectionString);
            LoadLabware();

            Closed += LabwareDefinitionsWindow_Closed;
        }

        // Handle custom title bar dragging
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        // Handle custom close button
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void LabwareDefinitionsWindow_Closed(object sender, EventArgs e)
        {
            //_labwareManager.Close(); // Dispose the LabwareManager, which will handle connection closing
        }

        private void LoadLabware()
        {
            lstLabware.Items.Clear();
            var labwares = _labwareManager.GetAllLabware();
            foreach (var labware in labwares)
            {
                lstLabware.Items.Add($"{labware.Identifier}: H:{labware.Height}, NH:{labware.NestedHeight}, LV:{(labware.IsLowVolume == 1 ? "Yes" : "No")}, Y:{labware.OffsetY}, T:{labware.Type}");
            }

            // Select the item matching the current identifier if it exists
            if (!string.IsNullOrEmpty(txtPlateIdentifier.Text))
            {
                foreach (var item in lstLabware.Items)
                {
                    string itemString = item.ToString();
                    if (itemString.Contains(txtPlateIdentifier.Text + ":"))
                    {
                        lstLabware.SelectedItem = item;
                        break;
                    }
                }
            }
        }

        private void btnAdd_Click(object sender, RoutedEventArgs e)
        {
            var plateIdentifier = txtPlateIdentifier.Text.Trim();
            var plateHeightText = txtPlateHeight.Text.Trim();
            var nestedPlateHeightText = txtNestedPlateHeight.Text.Trim();
            var isLowVolume = chkIsLowVolume.IsChecked ?? false;
            var offsetYText = txtOffsetY.Text.Trim();
            var type = txtType.Text.Trim();

            if (string.IsNullOrEmpty(plateIdentifier) || string.IsNullOrEmpty(plateHeightText) ||
                string.IsNullOrEmpty(nestedPlateHeightText) || string.IsNullOrEmpty(offsetYText) ||
                string.IsNullOrEmpty(type))
            {
                MessageBox.Show("Please enter values for all fields.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            double plateHeight, nestedPlateHeight, offsetY;
            if (!double.TryParse(plateHeightText, out plateHeight) ||
                !double.TryParse(nestedPlateHeightText, out nestedPlateHeight) ||
                !double.TryParse(offsetYText, out offsetY))
            {
                MessageBox.Show("Please enter valid numeric values for Height, Nested Height, and Y Offset.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                _labwareManager.AddLabware(new Labware
                {
                    Identifier = plateIdentifier,
                    Height = plateHeight,
                    NestedHeight = nestedPlateHeight,
                    IsLowVolume = isLowVolume ? 1 : 0,
                    OffsetY = offsetY,
                    Type = type
                });

                MessageBox.Show($"Labware '{plateIdentifier}' added successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                LoadLabware();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnUpdate_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = lstLabware.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(selectedItem))
            {
                MessageBox.Show("Please select a labware to update.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Extract the identifier from the selected item
            var identifier = selectedItem.Split(':')[0];
            var labware = _labwareManager.GetAllLabware().FirstOrDefault(l => l.Identifier == identifier);

            if (labware == null)
            {
                MessageBox.Show("Selected labware not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Validate inputs
            if (!ValidateInputs())
                return;

            // Update the labware object
            var originalIdentifier = labware.Identifier;
            labware.Identifier = txtPlateIdentifier.Text.Trim();
            labware.Height = double.Parse(txtPlateHeight.Text.Trim());
            labware.NestedHeight = double.Parse(txtNestedPlateHeight.Text.Trim());
            labware.IsLowVolume = (chkIsLowVolume.IsChecked ?? false) ? 1 : 0;
            labware.OffsetY = double.Parse(txtOffsetY.Text.Trim());
            labware.Type = txtType.Text.Trim();

            try
            {
                // Use the original identifier for the lookup, and the new identifier for the update
                labware.Identifier = originalIdentifier; // Temporarily set back for the update method
                _labwareManager.UpdateLabware(labware, txtPlateIdentifier.Text.Trim());

                MessageBox.Show($"Labware updated successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                LoadLabware();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnDelete_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = lstLabware.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(selectedItem))
            {
                MessageBox.Show("Please select a labware to delete.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Extract the identifier from the selected item
            var identifier = selectedItem.Split(':')[0];
            var labware = _labwareManager.GetAllLabware().FirstOrDefault(l => l.Identifier == identifier);

            if (labware == null)
            {
                MessageBox.Show("Selected labware not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var result = MessageBox.Show($"Are you sure you want to delete labware '{labware.Identifier}'?",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    _labwareManager.DeleteLabware(labware);
                    MessageBox.Show($"Labware '{labware.Identifier}' deleted successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    LoadLabware();
                    ClearInputs();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void btnClear_Click(object sender, RoutedEventArgs e)
        {
            ClearInputs();
            lstLabware.SelectedItem = null;
        }

        private void lstLabware_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstLabware.SelectedItem == null)
            {
                // Don't clear inputs when selection is cleared
                return;
            }

            var selectedItem = lstLabware.SelectedItem.ToString();
            // Extract the identifier (everything before the first colon)
            var identifier = selectedItem.Split(':')[0];
            var labware = _labwareManager.GetAllLabware().FirstOrDefault(l => l.Identifier == identifier);

            if (labware != null)
            {
                txtPlateIdentifier.Text = labware.Identifier;
                txtPlateHeight.Text = labware.Height.ToString();
                txtNestedPlateHeight.Text = labware.NestedHeight.ToString();
                chkIsLowVolume.IsChecked = labware.IsLowVolume == 1;
                txtOffsetY.Text = labware.OffsetY.ToString();
                txtType.Text = labware.Type;
            }
        }

        private bool ValidateInputs()
        {
            var plateIdentifier = txtPlateIdentifier.Text.Trim();
            var plateHeightText = txtPlateHeight.Text.Trim();
            var nestedPlateHeightText = txtNestedPlateHeight.Text.Trim();
            var offsetYText = txtOffsetY.Text.Trim();
            var type = txtType.Text.Trim();

            if (string.IsNullOrEmpty(plateIdentifier) || string.IsNullOrEmpty(plateHeightText) ||
                string.IsNullOrEmpty(nestedPlateHeightText) || string.IsNullOrEmpty(offsetYText) ||
                string.IsNullOrEmpty(type))
            {
                MessageBox.Show("Please enter values for all fields.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            double plateHeight, nestedPlateHeight, offsetY;
            if (!double.TryParse(plateHeightText, out plateHeight) ||
                !double.TryParse(nestedPlateHeightText, out nestedPlateHeight) ||
                !double.TryParse(offsetYText, out offsetY))
            {
                MessageBox.Show("Please enter valid numeric values for Height, Nested Height, and Y Offset.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            return true;
        }

        private void ClearInputs()
        {
            txtPlateIdentifier.Clear();
            txtPlateHeight.Clear();
            txtNestedPlateHeight.Clear();
            chkIsLowVolume.IsChecked = false;
            txtOffsetY.Clear();
            txtType.Clear();
        }
    }
}