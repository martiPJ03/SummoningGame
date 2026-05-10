using UnityEngine;

/// <summary>
/// Utility class for building health/mana bars with SpriteRenderers.
/// Abstracts common bar building logic used by UnitHealthBar and ManaBarUI.
/// </summary>
public static class BarBuilder
{
    /// <summary>
    /// Builds a bar with border, background, and fill components.
    /// </summary>
    /// <param name="barName">Name for the root GameObject</param>
    /// <param name="barWidth">Width of the bar in world units</param>
    /// <param name="barHeight">Height of the bar in world units</param>
    /// <param name="barPositionX">X position in world space</param>
    /// <param name="barPositionY">Y position in world space</param>
    /// <param name="barPositionZ">Z position in world space</param>
    /// <param name="borderColor">Color of the border</param>
    /// <param name="bgColor">Color of the background</param>
    /// <param name="sortingOrder">Sorting order for renderers</param>
    /// <param name="barRoot">Output: the root transform of the bar</param>
    /// <param name="border">Output: the border SpriteRenderer</param>
    /// <param name="bg">Output: the background SpriteRenderer</param>
    /// <param name="fill">Output: the fill SpriteRenderer</param>
    public static void BuildBar(
        string barName,
        float barWidth,
        float barHeight,
        float barPositionX,
        float barPositionY,
        float barPositionZ,
        Color borderColor,
        Color bgColor,
        int sortingOrder,
        out Transform barRoot,
        out SpriteRenderer border,
        out SpriteRenderer bg,
        out SpriteRenderer fill)
    {
        Sprite px = CreatePixelSprite();

        // Root: position in world space
        var root = new GameObject(barName);
        root.transform.SetParent(null);
        root.transform.localPosition = new Vector3(barPositionX, barPositionY, barPositionZ);
        barRoot = root.transform;

        float borderPad = barHeight * 0.3f;

        // Border — centrat, lleugerament més gran
        border = MakeRenderer(root, "Border", px, borderColor, sortingOrder - 2);
        border.transform.localScale = new Vector3(barWidth + borderPad,
                                                   barHeight + borderPad, 1f);
        border.transform.localPosition = Vector3.zero;

        // Background — centrat, mida exacta de la barra
        bg = MakeRenderer(root, "BG", px, bgColor, sortingOrder - 1);
        bg.transform.localScale = new Vector3(barWidth, barHeight, 1f);
        bg.transform.localPosition = Vector3.zero;

        // Fill — pivot al costat esquerre mitjançant posició
        // El sprite té pivot central (0.5, 0.5). Per simular pivot esquerre:
        //   posicionem el fill a (-barWidth/2 + fillWidth/2, 0)
        // Comencem amb fillWidth = barWidth (ple) i ho actualitzem a UpdateFillScale
        fill = MakeRenderer(root, "Fill", px, Color.white, sortingOrder);
        fill.transform.localScale = new Vector3(barWidth, barHeight, 1f);
        fill.transform.localPosition = new Vector3(0f, 0f, -0.05f);
    }

    /// <summary>
    /// Updates the fill scale and position to reflect the current fill amount.
    /// </summary>
    /// <param name="fill">The fill SpriteRenderer to update</param>
    /// <param name="barWidth">Width of the bar in world units</param>
    /// <param name="barHeight">Height of the bar in world units</param>
    /// <param name="currentFill">Current fill amount [0..1]</param>
    public static void UpdateFillScale(
        SpriteRenderer fill,
        float barWidth,
        float barHeight,
        float currentFill)
    {
        if (fill == null) return;

        float fillWidth = barWidth * currentFill;

        // Escalar en X
        fill.transform.localScale = new Vector3(fillWidth, barHeight, 1f);

        // Mantenir el pivot a l'esquerra:
        // centre del fill = -barWidth/2 + fillWidth/2
        fill.transform.localPosition = new Vector3(
            -barWidth * 0.5f + fillWidth * 0.5f,
            0f,
            -0.05f
        );
    }

    // ─────────────────────────────────────────────────────────────────────
    //  PRIVATE UTILS
    // ─────────────────────────────────────────────────────────────────────

    static SpriteRenderer MakeRenderer(GameObject parent, string name,
                                       Sprite sprite, Color color, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingOrder = order;
        return sr;
    }

    static Sprite CreatePixelSprite()
    {
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
    }
}
