// Copyright © 2020 Paddy Xu, Frank Becker
// 
// This file is part of QuickLook program.
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using QuickLook.Common.Annotations;
using QuickLook.Common.ExtensionMethods;

namespace QuickLook.Plugin.FolderViewer
{
    /// <summary>
    ///     Interaction logic for FolderInfoPanel.xaml
    /// </summary>
    public partial class FolderInfoPanel : UserControl, IDisposable, INotifyPropertyChanged
    {
        private readonly Dictionary<string, FileEntry> _fileEntries = new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private bool _disposed;
        private double _loadPercent;
        private bool _stop;

        public FolderInfoPanel(string path)
        {
            InitializeComponent();

            // design-time only
            Resources.MergedDictionaries.Clear();

            // Wire up lazy-loading callback: when the user expands a directory node,
            // FileListView will call back into this method.
            fileListView.LoadSubDirectoryRequested += LoadSubDirectory;

            BeginLoadDirectory(path);
        }

        public bool Stop
        {
            set => _stop = value;
            get => _stop;
        }

        public double LoadPercent
        {
            get => _loadPercent;
            private set
            {
                if (Math.Abs(value - _loadPercent) < 0.001)
                    return;
                _loadPercent = value;
                OnPropertyChanged();
            }
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);

            _disposed = true;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            if (fileListView != null)
                fileListView.LoadSubDirectoryRequested -= LoadSubDirectory;

            fileListView?.Dispose();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void BeginLoadDirectory(string path)
        {
            new Task(() =>
            {
                // Create root entry for the directory itself
                var root = new FileEntry(Path.GetFileName(path), true)
                {
                    FullPath = path,
                    IsLoaded = true
                };
                _fileEntries[path] = root;

                // Load only the first level
                LoadSingleLevel(path, root, _cts.Token, out var totalDirsL, out var totalFilesL, out var totalSizeL);

                Dispatcher.Invoke(() =>
                {
                    if (_disposed)
                        return;

                    // Set data context to root's sorted children
                    fileListView.SetDataContext(root.ChildrenSorted);
                    totalSize.Content =
                        $"Total size: {totalSizeL.ToPrettySize(2)}";
                    numFolders.Content =
                        $"Folders: {totalDirsL}";
                    numFiles.Content =
                        $"Files: {totalFilesL}";
                });

                LoadPercent = 100d;
            }).Start();
        }

        /// <summary>
        ///     Loads the immediate children (files + sub-directories) of a single directory.
        ///     Does NOT recurse into sub-directories; instead places a placeholder in each
        ///     sub-directory so the TreeView shows an expand arrow.
        /// </summary>
        private void LoadSingleLevel(string dirPath, FileEntry parentEntry, CancellationToken ct,
            out long totalDirs, out long totalFiles, out long totalSize)
        {
            totalDirs = totalFiles = totalSize = 0L;

            var childDirs = new List<FileEntry>();
            var childFiles = new List<FileEntry>();

            try
            {
                var dirInfo = new DirectoryInfo(dirPath);

                // Process files
                foreach (var file in dirInfo.EnumerateFiles())
                {
                    if (ct.IsCancellationRequested || _stop)
                        break;

                    totalFiles++;
                    totalSize += file.Length;

                    var fe = new FileEntry(file.Name, false)
                    {
                        Size = (ulong)file.Length,
                        ModifiedDate = file.LastWriteTime,
                        FullPath = file.FullName
                    };
                    _fileEntries[file.FullName] = fe;
                    childFiles.Add(fe);
                }

                // Process sub-directories
                if (!ct.IsCancellationRequested && !_stop)
                {
                    foreach (var dir in dirInfo.EnumerateDirectories())
                    {
                        if (ct.IsCancellationRequested || _stop)
                            break;

                        totalDirs++;

                        var fe = new FileEntry(dir.Name, true)
                        {
                            FullPath = dir.FullName
                        };
                        _fileEntries[dir.FullName] = fe;

                        // Probe whether the sub-directory has any children (fast check)
                        fe.HasSubItems = HasAnyChild(dir.FullName);

                        if (fe.HasSubItems == true)
                        {
                            // Add a placeholder so TreeView shows an expand arrow
                            fe.Children.Add(FileEntry.Placeholder);
                        }

                        childDirs.Add(fe);
                    }
                }
            }
            catch (Exception)
            {
                totalDirs++;
            }

            // Batch-add to parent: folders first, then files, then sort
            parentEntry.Children.AddRange(childDirs);
            parentEntry.Children.AddRange(childFiles);
            parentEntry.Children.Sort();
        }

        /// <summary>
        ///     Called by <see cref="FileListView" /> when the user expands a TreeViewItem
        ///     representing a sub-directory that hasn't been loaded yet.
        /// </summary>
        public void LoadSubDirectory(FileEntry dirEntry)
        {
            if (_disposed || _stop || dirEntry == null || dirEntry.IsLoaded)
                return;

            if (!dirEntry.IsFolder)
                return;

            // Mark as loaded immediately to prevent duplicate calls
            // from rapid expand/collapse/expand sequences.
            dirEntry.IsLoaded = true;

            // Cancel any pending load to avoid concurrent loads
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            new Task(() =>
            {
                if (ct.IsCancellationRequested || _stop)
                    return;

                LoadSingleLevel(dirEntry.FullPath, dirEntry, ct, out _, out _, out _);

                if (_disposed || _stop || ct.IsCancellationRequested)
                    return;

                Dispatcher.Invoke(() =>
                {
                    if (_disposed || _stop)
                        return;

                    // Remove placeholder and notify UI
                    dirEntry.Children.Remove(FileEntry.Placeholder);
                    dirEntry.NotifyChildrenChanged();
                });
            }).Start();
        }

        /// <summary>
        ///     Quickly checks whether a directory has any files or sub-directories.
        ///     Uses early-exit: returns true as soon as the first entry is found.
        /// </summary>
        private static bool HasAnyChild(string dirPath)
        {
            try
            {
                using (var enumerator = Directory.EnumerateFileSystemEntries(dirPath).GetEnumerator())
                {
                    return enumerator.MoveNext();
                }
            }
            catch
            {
                return false;
            }
        }

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}