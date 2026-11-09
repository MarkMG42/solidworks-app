using System;
using System.Collections.Generic;
using System.Linq;
namespace SWToolsHub.Portable
{
    internal sealed class LibraryViewState
    {
        private string section;
        private bool searching;
        private string[] paths;
        public void Invalidate() { paths = null; }
        public bool NeedsUpdate(string nextSection, bool nextSearching, IEnumerable<string> nextPaths)
        {
            string[] next = nextPaths.ToArray();
            if (paths != null && section == nextSection && searching == nextSearching && paths.SequenceEqual(next, StringComparer.OrdinalIgnoreCase)) return false;
            section = nextSection; searching = nextSearching; paths = next; return true;
        }
        public static int FullWidth(int clientWidth, bool scrollbarVisible, int scrollbarWidth) => clientWidth + (scrollbarVisible ? scrollbarWidth : 0);
    }
}
