namespace QuinSlate.AssetGenerator.Imaging;

/// <summary>
/// The resampling algorithm used to downscale the source image.
/// </summary>
public enum ResamplingMode
{
    /// <summary>Nearest-neighbour (WIC). No smoothing.</summary>
    NearestNeighbor,

    /// <summary>Bilinear (WIC).</summary>
    Linear,

    /// <summary>Bicubic (WIC). Sharper than Fant but prone to ringing.</summary>
    Cubic,

    /// <summary>Fant area-averaging (WIC). Clean but soft on large reductions.</summary>
    Fant,

    /// <summary>Lanczos windowed-sinc. Sharpest faithful downscale; the default.</summary>
    Lanczos
}
