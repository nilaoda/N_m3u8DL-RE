﻿using Spectre.Console;
using Spectre.Console.Rendering;

namespace N_m3u8DL_RE.Column
{
    internal sealed class RecordingStatusColumn : ProgressColumn
    {
        protected override bool NoWrap => true;
        public Style MyStyle { get; set; } = new Style(foreground: Color.Default);
        public Style FinishedStyle { get; set; } = new Style(foreground: Color.Yellow);
        public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
        {
            return task.IsFinished
                ? new Text($"{task.Value}/{task.MaxValue} Waiting  ", FinishedStyle).LeftJustified()
                : new Text($"{task.Value}/{task.MaxValue} Recording", MyStyle).LeftJustified();
        }
    }
}