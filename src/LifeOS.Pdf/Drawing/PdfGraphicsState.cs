using System;

namespace LifeOS.Pdf.Drawing
{
    /// <summary>
    /// Returned by <see cref="PdfGraphics.Save"/>. Disposing it restores the graphics
    /// state (transformations, opacity, clipping) to what it was before Save.
    /// </summary>
    public sealed class PdfGraphicsState : IDisposable
    {
        private readonly PdfGraphics _graphics;
        private readonly int _depth;
        private bool _disposed;

        internal PdfGraphicsState(PdfGraphics graphics, int depth)
        {
            _graphics = graphics;
            _depth = depth;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            // Restore everything opened since this Save, even nested states the caller forgot.
            _graphics.RestoreTo(_depth - 1);
        }
    }
}
