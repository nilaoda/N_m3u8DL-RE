namespace N_m3u8DL_RE.Config;

/// <summary>
/// 通过配置环境变量来实现更细节地控制某些逻辑
/// </summary>
public static class EnvConfigKey
{
    /// <summary>
    /// 当此值为1时, 在图形字幕处理逻辑中PNG生成后不再删除m4s文件
    /// </summary>
    public const string ReKeepImageSegments = "RE_KEEP_IMAGE_SEGMENTS";
    
    /// <summary>
    /// 控制启用PipeMux时, 具体ffmpeg命令行
    /// </summary>
    public const string ReLivePipeOptions = "RE_LIVE_PIPE_OPTIONS";
    
    /// <summary>
    /// 控制启用PipeMux时, 非Windows环境下命名管道文件的生成目录
    /// </summary>
    public const string ReLivePipeTmpDir = "RE_LIVE_PIPE_TMP_DIR";
}