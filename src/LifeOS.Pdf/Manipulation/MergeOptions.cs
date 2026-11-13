namespace LifeOS.Pdf.Manipulation
{
    /// <summary>Options for <see cref="PdfMerger"/>.</summary>
    public sealed class MergeOptions
    {
        /// <summary>Add one bookmark per input (its title, or the file name) with the input's own bookmarks under it. Default true.</summary>
        public bool BookmarkPerDocument { get; set; } = true;

        /// <summary>Keep the bookmarks of the inputs. Default true.</summary>
        public bool KeepBookmarks { get; set; } = true;

        /// <summary>Limits used when reading input files.</summary>
        public PdfReadOptions? ReadOptions { get; set; }
    }
}
