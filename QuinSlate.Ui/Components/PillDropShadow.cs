using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using System;
using System.Numerics;

namespace QuinSlate.Ui.Components;

/// <summary>
/// The hand-rolled Composition drop shadow behind the app's floating pills over the editor (the
/// limit notice and the search pill).
/// </summary>
/// <remarks>
/// Not a <c>ThemeShadow</c>: at this SDK target ThemeShadow projects onto XAML receiver surfaces,
/// and the only thing behind a pill is the WebView2's separate airspace, so a ThemeShadow would draw
/// nothing. This shadow is part of the host element's own visual, so it needs no receiver. The host
/// is an empty element behind the pill, stretched to its size; the owner animates both together so
/// the shadow tracks the pill.
/// </remarks>
internal sealed class PillDropShadow
{
    private const float BlurRadius = 16;
    private const float Opacity = 0.28f;
    private const float OffsetY = 4;

    private readonly Border pill;
    private readonly DropShadow dropShadow;
    private readonly SpriteVisual shadowVisual;

    /// <summary>Attaches the shadow to <paramref name="shadowHost"/>, shaped and sized after <paramref name="pill"/>.</summary>
    public PillDropShadow(UIElement shadowHost, Border pill)
    {
        if (shadowHost == null)
        {
            throw new ArgumentNullException(nameof(shadowHost));
        }

        if (pill == null)
        {
            throw new ArgumentNullException(nameof(pill));
        }

        this.pill = pill;
        Compositor compositor = ElementCompositionPreview.GetElementVisual(shadowHost).Compositor;

        dropShadow = compositor.CreateDropShadow();
        dropShadow.Color = Colors.Black;
        dropShadow.Opacity = Opacity;
        dropShadow.BlurRadius = BlurRadius;
        dropShadow.Offset = new Vector3(0, OffsetY, 0);

        shadowVisual = compositor.CreateSpriteVisual();
        shadowVisual.Shadow = dropShadow;

        ElementCompositionPreview.SetElementChildVisual(shadowHost, shadowVisual);
        pill.SizeChanged += OnPillSizeChanged;
    }

    private void OnPillSizeChanged(object sender, SizeChangedEventArgs e)
    {
        SyncGeometry();
    }

    // The shadow is masked to the pill's rounded rectangle so it does not bleed past the corners.
    // A Border exposes no alpha mask of its own, so the mask is built from a shape visual matching
    // the pill's size and corner radius, rendered to a surface, and handed to the shadow.
    private void SyncGeometry()
    {
        float width = (float)pill.ActualWidth;
        float height = (float)pill.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        Compositor compositor = shadowVisual.Compositor;
        var size = new Vector2(width, height);
        shadowVisual.Size = size;

        CompositionRoundedRectangleGeometry geometry = compositor.CreateRoundedRectangleGeometry();
        geometry.Size = size;
        geometry.CornerRadius = new Vector2((float)pill.CornerRadius.TopLeft);

        CompositionSpriteShape shape = compositor.CreateSpriteShape(geometry);
        shape.FillBrush = compositor.CreateColorBrush(Colors.Black);

        ShapeVisual maskVisual = compositor.CreateShapeVisual();
        maskVisual.Size = size;
        maskVisual.Shapes.Add(shape);

        CompositionVisualSurface maskSurface = compositor.CreateVisualSurface();
        maskSurface.SourceVisual = maskVisual;
        maskSurface.SourceSize = size;

        dropShadow.Mask = compositor.CreateSurfaceBrush(maskSurface);
    }
}
