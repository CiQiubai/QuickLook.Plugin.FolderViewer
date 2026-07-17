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
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using QuickLook.Common.Annotations;

namespace QuickLook.Plugin.FolderViewer
{
    public class FileEntry : IComparable<FileEntry>, INotifyPropertyChanged
    {
        private readonly List<FileEntry> _children = new List<FileEntry>();
        private bool _isLoaded;
        private bool? _hasSubItems;

        /// <summary>
        ///     Placeholder entry shown as a child of unloaded directories to make the expand arrow appear.
        /// </summary>
        public static readonly FileEntry Placeholder = new FileEntry("Loading...", false);

        public FileEntry(string name, bool isFolder)
        {
            Name = name;
            IsFolder = isFolder;
        }

        /// <summary>
        ///     Internal mutable list. Use <see cref="ChildrenSorted" /> for binding.
        /// </summary>
        public List<FileEntry> Children => _children;

        /// <summary>
        ///     Returns a sorted, read-only view of children (folders first, then alphabetical).
        ///     Rebuilt every time it is accessed to reflect the latest list state.
        /// </summary>
        public ReadOnlyCollection<FileEntry> ChildrenSorted
        {
            get
            {
                if (_children.Count == 0)
                    return null;

                _children.Sort();
                return _children.AsReadOnly();
            }
        }

        /// <summary>
        ///     Whether sub-directory contents have already been loaded.
        /// </summary>
        public bool IsLoaded
        {
            get => _isLoaded;
            set
            {
                if (_isLoaded == value)
                    return;
                _isLoaded = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        ///     Whether this directory has any sub-items (files or folders).
        ///     <c>null</c> means not yet probed.
        /// </summary>
        public bool? HasSubItems
        {
            get => _hasSubItems;
            set
            {
                if (_hasSubItems == value)
                    return;
                _hasSubItems = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        ///     Notifies the UI that <see cref="ChildrenSorted" /> has changed.
        /// </summary>
        public void NotifyChildrenChanged()
        {
            OnPropertyChanged(nameof(ChildrenSorted));
        }

        public string Name { get; set; }
        public bool IsFolder { get; set; }
        public ulong Size { get; set; }
        public DateTime ModifiedDate { get; set; }
        public string FullPath { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public int CompareTo(FileEntry other)
        {
            if (other == null)
                return 1;

            // Placeholder always sorts last
            if (ReferenceEquals(this, Placeholder))
                return 1;
            if (ReferenceEquals(other, Placeholder))
                return -1;

            if (IsFolder == other.IsFolder)
                return string.Compare(Name, other.Name, StringComparison.CurrentCulture);

            if (IsFolder)
                return -1;

            return 1;
        }

        public override string ToString()
        {
            if (IsFolder)
                return $"{Name}";

            return $"{Name},{IsFolder},{Size},{ModifiedDate}";
        }
    }
}