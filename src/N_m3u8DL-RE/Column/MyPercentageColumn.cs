﻿using Spectre.Console;
using Spectre.Console.Rendering;

namespace N_m3u8DL_RE.Column
{
    internal sealed class MyPercentageColumn : ProgressColumn
    {
        /// <summary>
        /// Gets or sets the style for a non-complete task.
        /// </summary>
        public Style Style { get; set; } = Style.Plain;

        /// <summary>
        /// Gets or sets the style for a completed task.
        /// </summary>
        public Style CompletedStyle { get; set; } = new Style(foreground: Color.Green);

        /// <inheritdoc/>
        public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
        {
            double percentage = task.Percentage;
            Style style = percentage == 100 ? CompletedStyle : Style ?? Style.Plain;
            return new Text($"{task.Value}/{task.MaxValue} {percentage:F2}%", style).RightJustified();
        }
    }
}