using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Data.SQLite;

namespace PinTransferWPF
{
    public partial class ScriptSelectionWindow : Window
    {
        public string SelectedScriptName { get; private set; }
        private readonly string _connectionString;

        public ScriptSelectionWindow(string connectionString)
        {
            InitializeComponent();
            _connectionString = connectionString;
            LoadScripts();
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
            DialogResult = false;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void LoadScripts()
        {
            var scripts = GetScriptSummaries();
            ScriptsDataGrid.ItemsSource = scripts;
        }

        private List<ScriptSummary> GetScriptSummaries()
        {
            var scripts = new List<ScriptSummary>();
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                // Check if Scripts table exists
                using (var command = new SQLiteCommand("SELECT name FROM sqlite_master WHERE type='table' AND name='Scripts';", connection))
                {
                    if (command.ExecuteScalar() == null)
                    {
                        return scripts;
                    }
                }

                using (var command = new SQLiteCommand(@"SELECT ScriptName, SourcePlatesJson, DestinationPlatesJson, SavedDate 
                    FROM Scripts ORDER BY SavedDate DESC", connection))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var scriptName = reader.GetString(reader.GetOrdinal("ScriptName"));
                            var sourcePlatesJson = reader.GetString(reader.GetOrdinal("SourcePlatesJson"));
                            var destinationPlatesJson = reader.GetString(reader.GetOrdinal("DestinationPlatesJson"));
                            var savedDate = DateTime.Parse(reader.GetString(reader.GetOrdinal("SavedDate")));

                            // Count plates by parsing JSON
                            var sourceCount = 0;
                            var destCount = 0;

                            try
                            {
                                var sourcePlates = System.Text.Json.JsonSerializer.Deserialize<List<Integration.SourcePlate>>(sourcePlatesJson);
                                var destPlates = System.Text.Json.JsonSerializer.Deserialize<List<Integration.DestinationPlate>>(destinationPlatesJson);
                                sourceCount = sourcePlates?.Count ?? 0;
                                destCount = destPlates?.Count ?? 0;
                            }
                            catch
                            {
                                // If JSON parsing fails, use 0
                            }

                            scripts.Add(new ScriptSummary
                            {
                                ScriptName = scriptName,
                                SourceCount = sourceCount,
                                DestinationCount = destCount,
                                SavedDate = savedDate
                            });
                        }
                    }
                }
            }
            return scripts;
        }

        private void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            if (ScriptsDataGrid.SelectedItem is ScriptSummary selectedScript)
            {
                SelectedScriptName = selectedScript.ScriptName;
                DialogResult = true;
            }
            else
            {
                MessageBox.Show("Please select a script to load.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (ScriptsDataGrid.SelectedItem is ScriptSummary selectedScript)
            {
                var result = MessageBox.Show($"Are you sure you want to delete script '{selectedScript.ScriptName}'?",
                    "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        DeleteScript(selectedScript.ScriptName);
                        LoadScripts(); // Refresh the list
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to delete script: {ex.Message}", "Delete Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            else
            {
                MessageBox.Show("Please select a script to delete.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void DeleteScript(string scriptName)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                using (var command = new SQLiteCommand("DELETE FROM Scripts WHERE ScriptName = @ScriptName", connection))
                {
                    command.Parameters.AddWithValue("@ScriptName", scriptName);
                    command.ExecuteNonQuery();
                }
            }
        }
    }

    public class ScriptSummary
    {
        public string ScriptName { get; set; }
        public int SourceCount { get; set; }
        public int DestinationCount { get; set; }
        public DateTime SavedDate { get; set; }
    }
}