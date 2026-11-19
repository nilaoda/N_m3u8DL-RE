using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Util;

namespace N_m3u8DL_RE.Runtime;

internal interface IUserInteraction
{
    Task<List<StreamSpec>> SelectStreamsAsync(List<StreamSpec> streams, CancellationToken token);
    Task<List<VodPartSelector.PartGroup>> SelectPartsAsync(List<VodPartSelector.PartGroup> groups, CancellationToken token);
}
