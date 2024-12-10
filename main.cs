using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace operating_systems_project
{
    public partial class Form1 : Form
    {
        private FileSystemWatcher _fileWatcher;
        private string _journalFolderPath;
        private Dictionary<string, List<string>> _fileSnapshots;
        private string selectedFolderPath;
        private string _currentlyOpenedFile;
        private List<string> deletedFiles = new List<string>();

        public Form1()
        {
            InitializeComponent();
            _fileSnapshots = new Dictionary<string, List<string>>();
        }

        private void Form1_Load(object sender, EventArgs e) { }

        private void button1_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog folderBrowser = new FolderBrowserDialog())
            {
                if (folderBrowser.ShowDialog() == DialogResult.OK)
                {
                    selectedFolderPath = folderBrowser.SelectedPath;
                    string[] files = Directory.GetFiles(selectedFolderPath);

                    listbx_files.Items.Clear();
                    foreach (string file in files)
                    {
                        listbx_files.Items.Add(Path.GetFileName(file));
                    }

                    listbx_files.Visible = true;
                    label1.Visible = true;

                    _journalFolderPath = Path.Combine(selectedFolderPath, ".journal");
                    Directory.CreateDirectory(_journalFolderPath); // hidden folder
                    DirectoryInfo journalDir = new DirectoryInfo(_journalFolderPath);
                    journalDir.Attributes |= FileAttributes.Hidden;

                    SetupFileWatcher(selectedFolderPath);
                    MessageBox.Show("Monitoring folder and saving changes in the journal!");
                }
            }
        }

        private void SetupFileWatcher(string folderPath)
        {
            _fileWatcher?.Dispose();

            _fileWatcher = new FileSystemWatcher(folderPath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };

            _fileWatcher.Changed += OnFileChanged;
            TakeInitialSnapshots(folderPath);
        }

        private void TakeInitialSnapshots(string folderPath)
        {
            string[] files = Directory.GetFiles(folderPath, "*.txt");
            foreach (string file in files)
            {
                _fileSnapshots[file] = File.ReadAllLines(file).ToList();
            }
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            if (File.Exists(e.FullPath) && Path.GetExtension(e.FullPath).Equals(".txt", StringComparison.OrdinalIgnoreCase))
            {
                var currentLines = File.ReadAllLines(e.FullPath).ToList();
                var previousLines = _fileSnapshots.ContainsKey(e.FullPath) ? _fileSnapshots[e.FullPath] : new List<string>();

                DetectLineChanges(e.FullPath, previousLines, currentLines);
                _fileSnapshots[e.FullPath] = currentLines;
            }
        }

        private void DetectLineChanges(string filePath, List<string> oldLines, List<string> newLines)
        {
            string journalFilePath = GetJournalFilePath(filePath);

            using (StreamWriter writer = new StreamWriter(journalFilePath, true))
            {
                for (int i = 0; i < Math.Max(oldLines.Count, newLines.Count); i++)
                {
                    if (i >= oldLines.Count)
                    {
                        writer.WriteLine($"{DateTime.Now:MM-dd-yy HH:mm:ss} + l{i + 1}:{newLines[i]}");
                    }
                    else if (i >= newLines.Count)
                    {
                        writer.WriteLine($"{DateTime.Now:MM-dd-yy HH:mm:ss} - l{i + 1}:{oldLines[i]}");
                    }
                    else if (oldLines[i] != newLines[i])
                    {
                        writer.WriteLine($"{DateTime.Now:MM-dd-yy HH:mm:ss} - l{i + 1}:{oldLines[i]}");
                        writer.WriteLine($"{DateTime.Now:MM-dd-yy HH:mm:ss} + l{i + 1}:{newLines[i]}");
                    }
                }
            }
            EnsureJournalLimit(journalFilePath);
        }

        private string GetJournalFilePath(string filePath)
        {
            string fileName = Path.GetFileName(filePath);
            string safeFileName = fileName.Replace(".", "_");
            string journalFileName = $"j1_{safeFileName}.DAT";
            string journalFilePath = Path.Combine(_journalFolderPath, journalFileName);

            if (!File.Exists(journalFilePath))
            {
                using (StreamWriter writer = new StreamWriter(journalFilePath))
                {
                    writer.WriteLine("Time Stamp  a/r  line");
                }
            }

            return journalFilePath;
        }

       
        private void EnsureJournalLimit(string journalFilePath)
        {
            const int maxJournalSizeInLines = 50;
            try
            {
                var lines = File.ReadAllLines(journalFilePath).ToList();

                if (lines.Count > maxJournalSizeInLines)
                {
                    // removes the oldest lines 
                    lines = lines.Skip(lines.Count - maxJournalSizeInLines).ToList();
                    File.WriteAllLines(journalFilePath, lines);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error enforcing journal limit: {ex.Message}", "Error");
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (_currentlyOpenedFile != null && File.Exists(_currentlyOpenedFile))
            {
                richTextBox1.ReadOnly = true;

                int selectionStart = richTextBox1.SelectionStart;
                if (selectionStart == -1)
                {
                    MessageBox.Show("No text is selected. Please select a line to replay from.", "Error");
                    return;
                }

                int selectedLine = richTextBox1.GetLineFromCharIndex(selectionStart);

                DateTime startDateTime = dateTimePickerStart.Value;

                ReplayFromDateTime(startDateTime);
            }
            else
            {
                MessageBox.Show("No file is currently opened or the file doesn't exist.", "Error");
            }
        }
        private void ReplayFromDateTime(DateTime startDateTime)
        {
            try
            {
                if (string.IsNullOrEmpty(_currentlyOpenedFile) || !File.Exists(_currentlyOpenedFile))
                {
                    MessageBox.Show("No journal file selected or the file does not exist.", "Error");
                    return;
                }

                string originalFileName = Path.GetFileName(_currentlyOpenedFile); 

                string fileBaseName = originalFileName.Replace("j1_", "").Replace("_txt", "").Replace(".DAT", "");  // make reconstructed name be the same as the original
                string reconstructedFileName = fileBaseName + ".txt";

                string reconstructedFilePath = Path.Combine(selectedFolderPath, reconstructedFileName);


                string directory = Path.GetDirectoryName(reconstructedFilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var journalEntries = File.ReadAllLines(_currentlyOpenedFile).Skip(1).ToList(); //skip the header that i created for convinience
                var fileLines = new List<string>();

                if (File.Exists(reconstructedFilePath))
                {
                    fileLines.AddRange(File.ReadAllLines(reconstructedFilePath));
                }

                foreach (var entry in journalEntries)
                {
                    var parts = entry.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 3) continue; 

                    try
                    {
                        DateTime entryTime = DateTime.ParseExact(parts[0] + " " + parts[1], "MM-dd-yy HH:mm:ss", null);
                        if (entryTime < startDateTime)
                            continue;

                        string action = parts[2]; 
                        string[] lineInfo = parts[3].Split(':');
                        int lineNumber = int.Parse(lineInfo[0].Substring(1)); 
                        string lineContent = lineInfo.Length > 1 ? lineInfo[1] : string.Empty; 

                        while (fileLines.Count < lineNumber)
                        {
                            fileLines.Add(string.Empty);
                        }

                        if (action == "+")
                            fileLines[lineNumber - 1] = lineContent;
                        else if (action == "-")
                            fileLines[lineNumber - 1] = string.Empty;
                    }
                    catch
                    {
                        continue;
                    }
                }

                File.WriteAllLines(reconstructedFilePath, fileLines.Where(l => !string.IsNullOrEmpty(l)));

                MessageBox.Show($"Journal replay completed! File saved at: {reconstructedFilePath}", "Success");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error replaying journal: {ex.Message}", "Error");
            }
        }



        private void listbx_files_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void listboxjournals_DoubleClick(object sender, EventArgs e)
        {
            if (listboxjournals.SelectedItem != null)
            {
                string selectedJournalFileName = listboxjournals.SelectedItem.ToString();
                string selectedJournalFilePath = Path.Combine(_journalFolderPath, selectedJournalFileName);

                if (File.Exists(selectedJournalFilePath))
                {
                    try
                    {
                        string journalContent = File.ReadAllText(selectedJournalFilePath);

                        richTextBox1.ReadOnly = true;

                        richTextBox1.Text = journalContent;
                        _currentlyOpenedFile = selectedJournalFilePath;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error loading journal file: {ex.Message}", "Error");
                    }
                }
            }
        }

       

        private void listbx_files_DoubleClick(object sender, EventArgs e)
        {
            richTextBox1.ReadOnly = false;

            if (listbx_files.SelectedItem != null)
            {
                string selectedFileName = listbx_files.SelectedItem.ToString();
                string selectedFilePath = Path.Combine(selectedFolderPath, selectedFileName);

                if (File.Exists(selectedFilePath))
                {
                    try
                    {
                        string fileContent = File.ReadAllText(selectedFilePath);
                        richTextBox1.Text = fileContent;
                        _currentlyOpenedFile = selectedFilePath;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error loading file: {ex.Message}", "Error");
                    }
                }
            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        

        private void btnviewjournals_Click(object sender, EventArgs e)
        {
            if (Directory.Exists(_journalFolderPath))
            {
                listboxjournals.Items.Clear();
                string[] journalFiles = Directory.GetFiles(_journalFolderPath, "*.DAT");

                foreach (string journalFile in journalFiles)
                {
                    listboxjournals.Items.Add(Path.GetFileName(journalFile));
                }
                label3.Visible = true;
                listboxjournals.Visible = true;
            }
            else
            {
                MessageBox.Show("No journal directory exists.", "Error");
            }
        }

     
       


        private void button4_Click(object sender, EventArgs e)
        {
                if (_currentlyOpenedFile != null && File.Exists(_currentlyOpenedFile))
                {
                    try
                    {
                        listBox1.Items.Add(Path.GetFileName(_currentlyOpenedFile));

                        File.Delete(_currentlyOpenedFile);

                        listbx_files.Items.Remove(Path.GetFileName(_currentlyOpenedFile));

                        deletedFiles.Add(Path.GetFileName(_currentlyOpenedFile));

                        _currentlyOpenedFile = null;

                        richTextBox1.Clear();
                        label2.Visible = true;
                        listBox1.Visible = true;
                    }
                    catch (Exception ex)
                    {
                        
                        MessageBox.Show($"Error deleting file: {ex.Message}", "Error");
                    }
                }
                else
                {
                    
                    MessageBox.Show("No file is currently opened for deletion or the file doesn't exist.", "Error");
                }
            

        }

        private void btnSave_Click_1(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentlyOpenedFile))
            {
                try
                {
                    File.WriteAllText(_currentlyOpenedFile, richTextBox1.Text);
                    MessageBox.Show("File saved successfully!", "Save File");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error saving file: {ex.Message}", "Error");
                }
            }
            else
            {
                MessageBox.Show("No file is currently opened for editing.", "Error");
            }
        }

        private void listboxjournals_SelectedIndexChanged(object sender, EventArgs e)
        {

            
        }

        

        

        private void dateTimePickerStart_ValueChanged(object sender, EventArgs e)
        {

        }
        

    }
}
