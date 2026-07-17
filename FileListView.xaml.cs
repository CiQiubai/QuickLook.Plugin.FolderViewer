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
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace QuickLook.Plugin.FolderViewer
{
    /// <summary>
    ///     Interaction logic for FileListView.xaml
    /// </summary>
    public partial class FileListView : UserControl, IDisposable
    {
        /// <summary>
        ///     Raised when the user expands a directory node whose contents haven't been loaded yet.
        ///     The subscriber (FolderInfoPanel) is responsible for loading the children asynchronously.
        /// </summary>
        public event Action<FileEntry> LoadSubDirectoryRequested;

        public FileListView()
        {
            InitializeComponent();
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }

        public void SetDataContext(object context)
        {
            treeGrid.DataContext = context;

            treeView.ItemContainerGenerator.StatusChanged += (sender, e) =>
            {
                if (treeView.ItemContainerGenerator.Status
                    != System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
                    return;

                // return when empty
                if (treeView.Items.Count == 0)
                    return;

                // return when there are more than one root nodes
                if (treeView.Items.Count > 1)
                    return;

                var root = (TreeViewItem)treeView.ItemContainerGenerator.ContainerFromItem(treeView.Items[0]);
                if (root == null)
                    return;

                root.IsExpanded = true;
            };

            // Hook the Expanded event at the TreeView level to catch all TreeViewItems
            treeView.AddHandler(TreeViewItem.ExpandedEvent,
                new RoutedEventHandler(OnTreeViewItemExpanded));
        }

        private void OnTreeViewItemExpanded(object sender, RoutedEventArgs e)
        {
            if (!(e.OriginalSource is TreeViewItem item))
                return;

            if (!(item.DataContext is FileEntry entry))
                return;

            // Only trigger lazy loading for unloaded directories
            if (!entry.IsFolder || entry.IsLoaded)
                return;

            // Delegate to FolderInfoPanel — it will set IsLoaded = true
            // at the start of LoadSubDirectory to prevent duplicate loads.
            LoadSubDirectoryRequested?.Invoke(entry);
        }

        private void OnItemMouseDoubleClick(object sender, MouseButtonEventArgs args)
        {
            if (sender is TreeViewItem item)
            {
                if (!item.IsSelected)
                {
                    return;
                }
                var fullPath = (item.DataContext as FileEntry)?.FullPath;
                if (File.Exists(fullPath) || Directory.Exists(fullPath))
                {
                    OpenWithDefaultProgram(fullPath);
                }
            }
        }

        public static void OpenWithDefaultProgram(string path)
        {
            Process fileopener = new Process();
            fileopener.StartInfo.FileName = "explorer";
            fileopener.StartInfo.Arguments = $"\"{path}\"";
            fileopener.Start();
        }
    }
}