using Spectre.Console.Rendering;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Column
{
    internal class MyPercentageColumn : ProgressColumn
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
            var percentage = task.Percentage;
            var style = percentage == 100 ? CompletedStyle : Style ?? Style.Plain;
            return new Text($"{task.Value}/{task.MaxValue} {percentage:F2}%", style).RightJustified();
        }
    }
}
