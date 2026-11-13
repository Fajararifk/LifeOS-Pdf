namespace LifeOS.Pdf.Forms
{
    /// <summary>What a push button does. JavaScript actions are never supported (§12.7.5).</summary>
    public enum ButtonAction
    {
        /// <summary>No action; the button only shows its caption.</summary>
        None,

        /// <summary>Resets the form to its default values (a ResetForm action).</summary>
        Reset,
    }
}
