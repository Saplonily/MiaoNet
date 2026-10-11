using Microsoft.Xna.Framework.Input;

namespace Celeste.Mod.MiaoNet;

partial class OuiMiaoNetOobe
{
    #region Layout Constants

    private const float OffScreenLeftX = -Celeste.TargetWidth;
    private const float OffScreenRightX = Celeste.TargetWidth * 3f / 2f;
    private const float ScreenCenterX = Celeste.TargetWidth / 2f;

    private const float LeftPanelLeft = 1f / 32f * Celeste.TargetWidth;
    private const float LeftPanelTop = 2f / 18f * Celeste.TargetHeight;
    private const float LeftPanelWidth = 15f / 32f * Celeste.TargetWidth;
    private const float LeftPanelHeight = 14f / 18f * Celeste.TargetHeight;
    private const float PanelTextMenuX = 17f / 32f * Celeste.TargetWidth;
    private const float PanelPageMenuMinWidth = 14f / 32f * Celeste.TargetWidth;

    private const float PanelBlockSpacing = 28f;
    private const float PanelImageSpacing = 16f;

    private const float PanelParagraphScale = 0.6f;

    private const float MaxImageUpscale = 1.25f;

    #endregion

    #region Left panel

    // for builder

    private abstract record PanelBlock;

    private sealed record PanelText(string Key, params MTexture[] Inputs) : PanelBlock;

    private sealed record PanelImageRow(params string[] Paths) : PanelBlock;

    // for renderer

    private readonly record struct PanelSegment(string? Text, MTexture? Icon);

    private readonly record struct PanelLine(List<PanelSegment> Segments, Vector2 Position);

    private readonly record struct PanelPicture(MTexture Texture, Vector2 Position, float Scale);

    private sealed class PanelPart
    {
        public List<List<PanelSegment>>? Lines;
        public List<MTexture>? Textures;
        public List<PanelPicture>? Pictures;
        public float Height;
    }

    private sealed class Panel
    {
        public readonly List<PanelLine> Lines = [];
        public readonly List<PanelPicture> Pictures = [];

        public void Draw(float alpha, float offsetX)
        {
            foreach (PanelPicture picture in Pictures)
            {
                picture.Texture.DrawJustified(
                    picture.Position + new Vector2(offsetX, 0f),
                    new Vector2(0.5f, 0.5f), Color.White * alpha, picture.Scale
                );
            }

            foreach (PanelLine line in Lines)
                DrawSegments(line.Segments, line.Position + new Vector2(offsetX, 0f), alpha);
        }
    }

    private sealed class PanelLayer
    {
        public Panel? Panel;
        public float Alpha = 1f;
        public float OffsetX;

        public void Fade(float alpha, float offsetX)
        {
            Alpha = alpha;
            OffsetX = offsetX;
        }

        public void Reset()
        {
            Panel = null;
            Alpha = 1f;
            OffsetX = 0f;
        }

        public void Draw()
            => Panel?.Draw(Alpha, OffsetX);
    }

    private Panel? BuildPanel(int step)
    {
        var createPanel = pages[step].CreatePanel;
        if (createPanel is null)
            return null;

        var blocks = createPanel();

        var parts = new List<PanelPart>();
        float textHeight = 0f;
        int imageParts = 0;

        foreach (PanelBlock block in blocks)
        {
            switch (block)
            {
            case PanelText text:
            {
                List<List<PanelSegment>> lines = BuildLines(Dialog.Clean(text.Key), text.Inputs);
                if (lines.Count == 0)
                    break;

                parts.Add(new PanelPart { Lines = lines });
                textHeight += LinesHeight(lines);
                break;
            }
            case PanelImageRow row:
            {
                List<MTexture> textures = row.Paths.Select(path => images[path]).ToList();
                if (textures.Count == 0)
                    break;

                parts.Add(new PanelPart { Textures = textures });
                imageParts++;
                break;
            }
            }
        }

        if (parts.Count == 0)
            return null;

        float spacing = PanelBlockSpacing * (parts.Count - 1);
        float imageBudget = imageParts > 0
            ? Math.Max(0f, LeftPanelHeight - textHeight - spacing) / imageParts
            : 0f;

        float totalHeight = textHeight + spacing;
        foreach (PanelPart part in parts)
        {
            if (part.Textures is null)
                continue;

            List<MTexture> textures = part.Textures;
            float cellWidth = (LeftPanelWidth - PanelImageSpacing * (textures.Count - 1)) / textures.Count;
            var pictures = new List<PanelPicture>(textures.Count);
            float rowHeight = 0f;

            for (int i = 0; i < textures.Count; i++)
            {
                MTexture texture = textures[i];
                float scale = Math.Min(
                    MaxImageUpscale,
                    Math.Min(cellWidth / texture.Width, imageBudget / texture.Height)
                );

                pictures.Add(new PanelPicture(
                    texture,
                    new Vector2(i * (cellWidth + PanelImageSpacing) + cellWidth / 2f, 0f),
                    scale
                ));
                rowHeight = Math.Max(rowHeight, texture.Height * scale);
            }

            part.Pictures = pictures;
            part.Height = rowHeight;
            totalHeight += rowHeight;
        }

        var panel = new Panel();
        float curY = LeftPanelTop + Math.Max(0f, (LeftPanelHeight - totalHeight) / 2f);

        foreach (PanelPart part in parts)
        {
            var lines = part.Lines;
            if (lines is not null)
            {
                float maxLineWidth = 0f;
                foreach (List<PanelSegment> line in lines)
                    maxLineWidth = Math.Max(maxLineWidth, SegmentsWidth(line));

                float curX = LeftPanelLeft + (LeftPanelWidth - maxLineWidth) / 2f;
                foreach (List<PanelSegment> line in lines)
                {
                    float lineHeight = SegmentsHeight(line);
                    panel.Lines.Add(new PanelLine(line, new Vector2(curX, curY + lineHeight / 2f)));
                    curY += lineHeight;
                }
            }
            else
            {
                float centerY = curY + part.Height / 2f;
                foreach (PanelPicture picture in part.Pictures!)
                {
                    panel.Pictures.Add(picture with
                    {
                        Position = picture.Position + new Vector2(LeftPanelLeft, centerY)
                    });
                }

                curY += part.Height;
            }

            curY += PanelBlockSpacing;
        }

        return panel;
    }

    private static List<List<PanelSegment>> BuildLines(string text, MTexture[] inputs, char inputPlaceholder = '|')
    {
        var lines = new List<List<PanelSegment>>();

        int index = 0;

        string[] paragraphs = text.Split('\n');
        foreach (string paragraph in paragraphs)
        {
            var line = new List<PanelSegment>();
            string[] parts = paragraph.Trim().Split(inputPlaceholder);

            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                {
                    line.Add(index < inputs.Length
                        ? new PanelSegment(null, inputs[index++])
                        : new PanelSegment(inputPlaceholder.ToString(), null)
                    );
                }

                if (parts[i].Length > 0)
                    line.Add(new PanelSegment(parts[i], null));
            }

            lines.Add(line);
        }

        while (lines.Count > 1 && lines[^1].Count == 0)
            lines.RemoveAt(lines.Count - 1);

        return lines;
    }

    private static float SegmentsWidth(IReadOnlyList<PanelSegment> segments)
    {
        float width = 0f;
        foreach (PanelSegment segment in segments)
            width += SegmentWidth(segment);
        return width;
    }

    private static float SegmentsHeight(IReadOnlyList<PanelSegment> segments)
    {
        float height = ActiveFont.LineHeight * PanelParagraphScale;

        foreach (PanelSegment segment in segments)
        {
            var icon = segment.Icon;
            if (icon is not null)
                height = Math.Max(height, icon.Height * PanelParagraphScale);
        }

        return height;
    }

    private static float LinesHeight(List<List<PanelSegment>> lines)
    {
        float height = 0f;
        foreach (List<PanelSegment> line in lines)
            height += SegmentsHeight(line);
        return height;
    }

    private static float SegmentWidth(PanelSegment segment)
    {
        MTexture? icon = segment.Icon;
        if (icon is not null)
            return icon.Width * PanelParagraphScale;

        return ActiveFont.Measure(segment.Text ?? string.Empty).X * PanelParagraphScale;
    }

    private static void DrawSegments(IReadOnlyList<PanelSegment> segments, Vector2 position, float alpha)
    {
        float x = position.X;

        foreach (PanelSegment segment in segments)
        {
            MTexture? icon = segment.Icon;
            if (icon is not null)
            {
                icon.DrawJustified(
                    new Vector2(x, position.Y), new Vector2(0f, 0.5f), Color.White * alpha, PanelParagraphScale
                );
                x += icon.Width * PanelParagraphScale;
                continue;
            }

            string text = segment.Text ?? string.Empty;
            ActiveFont.DrawOutline(
                text, new Vector2(x, position.Y), new Vector2(0f, 0.5f), Vector2.One * PanelParagraphScale,
                Color.Gray * alpha, 2f, Color.Black * (alpha * alpha * alpha)
            );
            x += ActiveFont.Measure(text).X * PanelParagraphScale;
        }
    }

    private const string QuestionIcon = "controls/keyboard/oemquestion";

    private static MTexture InputButton(VirtualButton button)
    {
        bool controller = Input.GuiInputController(Input.PrefixMode.Latest);
        return controller
            ? Input.GuiButton(button, Input.PrefixMode.Attached, QuestionIcon)
            : Input.GuiKey(Input.FirstKey(button), QuestionIcon);
    }

    private static MTexture InputButton(ButtonBinding binding)
    {
        VirtualButton? button = binding.Button;
        if (button is not null)
            return InputButton(button);

        return binding.Keys.Count > 0 ? InputKey(binding.Keys[0]) : GFX.Gui[QuestionIcon];
    }

    private static MTexture InputKey(Keys key)
        => Input.GuiKey(key, QuestionIcon);

    private static MTexture InputMouse(MInput.MouseData.MouseButtons button)
        => Input.GuiMouseButton(button, Input.PrefixMode.Latest, QuestionIcon);

    private sealed class SubHeaderWithInputs : TextMenu.Item
    {
        private readonly List<List<PanelSegment>> lines;
        private readonly float width;
        private readonly bool topPadding;

        public SubHeaderWithInputs(string key, bool topPadding, params MTexture[] inputs)
        {
            this.topPadding = topPadding;
            Selectable = false;

            lines = BuildLines(Dialog.Clean(key), inputs);

            float width = 0f;
            foreach (List<PanelSegment> line in lines)
                width = Math.Max(width, SegmentsWidth(line));
            this.width = width;
        }

        public override float LeftWidth() => width;

        public override float RightWidth() => 0f;

        public override float Height()
            => LinesHeight(lines) + (topPadding ? 48f : 0f);

        public override void Render(Vector2 position, bool highlighted)
        {
            bool column = Container.InnerContent == TextMenu.InnerContentMode.TwoColumn;
            float alpha = Container.Alpha;
            float y = position.Y + (topPadding ? 32f : 0f) - LinesHeight(lines) * 0.5f;

            foreach (List<PanelSegment> line in lines)
            {
                float lineHeight = SegmentsHeight(line);
                float left = column ? position.X : position.X + (Container.Width - SegmentsWidth(line)) * 0.5f;
                DrawSegments(line, new Vector2(left, y + lineHeight * 0.5f), alpha);
                y += lineHeight;
            }
        }
    }

    #endregion

}
