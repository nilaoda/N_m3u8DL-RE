using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Runtime;
using N_m3u8DL_RE.Util;
using Spectre.Console;

namespace N_m3u8DL_RE.ConsoleUI;

internal sealed class ConsoleUserInteraction : IUserInteraction
{
    public Task<List<StreamSpec>> SelectStreamsAsync(List<StreamSpec> streams, CancellationToken token) =>
        Task.FromResult(FilterUtil.SelectStreams(streams));

    public Task<List<VodPartSelector.PartGroup>> SelectPartsAsync(List<VodPartSelector.PartGroup> groups, CancellationToken token)
    {
        var prompt = new MultiSelectionPrompt<VodPartSelector.PartGroup>()
            .Title(ResString.vodPromptTitle).UseConverter(g => g.Display.EscapeMarkup())
            .Required().PageSize(10).MoreChoicesText(ResString.promptChoiceText)
            .InstructionsText(ResString.vodPromptInfo);
        prompt.AddChoices(groups);
        foreach (var group in groups)
            prompt.Select(group);
        return Task.FromResult(CustomAnsiConsole.Console.Prompt(prompt));
    }
}
