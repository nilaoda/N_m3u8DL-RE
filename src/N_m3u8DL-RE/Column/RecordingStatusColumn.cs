using Spectre.Console;
using Spectre.Console.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Column
{
    internal class RecordingStatusColumn : ProgressColumn
    {
        protected override bool NoWrap => true;
        public Style MyStyle { get; set; } = new Style(foreground: Color.Default);
        public Style FinishedStyle { get; set; } = new Style(foreground: Color.Yellow);
        public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
        {
            if (task.IsFinished)
                return new Text($"{task.Value}/{task.MaxValue} Waiting  ", FinishedStyle).LeftJustified();
            return new Text($"{task.Value}/{task.MaxValue} Recording", MyStyle).LeftJustified();
        }
    }
}
