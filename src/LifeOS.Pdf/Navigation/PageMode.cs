namespace LifeOS.Pdf.Navigation
{
    /// <summary>Which side panel the viewer shows when the document opens (§7.7.2 /PageMode).</summary>
    public enum PageMode
    {
        /// <summary>Not specified. The bookmark panel is opened automatically when the document has bookmarks.</summary>
        Default,

        /// <summary>No panel.</summary>
        None,

        /// <summary>Bookmarks.</summary>
        Outlines,

        /// <summary>Page thumbnails.</summary>
        Thumbnails,

        /// <summary>Attachments panel.</summary>
        Attachments,

        /// <summary>Full-screen presentation mode.</summary>
        FullScreen,
    }
}
