using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Integration;
using PinTransferParameters;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Windows.Data;
using System.Windows.Controls;
using System.Windows.Input;
using System.Data.SQLite;

namespace ViewModels
{
    using static Integration.RunLogger;
    using StackType = HotelStacker;

    public class AddButtonViewModel : ObservableObject
    {
        public string Type { get; set; }  // "Source" or "Destination"
        public ICommand AddCommand { get; }

        public AddButtonViewModel(string type, ICommand addCommand)
        {
            Type = type;
            AddCommand = addCommand;
        }
    }

    public partial class MainViewModel : ObservableObject
    {
        public InstrumentController _instrumentController;
        public InstrumentEvents _events;
        private JournalParser<StackType> _parser;
        private CommandRunner<StackType> _epsonRunner;
        private CommandRunner<StackType> _kx2Runner;
        private RunLogger _runLogger;
        private Carousel<StackType> _carousel;
        private CancellationTokenSource _cts;
        private int _numStacks = Parameters.numStacks;
        private int _stackCapacity = 25; //TODO calculate programatically
        string connectionString = "Data Source=" + Parameters.LoggingDatabase;
        private ObservableCollection<SourcePlate> _sourcePlates;
        private ObservableCollection<int> _pinVolumes;

        public ObservableCollection<int> PinVolumes
        {
            get => _pinVolumes;
            set
            {
                if (value != _pinVolumes)
                {
                    _pinVolumes = value;
                    OnPropertyChanged(nameof(PinVolumes));
                }
            }
        }
        public ObservableCollection<SourcePlate> SourcePlates
        {
            get => _sourcePlates;
            set
            {
                if (value != _sourcePlates)
                {
                    _sourcePlates = value;
                    OnPropertyChanged(nameof(SourcePlates));
                }
            }
        }
        private ObservableCollection<DestinationPlate> _destinationPlates;
        public ObservableCollection<DestinationPlate> DestinationPlates
        {
            get => _destinationPlates;
            set
            {
                if (value != _destinationPlates)
                {
                    _destinationPlates = value;
                    OnPropertyChanged(nameof(DestinationPlates));
                }
            }
        }

        [ObservableProperty]
        private ObservableCollection<object> _sourceItemsWithAddButton;

        [ObservableProperty]
        private ObservableCollection<object> _destinationItemsWithAddButton;

        [ObservableProperty]
        private bool _isInSourceAddMode = false;

        [ObservableProperty]
        private bool _isInDestAddMode = false;

        [ObservableProperty]
        private int _selectedTransferVolume; //TODO fix

        [ObservableProperty]
        private string _runId;

        [ObservableProperty]
        private int _screenNumber;

        [ObservableProperty]
        private string _userName;

        [ObservableProperty]
        private string _journalId;

        public MainViewModel(InstrumentController instrumentController, string connectionString)
        {
            SourcePlates = new ObservableCollection<SourcePlate>();
            DestinationPlates = new ObservableCollection<DestinationPlate>();
            PinVolumes = new ObservableCollection<int>();
            SourcesToAdd = 1;                    // Default number of sources
            ReplicatesOfSourcesToAdd = 2;        // Default number of replicates
            VolumeOfSourcesToAdd = 100;          // Default volume (will be selected in dropdown)
            Func<int, StackType> stackerFactory = _stackCapacity => new StackType(_stackCapacity);
            _carousel = new Carousel<StackType>(_numStacks, _stackCapacity, stackerFactory);
            _instrumentController = instrumentController;
            _events = new InstrumentEvents();
            _runLogger = new RunLogger(connectionString);
            _parser = new JournalParser<StackType>(connectionString, _events, _carousel);
            _epsonRunner = new CommandRunner<StackType>(_parser, "Epson", _runLogger, _events);
            _kx2Runner = new CommandRunner<StackType>(_parser, "KX2", _runLogger, _events);

            SourceItemsWithAddButton = new ObservableCollection<object>();
            DestinationItemsWithAddButton = new ObservableCollection<object>();

            // Create add buttons
            var addSourceButton = new AddButtonViewModel("Source", new RelayCommand<string>(_ => StartAddSourcePlate()));
            var addDestinationButton = new AddButtonViewModel("Destination", new RelayCommand<string>(_ => StartAddDestinationPlate()));


            // Watch for changes to plates collections
            SourcePlates.CollectionChanged += (s, e) => UpdateSourceItemsCollection(addSourceButton);
            DestinationPlates.CollectionChanged += (s, e) => UpdateDestinationItemsCollection(addDestinationButton);

            // Initialize with add buttons
            UpdateSourceItemsCollection(addSourceButton);
            UpdateDestinationItemsCollection(addDestinationButton);

            // TODO: fix hardcoding..
            PinVolumes.Add(33);
            PinVolumes.Add(100);
            PinVolumes.Add(300);
            
            if (Parameters.UsingInstruments)
            {
                SetupEventHandlers();
            }
            else
            {
                SetupEventHandlersText();
            }

            CheckForUnfinishedRun(connectionString);
        }

        private void UpdateSourceItemsCollection(AddButtonViewModel addButton)
        {
            SourceItemsWithAddButton.Clear();
            foreach (var plate in SourcePlates)
                SourceItemsWithAddButton.Add(plate);
            SourceItemsWithAddButton.Add(addButton);
        }

        private void UpdateDestinationItemsCollection(AddButtonViewModel addButton)
        {
            DestinationItemsWithAddButton.Clear();
            foreach (var plate in DestinationPlates)
                DestinationItemsWithAddButton.Add(plate);
            DestinationItemsWithAddButton.Add(addButton);
        }
        private void StartAddSourcePlate()
        {
            // Clear all selections first
            ClearAllPlateSelections();

            // Exit other add mode if active
            if (IsInDestAddMode)
            {
                IsInDestAddMode = false;

            }
            IsInSourceAddMode = true;
            StatusText += "Please select destination plates to link, then click Confirm Add Source Plate\n";

            // Notify UI that we're in add mode
            OnPropertyChanged(nameof(IsInSourceAddMode));
        }

        private void StartAddDestinationPlate()
        {
            // Clear all selections first
            ClearAllPlateSelections();

            if (IsInSourceAddMode)
            {
                IsInSourceAddMode = false;
            }

            IsInDestAddMode = true;
            StatusText += "Please select source plates to link, then click Confirm Add Destination Plate\n";

            // Notify UI that we're in add mode
            OnPropertyChanged(nameof(IsInDestAddMode));
        }

        // AI
        [RelayCommand]
        private void ConfirmAddSourcePlate()
        {
            if (!IsInSourceAddMode) return;

            // Get selected destination plates
            var selectedDestPlates = new List<DestinationPlate>();
            foreach (var destPlate in DestinationPlates)
            {
                if (destPlate.IsSelected)
                    selectedDestPlates.Add(destPlate);
            }

            if (selectedDestPlates.Count == 0)
            {
                StatusText += "No destination plates selected. Canceled adding source plate.\n";
                IsInSourceAddMode = false;
                return;
            }

            // Check capacity for SourcePlate type specifically
            if (!HasCapacityFor(1, typeof(SourcePlate)))
            {
                MessageBox.Show("Cannot add source plate - no available slots in stacks that can accept source plates!",
                               "Capacity Exceeded", MessageBoxButton.OK, MessageBoxImage.Warning);
                StatusText += "Cannot add source plate - capacity exceeded.\n";
                return;
            }

            try
            {
                // Get next available position
                var (stack, position) = GetNextAvailablePosition(typeof(SourcePlate));

                var newSourcePlate = new SourcePlate
                {
                    ID = "source_" + (SourcePlates.Count + 1).ToString(),
                    Stack = stack,
                    FinalStack = stack,
                    PositionInStack = position,
                    FinalPositionInStack = position,
                    Status = new Dictionary<string, bool>() { { "pinned", false } },
                    Replicates = new Tuple<int, int>(SelectedTransferVolume, selectedDestPlates.Count)
                };

                // Add the source plate
                SourcePlates.Add(newSourcePlate);

                // Link to selected destination plates
                foreach (var destPlate in selectedDestPlates)
                {
                    destPlate.AddSourcePlate(newSourcePlate.ID, SelectedTransferVolume);
                }

                StatusText += $"Added source plate {newSourcePlate.ID} at Stack {stack}, Position {position}\n";
                StatusText += GetCapacityInfo() + "\n";
                StatusText += GetStackAssignmentInfo() + "\n";
                IsInSourceAddMode = false;

                // Set as selected plate after it's added
                HighlightSelectedPlate(newSourcePlate);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "Capacity Exceeded", MessageBoxButton.OK, MessageBoxImage.Warning);
                StatusText += ex.Message + "\n";
            }
        }

        [RelayCommand]
        private void ConfirmAddDestinationPlate()
        {
            if (!IsInDestAddMode) return;

            // Get selected source plates
            var selectedSourcePlates = new List<SourcePlate>();
            foreach (var sourcePlate in SourcePlates)
            {
                if (sourcePlate.IsSelected)
                    selectedSourcePlates.Add(sourcePlate);
            }

            if (selectedSourcePlates.Count == 0)
            {
                StatusText += "No source plates selected. Canceled adding destination plate.\n";
                IsInDestAddMode = false;
                return;
            }

            // Check capacity for DestinationPlate type specifically
            if (!HasCapacityFor(1, typeof(DestinationPlate)))
            {
                MessageBox.Show("Cannot add destination plate - no available slots in stacks that can accept destination plates!",
                               "Capacity Exceeded", MessageBoxButton.OK, MessageBoxImage.Warning);
                StatusText += "Cannot add destination plate - capacity exceeded.\n";
                return;
            }

            try
            {
                // Get next available position
                var (stack, position) = GetNextAvailablePosition(typeof(DestinationPlate));

                // Create the new destination plate
                var newDestPlate = new DestinationPlate
                {
                    ID = "destination_" + (DestinationPlates.Count + 1).ToString(),
                    Stack = stack,
                    FinalStack = stack,
                    PositionInStack = position,
                    FinalPositionInStack = position,
                    Status = new Dictionary<string, bool>() { { "pinned", false } }
                };

                // Add the source connections
                foreach (var sourcePlate in selectedSourcePlates)
                {
                    newDestPlate.AddSourcePlate(sourcePlate.ID, SelectedTransferVolume);
                }

                // Add the destination plate
                DestinationPlates.Add(newDestPlate);

                StatusText += $"Added destination plate {newDestPlate.ID} at Stack {stack}, Position {position}\n";
                StatusText += GetCapacityInfo() + "\n";
                StatusText += GetStackAssignmentInfo() + "\n";
                IsInDestAddMode = false;

                // Set as selected plate after it's added
                HighlightSelectedPlate(newDestPlate);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "Capacity Exceeded", MessageBoxButton.OK, MessageBoxImage.Warning);
                StatusText += ex.Message + "\n";
            }
        }

        // AI
        // Add this event to your MainViewModel class
        public event EventHandler MultiSelectionReset;

        // Then modify the CancelAddPlate method
        [RelayCommand]
        private void CancelAddPlate()
        {
            if (IsInSourceAddMode)
            {
                IsInSourceAddMode = false;
                StatusText += "Canceled adding source plate.\n";

                // Reset selections
                foreach (var destPlate in DestinationPlates)
                {
                    destPlate.IsSelected = false;
                    destPlate.SelectionColor = null;
                }
            }

            if (IsInDestAddMode)
            {
                IsInDestAddMode = false;
                StatusText += "Canceled adding destination plate.\n";

                // Reset selections
                foreach (var sourcePlate in SourcePlates)
                {
                    sourcePlate.IsSelected = false;
                    sourcePlate.SelectionColor = null;
                }
            }

            // Raise the event to notify MainWindow to reset its multi-selection state
            MultiSelectionReset?.Invoke(this, EventArgs.Empty);

            // Notify that plate visuals need to be updated
            PlateVisualsNeedUpdate?.Invoke(this, EventArgs.Empty);
        }

        //AI
        [RelayCommand]
        private void DeletePlate(object parameter)
        {
            int stackToCheck = -1;
            if (parameter is SourcePlate sourcePlate)
            {
                stackToCheck = sourcePlate.Stack;
                // First, remove references from destination plates
                foreach (var destPlate in DestinationPlates.ToList())
                {
                    destPlate.SourcePlates.Remove(sourcePlate.ID);
                }

                // Remove the plate from the carousel if it exists there
                if (_carousel != null)
                {
                    try
                    {
                        // Try to find the plate in any stacker
                        bool found = false;
                        for (int i = 0; i < _carousel.Stackers.Count; i++)
                        {
                            // Check if the plate exists in this stacker's Plates collection
                            if (_carousel.Stackers[i].Plates.Any(p => p.ID == sourcePlate.ID))
                            {
                                // Update the plate's Stack property to match where it actually is
                                sourcePlate.Stack = i + 1;
                                _carousel.RemovePlate(sourcePlate);
                                found = true;
                                break;
                            }
                        }

                        if (!found)
                        {
                            // If not found in any stacker, skip carousel removal
                            StatusText += $"Note: Plate {sourcePlate.ID} was not found in carousel stackers\n";
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log exception but continue with deletion
                        StatusText += $"Warning: {ex.Message} (Carousel removal)\n";
                    }
                }

                // Then remove the plate itself
                SourcePlates.Remove(sourcePlate);
                StatusText += $"Deleted source plate {sourcePlate.ID}\n";
            }
            else if (parameter is DestinationPlate destPlate)
            {
                stackToCheck = destPlate.Stack;
                // Remove the plate from carousel if it exists there
                if (_carousel != null)
                {
                    try
                    {
                        // Try to find the plate in any stacker
                        bool found = false;
                        for (int i = 0; i < _carousel.Stackers.Count; i++)
                        {
                            // Check if the plate exists in this stacker's Plates collection
                            if (_carousel.Stackers[i].Plates.Any(p => p.ID == destPlate.ID))
                            {
                                // Update the plate's Stack property to match where it actually is
                                destPlate.Stack = i + 1;
                                _carousel.RemovePlate(destPlate);
                                found = true;
                                break;
                            }
                        }

                        if (!found)
                        {
                            // If not found in any stacker, skip carousel removal
                            StatusText += $"Note: Plate {destPlate.ID} was not found in carousel stackers\n";
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log exception but continue with deletion
                        StatusText += $"Warning: {ex.Message} (Carousel removal)\n";
                    }
                }

                // Remove the destination plate
                DestinationPlates.Remove(destPlate);
                StatusText += $"Deleted destination plate {destPlate.ID}\n";
            }

            // Check if the stack should be unassigned
            if (stackToCheck >= 0)
            {
                CheckAndUnassignEmptyStack(stackToCheck);
            }

            // Clear any selections
            ClearAllPlateSelections();

            // Update UI collections
            UpdateSourceItemsCollection(new AddButtonViewModel("Source", new RelayCommand<string>(_ => StartAddSourcePlate())));
            UpdateDestinationItemsCollection(new AddButtonViewModel("Destination", new RelayCommand<string>(_ => StartAddDestinationPlate())));

            // Update capacity display
            OnPropertyChanged(nameof(CapacityInfo));
            StatusText += GetCapacityInfo() + "\n";
            StatusText += GetStackAssignmentInfo() + "\n";

            // Trigger visual update for the stacker
            PlateVisualsNeedUpdate?.Invoke(this, EventArgs.Empty);
        }

        //AI
        [RelayCommand]
        private void ClearAllPlates()
        {
            // Ask for confirmation
            //MessageBoxResult result = MessageBox.Show("Are you sure you want to delete all plates?",
            //                                         "Confirmation",
            //                                         MessageBoxButton.YesNo,
            //                                         MessageBoxImage.Question);
            MessageBoxResult result = MessageBoxResult.Yes;
            if (result == MessageBoxResult.Yes)
            {
                // Clear carousel if it exists
                if (_carousel != null)
                {
                    try
                    {
                        // Use the built-in method to remove all plates
                        _carousel.RemoveAllPlates();
                    }
                    catch (Exception ex)
                    {
                        // Log exception but continue with clearing
                        StatusText += $"Warning: {ex.Message} (Carousel clearing)\n";
                    }
                }

                // Clear all plates
                SourcePlates.Clear();
                DestinationPlates.Clear();

                // Update the collections with add buttons
                UpdateSourceItemsCollection(new AddButtonViewModel("Source", new RelayCommand<string>(_ => StartAddSourcePlate())));
                UpdateDestinationItemsCollection(new AddButtonViewModel("Destination", new RelayCommand<string>(_ => StartAddDestinationPlate())));

                // Trigger visual update for the stacker
                PlateVisualsNeedUpdate?.Invoke(this, EventArgs.Empty);

                StatusText += "All plates cleared.\n";
            }
        }

        private void CheckForUnfinishedRun(string connectionString)
        {
            var lastUnfinishedRunState = _runLogger.LoadMostRecentUnfinishedRunState();
            if (lastUnfinishedRunState != null)
            {
                var result = MessageBox.Show($"Last run (Journal ID: {lastUnfinishedRunState.JournalID}) wasn't finished. Do you want to resume?", "Resume Run", MessageBoxButton.YesNo);
                if (result == MessageBoxResult.Yes)
                {
                    ResumeRun(lastUnfinishedRunState);
                }
            }
        }

        [ObservableProperty]
        private WindowState windowState = WindowState.Normal;

        [ObservableProperty]
        private string statusText;

        private bool _canRunCommands = true;
        public bool CanRunCommands
        {
            get => _canRunCommands;
            set => SetProperty(ref _canRunCommands, value);
        }
        [ObservableProperty]
        private bool canCancel;

        [ObservableProperty]
        private bool isMaximized;

        [ObservableProperty]
        private ObservableCollection<string> fileOptions = new ObservableCollection<string>
        {
            "Save Script",
            "Load Script"
        };

        [ObservableProperty]
        private string selectedFileOption;

        [ObservableProperty]
        private ObservableCollection<string> toolOptions = new ObservableCollection<string>
        {
            "Labware Manager"
        };

        [ObservableProperty]
        private string selectedToolOption;

        public RunInfo RunInfo { get; set; } = new RunInfo
        {
            RunID = null,
            TimeRun = DateTime.Now,
            ScreenNumber = -1,
            UserName = "",
            JournalID = ""
        };

        private void AppendStatus(string message)
        {
            StatusText += $"{DateTime.Now:HH:mm:ss} - {message}\n";
        }

        // Add these new properties
        [ObservableProperty]
        private bool _autoFillSlots = true; // Option to fill open slots vs always append

        private Dictionary<int, Type> _stackAssignments = new Dictionary<int, Type>();

        // Method to get the assigned plate type for a stack (null if unassigned)
        private Type GetStackPlateType(int stackIndex)
        {
            return _stackAssignments.TryGetValue(stackIndex, out Type plateType) ? plateType : null;
        }

        // Method to assign a stack to a plate type
        private void AssignStackToPlateType(int stackIndex, Type plateType)
        {
            if (_stackAssignments.ContainsKey(stackIndex))
            {
                if (_stackAssignments[stackIndex] != plateType)
                {
                    throw new InvalidOperationException($"Stack {stackIndex} is already assigned to {_stackAssignments[stackIndex].Name} plates");
                }
            }
            else
            {
                _stackAssignments[stackIndex] = plateType;
                StatusText += $"Stack {stackIndex} assigned to {plateType.Name} plates\n";
            }
        }

        // Method to unassign a stack when it becomes empty
        private void CheckAndUnassignEmptyStack(int stackIndex)
        {
            bool hasSourcePlates = SourcePlates.Any(p => p.Stack == stackIndex);
            bool hasDestPlates = DestinationPlates.Any(p => p.Stack == stackIndex);

            if (!hasSourcePlates && !hasDestPlates && _stackAssignments.ContainsKey(stackIndex))
            {
                Type removedType = _stackAssignments[stackIndex];
                _stackAssignments.Remove(stackIndex);
                StatusText += $"Stack {stackIndex} unassigned from {removedType.Name} plates (now empty)\n";
            }
        }

        // Updated method to get available slots for a specific plate type
        private List<int> GetAvailableSlots(int stackIndex, Type plateType)
        {
            var availableSlots = new List<int>();
            var occupiedSlots = new HashSet<int>();

            // Check if this stack can accept this plate type
            Type assignedType = GetStackPlateType(stackIndex);
            if (assignedType != null && assignedType != plateType)
            {
                return availableSlots; // Empty list - stack is assigned to different plate type
            }

            // Get all plates in this stack
            foreach (var plate in SourcePlates.Where(p => p.Stack == stackIndex))
            {
                occupiedSlots.Add(plate.PositionInStack);
            }
            foreach (var plate in DestinationPlates.Where(p => p.Stack == stackIndex))
            {
                occupiedSlots.Add(plate.PositionInStack);
            }

            // Find available slots
            for (int i = 0; i < _stackCapacity; i++)
            {
                if (!occupiedSlots.Contains(i))
                {
                    availableSlots.Add(i);
                }
            }

            return availableSlots.OrderBy(x => x).ToList();
        }

        // Updated method to get the next available position for a specific plate type
        private (int stack, int position) GetNextAvailablePosition(Type plateType)
        {
            if (AutoFillSlots)
            {
                // Try to fill empty slots first, but respect the original stacking direction
                if (plateType == typeof(SourcePlate))
                {
                    // Source plates: search from left to right (stack 0 to numStacks-1)
                    for (int stack = 0; stack < _numStacks; stack++)
                    {
                        var availableSlots = GetAvailableSlots(stack, plateType);
                        if (availableSlots.Count > 0)
                        {
                            if (GetStackPlateType(stack) == null)
                            {
                                AssignStackToPlateType(stack, plateType);
                            }
                            return (stack, availableSlots[0]);
                        }
                    }
                }
                else if (plateType == typeof(DestinationPlate))
                {
                    // Destination plates: search from right to left (stack numStacks-1 to 0)
                    for (int stack = _numStacks - 1; stack >= 0; stack--)
                    {
                        var availableSlots = GetAvailableSlots(stack, plateType);
                        if (availableSlots.Count > 0)
                        {
                            if (GetStackPlateType(stack) == null)
                            {
                                AssignStackToPlateType(stack, plateType);
                            }
                            return (stack, availableSlots[0]);
                        }
                    }
                }
            }
            else
            {
                // Append mode: maintain original behavior
                if (plateType == typeof(SourcePlate))
                {
                    // Source plates: start from stack 0 and work right
                    int totalSourcePlates = SourcePlates.Count;
                    int preferredStack = totalSourcePlates / _stackCapacity;
                    int preferredPosition = totalSourcePlates % _stackCapacity;

                    // Check if preferred position is available
                    if (preferredStack < _numStacks)
                    {
                        Type assignedType = GetStackPlateType(preferredStack);
                        if (assignedType == null || assignedType == typeof(SourcePlate))
                        {
                            var availableSlots = GetAvailableSlots(preferredStack, plateType);
                            if (availableSlots.Contains(preferredPosition))
                            {
                                if (assignedType == null)
                                {
                                    AssignStackToPlateType(preferredStack, plateType);
                                }
                                return (preferredStack, preferredPosition);
                            }
                        }
                    }

                    // Fallback: find any available slot from left to right
                    for (int stack = 0; stack < _numStacks; stack++)
                    {
                        Type assignedType = GetStackPlateType(stack);
                        if (assignedType == null || assignedType == plateType)
                        {
                            var availableSlots = GetAvailableSlots(stack, plateType);
                            if (availableSlots.Count > 0)
                            {
                                if (assignedType == null)
                                {
                                    AssignStackToPlateType(stack, plateType);
                                }
                                return (stack, availableSlots.Max()); // Use highest available position for append
                            }
                        }
                    }
                }
                else if (plateType == typeof(DestinationPlate))
                {
                    // Destination plates: start from rightmost stack and work left
                    int totalDestPlates = DestinationPlates.Count;
                    int preferredStack = (_numStacks - 1) - (totalDestPlates / _stackCapacity);
                    int preferredPosition = totalDestPlates - (((_numStacks - 1) - preferredStack) * _stackCapacity);

                    // Check if preferred position is available
                    if (preferredStack >= 0)
                    {
                        Type assignedType = GetStackPlateType(preferredStack);
                        if (assignedType == null || assignedType == typeof(DestinationPlate))
                        {
                            var availableSlots = GetAvailableSlots(preferredStack, plateType);
                            if (availableSlots.Contains(preferredPosition))
                            {
                                if (assignedType == null)
                                {
                                    AssignStackToPlateType(preferredStack, plateType);
                                }
                                return (preferredStack, preferredPosition);
                            }
                        }
                    }

                    // Fallback: find any available slot from right to left
                    for (int stack = _numStacks - 1; stack >= 0; stack--)
                    {
                        Type assignedType = GetStackPlateType(stack);
                        if (assignedType == null || assignedType == plateType)
                        {
                            var availableSlots = GetAvailableSlots(stack, plateType);
                            if (availableSlots.Count > 0)
                            {
                                if (assignedType == null)
                                {
                                    AssignStackToPlateType(stack, plateType);
                                }
                                return (stack, availableSlots.Max()); // Use highest available position for append
                            }
                        }
                    }
                }
            }

            throw new InvalidOperationException($"No available slots for {plateType.Name} plates");
        }

        // Updated method to check if there's capacity for new plates of a specific type
        private bool HasCapacityFor(int plateCount, Type plateType)
        {
            int totalAvailableSlots = 0;
            for (int i = 0; i < _numStacks; i++)
            {
                totalAvailableSlots += GetAvailableSlots(i, plateType).Count;
            }
            return totalAvailableSlots >= plateCount;
        }

        // Updated method to get capacity info for display
        public string GetCapacityInfo()
        {
            int sourceCapacity = 0;
            int destCapacity = 0;
            int unassignedCapacity = 0;

            for (int i = 0; i < _numStacks; i++)
            {
                Type assignedType = GetStackPlateType(i);
                int availableSlots = GetAvailableSlots(i, typeof(SourcePlate)).Count + GetAvailableSlots(i, typeof(DestinationPlate)).Count;

                if (assignedType == null)
                {
                    unassignedCapacity += _stackCapacity - (SourcePlates.Count(p => p.Stack == i) + DestinationPlates.Count(p => p.Stack == i));
                }
                else if (assignedType == typeof(SourcePlate))
                {
                    sourceCapacity += GetAvailableSlots(i, typeof(SourcePlate)).Count;
                }
                else if (assignedType == typeof(DestinationPlate))
                {
                    destCapacity += GetAvailableSlots(i, typeof(DestinationPlate)).Count;
                }
            }

            return $"Available slots - Source: {sourceCapacity}, Destination: {destCapacity}, Unassigned: {unassignedCapacity}";
        }

        // Method to get stack assignment info for display
        public string GetStackAssignmentInfo()
        {
            var assignments = new List<string>();
            for (int i = 0; i < _numStacks; i++)
            {
                Type assignedType = GetStackPlateType(i);
                string typeName = assignedType?.Name.Replace("Plate", "") ?? "Unassigned";
                assignments.Add($"Stack {i}: {typeName}");
            }
            return string.Join(", ", assignments);
        }

        [ObservableProperty]
        private Plate selectedSourcePlate;
        
        [ObservableProperty]
        private Plate selectedDestinationPlate;

        [ObservableProperty]
        private int sourcesToAdd;

        [ObservableProperty]
        private int replicatesOfSourcesToAdd;

        [ObservableProperty]
        private int volumeOfSourcesToAdd;
        public string CapacityInfo => GetCapacityInfo();

        [RelayCommand]
        private void CreatePlates()
        {
            // Check capacity before creating plates
            int totalNewDestPlates = SourcesToAdd * ReplicatesOfSourcesToAdd;

            if (!HasCapacityFor(SourcesToAdd, typeof(SourcePlate)))
            {
                MessageBox.Show($"Cannot create {SourcesToAdd} source plates - insufficient capacity!",
                               "Capacity Exceeded", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!HasCapacityFor(totalNewDestPlates, typeof(DestinationPlate)))
            {
                MessageBox.Show($"Cannot create {totalNewDestPlates} destination plates - insufficient capacity!",
                               "Capacity Exceeded", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Create source plates
                for (int sourceCount = 0; sourceCount < SourcesToAdd; sourceCount++)
                {
                    var (stack, position) = GetNextAvailablePosition(typeof(SourcePlate));

                    SourcePlates.Add(new SourcePlate
                    {
                        ID = "source_" + (SourcePlates.Count + 1).ToString(),
                        Stack = stack,
                        FinalStack = stack,
                        PositionInStack = position,
                        FinalPositionInStack = position,
                        Status = new Dictionary<string, bool>() { { "pinned", false } },
                        Replicates = new Tuple<int, int>(VolumeOfSourcesToAdd, ReplicatesOfSourcesToAdd)
                    });
                }

                // Create destination plates for each source plate
                var newSourcePlates = SourcePlates.Skip(SourcePlates.Count - SourcesToAdd).ToList();
                foreach (var sourcePlate in newSourcePlates)
                {
                    for (int replicate = 1; replicate <= sourcePlate.Replicates.Item2; replicate++)
                    {
                        var (stack, position) = GetNextAvailablePosition(typeof(DestinationPlate));

                        var destPlate = new DestinationPlate
                        {
                            ID = "destination_" + (DestinationPlates.Count + 1).ToString(),
                            Stack = stack,
                            FinalStack = stack,
                            PositionInStack = position,
                            FinalPositionInStack = position,
                            Status = new Dictionary<string, bool>() { { "pinned", false } }
                        };

                        destPlate.AddSourcePlate(sourcePlate.ID, sourcePlate.Replicates.Item1);
                        DestinationPlates.Add(destPlate);
                    }
                }

                StatusText += $"Created {SourcesToAdd} source plates and {totalNewDestPlates} destination plates\n";
                StatusText += GetCapacityInfo() + "\n";

                // Select the last created source plate if any were created
                if (SourcesToAdd > 0)
                {
                    var lastSourcePlate = SourcePlates.LastOrDefault();
                    if (lastSourcePlate != null)
                    {
                        HighlightSelectedPlate(lastSourcePlate);
                    }
                }
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "Capacity Exceeded", MessageBoxButton.OK, MessageBoxImage.Warning);
                StatusText += ex.Message + "\n";
            }
        }

        [RelayCommand]
        private void CreateRun()
        {
            //TODO fix sequential logic
            try
            {
                // Update RunInfo with current values
                RunInfo.RunID = RunId;
                RunInfo.ScreenNumber = ScreenNumber;
                RunInfo.UserName = UserName;
                RunInfo.JournalID = JournalId;
                RunInfo.TimeRun = DateTime.Now;

                JournalInfo journalInfo = new JournalInfo
                {
                    JournalID = RunInfo.JournalID,
                    SourcePlates = SourcePlates.ToList(),
                    DestinationPlates = DestinationPlates.ToList()
                };

                _runLogger.CreateJournal(journalInfo);
                _runLogger.CreateRun(RunInfo);

                // Create the dictionary of plates
                var platesDictionary = new Dictionary<string, Tuple<int, int>>();

                // Add SourcePlates to the dictionary
                foreach (var plate in SourcePlates)
                {
                    platesDictionary[plate.ID] = new Tuple<int, int>(plate.Stack, plate.PositionInStack);
                }

                // Add DestinationPlates to the dictionary
                foreach (var plate in DestinationPlates)
                {
                    platesDictionary[plate.ID] = new Tuple<int, int>(plate.Stack, plate.PositionInStack);
                }
                // make initial runstate
                _events.ResetEvents();
                List<Plate> allPlates = new List<Plate>();
                allPlates.AddRange(SourcePlates);
                allPlates.AddRange(DestinationPlates);
                _events._plates = allPlates;
                _epsonRunner.SaveRunState(journalInfo.JournalID, 1);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PopulateCarousel(List<Plate> deserializedPlates)
        {
            _ = _carousel.RemoveAllPlates();
            foreach (Plate plate in deserializedPlates)
            {
                _carousel.AddPlate(plate, plate.Stack);
            }
        }

        [RelayCommand]
        private async Task StartRun()
        {
            if (Parameters.UsingInstruments)
            {
                if (!_instrumentController.KX2.IsInitialized())
                {
                    await Task.Run(() =>
                    {
                        _instrumentController.InitializeArm();
                    });
                }
            }
            string currentJournalID = RunInfo.JournalID; // Replace with actual journal ID
            _events.ResetEvents();
            string serializedPlates = _runLogger.LoadRunState(currentJournalID).InitialPlates;
            List<Plate> deserializedPlates = PlateSerializer.DeserializePlates(serializedPlates);
            _events._plates = deserializedPlates;
            PopulateCarousel(deserializedPlates);
            _events.ResumeLine = 0;
            _kx2Runner.SaveRunState(currentJournalID, 1);

            CanRunCommands = false;
            CanCancel = true;
            StatusText = string.Empty;
            AppendStatus("Running commands...");

            _cts = new CancellationTokenSource();

            // make carousel from starting plate positions for journal

            try
            {
                await Task.WhenAll(
                    _epsonRunner.RunCommandsAsync(currentJournalID, 1, _cts.Token),
                    _kx2Runner.RunCommandsAsync(currentJournalID, 1, _cts.Token)
                );
            }
            catch (OperationCanceledException)
            {
                AppendStatus("Command execution was cancelled.");
            }
            catch (Exception ex)
            {
                AppendStatus($"An error occurred: {ex.Message}");
            }
            finally
            {
                if (!_cts.Token.IsCancellationRequested)
                {
                    AppendStatus("All commands completed successfully.");
                    _runLogger.MarkRunAsCompleted(currentJournalID);
                }
                CanRunCommands = true;
                CanCancel = false;
            }
        }

        [RelayCommand]
        private async Task ResumeRun()
        {
            var lastRunState = _runLogger.LoadRunState("testJournal8"); // TODO: Replace with actual journal ID
            if (Parameters.UsingInstruments)
            {
                if (!_instrumentController.KX2.IsInitialized())
                {
                    await Task.Run(() =>
                    {
                        _instrumentController.InitializeArm();
                    });
                }
            }
            ResumeRun(lastRunState);
        }

        [RelayCommand]
        private void CancelRun()
        {
            CanCancel = false;
            AppendStatus("Cancelling...");
            _instrumentController.KX2.ScriptStop();
            _cts?.Cancel();
            if (Parameters.UsingInstruments)
            {
                _instrumentController.StopAll();
            }
        }

        private async void ResumeRun(RunState runState)
        {
            // make carousel from saved plate positions
            _events.DeserializeToolStates(runState.SerializedToolStates);
            _events.DeserializeArmStates(runState.SerializedArmStates);
            _events.DeserializeStageStates(runState.SerializedStageStates);
            _events.DeserializeCarouselStates(runState.SerializedCarouselStates);
            string serializedPlates = runState.Plates;
            List<Plate> deserializedPlates = PlateSerializer.DeserializePlates(serializedPlates);
            PopulateCarousel(deserializedPlates);
            _events._plates = deserializedPlates;
            _events.ResumeLine = runState.ResumeLine;

            CanRunCommands = false;
            CanCancel = true;
            StatusText = string.Empty;
            AppendStatus($"Resuming run for Journal ID: {runState.JournalID}...");

            _cts = new CancellationTokenSource();

            try
            {
                await Task.WhenAll(
                    _epsonRunner.RunCommandsAsync(runState.JournalID, runState.EpsonCommandID, _cts.Token),
                    _kx2Runner.RunCommandsAsync(runState.JournalID, runState.KX2CommandID, _cts.Token)
                );
            }
            catch (OperationCanceledException)
            {
                AppendStatus("Command execution was cancelled.");
            }
            catch (Exception ex)
            {
                AppendStatus($"An error occurred: {ex.Message}");
            }
            finally
            {
                if (!_cts.Token.IsCancellationRequested)
                {
                    AppendStatus("All commands completed successfully.");
                    _runLogger.MarkRunAsCompleted(runState.JournalID);
                }
                CanRunCommands = true;
                CanCancel = false;
            }
        }
        
        partial void OnSelectedFileOptionChanged(string value)
        {
            if (value == "Save Script")
            {
                OpenSaveScriptWindow();
            }
            else if (value == "Load Script")
            {
                OpenLoadScriptWindow();
            }
        }

        // Add these properties to MainViewModel class
        [ObservableProperty]
        private SourcePlate _currentlySelectedSourcePlate;

        [ObservableProperty]
        private DestinationPlate _currentlySelectedDestinationPlate;

        // These methods handle the visualization of selected plates
        // Add this to MainViewModel class
        public event EventHandler PlateVisualsNeedUpdate;

        // AI
        public void HighlightSelectedPlate(Plate selectedPlate)
        {
            // Clear previous selections first
            ClearAllPlateSelections();

            if (selectedPlate is SourcePlate sourcePlate)
            {
                // Set this plate as selected with primary color
                CurrentlySelectedSourcePlate = sourcePlate;
                sourcePlate.IsSelected = true;
                sourcePlate.SelectionColor = "Primary";

                // Clear any previously selected destination plate
                CurrentlySelectedDestinationPlate = null;

                // Highlight linked destination plates with secondary color
                foreach (var linkedDestPlate in DestinationPlates)
                {
                    if (linkedDestPlate.SourcePlates.Any(sp => sp.Key == sourcePlate.ID))
                    {
                        linkedDestPlate.IsSelected = true;
                        linkedDestPlate.SelectionColor = "SecondaryDestination";
                    }
                }
            }
            else if (selectedPlate is DestinationPlate destPlate)
            {
                // Set this plate as selected with primary color
                CurrentlySelectedDestinationPlate = destPlate;
                destPlate.IsSelected = true;
                destPlate.SelectionColor = "PrimaryDestination";

                // Clear any previously selected source plate
                CurrentlySelectedSourcePlate = null;

                // Highlight linked source plates with secondary color
                foreach (var linkedSourcePlate in SourcePlates)
                {
                    if (destPlate.SourcePlates.Any(sp => sp.Key == linkedSourcePlate.ID))
                    {
                        linkedSourcePlate.IsSelected = true;
                        linkedSourcePlate.SelectionColor = "Secondary";
                    }
                }
            }

            // Trigger UI update
            OnPropertyChanged(nameof(SourcePlates));
            OnPropertyChanged(nameof(DestinationPlates));

            // Notify that plate visuals need to be updated
            PlateVisualsNeedUpdate?.Invoke(this, EventArgs.Empty);
        }

        public void ClearAllPlateSelections()
        {
            foreach (var plate in SourcePlates)
            {
                plate.IsSelected = false;
                plate.SelectionColor = null;
            }

            foreach (var plate in DestinationPlates)
            {
                plate.IsSelected = false;
                plate.SelectionColor = null;
            }

            CurrentlySelectedSourcePlate = null;
            CurrentlySelectedDestinationPlate = null;

            // Notify that plate visuals need to be updated
            PlateVisualsNeedUpdate?.Invoke(this, EventArgs.Empty);
        }

        public void TogglePlateSelection(Plate plate)
        {
            if (IsInSourceAddMode && plate is DestinationPlate destPlate)
            {
                // In source add mode, toggle destination plate selection
                destPlate.IsSelected = !destPlate.IsSelected;
                destPlate.SelectionColor = destPlate.IsSelected ? "SecondaryDestination" : null;
                OnPropertyChanged(nameof(DestinationPlates));
                PlateVisualsNeedUpdate?.Invoke(this, EventArgs.Empty);
            }
            else if (IsInDestAddMode && plate is SourcePlate sourcePlate)
            {
                // In destination add mode, toggle source plate selection
                sourcePlate.IsSelected = !sourcePlate.IsSelected;
                sourcePlate.SelectionColor = sourcePlate.IsSelected ? "Secondary" : null;
                OnPropertyChanged(nameof(SourcePlates));
                PlateVisualsNeedUpdate?.Invoke(this, EventArgs.Empty);
            }
            else if (!IsInSourceAddMode && !IsInDestAddMode)
            {
                // Normal mode, use normal highlighting
                HighlightSelectedPlate(plate);
            }
        }

        partial void OnSelectedSourcePlateChanged(Plate value)
        {
            if (value != null)
            {
                HighlightSelectedPlate(value);
            }
        }

        partial void OnSelectedDestinationPlateChanged(Plate value)
        {
            if (value != null)
            {
                HighlightSelectedPlate(value);
            }
        }

        private void OpenSaveScriptWindow()
        {
            // Logic to open Save Script window
        }

        private void OpenLoadScriptWindow()
        {
            // Logic to open Load Script window

        }
        partial void OnSelectedToolOptionChanged(string value)
        {
            if (value == "Labware Manager")
            {
                OpenLabware();
            }
        }
        private void OpenLabware()
        {
            OpenLabwareRequested?.Invoke(this, EventArgs.Empty);
        }
        public event EventHandler OpenLabwareRequested;

        private void SetupEventHandlers()
        {
            //TODO add appropriate checks before opening grippers, etc..
            short ret;
            int timeout = 0;
            byte index = 0;
            short errorCode = 0;

            // Clamps
            _events.OnClampsStateChanged += async (state, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Clamps {state}");
                await Task.Run(() =>
                {
                    if (state == "open")
                    {
                        _instrumentController.m_spel.Call("OpenClamps");
                    }
                    else if (state == "close")
                    {
                        _instrumentController.m_spel.Call("CloseClamps");
                    }
                });
            };

            // Epson
            _events.OnToolAttached += async (toolId, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Attaching {toolId}");
                await Task.Run(() =>
                {
                    switch (toolId)
                    {
                        case "33":
                            _instrumentController.m_spel.Call("AttachSM");
                            break;
                        case "100":
                            _instrumentController.m_spel.Call("AttachMD");
                            break;
                        case "300":
                            _instrumentController.m_spel.Call("AttachLG");
                            break;
                        case "96":
                            _instrumentController.m_spel.Call("Attach96");
                            break;
                    }
                });
            };

            _events.OnToolDetached += async (toolId, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Detaching {toolId}");
                await Task.Run(() =>
                {
                    switch (toolId)
                    {
                        case "33":
                            _instrumentController.m_spel.Call("DetachSM");
                            break;
                        case "100":
                            _instrumentController.m_spel.Call("DetachMD");
                            break;
                        case "300":
                            _instrumentController.m_spel.Call("DetachLG");
                            break;
                        case "96":
                            _instrumentController.m_spel.Call("Detach96");
                            break;
                    }
                });
            };

            _events.OnWashCompleted += async (toolId, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Washing {toolId}");
                await Task.Run(() =>
                {
                    if (Parameters.Testing)
                    {
                        _instrumentController.m_spel.Call("WashFake");
                    }
                    else
                    {
                        switch (toolId)
                        {
                            case "33":
                                _instrumentController.m_spel.Call("WashSM");
                                break;
                            case "100":
                                _instrumentController.m_spel.Call("WashMD");
                                break;
                            case "300":
                                _instrumentController.m_spel.Call("WashLG");
                                break;
                            case "96":
                                _instrumentController.m_spel.Call("Wash96");
                                break;
                        }
                    }
                });
            };

            _events.OnTransferCompleted += async (toolId, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Transfering {toolId}");
                await Task.Run(() =>
                {
                    switch (toolId)
                    {
                        case "33":
                            _instrumentController.m_spel.Call("TransferSM");
                            break;
                        case "100":
                            _instrumentController.m_spel.Call("TransferMD");
                            break;
                        case "300":
                            _instrumentController.m_spel.Call("TransferLG");
                            break;
                        case "96":
                            _instrumentController.m_spel.Call("Transfer96");
                            break;
                    }
                });
            };

            _events.OnToolSafe += async (toolId, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Safe Move {toolId}");
                await Task.Run(() =>
                {
                    _instrumentController.m_spel.Call("MoveSafe");
                });
            };

            //KX2
            _events.OnArmSafe += async (ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Safe Move for Arm");
                await Task.Run(() =>
                {
                    _instrumentController.MovetoTeachPoint("SafeLow");
                });
            };

            _events.OnArmHome += async (ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Arm homed");
                await Task.Run(() =>
                {
                    _instrumentController.KX2.WarningIdleStartTimeUpdate(); //Suppress warning/buzzer
                    ret = _instrumentController.KX2.TeachPointMoveTo("Home", Parameters.HomeArmSpeed, Parameters.ArmAccel, true, TimeoutMsec: ref timeout, SendEventWhenMoveDone: false, Index: ref index);
                });
            };

            _events.OnPlateGrabbedFromSequential += async (plateID, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"{plateID} grabbed from hotel");
                await Task.Run(() =>
                {
                    // TODO
                });
            };

            _events.OnPlateGrabbedFromHotel += async (plateID, location, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"{plateID} grabbed from hotel location {location}");
                await Task.Run(() =>
                {
                    if (_events.ResumeLine == 0)
                    {
                        errorCode = _instrumentController.GetPlateFromStack(plateID, (short)_stackCapacity, (short)location);
                    }
                    else
                    {
                        errorCode = _instrumentController.GetPlateFromStack(plateID, (short)_stackCapacity, (short)location, (short)_events.ResumeLine);
                    }
                    Int32.TryParse(_instrumentController.KX2.GetErrorCode(2), out _events.ResumeLine);
                });
            };

            _events.OnPlateGrabbedFromStage += async (plateID, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"{plateID} grabbed from stage");
                await Task.Run(() =>
                {
                    if (_events.ResumeLine == 0)
                    {
                        errorCode = _instrumentController.GetPlateFromStage(plateID);
                    }
                    else
                    {
                        errorCode = _instrumentController.GetPlateFromStage(plateID, (short)_events.ResumeLine);
                    }
                    Int32.TryParse(_instrumentController.KX2.GetErrorCode(2), out _events.ResumeLine);
                });
            };

            _events.OnPlatePlacedToStack += async (plateID, location, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"{plateID} placed to hotel location {location}");
                await Task.Run(() =>
                {
                    if (_events.ResumeLine == 0)
                    {
                        errorCode = _instrumentController.SetPlateToStack(plateID, (short)_stackCapacity, (short)location);
                    }
                    else
                    {
                        errorCode = _instrumentController.SetPlateToStack(plateID, (short)_stackCapacity, (short)location, (short)_events.ResumeLine);
                    }
                    Int32.TryParse(_instrumentController.KX2.GetErrorCode(2), out _events.ResumeLine);
                });
            };

            _events.OnPlatePlacedToStage += async (plateID, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"{plateID} placed to stage");
                await Task.Run(() =>
                {
                    if (_events.ResumeLine == 0)
                    {
                        errorCode = _instrumentController.SetPlateToStage(plateID);
                    }
                    else
                    {
                        errorCode = _instrumentController.SetPlateToStage(plateID, (short)_events.ResumeLine);
                    }
                    Int32.TryParse(_instrumentController.KX2.GetErrorCode(2), out _events.ResumeLine);
                });
            };

            // Carousel
            _events.OnCarouselRotated += async (stacker, plateType, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Carousel rotated to {stacker}");
                await Task.Run(() =>
                {
                    _instrumentController.RotateCarousel(stacker, plateType);
                });
            };
        }

        private void SetupEventHandlersText()
        {
            // Clamps
            _events.OnClampsStateChanged += async (state, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Clamps {state}");
                if (state == "open")
                {
                    // open clamps
                }
                else if (state == "close")
                {
                    // close clamps
                }
                await Task.Delay(1000, ct);
            };

            // Epson
            _events.OnToolAttached += async (toolId, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Tool {toolId} attached");
                await Task.Delay(1000, ct);
            };

            _events.OnToolDetached += async (toolId, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Tool {toolId} detached");
                await Task.Delay(1000, ct);
            };

            _events.OnWashCompleted += async (toolId, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Wash for Tool {toolId}");
                await Task.Delay(2000, ct);
            };

            _events.OnTransferCompleted += async (toolId, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Transfer for Tool {toolId}");
                await Task.Delay(1500, ct);
            };

            _events.OnToolSafe += async (toolId, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Safe Move for Tool {toolId}");
                await Task.Delay(1000, ct);
            };

            //KX2
            _events.OnArmSafe += async (ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Safe Move for Arm");
                await Task.Delay(1000, ct);
            };

            _events.OnArmHome += async (ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Arm homed");
                await Task.Delay(1000, ct);
            };

            _events.OnPlateGrabbedFromSequential += async (plateID, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"{plateID} grabbed from hotel");
                await Task.Delay(2000, ct);
            };

            _events.OnPlateGrabbedFromHotel += async (plateID, location, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"{plateID} grabbed from hotel location {location}");
                await Task.Delay(2000, ct);
            };

            _events.OnPlateGrabbedFromStage += async (plateID, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"{plateID} grabbed from stage");
                await Task.Delay(2000, ct);
            };

            _events.OnPlatePlacedToStack += async (plateID, location, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"{plateID} placed to hotel location {location}");
                await Task.Delay(2000, ct);
            };

            _events.OnPlatePlacedToStage += async (plateID, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"{plateID} placed to stage");
                await Task.Delay(2000, ct);
            };

            // Carousel
            _events.OnCarouselRotated += async (stacker, plateType, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                AppendStatus($"Carousel rotated to {stacker}");
                await Task.Delay(2000, ct);
            };
        }

        [RelayCommand]
        private void MinimizeWindow()
        {
            MinimizeWindowRequested?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler MinimizeWindowRequested;

        [RelayCommand]
        private void MaximizeWindow()
        {
            MaximizeWindowRequested?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler MaximizeWindowRequested;

        [RelayCommand]
        private void CloseWindow()
        {
            CloseWindowRequested?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler CloseWindowRequested;

        //**************LOAD/SAVE*******************
        // Add these properties to track script state
        [ObservableProperty]
        private bool _hasUnsavedChanges = false;

        [ObservableProperty]
        private string _currentScriptName = null;

        [ObservableProperty]
        private bool _isWorkingWithScript = false;

        // Add this method to track changes
        private void MarkAsModified()
        {
            HasUnsavedChanges = true;
        }

        // Update existing methods to mark as modified
        partial void OnSourcesToAddChanged(int value) => MarkAsModified();
        partial void OnReplicatesOfSourcesToAddChanged(int value) => MarkAsModified();
        partial void OnVolumeOfSourcesToAddChanged(int value) => MarkAsModified();

        // Add to existing plate collection change handlers
        private void OnSourcePlatesCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            MarkAsModified();
        }

        private void OnDestinationPlatesCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            MarkAsModified();
        }

        [RelayCommand]
        private void SaveScript()
        {
            // Create save dialog or use current script name
            string scriptName = CurrentScriptName;

            if (string.IsNullOrEmpty(scriptName))
            {
                // Show dialog to get script name
                var dialog = new Microsoft.Win32.SaveFileDialog()
                {
                    Filter = "Script files (*.script)|*.script|All files (*.*)|*.*",
                    DefaultExt = ".script"
                };

                if (dialog.ShowDialog() == true)
                {
                    scriptName = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
                }
                else
                {
                    return; // User cancelled
                }
            }

            try
            {
                SaveScriptToDatabase(scriptName);
                CurrentScriptName = scriptName;
                HasUnsavedChanges = false;
                IsWorkingWithScript = true;
                StatusText += $"Script '{scriptName}' saved successfully.\n";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save script: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void LoadScript()
        {
            try
            {
                var availableScripts = GetAvailableScripts();

                if (availableScripts.Count == 0)
                {
                    MessageBox.Show("No saved scripts found.", "Load Script", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Create a simple selection dialog (you might want to create a proper dialog)
                var scriptNames = string.Join("\n", availableScripts.Select((s, i) => $"{i + 1}. {s}"));
                var result = MessageBox.Show($"Available scripts:\n{scriptNames}\n\nEnter script name to load:",
                                           "Load Script", MessageBoxButton.OKCancel, MessageBoxImage.Question);

                if (result == MessageBoxResult.OK)
                {
                    // For now, just load the first one - you should implement proper selection
                    if (availableScripts.Count > 0)
                    {
                        LoadScriptFromDatabase(availableScripts[0]);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load script: {ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveScriptToDatabase(string scriptName)
        {
            var scriptInfo = new ScriptInfo
            {
                ScriptName = scriptName,
                SourcePlates = SourcePlates.ToList(),
                DestinationPlates = DestinationPlates.ToList(),
                SourcesToAdd = SourcesToAdd,
                ReplicatesOfSourcesToAdd = ReplicatesOfSourcesToAdd,
                VolumeOfSourcesToAdd = VolumeOfSourcesToAdd,
                AutoFillSlots = AutoFillSlots,
                SavedDate = DateTime.Now
            };

            SaveScriptToDatabase(scriptInfo);
        }

        private void SaveScriptToDatabase(ScriptInfo scriptInfo)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                // Create Scripts table if it doesn't exist
                using (var command = new SQLiteCommand(@"CREATE TABLE IF NOT EXISTS Scripts (
            ScriptName TEXT PRIMARY KEY,
            SourcePlatesJson TEXT NOT NULL,
            DestinationPlatesJson TEXT NOT NULL,
            SourcesToAdd INTEGER NOT NULL,
            ReplicatesOfSourcesToAdd INTEGER NOT NULL,
            VolumeOfSourcesToAdd INTEGER NOT NULL,
            AutoFillSlots INTEGER NOT NULL,
            SavedDate TEXT NOT NULL
        )", connection))
                {
                    command.ExecuteNonQuery();
                }

                // Serialize plates to JSON
                var sourcePlatesJson = System.Text.Json.JsonSerializer.Serialize(scriptInfo.SourcePlates);
                var destinationPlatesJson = System.Text.Json.JsonSerializer.Serialize(scriptInfo.DestinationPlates);

                // Save or update script
                using (var command = new SQLiteCommand(@"INSERT OR REPLACE INTO Scripts 
            (ScriptName, SourcePlatesJson, DestinationPlatesJson, SourcesToAdd, ReplicatesOfSourcesToAdd, 
             VolumeOfSourcesToAdd, AutoFillSlots, SavedDate)
            VALUES (@ScriptName, @SourcePlatesJson, @DestinationPlatesJson, @SourcesToAdd, 
                    @ReplicatesOfSourcesToAdd, @VolumeOfSourcesToAdd, @AutoFillSlots, @SavedDate)", connection))
                {
                    command.Parameters.AddWithValue("@ScriptName", scriptInfo.ScriptName);
                    command.Parameters.AddWithValue("@SourcePlatesJson", sourcePlatesJson);
                    command.Parameters.AddWithValue("@DestinationPlatesJson", destinationPlatesJson);
                    command.Parameters.AddWithValue("@SourcesToAdd", scriptInfo.SourcesToAdd);
                    command.Parameters.AddWithValue("@ReplicatesOfSourcesToAdd", scriptInfo.ReplicatesOfSourcesToAdd);
                    command.Parameters.AddWithValue("@VolumeOfSourcesToAdd", scriptInfo.VolumeOfSourcesToAdd);
                    command.Parameters.AddWithValue("@AutoFillSlots", scriptInfo.AutoFillSlots ? 1 : 0);
                    command.Parameters.AddWithValue("@SavedDate", scriptInfo.SavedDate.ToString("O"));
                    command.ExecuteNonQuery();
                }
            }
        }

        private List<string> GetAvailableScripts()
        {
            var scripts = new List<string>();
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                // Check if Scripts table exists
                using (var command = new SQLiteCommand("SELECT name FROM sqlite_master WHERE type='table' AND name='Scripts';", connection))
                {
                    if (command.ExecuteScalar() == null)
                    {
                        return scripts; // Table doesn't exist, return empty list
                    }
                }

                using (var command = new SQLiteCommand("SELECT ScriptName FROM Scripts ORDER BY SavedDate DESC", connection))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            scripts.Add(reader.GetString(0));
                        }
                    }
                }
            }
            return scripts;
        }

        private void LoadScriptFromDatabase(string scriptName)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var command = new SQLiteCommand("SELECT * FROM Scripts WHERE ScriptName = @ScriptName", connection))
                {
                    command.Parameters.AddWithValue("@ScriptName", scriptName);

                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            // Clear existing plates
                            ClearAllPlatesCommand.Execute(null);

                            // Deserialize plates
                            var sourcePlatesJson = reader.GetString("SourcePlatesJson");
                            var destinationPlatesJson = reader.GetString("DestinationPlatesJson");

                            var loadedSourcePlates = System.Text.Json.JsonSerializer.Deserialize<List<SourcePlate>>(sourcePlatesJson);
                            var loadedDestinationPlates = System.Text.Json.JsonSerializer.Deserialize<List<DestinationPlate>>(destinationPlatesJson);

                            // Load plates into collections
                            foreach (var plate in loadedSourcePlates)
                            {
                                SourcePlates.Add(plate);
                            }

                            foreach (var plate in loadedDestinationPlates)
                            {
                                DestinationPlates.Add(plate);
                            }

                            // Load other settings
                            SourcesToAdd = reader.GetInt32("SourcesToAdd");
                            ReplicatesOfSourcesToAdd = reader.GetInt32("ReplicatesOfSourcesToAdd");
                            VolumeOfSourcesToAdd = reader.GetInt32("VolumeOfSourcesToAdd");
                            AutoFillSlots = reader.GetInt32("AutoFillSlots") == 1;

                            // Update script state
                            CurrentScriptName = scriptName;
                            HasUnsavedChanges = false;
                            IsWorkingWithScript = true;

                            // Update UI
                            PopulateCarousel(new List<Plate>(loadedSourcePlates.Cast<Plate>().Concat(loadedDestinationPlates.Cast<Plate>())));

                            StatusText += $"Script '{scriptName}' loaded successfully.\n";
                        }
                        else
                        {
                            throw new Exception($"Script '{scriptName}' not found.");
                        }
                    }
                }
            }
        }

        // Update the file option handlers
        partial void OnSelectedFileOptionChanged(string value)
        {
            if (value == "Save Script")
            {
                SaveScriptCommand.Execute(null);
                SelectedFileOption = null; // Reset selection
            }
            else if (value == "Load Script")
            {
                LoadScriptCommand.Execute(null);
                SelectedFileOption = null; // Reset selection
            }
        }

        // Add this method to update collection change handlers in constructor
        private void SetupCollectionChangeHandlers()
        {
            SourcePlates.CollectionChanged += OnSourcePlatesCollectionChanged;
            DestinationPlates.CollectionChanged += OnDestinationPlatesCollectionChanged;
            SourcePlates.CollectionChanged += (s, e) => UpdateSourceItemsCollection(new AddButtonViewModel("Source", new RelayCommand<string>(_ => StartAddSourcePlate())));
            DestinationPlates.CollectionChanged += (s, e) => UpdateDestinationItemsCollection(new AddButtonViewModel("Destination", new RelayCommand<string>(_ => StartAddDestinationPlate())));
        }

        // Add the ScriptInfo class
        public class ScriptInfo
        {
            public string ScriptName { get; set; }
            public List<SourcePlate> SourcePlates { get; set; }
            public List<DestinationPlate> DestinationPlates { get; set; }
            public int SourcesToAdd { get; set; }
            public int ReplicatesOfSourcesToAdd { get; set; }
            public int VolumeOfSourcesToAdd { get; set; }
            public bool AutoFillSlots { get; set; }
            public DateTime SavedDate { get; set; }
        }
    }
}